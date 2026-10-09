# Veritabanı (4. gün)

EF Core Code First, SQL Server. Kod: `src/EnterpriseInventory.Infrastructure/Persistence`.
Testler: `tests/EnterpriseInventory.IntegrationTests/Persistence`.

## Tasarım

- **Tablolar:** `Assets`, `AssetAssignments`, `Brands`, `AssetModels`, `Cities`, `Departments`, `Locations`,
  `Employees`, `AdminUsers`, `AuditLogs`. Enum'lar `int` olarak saklanır ve `CHECK` ile sınırlanır.
- **Eşzamanlılık:** Demirbaş, tanım (marka, model, şehir, departman, lokasyon) ve çalışan kayıtlarında
  `RowVersion` (`rowversion`) vardır. Eski bir kopya kaydedilirse EF Core `DbUpdateConcurrencyException` atar;
  API bunu 13. günde HTTP 409'a çevirecek. Zimmetlerin kendi `RowVersion`'ı yoktur, demirbaşın parçasıdır:
  zimmet eklendiğinde veya değiştiğinde `ApplicationDbContext` demirbaş satırını da günceller. Böylece aynı
  demirbaşa aynı anda yapılan zimmet, iade ve devir (iade + yeni zimmet) işlemlerinden ikincisi çakışma alır.
  `AdminUsers` yalnızca giriş bilgisini tutar, orada son yazan kazanır; `AuditLogs` değiştirilmez.
- **Audit alanları:** `CreatedAt/By`, `UpdatedAt/By` kayıt sırasında `AuditableEntityInterceptor` tarafından
  oturum açmış kullanıcıyla doldurulur. Saatler UTC'dir. Oturum açmış kullanıcı yoksa audit alanı olmayan
  tablolar dahil hiçbir değişiklik kaydedilmez (anonim yazma yok). Giriş sırasında `AdminUsers` kaydı
  yazılacağı için login akışı (7–8. gün) giriş yapan kullanıcıyı bu kontrole tanıtacak.
- **Silme yok:** Tüm ilişkiler `ON DELETE NO ACTION`. Demirbaş arşivlenir (`IsDeleted`), zimmet geçmişi ve
  tanımlar silinmez. Arşivlenen kayıtlar için global sorgu filtresi yoktur; listeler `IsDeleted = 0`
  koşulunu kendisi ekler, böylece geçmiş ve raporlar arşivi görmeye devam eder.

Veritabanının kendisi şu kuralları uygular (uygulamayı atlayan bir SQL de reddedilir):

| Kural | Nesne |
| --- | --- |
| Bir demirbaşın tek aktif zimmeti olur | `UX_AssetAssignments_AssetId_Active` (filtreli benzersiz indeks, `ReturnedAt IS NULL`) |
| Demirbaş kodu benzersiz, boş olamaz | `IX_Assets_AssetCode`, `CK_Assets_AssetCode_NotBlank` |
| Seri numarası doluysa benzersiz; boş olabilir | `IX_Assets_SerialNumber` (filtreli), `CK_Assets_SerialNumber_NotBlank` |
| Marka modelle çelişemez | `FK_Assets_AssetModels_ModelId_BrandId` → `AssetModels(Id, BrandId)` |
| Lokasyon demirbaşın şehrinde olmalı | `FK_Assets_Locations_LocationId_CityId` → `Locations(Id, CityId)`; lokasyon boşsa kontrol edilmez |
| Zimmetli demirbaş arşivlenemez | `CK_Assets_ArchivedNotAssigned` |
| İade, zimmetten önce olamaz; iade tarihi ile iadeyi alan birlikte dolu | `CK_AssetAssignments_ReturnAfterAssign`, `CK_AssetAssignments_ReturnedByWithReturn` |
| Marka, şehir, departman adları benzersiz; model adı markada, lokasyon adı şehirde benzersiz | `IX_*_Name` indeksleri |
| Durum, tür ve audit işlem değerleri geçerli | `CK_Assets_Status`, `CK_Assets_AssetType`, `CK_AuditLogs_Action` |

## Collation

