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
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [ApplicationUsers] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [Email] nvarchar(320) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [EmailVerified] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAt] datetimeoffset NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_ApplicationUsers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [Devices] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DeviceId] nvarchar(100) NOT NULL,
        [SerialNumber] nvarchar(100) NOT NULL,
        [DeviceApiKeyHash] nvarchar(500) NOT NULL,
        [OwnerUserId] uniqueidentifier NULL,
        [LastSeenAt] datetimeoffset NULL,
        [ScheduleVersion] int NOT NULL DEFAULT 0,
        CONSTRAINT [PK_Devices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Devices_ApplicationUsers_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [PrescriptionImages] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [OwnerUserId] uniqueidentifier NOT NULL,
        [StorageKey] nvarchar(500) NOT NULL,
        [OriginalFileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [SizeBytes] int NOT NULL,
        [UploadedAt] datetimeoffset NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_PrescriptionImages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PrescriptionImages_ApplicationUsers_OwnerUserId] FOREIGN KEY ([OwnerUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ActorUserId] uniqueidentifier NULL,
        [DeviceId] uniqueidentifier NULL,
        [Action] nvarchar(100) NOT NULL,
        [EntityType] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(100) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [Details] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuditLogs_ApplicationUsers_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AuditLogs_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [CareRelationships] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DeviceId] uniqueidentifier NOT NULL,
        [RelativeUserId] uniqueidentifier NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AcceptedAt] datetimeoffset NOT NULL,
        [RevokedAt] datetimeoffset NULL,
        CONSTRAINT [PK_CareRelationships] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CareRelationships_Status] CHECK ([Status] IN (N'Active', N'Revoked')),
        CONSTRAINT [FK_CareRelationships_ApplicationUsers_RelativeUserId] FOREIGN KEY ([RelativeUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CareRelationships_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [Compartments] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DeviceId] uniqueidentifier NOT NULL,
        [SlotNumber] int NOT NULL,
        [Name] nvarchar(30) NOT NULL,
        [IsEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
        CONSTRAINT [PK_Compartments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Compartments_SlotNumber] CHECK ([SlotNumber] BETWEEN 1 AND 3),
        CONSTRAINT [FK_Compartments_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [DevicePairingCodes] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DeviceId] uniqueidentifier NOT NULL,
        [CodeHash] nvarchar(500) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        [UsedAt] datetimeoffset NULL,
        [UsedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_DevicePairingCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DevicePairingCodes_ApplicationUsers_UsedByUserId] FOREIGN KEY ([UsedByUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DevicePairingCodes_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [RelativeInvitations] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DeviceId] uniqueidentifier NOT NULL,
        [InviteeEmail] nvarchar(320) NOT NULL,
        [TokenHash] nvarchar(500) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ExpiresAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_RelativeInvitations] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RelativeInvitations_Status] CHECK ([Status] IN (N'Pending', N'Accepted', N'Revoked', N'Expired')),
        CONSTRAINT [FK_RelativeInvitations_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [MedicationSchedules] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DeviceId] uniqueidentifier NOT NULL,
        [CompartmentId] uniqueidentifier NOT NULL,
        [TimeOfDay] time(0) NOT NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NULL,
        [GracePeriodMinutes] int NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_MedicationSchedules] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MedicationSchedules_Grace] CHECK ([GracePeriodMinutes] BETWEEN 1 AND 120),
        CONSTRAINT [FK_MedicationSchedules_Compartments_CompartmentId] FOREIGN KEY ([CompartmentId]) REFERENCES [Compartments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_MedicationSchedules_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [DoseInstances] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ScheduleId] uniqueidentifier NOT NULL,
        [DeviceId] uniqueidentifier NOT NULL,
        [CompartmentId] uniqueidentifier NOT NULL,
        [ScheduledAt] datetimeoffset NOT NULL,
        [GracePeriodMinutes] int NOT NULL,
        [DeadlineAt] datetimeoffset NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ConfirmedAt] datetimeoffset NULL,
        CONSTRAINT [PK_DoseInstances] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DoseInstances_Grace] CHECK ([GracePeriodMinutes] BETWEEN 1 AND 120),
        CONSTRAINT [CK_DoseInstances_Status] CHECK ([Status] IN (N'Pending', N'Confirmed', N'Overdue', N'Unknown')),
        CONSTRAINT [FK_DoseInstances_Compartments_CompartmentId] FOREIGN KEY ([CompartmentId]) REFERENCES [Compartments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoseInstances_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DoseInstances_MedicationSchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [MedicationSchedules] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [ScheduleDays] (
        [ScheduleId] uniqueidentifier NOT NULL,
        [DayOfWeek] int NOT NULL,
        CONSTRAINT [PK_ScheduleDays] PRIMARY KEY ([ScheduleId], [DayOfWeek]),
        CONSTRAINT [CK_ScheduleDays_DayOfWeek] CHECK ([DayOfWeek] BETWEEN 1 AND 7),
        CONSTRAINT [FK_ScheduleDays_MedicationSchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [MedicationSchedules] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [ScheduleItems] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ScheduleId] uniqueidentifier NOT NULL,
        [MedicineName] nvarchar(200) NOT NULL,
        [Quantity] decimal(10,2) NOT NULL,
        [Unit] nvarchar(50) NOT NULL,
        [Instructions] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_ScheduleItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ScheduleItems_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_ScheduleItems_MedicationSchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [MedicationSchedules] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [Alerts] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DoseInstanceId] uniqueidentifier NOT NULL,
        [AlertType] nvarchar(40) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [ResolvedAt] datetimeoffset NULL,
        CONSTRAINT [PK_Alerts] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Alerts_AlertType] CHECK ([AlertType] IN (N'OverdueUnconfirmed', N'UnconfirmedOffline')),
        CONSTRAINT [FK_Alerts_DoseInstances_DoseInstanceId] FOREIGN KEY ([DoseInstanceId]) REFERENCES [DoseInstances] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [DeviceEvents] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DeviceId] uniqueidentifier NOT NULL,
        [EventId] nvarchar(100) NOT NULL,
        [CompartmentId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(40) NOT NULL,
        [OccurredAt] datetimeoffset NOT NULL,
        [ReceivedAt] datetimeoffset NOT NULL,
        [MatchedDoseInstanceId] uniqueidentifier NULL,
        CONSTRAINT [PK_DeviceEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DeviceEvents_EventType] CHECK ([EventType] IN (N'LidOpened', N'LidClosed')),
        CONSTRAINT [FK_DeviceEvents_Compartments_CompartmentId] FOREIGN KEY ([CompartmentId]) REFERENCES [Compartments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DeviceEvents_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Devices] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DeviceEvents_DoseInstances_MatchedDoseInstanceId] FOREIGN KEY ([MatchedDoseInstanceId]) REFERENCES [DoseInstances] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [DoseItemSnapshots] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [DoseInstanceId] uniqueidentifier NOT NULL,
        [MedicineName] nvarchar(200) NOT NULL,
        [Quantity] decimal(10,2) NOT NULL,
        [Unit] nvarchar(50) NOT NULL,
        [Instructions] nvarchar(500) NOT NULL,
        CONSTRAINT [PK_DoseItemSnapshots] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_DoseItemSnapshots_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_DoseItemSnapshots_DoseInstances_DoseInstanceId] FOREIGN KEY ([DoseInstanceId]) REFERENCES [DoseInstances] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [AlertRecipients] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [AlertId] uniqueidentifier NOT NULL,
        [RecipientUserId] uniqueidentifier NOT NULL,
        [Channel] nvarchar(20) NOT NULL,
        [ReadAt] datetimeoffset NULL,
        CONSTRAINT [PK_AlertRecipients] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_AlertRecipients_Channel] CHECK ([Channel] IN (N'Email')),
        CONSTRAINT [FK_AlertRecipients_Alerts_AlertId] FOREIGN KEY ([AlertId]) REFERENCES [Alerts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AlertRecipients_ApplicationUsers_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [ApplicationUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE TABLE [EmailDeliveries] (
        [Id] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [AlertRecipientId] uniqueidentifier NOT NULL,
        [AttemptNumber] int NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [NextAttemptAt] datetimeoffset NULL,
        [SentAt] datetimeoffset NULL,
        [ProviderMessageId] nvarchar(200) NULL,
        [LockedUntil] datetimeoffset NULL,
        CONSTRAINT [PK_EmailDeliveries] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EmailDeliveries_Status] CHECK ([Status] IN (N'Pending', N'Sent', N'Failed', N'Exhausted')),
        CONSTRAINT [FK_EmailDeliveries_AlertRecipients_AlertRecipientId] FOREIGN KEY ([AlertRecipientId]) REFERENCES [AlertRecipients] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AlertRecipients_AlertId_RecipientUserId_Channel] ON [AlertRecipients] ([AlertId], [RecipientUserId], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AlertRecipients_RecipientUserId] ON [AlertRecipients] ([RecipientUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Alerts_DoseInstanceId_AlertType] ON [Alerts] ([DoseInstanceId], [AlertType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApplicationUsers_Email] ON [ApplicationUsers] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_ActorUserId] ON [AuditLogs] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_DeviceId] ON [AuditLogs] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CareRelationships_DeviceId_RelativeUserId] ON [CareRelationships] ([DeviceId], [RelativeUserId]) WHERE [Status] = N''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_CareRelationships_RelativeUserId] ON [CareRelationships] ([RelativeUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Compartments_DeviceId_SlotNumber] ON [Compartments] ([DeviceId], [SlotNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DeviceEvents_CompartmentId] ON [DeviceEvents] ([CompartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DeviceEvents_DeviceId_EventId] ON [DeviceEvents] ([DeviceId], [EventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DeviceEvents_DeviceId_OccurredAt] ON [DeviceEvents] ([DeviceId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DeviceEvents_MatchedDoseInstanceId] ON [DeviceEvents] ([MatchedDoseInstanceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DevicePairingCodes_CodeHash] ON [DevicePairingCodes] ([CodeHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DevicePairingCodes_DeviceId] ON [DevicePairingCodes] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DevicePairingCodes_UsedByUserId] ON [DevicePairingCodes] ([UsedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Devices_DeviceId] ON [Devices] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Devices_OwnerUserId] ON [Devices] ([OwnerUserId]) WHERE [OwnerUserId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Devices_SerialNumber] ON [Devices] ([SerialNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DoseInstances_CompartmentId] ON [DoseInstances] ([CompartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DoseInstances_DeviceId] ON [DoseInstances] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DoseInstances_ScheduleId_ScheduledAt] ON [DoseInstances] ([ScheduleId], [ScheduledAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DoseInstances_Status_DeadlineAt] ON [DoseInstances] ([Status], [DeadlineAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_DoseItemSnapshots_DoseInstanceId] ON [DoseItemSnapshots] ([DoseInstanceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmailDeliveries_AlertRecipientId_AttemptNumber] ON [EmailDeliveries] ([AlertRecipientId], [AttemptNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_EmailDeliveries_Status_NextAttemptAt] ON [EmailDeliveries] ([Status], [NextAttemptAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_MedicationSchedules_CompartmentId] ON [MedicationSchedules] ([CompartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_MedicationSchedules_DeviceId_IsDeleted] ON [MedicationSchedules] ([DeviceId], [IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_PrescriptionImages_OwnerUserId] ON [PrescriptionImages] ([OwnerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RelativeInvitations_DeviceId_InviteeEmail] ON [RelativeInvitations] ([DeviceId], [InviteeEmail]) WHERE [Status] = N''Pending''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    CREATE INDEX [IX_ScheduleItems_ScheduleId] ON [ScheduleItems] ([ScheduleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029070908_InitialSchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029070908_InitialSchema', N'10.0.11');
END;

COMMIT;
GO

