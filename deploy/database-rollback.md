# Veritabanı: yayın, yedek ve geri dönüş planı

Şema değişiklikleri yalnızca onaylı yayın adımında uygulanır. Uygulama başlarken migration çalıştırmaz
(`Database.Migrate` veya `EnsureCreated` yok; `DeploymentScriptTests.The_API_never_creates_or_migrates_the_database_when_it_starts`
bunu boş bir veritabanıyla Production ortamında doğrular). Runtime hesabının şema değiştirme yetkisi yoktur
([`docs/database.md`](../docs/database.md#hesaplar-migration-ve-runtime-ayrı)).

| Dosya | Ne işe yarar |
| --- | --- |
| [`sql/Update-EnterpriseInventoryDatabase.ps1`](sql/Update-EnterpriseInventoryDatabase.ps1) | Yayın betiği: aşağıdaki SQL betiklerini sırayla ve ilk hatada durarak çalıştırır, sonucu denetler (`-WhatIf` destekler) |
| [`sql/Restore-EnterpriseInventoryDatabase.ps1`](sql/Restore-EnterpriseInventoryDatabase.ps1) | Yedekten dönüş: yedeği doğrular, onay ister, geri yükler, veritabanının durumunu ve runtime yetkilerini denetler |
| [`sql/migrate-idempotent.sql`](sql/migrate-idempotent.sql) | Tüm migration'ların idempotent betiği. Uygulanmış migration'ları atlar, tekrar çalıştırılabilir. |
| [`sql/backup-before-migration.sql`](sql/backup-before-migration.sql) | Yayından hemen önce `COPY_ONLY` tam yedek ve `RESTORE VERIFYONLY` |
| [`sql/restore-from-backup.sql`](sql/restore-from-backup.sql) | Veritabanını o yedeğe geri döndürür; önce dosyanın bu veritabanının bu sunucuda alınmış yedeği olduğunu (msdb geçmişi) ve sağlam olduğunu (`RESTORE VERIFYONLY`) denetler, ancak sonra bağlantıları kapatır |
| [`sql/verify-runtime-permissions.sql`](sql/verify-runtime-permissions.sql) | Runtime hesabının yetkilerini onun gözünden denetler; bir yetki fazla veya eksikse hata verir. Hiçbir şeyi değiştirmez |
| [`../scripts/sql/grant-runtime-permissions.sql`](../scripts/sql/grant-runtime-permissions.sql) | Runtime hesabına `ei_app_runtime` rolünü ve en az yetkiyi verir |

Yayını yapan hesap (DBA veya migration hesabı) veritabanında `db_owner`'dır; uygulamanın runtime hesabı şemayı
değiştiremez, kayıt silemez, audit kayıtlarını değiştiremez ve migration geçmişine yazamaz. İkisi aynı hesap olamaz;
yayın betiği bunu denetler. PowerShell betikleri sqlcmd'yi her zaman şifreli bağlantı (`-Nm`) ve sertifika
doğrulamasıyla çağırır; sqlcmd değişkenleri betiğe metin olarak yerleştiği için veritabanı, hesap ve dosya adları dar
bir karakter kümesiyle sınırlıdır.

## Yayın adımları

Depo, yayınlanan sürümün commit'inde olmalıdır (yayın klasöründeki `release.json`).

1. IIS uygulama havuzunu durdurun (yayın sırasında yazma olmasın).
2. Yayın betiğini önce `-WhatIf` ile, sonra onsuz çalıştırın. Bekleyen migration varsa yedek alır ve doğrular,
   migration'ları uygular, hepsinin uygulandığını denetler; ardından runtime yetkilerini verir ve denetler:

   ```powershell
   .\deploy\sql\Update-EnterpriseInventoryDatabase.ps1 -SqlServer <sql sunucusu> -DatabaseName <veritabanı> `
       -BackupDirectory '<SQL Server makinesindeki yedek klasörü>' -RuntimeUser '<DOMAIN>\<gmsa>$' -WhatIf
   ```

   Çıktıdaki her satır `PASS`, `BİLGİ` veya `HATA`'dır; `HATA` varsa betik durur, çıkış kodu 1'dir ve yayına devam
   edilmez. Veritabanında bu sürümün bilmediği bir migration varsa (yanlış commit) hiçbir şey yapmadan durur.
3. Yeni sürümü yayınlayıp uygulama havuzunu başlatın. `/api/health/ready` `Healthy` dönmelidir; bekleyen migration
   varsa `Unhealthy` döner.

Betiği çalıştırmak mümkün değilse aynı adımlar elle yapılır:

1. Yedek alın ve doğrulayın; betik `RESTORE VERIFYONLY` hata verirse yayına devam etmeyin:

   ```bash
   sqlcmd -S <sunucu> -E -I -b -v DatabaseName="<veritabanı>" BackupFile="<yedek klasörü>\<veritabanı>_<tarih>.bak" -i deploy/sql/backup-before-migration.sql
   ```

2. Betiği DBA inceler ve migration hesabıyla çalıştırır (`-I`: filtreli indeksler için `QUOTED_IDENTIFIER ON`;
   `-b`: ilk hatada durur, yarım kalan migration'ın transaction'ı geri alınır):

   ```bash
   sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -i deploy/sql/migrate-idempotent.sql
   ```

3. Runtime yetkilerini verip denetleyin:

   ```bash
   sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -v RuntimeUser="<DOMAIN\hesap>" -i scripts/sql/grant-runtime-permissions.sql
   sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -v RuntimeUser="<DOMAIN\hesap>" -i deploy/sql/verify-runtime-permissions.sql
   ```

## Geri dönüş seçenekleri

| Durum | Yol | Veri kaybı |
| --- | --- | --- |
| Betik hata verdi | Bir şey yapmayın: hatalı migration'ın transaction'ı geri alınır, önceki migration'lar uygulanmış kalır. Hatayı giderip betiği yeniden çalıştırın. | Yok |
| Yayın sonrası sorun, kimse yazmadı | Yedekten dönün (aşağıda), önceki uygulama sürümünü yayınlayın. | Yok |
| Yayın sonrası sorun, yeni veri yazıldı | Önce ileri düzeltme (yeni sürüm veya yeni migration) düşünün. Geri dönmek zorunluysa aşağıdaki tablodan migration bazında geri alın. | Tabloya bakın |
| Veritabanı bozuldu, migration'la düzelmiyor | Yedekten dönün. | Yedekten sonra yazılan her şey |

Yedekten dönüş, yedekten sonra yazılan **her şeyi** (demirbaş değişiklikleri, oturumlar, audit kayıtları) siler.
Bu yüzden yazma başladıktan sonra yalnızca son çare olarak kullanılır ve öncesinde kaybolacak kayıtlar dışa aktarılır.

## Yedekten dönüş

1. IIS uygulama havuzunu durdurun ve saklanması gereken kayıtları dışa aktarın.
2. Yayın betiğinin yazdığı yedek dosyasıyla geri yükleyin. Betik dosyayı msdb yedek geçmişinde arar ve bilgisini
   yazar, onay ister, `restore-from-backup.sql`'i çalıştırır; sonra veritabanının `ONLINE` ve `MULTI_USER` olduğunu,
   uygulanmış migration'ları ve runtime yetkilerini denetler:

   ```powershell
   .\deploy\sql\Restore-EnterpriseInventoryDatabase.ps1 -SqlServer <sql sunucusu> -DatabaseName <veritabanı> `
       -BackupFile '<yedek klasörü>\<veritabanı>_<tarih>_migration-oncesi.bak' -RuntimeUser '<DOMAIN>\<gmsa>$'
   ```

   Dosya bu sunucuda bu veritabanının tam yedeği olarak kayıtlı değilse (başka veritabanının yedeği, yanlış yol,
   silinmiş dosya) veya `RESTORE VERIFYONLY` hata verirse, kimsenin bağlantısı kesilmeden durur.
3. Betiğin yazdığı son migration'a uyan önceki uygulama sürümünü yayınlayın
   ([IIS geri dönüş](iis/README.md#geri-dönüş)); `/api/health/ready` `Healthy` dönmelidir. Yeni sürüm eski şemayla
   `Unhealthy` döner.

Geri yükleme yarıda kalırsa (ör. disk doldu) veritabanı `SINGLE_USER` veya `RESTORING` durumunda kalabilir; bu durum
denenmedi. DBA nedeni giderip geri yüklemeyi `RESTORE DATABASE ... WITH REPLACE, CHECKSUM` ile elle tamamlar ve
`ALTER DATABASE ... SET MULTI_USER` çalıştırır; ardından betiğin 3. adımındaki denetimler elle yapılır.

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

### Önceki günler

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

### 39. gün

Otomatik testler (test SQL Server'ı, her test kendi veritabanında):

- `DatabaseRecoveryTests.Restoring_the_backup_taken_before_a_migration_brings_back_its_schema_and_data`: bir önceki
  migration'a kadar kurulmuş, demirbaş ve audit kaydı olan veritabanının `backup-before-migration.sql` ile yedeği
  alınır; migration uygulanır, yeni demirbaş eklenir ve eskisi güncellenir; `restore-from-backup.sql` ile geri
  dönülür. Sonuç: aynı satırlar ve aynı `RowVersion`, tek audit kaydı, `ONLINE` ve `MULTI_USER`, yedekteki migration
  listesi. Ardından migration yeniden uygulanır.
- `DatabaseRecoveryTests.A_backup_of_another_database_or_a_missing_file_is_refused_before_anyone_is_disconnected`:
  başka veritabanının yedeği ve olmayan dosya reddedilir, veritabanı `MULTI_USER` kalır.
- `RuntimePermissionTests`: commit'teki yetki betiğinden sonra `verify-runtime-permissions.sql` geçer; runtime hesabı
  olarak tablo oluşturma, kolon ekleme, tablo silme, `TRUNCATE`, demirbaş silme, audit güncelleme veya silme,
  migration geçmişine yazma, kullanıcı oluşturma ve yetki verme reddedilir ve hiçbir şey değişmez. Hesaba
  `db_datawriter` verilince denetim hata verir. Uygulama yalnızca runtime yetkileriyle ve kendi SQL login'iyle açılır;
  giriş, marka ekleme, demirbaş ekleme/güncelleme/zimmet/iade/arşiv, audit, rapor ve çıkış çalışır.

Elle tatbikat (Linux'ta PowerShell 7.6 ve go-sqlcmd 1.8; yayın hesabı `sa`, runtime hesabı ayrı bir SQL login'i;
bağlantı şifreli ve sertifika test CA'sıyla doğrulandı; uygulama 38. günün yayın paketiyle çalıştı):

1. `Update-EnterpriseInventoryDatabase.ps1 -WhatIf` boş veritabanında 4 bekleyen migration'ı yazdı, bir şey
   değiştirmedi. Onsuz çalıştırma yedek aldı, 4 migration'ı uyguladı, runtime yetkilerini verip 68 denetimle doğruladı.
   Uygulama açıldı ve bir demirbaş eklendi.
2. `AddReportingIndexes` EF geri alma betiğiyle geri alındı; uygulama `/api/health/ready` için `Unhealthy` (503) döndü.
   Yayın betiği 1 bekleyen migration için yeni yedek aldı ve uyguladı; `Healthy`, ikinci demirbaş eklendi.
3. Bu yedek başka bir veritabanına geri yüklenmek istendi: reddedildi (çıkış kodu 1). Uygulama durdurulup
   `Restore-EnterpriseInventoryDatabase.ps1` ile asıl veritabanına geri yüklendi: `ONLINE MULTI_USER`, 3 migration,
   runtime yetkileri doğru. Yeni sürüm bu şemayla `Unhealthy` döndü; yayın betiği yeniden çalışınca `Healthy` oldu ve
   yalnızca yedekten önceki demirbaş kaldı.
4. Runtime hesabıyla `migrate-idempotent.sql` çalıştırıldı: SQL Server reddetti (Msg 1088), veritabanı değişmedi.
   Yayın betiği runtime hesabıyla "Yayın, runtime hesabıyla yapılamaz" diyerek durdu.

Denenmeyenler: Windows'ta ODBC sqlcmd 18 ve Windows PowerShell 5.1, Windows kimlik doğrulamasıyla (gMSA) SQL bağlantısı,
şirketin SQL Server'ı, gerçek yedek klasörü ve hesaplar. İlk yayından önce DBA ile aynı tatbikat yapılmalıdır.
