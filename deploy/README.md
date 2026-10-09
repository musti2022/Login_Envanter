# deploy

Yayın dosyaları. IIS dosyaları (web.config, yayın yönergeleri) yayın günlerinde eklenecek.

| Dosya | İçerik |
| --- | --- |
| [`database-rollback.md`](database-rollback.md) | Veritabanı yayın adımları, yedek, geri dönüş planı ve migration bazında veri kaybı riski |
| [`sql/migrate-idempotent.sql`](sql/migrate-idempotent.sql) | Tüm migration'ların idempotent SQL betiği (`dotnet ef migrations script --idempotent` çıktısı) |
| [`sql/backup-before-migration.sql`](sql/backup-before-migration.sql) | Yayından önce `COPY_ONLY` tam yedek ve doğrulama |
| [`sql/restore-from-backup.sql`](sql/restore-from-backup.sql) | Yedekten dönüş |
