-- Puts the database back to the backup taken by backup-before-migration.sql (see deploy/database-rollback.md).
-- Everything written after the backup is lost: stop the IIS application pool first and export what must be kept.
--   sqlcmd -S <sunucu> -E -I -b -v DatabaseName="<veritabanı>" BackupFile="<yedek dosyası>" -i deploy/sql/restore-from-backup.sql
SET NOCOUNT ON;
USE [master];
GO

-- Before anyone is disconnected: the file must be a backup of this database taken on this server (backup history in
-- msdb) and must read back with valid checksums. A missing, damaged or wrong file stops the script here.
IF NOT EXISTS (
    SELECT 1
    FROM msdb.dbo.backupset b
    JOIN msdb.dbo.backupmediafamily m ON m.media_set_id = b.media_set_id
    WHERE m.physical_device_name = N'$(BackupFile)' AND b.database_name = N'$(DatabaseName)' AND b.type = 'D')
    THROW 50000, N'Bu dosya bu sunucuda bu veritabanının tam yedeği olarak kayıtlı değil; geri yükleme yapılmadı.', 1;
GO

RESTORE VERIFYONLY FROM DISK = N'$(BackupFile)' WITH CHECKSUM;
GO

-- Disconnects remaining sessions; their open transactions are rolled back.
ALTER DATABASE [$(DatabaseName)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
GO

RESTORE DATABASE [$(DatabaseName)] FROM DISK = N'$(BackupFile)' WITH REPLACE, CHECKSUM;
GO

ALTER DATABASE [$(DatabaseName)] SET MULTI_USER;
GO
