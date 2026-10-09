-- Full backup right before a schema update (see deploy/database-rollback.md). Run with sqlcmd, for example:
--   sqlcmd -S <sunucu> -E -I -b -v DatabaseName="<veritabanı>" BackupFile="<yedek klasörü>\<veritabanı>_<tarih>.bak" -i deploy/sql/backup-before-migration.sql
-- COPY_ONLY keeps the company's regular backup chain (differential backups) intact. The file is written by the
-- SQL Server service account, so the folder must be writable for it and is a path on the SQL Server machine.
SET NOCOUNT ON;

BACKUP DATABASE [$(DatabaseName)]
    TO DISK = N'$(BackupFile)'
    WITH COPY_ONLY, CHECKSUM, INIT, NAME = N'$(DatabaseName) - before migration';
GO

-- Reads the whole file back and checks its checksums; a damaged backup fails here, before the migration runs.
RESTORE VERIFYONLY FROM DISK = N'$(BackupFile)' WITH CHECKSUM;
GO
