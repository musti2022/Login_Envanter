IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [AdminUsers] (
        [Id] int NOT NULL IDENTITY,
        [ObjectGuid] uniqueidentifier NOT NULL,
        [SamAccountName] nvarchar(20) NOT NULL,
        [DisplayName] nvarchar(256) NOT NULL,
        [FirstLoginAt] datetimeoffset NOT NULL,
        [LastLoginAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_AdminUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [EntityName] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(64) NOT NULL,
        [Action] int NOT NULL,
        [OldValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NULL,
        [UserName] nvarchar(256) NOT NULL,
        [Timestamp] datetimeoffset NOT NULL,
        [CorrelationId] nvarchar(64) NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AuditLogs_Action] CHECK ([Action] IN (1, 2, 3, 4, 5, 6, 7)),
        CONSTRAINT [CK_AuditLogs_HasValues] CHECK ([OldValues] IS NOT NULL OR [NewValues] IS NOT NULL)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [Brands] (
        [Id] int NOT NULL IDENTITY,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(256) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Brands] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [Cities] (
        [Id] int NOT NULL IDENTITY,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(256) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Cities] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [Departments] (
        [Id] int NOT NULL IDENTITY,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(256) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [Employees] (
        [Id] int NOT NULL IDENTITY,
        [ObjectGuid] uniqueidentifier NOT NULL,
        [SamAccountName] nvarchar(20) NOT NULL,
        [DisplayName] nvarchar(256) NOT NULL,
        [Email] nvarchar(256) NULL,
        [Department] nvarchar(128) NULL,
        [Title] nvarchar(128) NULL,
        [IsActive] bit NOT NULL,
        [LastSyncedAt] datetimeoffset NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(256) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Employees] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [AssetModels] (
        [Id] int NOT NULL IDENTITY,
        [BrandId] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(256) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_AssetModels] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_AssetModels_Id_BrandId] UNIQUE ([Id], [BrandId]),
        CONSTRAINT [FK_AssetModels_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [Locations] (
        [Id] int NOT NULL IDENTITY,
        [CityId] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(256) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Locations] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Locations_Id_CityId] UNIQUE ([Id], [CityId]),
        CONSTRAINT [FK_Locations_Cities_CityId] FOREIGN KEY ([CityId]) REFERENCES [Cities] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [Assets] (
        [Id] int NOT NULL IDENTITY,
        [AssetCode] nvarchar(50) NOT NULL,
        [ComputerName] nvarchar(64) NULL,
        [BrandId] int NOT NULL,
        [ModelId] int NOT NULL,
        [SerialNumber] nvarchar(100) NULL,
        [AssetType] int NOT NULL,
        [Status] int NOT NULL,
        [Description] nvarchar(1000) NULL,
        [CityId] int NOT NULL,
        [DepartmentId] int NOT NULL,
        [LocationId] int NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedBy] nvarchar(256) NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UpdatedBy] nvarchar(256) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Assets] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Assets_ArchivedNotAssigned] CHECK (NOT ([IsDeleted] = 1 AND [Status] = 2)),
        CONSTRAINT [CK_Assets_AssetCode_NotBlank] CHECK (LEN([AssetCode]) > 0),
        CONSTRAINT [CK_Assets_AssetType] CHECK ([AssetType] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 99)),
        CONSTRAINT [CK_Assets_SerialNumber_NotBlank] CHECK ([SerialNumber] IS NULL OR LEN([SerialNumber]) > 0),
        CONSTRAINT [CK_Assets_Status] CHECK ([Status] IN (1, 2, 3, 4)),
        CONSTRAINT [FK_Assets_AssetModels_ModelId_BrandId] FOREIGN KEY ([ModelId], [BrandId]) REFERENCES [AssetModels] ([Id], [BrandId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Assets_Brands_BrandId] FOREIGN KEY ([BrandId]) REFERENCES [Brands] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Assets_Cities_CityId] FOREIGN KEY ([CityId]) REFERENCES [Cities] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Assets_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Assets_Locations_LocationId_CityId] FOREIGN KEY ([LocationId], [CityId]) REFERENCES [Locations] ([Id], [CityId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE TABLE [AssetAssignments] (
        [Id] int NOT NULL IDENTITY,
        [AssetId] int NOT NULL,
        [EmployeeId] int NOT NULL,
        [AssignmentDescription] nvarchar(500) NULL,
        [Notes] nvarchar(1000) NULL,
        [AssignedAt] datetimeoffset NOT NULL,
        [AssignedBy] nvarchar(256) NOT NULL,
        [ReturnedAt] datetimeoffset NULL,
        [ReturnedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_AssetAssignments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AssetAssignments_ReturnAfterAssign] CHECK ([ReturnedAt] IS NULL OR [ReturnedAt] >= [AssignedAt]),
        CONSTRAINT [CK_AssetAssignments_ReturnedByWithReturn] CHECK (([ReturnedAt] IS NULL AND [ReturnedBy] IS NULL) OR ([ReturnedAt] IS NOT NULL AND [ReturnedBy] IS NOT NULL)),
        CONSTRAINT [FK_AssetAssignments_Assets_AssetId] FOREIGN KEY ([AssetId]) REFERENCES [Assets] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AssetAssignments_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AdminUsers_ObjectGuid] ON [AdminUsers] ([ObjectGuid]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssetAssignments_AssetId_AssignedAt] ON [AssetAssignments] ([AssetId], [AssignedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AssetAssignments_EmployeeId] ON [AssetAssignments] ([EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_AssetAssignments_AssetId_Active] ON [AssetAssignments] ([AssetId]) WHERE [ReturnedAt] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AssetModels_BrandId_Name] ON [AssetModels] ([BrandId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Assets_AssetCode] ON [Assets] ([AssetCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assets_BrandId] ON [Assets] ([BrandId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assets_CityId] ON [Assets] ([CityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assets_DepartmentId] ON [Assets] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assets_IsDeleted_Status] ON [Assets] ([IsDeleted], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assets_LocationId_CityId] ON [Assets] ([LocationId], [CityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Assets_ModelId_BrandId] ON [Assets] ([ModelId], [BrandId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Assets_SerialNumber] ON [Assets] ([SerialNumber]) WHERE [SerialNumber] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_CorrelationId] ON [AuditLogs] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityName_EntityId] ON [AuditLogs] ([EntityName], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_Timestamp] ON [AuditLogs] ([Timestamp]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Brands_Name] ON [Brands] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Cities_Name] ON [Cities] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Employees_DisplayName] ON [Employees] ([DisplayName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Employees_ObjectGuid] ON [Employees] ([ObjectGuid]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Employees_SamAccountName] ON [Employees] ([SamAccountName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Locations_CityId_Name] ON [Locations] ([CityId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009075751_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009075751_InitialCreate', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009113648_AddSignInAuditActions'
)
BEGIN
    ALTER TABLE [AuditLogs] DROP CONSTRAINT [CK_AuditLogs_Action];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009113648_AddSignInAuditActions'
)
BEGIN
    EXEC(N'ALTER TABLE [AuditLogs] ADD CONSTRAINT [CK_AuditLogs_Action] CHECK ([Action] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009113648_AddSignInAuditActions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009113648_AddSignInAuditActions', N'10.0.12');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009125637_AddUserSessions'
)
BEGIN
    CREATE TABLE [UserSessions] (
        [Id] int NOT NULL IDENTITY,
        [AdminUserId] int NOT NULL,
        [KeyHash] binary(32) NOT NULL,
        [StartedAt] datetimeoffset NOT NULL,
        [LastSeenAt] datetimeoffset NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [LastAccessCheckAt] datetimeoffset NOT NULL,
        [LastFailedAccessCheckAt] datetimeoffset NULL,
        [ClientAddress] nvarchar(45) NULL,
        [EndedAt] datetimeoffset NULL,
        [EndReason] int NULL,
        CONSTRAINT [PK_UserSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_UserSessions_Ended] CHECK (([EndedAt] IS NULL AND [EndReason] IS NULL) OR ([EndedAt] IS NOT NULL AND [EndReason] IS NOT NULL)),
        CONSTRAINT [CK_UserSessions_EndReason] CHECK ([EndReason] IS NULL OR [EndReason] IN (1, 2, 3, 4, 5, 6)),
        CONSTRAINT [CK_UserSessions_Times] CHECK ([LastSeenAt] >= [StartedAt] AND [ExpiresAt] > [StartedAt]),
        CONSTRAINT [FK_UserSessions_AdminUsers_AdminUserId] FOREIGN KEY ([AdminUserId]) REFERENCES [AdminUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009125637_AddUserSessions'
)
BEGIN
    CREATE INDEX [IX_UserSessions_AdminUserId] ON [UserSessions] ([AdminUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009125637_AddUserSessions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserSessions_KeyHash] ON [UserSessions] ([KeyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009125637_AddUserSessions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009125637_AddUserSessions', N'10.0.12');
END;

COMMIT;
GO

