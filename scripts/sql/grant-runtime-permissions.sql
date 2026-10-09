-- EnterpriseInventory: uygulamanın çalışma (runtime) hesabına en az yetkiyi verir.
--
-- Bir DBA tarafından, migration uygulandıktan sonra uygulama veritabanında çalıştırılır. Login sunucuda önceden
-- oluşturulmuş olmalıdır (ör. IIS uygulama havuzunun gMSA hesabı). Betik tekrar çalıştırılabilir.
--
--   sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -v RuntimeUser="<DOMAIN\hesap>" -i scripts/sql/grant-runtime-permissions.sql
--
-- Runtime hesabı şema değiştiremez ve kayıt silemez: varlıklar arşivlenir (soft delete), zimmet geçmişi ve
-- tanımlar silinmez, audit kayıtları yalnızca eklenir. Gerçek hesap adları repoya yazılmaz.

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

IF DATABASE_PRINCIPAL_ID(N'ei_app_runtime') IS NULL
    CREATE ROLE [ei_app_runtime];

IF DATABASE_PRINCIPAL_ID(N'$(RuntimeUser)') IS NULL
    CREATE USER [$(RuntimeUser)] FOR LOGIN [$(RuntimeUser)];

IF IS_ROLEMEMBER(N'ei_app_runtime', N'$(RuntimeUser)') = 0
    ALTER ROLE [ei_app_runtime] ADD MEMBER [$(RuntimeUser)];

GRANT SELECT, INSERT, UPDATE ON SCHEMA::[dbo] TO [ei_app_runtime];

-- Açık DENY, hesap ileride geniş bir role (ör. db_datawriter) eklense bile geçerli kalır.
DENY UPDATE, DELETE ON OBJECT::[dbo].[AuditLogs] TO [ei_app_runtime];
DENY INSERT, UPDATE, DELETE ON OBJECT::[dbo].[__EFMigrationsHistory] TO [ei_app_runtime];

COMMIT TRANSACTION;
