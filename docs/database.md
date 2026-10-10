# Veritabanı (4. gün; 9. günde `UserSessions`; 20. günde talimat v1.1 EF Core kurallarına göre denetlendi)

EF Core 10.0.12 Code First, SQL Server. Kod: `src/EnterpriseInventory.Infrastructure/Persistence`
(`ApplicationDbContext`, `Configurations`, `Migrations`, `Seed`). EF'in katmanlardaki yeri ve komutlar:
[README](../README.md#veritabanı-ef-core). Testler: `tests/EnterpriseInventory.IntegrationTests/Persistence`,
`tests/EnterpriseInventory.UnitTests/Persistence`.

## Tasarım

- **Tablolar:** `Assets`, `AssetAssignments`, `Brands`, `AssetModels`, `Cities`, `Departments`, `Locations`,
  `Employees`, `AdminUsers`, `UserSessions`, `AuditLogs`. Enum'lar `int` olarak saklanır ve `CHECK` ile sınırlanır.
  Her entity'nin `IEntityTypeConfiguration<T>` sınıfı tablo adını, anahtarını, zorunlu alanlarını, metin
  uzunluklarını, ilişkilerini, indekslerini ve kısıtlarını açıkça tanımlar. Uzunluğu olmayan tek metinler audit
  kayıtlarının JSON anlık görüntüleridir (`OldValues`, `NewValues`, açıkça `nvarchar(max)`).
- **Eşzamanlılık:** Demirbaş, tanım (marka, model, şehir, departman, lokasyon) ve çalışan kayıtlarında
  `RowVersion` (`rowversion`) vardır. Eski bir kopya kaydedilirse EF Core `DbUpdateConcurrencyException` atar;
  API bunu HTTP 409'a çevirir. Güncelleme ve arşivlemede istemcinin gönderdiği `rowVersion`, EF Core'un
  `OriginalValue`'su yapılır; böylece `UPDATE … WHERE RowVersion = <istemcinin sürümü>` çalışır ve okuma ile kaydetme
  arasında başka bir `DbContext`'in yaptığı değişiklik de ezilmez. Zimmetlerin kendi `RowVersion`'ı yoktur, demirbaşın parçasıdır:
  zimmet eklendiğinde veya değiştiğinde `ApplicationDbContext` demirbaş satırını da günceller. Böylece aynı
  demirbaşa aynı anda yapılan zimmet, iade ve devir (iade + yeni zimmet) işlemlerinden ikincisi çakışma alır.
  Zimmet ve iade ayrıca demirbaş satırını okumadan önce kilitler (`UPDLOCK`), zimmet önce çalışana bağlı bir uygulama
  kilidi alır (`sp_getapplock`); ayrıntı: [`assignments-api.md`](assignments-api.md), [`concurrency.md`](concurrency.md).
  `AdminUsers` yalnızca giriş bilgisini tutar, orada son yazan kazanır; `AuditLogs` değiştirilmez.
- **Audit alanları:** `CreatedAt/By`, `UpdatedAt/By` kayıt sırasında `AuditableEntityInterceptor` tarafından
  oturum açmış kullanıcıyla doldurulur. Saatler UTC'dir. Oturum açmış kullanıcı yoksa audit alanı olmayan
  tablolar dahil hiçbir değişiklik kaydedilmez (anonim yazma yok). Tek istisna girişin kendisidir: AD parolayı
  ve grup üyeliğini doğruladıktan sonra `UserSessionService`, yalnızca o kaydetme işlemi için giriş yapan kullanıcıyı
  kaydedici olarak tanıtır (`SignInIdentity`). `AdminUsers` kaydı, yeni `UserSessions` satırı ve `AuditLogs`'daki
  `SignedIn` satırı aynı transaction'da yazılır.
- **Oturumlar (`UserSessions`):** Her giriş bir satırdır: yöneticinin kaydı, oturum anahtarının yalnızca SHA-256
  özeti (`KeyHash`, `binary(32)`, benzersiz), başlangıç, son işlem, mutlak bitiş, son AD kontrolü ve son başarısız
  kontrol zamanları, istemci adresi, bitiş zamanı ve nedeni (`EndReason` 1–6). Oturumlar silinmez, bitirilir;
  bitmiş satırlar geçmiş olarak kalır (temizleme politikası yayın aşamasında belirlenecek). Kurallar:
  [`session-security.md`](session-security.md).
- **Silme yok:** Tüm ilişkiler `ON DELETE NO ACTION` (`DeleteBehavior.Restrict`); hiçbir silme zimmet veya audit
  geçmişini götürmez. Demirbaş arşivlenir (soft delete, `IsDeleted`), zimmet geçmişi ve tanımlar silinmez.
- **Soft delete filtresi:** `Assets` ve `AssetAssignments` üzerinde `SoftDelete` adlı global sorgu filtresi
  (`HasQueryFilter`) vardır; arşivlenmiş demirbaşlar ve zimmetleri her sorgudan çıkar. Filtre yalnızca
  `SoftDelete.IncludingArchived()` ile kapatılır ve bu bilerek arşivi veya geçmişi okuyan sorgularla sınırlıdır: arşiv
  listesi (`archived=true`), demirbaş detayı ve geçmişi, düzenleme/arşivleme için yükleme (arşivlenmiş kayıt "yok"
  yerine "arşivlenmiş" diye reddedilsin), kod/seri no benzersizlik kontrolü ve gösterge panelindeki arşiv sayısı ile
  son işlemler. `IgnoreQueryFilters` kodda başka yerde geçmez (`LayerDependencyTests` denetler).
- **Seri numarası:** Tüm boşluklar silinir, harfler değişmez kültürle büyütülür, Türkçe `ı` ve `İ` `I` olarak
  saklanır (`Asset.NormalizeSerialNumber`); boş değer `null` olur. Böylece `5cd 1234 xyz` ile `5CD1234XYZ` aynı
  numaradır ve Türkçe collation'ın `i`/`I` farkı tekrar kayda yol açmaz. Filtreli benzersiz indeks yalnızca dolu
  değerlere uygulanır. Henüz yayınlanmış veri olmadığı için mevcut kayıtları dönüştüren bir migration gerekmedi.

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
| Oturum anahtarı özeti benzersiz; bitiş zamanı ile bitiş nedeni birlikte dolu; geçerli bitiş nedeni; son işlem ve mutlak bitiş başlangıçtan sonra | `IX_UserSessions_KeyHash`, `CK_UserSessions_Ended`, `CK_UserSessions_EndReason`, `CK_UserSessions_Times` |

Kural taşımayan, yalnızca sorguları hızlandıran indeksler (`IX_Assets_IsDeleted_AssetCode`,
`IX_AssetAssignments_AssignedAt`, filtreli `IX_AssetAssignments_ReturnedAt`) ve ölçümleri:
[`performance.md`](performance.md).

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
| Yayın (migration) | Yalnızca yayın sırasında: yedek, şema güncellemesi, runtime yetkileri | Veritabanında `db_owner` (veya betikleri DBA çalıştırır) |
| Runtime | Uygulama (`ConnectionStrings:DefaultConnection`) | `ei_app_runtime` rolü: `SELECT`, `INSERT`, `UPDATE`; `DELETE` ve şema değişikliği yok; `AuditLogs` yalnızca eklenir; `__EFMigrationsHistory` yalnızca okunur |

Runtime yetkileri [`scripts/sql/grant-runtime-permissions.sql`](../scripts/sql/grant-runtime-permissions.sql)
ile verilir ve [`deploy/sql/verify-runtime-permissions.sql`](../deploy/sql/verify-runtime-permissions.sql) ile
denetlenir: betik runtime hesabının gözünden veritabanı yetkilerini (tablo/prosedür oluşturma, şema, kullanıcı ve rol
değiştirme) ve her tablo için `SELECT`/`INSERT`/`UPDATE`/`DELETE`/`ALTER`'ı beklenenle karşılaştırır, bir fark varsa
hata verir. Yayın betiği ([`deploy/sql/Update-EnterpriseInventoryDatabase.ps1`](../deploy/sql/Update-EnterpriseInventoryDatabase.ps1))
ikisini her yayında çalıştırır ve runtime hesabıyla yayın yapmayı reddeder. Uygulama başlarken migration çalıştırmaz.

`RuntimePermissionTests` bunu SQL Server'da dener: denetim geçer; runtime hesabı olarak şema değişikliği, silme, audit
değiştirme, migration geçmişine yazma ve yetki verme reddedilir; hesaba `db_datawriter` verilince denetim hata verir;
uygulama yalnızca runtime yetkileriyle bütün iş akışlarını (giriş, tanım, demirbaş, zimmet, iade, arşiv, audit, rapor)
çalıştırır.

## Migration komutları

Araçlar: `dotnet tool restore` (`dotnet-ef`, `dotnet-tools.json` içinde sabit sürüm).

```bash
# Model değiştiyse yeni migration
dotnet ef migrations add <Ad> --project src/EnterpriseInventory.Infrastructure --startup-project src/EnterpriseInventory.Infrastructure

# Yayın betiği: idempotent, tekrar çalıştırılabilir; Git'te tutulur, DBA inceleyip çalıştırır
dotnet ef migrations script --idempotent --project src/EnterpriseInventory.Infrastructure \
  --startup-project src/EnterpriseInventory.Infrastructure -o deploy/sql/migrate-idempotent.sql
sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i deploy/sql/migrate-idempotent.sql

# Geliştirme veritabanına doğrudan uygulama (migration hesabıyla)
export ConnectionStrings__Migrations="<migration hesabının bağlantı dizesi>"
dotnet ef database update --project src/EnterpriseInventory.Infrastructure --startup-project src/EnterpriseInventory.Infrastructure
```

Migration dosyaları, model snapshot'ı ve `deploy/sql/migrate-idempotent.sql` Git'tedir. Yeni migration eklenince
betik yeniden üretilmelidir; eskirse `MigrationTests` kırılır.

- `sqlcmd` ile `-I` (QUOTED_IDENTIFIER ON) zorunludur; filtreli indeksler bu ayar olmadan oluşturulamaz.
  SSMS bu ayarı varsayılan olarak açık tutar.
- Betik her migration'ı ayrı transaction'da uygular. `-b` ile sqlcmd ilk hatada durur ve yarım kalan
  migration geri alınır (denendi: hatalı bir çalıştırmadan sonra şemada değişiklik kalmadı).
- `ConnectionStrings__Migrations` yalnızca `dotnet ef` için kullanılır, uygulama okumaz. Tanımlı değilse
  `database update` hiçbir veritabanına bağlanmadan hata verir; `migrations add` ve `migrations script`
  veritabanı gerektirmez. Bağlantı dizeleri repoya yazılmaz.
- Yedek, yayın adımları ve migration bazında veri kaybı riski: [`deploy/database-rollback.md`](../deploy/database-rollback.md).

## Sorgu ve transaction kuralları

- `ApplicationDbContext` DI'da scoped'dur; her istek kendi context'ini alır. Aynı context'te paralel sorgu
  çalıştırılmaz. Uzun ömürlü tek servis olan veritabanı health check'i her kontrolde kendi scope'unu açar; arka plan
  işi yoktur.
- Sorgular async'tir ve `CancellationToken` taşır. Salt okunur sorgular `AsNoTracking` ve `Select` ile DTO'ya
  projekte edilir; entity'ler API yanıtı olmaz. Filtre, arama, sıralama ve sayfalama SQL Server'da çalışır; sıralama
  her zaman demirbaş kodu ve `Id` ile biter. Liste isteği iki komuttur (toplam ve sayfa), detay tek komuttur; sayfa
  büyüklüğü komut sayısını değiştirmez (N+1 yok). Lazy loading kapalıdır (proxy paketi yok). Ham SQL'e kullanıcı
  girdisi birleştirilmez; arama terimleri parametredir.
- Tek `SaveChanges` ile atomik olan işlemler (güncelleme ve audit, arşivleme ve audit) ek transaction kullanmaz.
  Kimliği gerekip iki kez kaydedilen işlemler (demirbaş ekleme ve `Created` audit'i, tanım ekleme, giriş kaydı) açık
  transaction kullanır; audit yazılamazsa değişiklik de geri alınır. Audit kayıtları `AuditLogs`'a eklenir, kendileri
  tekrar audit'lenmez.
- Bağlantı hatasında yeniden deneme (retry) kapalıdır. Açılırsa her açık transaction'ın tamamı
  `Database.CreateExecutionStrategy().ExecuteAsync` içine alınmalıdır; `DbContextRegistrationTests` bunu hatırlatır.

## Geliştirme verisi (seed)

`Persistence/Seed/DevelopmentSeed` örnek marka/model, şehir/lokasyon ve departmanları ekler. Uygulama başlarken
çalışmaz, bilerek çalıştırılır: `dotnet run --project src/EnterpriseInventory.Api -- seed-development-data`. Yalnızca
Development ortamında çalışır, diğerlerinde hata verip durur. İdempotenttir: var olan değeri (Türkçe collation ile adına
göre) atlar; ikinci çalıştırma 0 satır ekler. Çalışan, kullanıcı, parola veya sahte AD hesabı eklemez.

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
kullanın. EF InMemory veya SQLite provider'ı kullanılmaz (`LayerDependencyTests` bunu da denetler); SQL Server
davranışının kanıtı yalnızca gerçek SQL Server testleridir. Testler şunları doğrular:

| Konu | Testler |
| --- | --- |
| Model (veritabanı gerekmez): açık tablo adı ve anahtar, cascade yok, metin uzunlukları, `rowversion`, marka–model ve şehir–lokasyon bileşik FK'leri, benzersiz ve filtreli indeksler, soft delete filtresi yalnızca demirbaş ve zimmette, lazy loading yok | `UnitTests/Persistence/ModelConfigurationTests` |
| Kayıt/okuma ve audit alanları, iki ayrı `DbContext` ile RowVersion çakışması, iki yöneticinin aynı anda zimmet vermesi, aynı anda iade ve devir, iade sonrası yeniden zimmet, lokasyonu temizleme | `AssetPersistenceTests` |
| Yukarıdaki tablodaki her kısıt ve indeks (FK, filtreli benzersiz indeks, çift aktif zimmet) | `DatabaseConstraintTests` |
| Soft delete: arşivlenmiş demirbaş ve zimmetleri normal sorgularda yok, `IncludingArchived` ile var, filtre SQL'de; arşivlenmişin kodu ve seri no'su rezerve | `SoftDeleteTests` |
| API okuduktan sonra başka bir `DbContext` kaydederse güncelleme ve arşivleme `409`, diğer değişiklik korunur; audit yazılamazsa ekleme, güncelleme ve arşivleme tamamen geri alınır | `Assets/AssetConsistencyTests` |
| Liste ve detayın SQL komutları: sayı, `OFFSET/FETCH`, sıralamada `AssetCode, Id`, parametreli filtre | `Assets/AssetQueryTests` |
| Idempotent betik güncel, iki kez uygulanır; migration'lar geri alınıp yeniden uygulanır; giriş audit'i varken geri alma reddedilir; API başlarken şemaya dokunmaz | `MigrationTests`, `DeploymentScriptTests` |
| Seed: ikinci çalıştırma 0 satır, yalnızca eksikleri ekler (Türkçe collation ile), kişi eklemez, Development dışında red | `DevelopmentSeedTests`; README'deki komut da elle çalıştırıldı |