Veritabanı **`Turkish_CI_AS`** ile oluşturulmalıdır. Benzersiz ad kontrolleri ve sıralama Türkçe kurallara
uyar: `izmir` ile `İZMİR` aynı ad sayılır, `ç`, `ğ`, `ı`, `ö`, `ş`, `ü` doğru sıralanır. Collation
migration'da değiştirilmez; veritabanını oluşturan DBA belirler:

```sql
CREATE DATABASE [<veritabanı>] COLLATE Turkish_CI_AS;
```

## Hesaplar: migration ve runtime ayrı

| Hesap | Kullanım | Yetki |
| --- | --- | --- |
| Migration | Yalnızca yayın sırasında şemayı günceller | `db_ddladmin` + `db_datareader` + `db_datawriter` (veya betiği DBA çalıştırır) |
| Runtime | Uygulama (`ConnectionStrings:DefaultConnection`) | `ei_app_runtime` rolü: `SELECT`, `INSERT`, `UPDATE`; `DELETE` ve şema değişikliği yok; `AuditLogs` yalnızca eklenir |

Runtime yetkileri [`scripts/sql/grant-runtime-permissions.sql`](../scripts/sql/grant-runtime-permissions.sql)
ile verilir. Uygulama başlarken migration çalıştırmaz.

## Migration komutları

Araçlar: `dotnet tool restore` (`dotnet-ef`, `dotnet-tools.json` içinde sabit sürüm).

```bash
# Model değiştiyse yeni migration
dotnet ef migrations add <Ad> --project src/EnterpriseInventory.Infrastructure

# Yayın betiği: idempotent, tekrar çalıştırılabilir; DBA inceleyip çalıştırır
dotnet ef migrations script --idempotent --project src/EnterpriseInventory.Infrastructure -o artifacts/sql/migrate.sql
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i artifacts/sql/migrate.sql

# Geliştirme veritabanına doğrudan uygulama (migration hesabıyla)
export ConnectionStrings__Migrations="<migration hesabının bağlantı dizesi>"
dotnet ef database update --project src/EnterpriseInventory.Infrastructure
```

- `sqlcmd` ile `-I` (QUOTED_IDENTIFIER ON) zorunludur; filtreli indeksler bu ayar olmadan oluşturulamaz.
  SSMS bu ayarı varsayılan olarak açık tutar.
- Betik her migration'ı ayrı transaction'da uygular. `-b` ile sqlcmd ilk hatada durur ve yarım kalan
  migration geri alınır (denendi: hatalı bir çalıştırmadan sonra şemada değişiklik kalmadı).
- `ConnectionStrings__Migrations` yalnızca `dotnet ef` için kullanılır, uygulama okumaz. Tanımlı değilse
  `database update` hiçbir veritabanına bağlanmadan hata verir; `migrations add` ve `migrations script`
  veritabanı gerektirmez. Bağlantı dizeleri repoya yazılmaz.
- Geri alma (rollback) ve yedekten dönüş senaryoları yayın aşamasında (`deploy/`) yazılıp test edilecek.

## Testler

`MigrationTests.Model_has_no_changes_missing_from_a_migration` her zaman çalışır: model değişip migration
eklenmediyse test kırılır. SQL Server testleri yalnızca `EI_TEST_SQL_CONNECTION` tanımlıysa çalışır, yoksa
"skipped" görünür:

```bash
export EI_TEST_SQL_CONNECTION="Server=<test sunucusu>;Integrated Security=true;TrustServerCertificate=false"
dotnet test EnterpriseInventory.slnx
```

Her test çalıştırması `EI_Test_<guid>` adlı yeni bir veritabanı oluşturur (`Turkish_CI_AS`), migration'ları
uygular ve sonunda siler. Bu yüzden hesabın `CREATE DATABASE` yetkisi olmalı; yalnızca test sunucusunda
kullanın. Testler gerçek SQL Server'da şunları doğrular: kayıt/okuma ve audit alanları, RowVersion çakışması,
iki yöneticinin aynı anda zimmet vermesi, aynı anda iade ve devir, iade sonrası yeniden zimmet ve geçmiş,
lokasyonu temizleme ve yukarıdaki tablodaki her kısıt.
