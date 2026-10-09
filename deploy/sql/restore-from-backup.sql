-- Puts the database back to the backup taken by backup-before-migration.sql (see deploy/database-rollback.md).
-- Everything written after the backup is lost: stop the IIS application pool first and export what must be kept.
--   sqlcmd -S <sunucu> -E -I -b -v DatabaseName="<veritabanı>" BackupFile="<yedek dosyası>" -i deploy/sql/restore-from-backup.sql
SET NOCOUNT ON;
USE [master];
GO

-- Disconnects remaining sessions; their open transactions are rolled back.
ALTER DATABASE [$(DatabaseName)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
GO

RESTORE DATABASE [$(DatabaseName)] FROM DISK = N'$(BackupFile)' WITH REPLACE, CHECKSUM;
GO

ALTER DATABASE [$(DatabaseName)] SET MULTI_USER;
GO
