# deploy

Yayın dosyaları. IIS kurulumu, güncelleme ve geri dönüş: [`iis/README.md`](iis/README.md).

| Dosya | İçerik |
| --- | --- |
| [`iis/`](iis/README.md) | IIS yayın betikleri: yayın klasörü, sunucu ön kontrolü, site kurulumu, duman testi |
| [`database-rollback.md`](database-rollback.md) | Veritabanı yayın adımları, yedek, geri dönüş planı ve migration bazında veri kaybı riski |
| [`sql/migrate-idempotent.sql`](sql/migrate-idempotent.sql) | Tüm migration'ların idempotent SQL betiği (`dotnet ef migrations script --idempotent` çıktısı) |
| [`sql/backup-before-migration.sql`](sql/backup-before-migration.sql) | Yayından önce `COPY_ONLY` tam yedek ve doğrulama |
| [`sql/restore-from-backup.sql`](sql/restore-from-backup.sql) | Yedekten dönüş; önce yedeğin bu veritabanına ait ve sağlam olduğunu denetler |
| [`sql/verify-runtime-permissions.sql`](sql/verify-runtime-permissions.sql) | Runtime hesabının yetkilerini denetler; fazla veya eksik yetkide hata verir |
| [`sql/Update-EnterpriseInventoryDatabase.ps1`](sql/Update-EnterpriseInventoryDatabase.ps1) | Veritabanı yayını: yedek, migration, runtime yetkileri ve denetim (`-WhatIf` destekler) |
| [`sql/Restore-EnterpriseInventoryDatabase.ps1`](sql/Restore-EnterpriseInventoryDatabase.ps1) | Yayından önceki yedeğe dönüş ve sonrasındaki denetimler |
| [`sql/SqlDeployment.psm1`](sql/SqlDeployment.psm1) | İki betiğin ortak sqlcmd yardımcıları (her zaman şifreli bağlantı ve sertifika doğrulaması) |
