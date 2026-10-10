# Veritabanı: yayın, yedek ve geri dönüş planı

Şema değişiklikleri yalnızca onaylı yayın adımında uygulanır. Uygulama başlarken migration çalıştırmaz
(`Database.Migrate` veya `EnsureCreated` yok; `DeploymentScriptTests.The_API_never_creates_or_migrates_the_database_when_it_starts`
bunu boş bir veritabanıyla Production ortamında doğrular). Runtime hesabının şema değiştirme yetkisi yoktur
([`docs/database.md`](../docs/database.md#hesaplar-migration-ve-runtime-ayrı)).

| Dosya | Ne işe yarar |
| --- | --- |
| [`sql/migrate-idempotent.sql`](sql/migrate-idempotent.sql) | Tüm migration'ların idempotent betiği. Uygulanmış migration'ları atlar, tekrar çalıştırılabilir. |
| [`sql/backup-before-migration.sql`](sql/backup-before-migration.sql) | Yayından hemen önce `COPY_ONLY` tam yedek ve `RESTORE VERIFYONLY` |
| [`sql/restore-from-backup.sql`](sql/restore-from-backup.sql) | Veritabanını o yedeğe geri döndürür |

## Yayın adımları

1. IIS uygulama havuzunu durdurun (yayın sırasında yazma olmasın).
2. Yedek alın ve doğrulayın; betik `RESTORE VERIFYONLY` hata verirse yayına devam etmeyin:

   ```bash
   sqlcmd -S <sunucu> -E -I -b -v DatabaseName="<veritabanı>" BackupFile="<yedek klasörü>\<veritabanı>_<tarih>.bak" -i deploy/sql/backup-before-migration.sql
   ```

3. Betiği DBA inceler ve migration hesabıyla çalıştırır (`-I`: filtreli indeksler için `QUOTED_IDENTIFIER ON`;
   `-b`: ilk hatada durur, yarım kalan migration'ın transaction'ı geri alınır):

   ```bash
   sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i deploy/sql/migrate-idempotent.sql
   ```

4. Yeni sürümü yayınlayıp uygulama havuzunu başlatın. `/api/health/ready` `Healthy` dönmelidir; bekleyen migration
   varsa `Unhealthy` döner.

## Geri dönüş seçenekleri

| Durum | Yol | Veri kaybı |
| --- | --- | --- |
| Betik hata verdi | Bir şey yapmayın: hatalı migration'ın transaction'ı geri alınır, önceki migration'lar uygulanmış kalır. Hatayı giderip betiği yeniden çalıştırın. | Yok |
| Yayın sonrası sorun, kimse yazmadı | Yedekten dönün (`restore-from-backup.sql`), önceki uygulama sürümünü yayınlayın. | Yok |
| Yayın sonrası sorun, yeni veri yazıldı | Önce ileri düzeltme (yeni sürüm veya yeni migration) düşünün. Geri dönmek zorunluysa aşağıdaki tablodan migration bazında geri alın. | Tabloya bakın |
| Veritabanı bozuldu, migration'la düzelmiyor | Yedekten dönün. | Yedekten sonra yazılan her şey |

Yedekten dönüş, yedekten sonra yazılan **her şeyi** (demirbaş değişiklikleri, oturumlar, audit kayıtları) siler.
Bu yüzden yazma başladıktan sonra yalnızca son çare olarak kullanılır ve öncesinde kaybolacak kayıtlar dışa aktarılır.

## Migration bazında geri alma

Bir migration'ı geri almanın betiği (`<şimdiki>` → `<hedef>`; hedef `0` her şeyi siler):

```bash
dotnet ef migrations script <şimdiki migration> <hedef migration> \
  --project src/EnterpriseInventory.Infrastructure --startup-project src/EnterpriseInventory.Infrastructure \
  -o artifacts/sql/rollback.sql
```

Her migration'ın geri alınması kendi transaction'ındadır. Birden fazla adım geri alınırken biri başarısız olursa
**ondan önceki adımlar geri alınmış olarak kalır**; bu yüzden geri alma her seferinde tek migration ve öncesinde yedekle
yapılır.

| Migration | Geri alındığında | Veri kaybı riski |
| --- | --- | --- |
| `AddReportingIndexes` | Üç indeks silinir (`IX_Assets_IsDeleted_AssetCode`, `IX_AssetAssignments_AssignedAt`, `IX_AssetAssignments_ReturnedAt`). | Veri kaybı yok; derin sayfalar ve zimmet hareketleri raporu yavaşlar ([performans](../docs/performance.md)). |
| `AddUserSessions` | `UserSessions` tablosu silinir. | Oturum geçmişi kaybolur, açık oturumlar biter (kullanıcılar yeniden giriş yapar). Giriş/çıkış audit kayıtları `AuditLogs`'ta kalır. Gerekirse tablo önceden yedeklenir. |
| `AddSignInAuditActions` | `CK_AuditLogs_Action` eski haline (1–7) döner. | Veri silinmez, ama `SignedIn`/`SignedOut`/`AccessRevoked` (8–10) audit kaydı varsa kısıt eklenemez ve geri alma **reddedilir**. Audit kayıtları silinmez; bu noktadan geriye yalnızca yedekten dönülür. |
| `InitialCreate` | Bütün tablolar silinir. | **Tüm veri.** Üretimde kullanılmaz; yerine yedekten dönülür. |

## Denenenler

Test SQL Server'ında (SQL Server 2022 container, `Turkish_CI_AS`):

- `migrate-idempotent.sql` boş veritabanına iki kez uygulandı; ikinci çalıştırma bir şey değiştirmedi, filtreli
  benzersiz indeksler oluştu (`DeploymentScriptTests.The_script_builds_the_schema_and_can_be_run_again`). Commit'teki
  betik, migration'lardan üretilen betikle aynı olmalıdır; eskirse `MigrationTests` kırılır.
- Boş veritabanında bütün migration'lar tek tek geri alınıp yeniden uygulandı
  (`DeploymentScriptTests.Every_migration_can_be_rolled_back_and_applied_again_on_an_empty_database`).
- Giriş audit kaydı olan veritabanında `AddSignInAuditActions`'ın geri alınması `CK_AuditLogs_Action` ile reddedildi,
  audit kaydı yerinde kaldı ve `AddUserSessions` geri alınmış olarak kaldı
  (`DeploymentScriptTests.Rolling_back_past_the_sign_in_audit_actions_is_refused_once_sign_ins_are_recorded`).
- Elle tatbikat: geliştirme seed'i yüklenmiş bir veritabanında `backup-before-migration.sql` yedek alıp doğruladı;
  yedekten sonra bir kayıt eklendi; `restore-from-backup.sql` veritabanını yedekteki haline döndürdü (eklenen kayıt
  gitti, veritabanı yeniden `MULTI_USER`). `dotnet ef migrations script AddUserSessions AddSignInAuditActions` ile
  üretilen geri alma betiği `UserSessions`'ı sildi; ardından `migrate-idempotent.sql` onu yeniden oluşturdu.

Şirketin SQL Server'ında, gerçek yedek klasörü ve hesaplarla henüz denenmedi; ilk yayından önce DBA ile aynı tatbikat
yapılmalıdır.
