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
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(80) NOT NULL,
        [Slug] nvarchar(80) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Categories_Slug] CHECK ([Slug] COLLATE Latin1_General_BIN NOT LIKE '%[^a-z0-9-]%'),
        CONSTRAINT [CK_Categories_SortOrder] CHECK ([SortOrder] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE TABLE [Restaurants] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Slug] nvarchar(80) NOT NULL,
        [City] nvarchar(80) NOT NULL,
        [CuisineType] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Restaurants] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Restaurants_Slug] CHECK ([Slug] COLLATE Latin1_General_BIN NOT LIKE '%[^a-z0-9-]%')
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE TABLE [Dishes] (
        [Id] bigint NOT NULL IDENTITY,
        [RestaurantId] bigint NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Price] decimal(18,2) NOT NULL,
        [WeightGrams] int NULL,
        [Calories] int NULL,
        [IsAvailable] bit NOT NULL,
        [AvailablePortions] int NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Dishes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Dishes_AvailablePortions] CHECK ([AvailablePortions] >= 0),
        CONSTRAINT [CK_Dishes_Calories] CHECK ([Calories] IS NULL OR [Calories] BETWEEN 0 AND 5000),
        CONSTRAINT [CK_Dishes_Price] CHECK ([Price] > 0),
        CONSTRAINT [CK_Dishes_Weight] CHECK ([WeightGrams] IS NULL OR [WeightGrams] > 0),
        CONSTRAINT [FK_Dishes_Restaurants_RestaurantId] FOREIGN KEY ([RestaurantId]) REFERENCES [Restaurants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE TABLE [RestaurantProfiles] (
        [RestaurantId] bigint NOT NULL,
        [Address] nvarchar(300) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [OpensAt] time(0) NOT NULL,
        [ClosesAt] time(0) NOT NULL,
        [MinOrderAmount] decimal(18,2) NOT NULL,
        [Description] nvarchar(1000) NULL,
        CONSTRAINT [PK_RestaurantProfiles] PRIMARY KEY ([RestaurantId]),
        CONSTRAINT [CK_RestaurantProfiles_Hours] CHECK ([OpensAt] <> [ClosesAt]),
        CONSTRAINT [CK_RestaurantProfiles_MinOrder] CHECK ([MinOrderAmount] >= 0),
        CONSTRAINT [FK_RestaurantProfiles_Restaurants_RestaurantId] FOREIGN KEY ([RestaurantId]) REFERENCES [Restaurants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE TABLE [DishCategories] (
        [DishId] bigint NOT NULL,
        [CategoryId] int NOT NULL,
        [AddedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [IsFeatured] bit NOT NULL,
        CONSTRAINT [PK_DishCategories] PRIMARY KEY ([DishId], [CategoryId]),
        CONSTRAINT [FK_DishCategories_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DishCategories_Dishes_DishId] FOREIGN KEY ([DishId]) REFERENCES [Dishes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug', N'SortOrder') AND [object_id] = OBJECT_ID(N'[Categories]'))
        SET IDENTITY_INSERT [Categories] ON;
    EXEC(N'INSERT INTO [Categories] ([Id], [Name], [Slug], [SortOrder])
    VALUES (1, N''Перші страви'', N''soups'', 10),
    (2, N''Основні страви'', N''mains'', 20),
    (3, N''Піца'', N''pizza'', 30),
    (4, N''Суші та роли'', N''sushi'', 40),
    (5, N''Напої'', N''drinks'', 50),
    (6, N''Вегетаріанське'', N''vegetarian'', 60),
    (7, N''Хіт продажу'', N''bestseller'', 0)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug', N'SortOrder') AND [object_id] = OBJECT_ID(N'[Categories]'))
        SET IDENTITY_INSERT [Categories] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Name] ON [Categories] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Slug] ON [Categories] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DishCategories_CategoryId_IsFeatured_AddedAt] ON [DishCategories] ([CategoryId], [IsFeatured], [AddedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Dishes_Name] ON [Dishes] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Dishes_Price] ON [Dishes] ([Price]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Dishes_RestaurantId_Name] ON [Dishes] ([RestaurantId], [Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Restaurants_City_Name] ON [Restaurants] ([City], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Restaurants_Slug] ON [Restaurants] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123212622_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123212622_InitialCreate', N'9.0.23');
END;

COMMIT;
GO

