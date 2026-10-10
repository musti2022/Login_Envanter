-- Checks, as the application's runtime user, that it has exactly the rights it needs (see docs/database.md): read, add
-- and change rows; no deletes and no schema changes; audit records only added; the migration history only read.
-- Run by the DBA after grant-runtime-permissions.sql; with -b sqlcmd exits with 1 if any right differs. Changes nothing.
--   sqlcmd -S <sunucu> -d <veritabanı> -E -I -b -v RuntimeUser="<DOMAIN\hesap>" -i deploy/sql/verify-runtime-permissions.sql
SET NOCOUNT ON;

-- The tables are listed before impersonating: the runtime user would not see a table it has no right on.
DECLARE @tables TABLE (SchemaName sysname NOT NULL, TableName sysname NOT NULL);
INSERT INTO @tables (SchemaName, TableName)
SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id;

DECLARE @checks TABLE (Name nvarchar(300) NOT NULL, Expected bit NOT NULL, Actual bit NULL);

EXECUTE AS USER = N'$(RuntimeUser)';

INSERT INTO @checks (Name, Expected, Actual)
VALUES
    (N'DATABASE CONTROL', 0, HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CONTROL')),
    (N'DATABASE ALTER', 0, HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER')),
    (N'DATABASE CREATE TABLE', 0, HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE')),
    (N'DATABASE CREATE PROCEDURE', 0, HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE PROCEDURE')),
    (N'DATABASE ALTER ANY SCHEMA', 0, HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY SCHEMA')),
    (N'DATABASE ALTER ANY USER', 0, HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY USER')),
    (N'DATABASE ALTER ANY ROLE', 0, HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY ROLE')),
    (N'SCHEMA dbo ALTER', 0, HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'ALTER'));

INSERT INTO @checks (Name, Expected, Actual)
SELECT
    CONCAT(t.SchemaName, N'.', t.TableName, N' ', p.Permission),
    CASE
        WHEN t.SchemaName <> N'dbo' THEN 0
        WHEN t.TableName = N'__EFMigrationsHistory' THEN IIF(p.Permission = N'SELECT', 1, 0)
        WHEN t.TableName = N'AuditLogs' THEN IIF(p.Permission IN (N'SELECT', N'INSERT'), 1, 0)
        ELSE IIF(p.Permission IN (N'SELECT', N'INSERT', N'UPDATE'), 1, 0)
    END,
    HAS_PERMS_BY_NAME(QUOTENAME(t.SchemaName) + N'.' + QUOTENAME(t.TableName), N'OBJECT', p.Permission)
FROM @tables t
CROSS JOIN (VALUES (N'SELECT'), (N'INSERT'), (N'UPDATE'), (N'DELETE'), (N'ALTER')) p (Permission);

REVERT;

SELECT Name, Expected, Actual, IIF(Expected = Actual, N'PASS', N'HATA') AS Result
FROM @checks
ORDER BY IIF(Expected = Actual, 1, 0), Name;

IF EXISTS (SELECT 1 FROM @checks WHERE Actual IS NULL OR Actual <> Expected)
    THROW 50000, N'Runtime hesabının yetkileri beklenenden farklı; HATA satırlarına bakın.', 1;

IF NOT EXISTS (SELECT 1 FROM @tables WHERE SchemaName = N'dbo' AND TableName = N'AuditLogs')
    THROW 50000, N'Şema yok veya eksik; önce migrate-idempotent.sql çalıştırılmalı.', 1;

PRINT N'Runtime hesabının yetkileri doğru.';
GO
