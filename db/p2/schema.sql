/* =====================================================================
   SmakoViz · Проєкт №2 · Контекст «Меню» (Menu)
   SQL Server · далі: EF Core Code First + Fluent API + міграції (Лаб. 03)

   Цей скрипт — проєкт схеми, з якої в Лаб. 03 буде початкова міграція
   (InitialCreate). Подальші зміни структури — лише через міграції EF,
   а не ручним ALTER (схема готова до версіонування).

   5 сутностей і всі типи зв'язків:
     1:1  Restaurants <-> RestaurantProfiles  (FK RestaurantId = PK профілю)
     1:N  Restaurants  -> Dishes              (FK RestaurantId на боці «багатьох»)
     M:N  Dishes      <-> Categories через DishCategories
          (проміжна СУТНІСТЬ із власними полями AddedAt і IsFeatured)

   Власник даних: назва й ціна страви, назва ресторану живуть ТУТ.
   Інші контексти (Доставка, Відгуки) мають лише копії/знімки — див. README.
   ===================================================================== */

IF DB_ID(N'SmakoViz_Menu') IS NULL
    CREATE DATABASE SmakoViz_Menu;
GO

USE SmakoViz_Menu;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів і обчислюваних колонок
GO

/* ---------- Restaurants ---------- */
IF OBJECT_ID(N'dbo.Restaurants', N'U') IS NULL
CREATE TABLE dbo.Restaurants (
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Restaurants PRIMARY KEY,
    Name       NVARCHAR(150) NOT NULL,
    Slug       NVARCHAR(80)  NOT NULL CONSTRAINT UQ_Restaurants_Slug UNIQUE,     -- для URL: /restaurants/pizza-pronto
    City       NVARCHAR(80)  NOT NULL,
    CuisineType NVARCHAR(50) NOT NULL,
    IsActive   BIT           NOT NULL CONSTRAINT DF_Restaurants_IsActive DEFAULT 1,
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_Restaurants_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy  NVARCHAR(100) NULL,
    UpdatedAt  DATETIME2(3)  NULL,
    UpdatedBy  NVARCHAR(100) NULL,
    IsDeleted  BIT           NOT NULL CONSTRAINT DF_Restaurants_IsDeleted DEFAULT 0,
    RowVersion ROWVERSION    NOT NULL,
    -- лише малі латинські літери, цифри й дефіс (бінарне порівняння, бо стандартне сортування нечутливе до регістру)
    CONSTRAINT CK_Restaurants_Slug CHECK (Slug COLLATE Latin1_General_BIN NOT LIKE '%[^a-z0-9-]%')
);
GO

/* ---------- RestaurantProfiles: 1:1 ---------- */
IF OBJECT_ID(N'dbo.RestaurantProfiles', N'U') IS NULL
CREATE TABLE dbo.RestaurantProfiles (
    RestaurantId   BIGINT        NOT NULL CONSTRAINT PK_RestaurantProfiles PRIMARY KEY
                   CONSTRAINT FK_RestaurantProfiles_Restaurants
                   REFERENCES dbo.Restaurants(Id) ON DELETE CASCADE,   -- профіль не існує без ресторану
    Address        NVARCHAR(300) NOT NULL,
    Phone          NVARCHAR(20)  NOT NULL,
    OpensAt        TIME(0)       NOT NULL,
    ClosesAt       TIME(0)       NOT NULL,
    MinOrderAmount DECIMAL(10,2) NOT NULL CONSTRAINT DF_RestaurantProfiles_MinOrder DEFAULT 0
                   CONSTRAINT CK_RestaurantProfiles_MinOrder CHECK (MinOrderAmount >= 0),
    Description    NVARCHAR(1000) NULL,
    CONSTRAINT CK_RestaurantProfiles_Hours CHECK (OpensAt <> ClosesAt)
);
GO

/* ---------- Dishes: 1:N від Restaurants ---------- */
IF OBJECT_ID(N'dbo.Dishes', N'U') IS NULL
CREATE TABLE dbo.Dishes (
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Dishes PRIMARY KEY,
    -- без каскаду: ресторан «видаляється» м'яко (IsDeleted), а страви потрібні для історії
    RestaurantId BIGINT        NOT NULL
                 CONSTRAINT FK_Dishes_Restaurants REFERENCES dbo.Restaurants(Id),
    Name         NVARCHAR(150) NOT NULL,
    Description  NVARCHAR(500) NULL,
    Price        DECIMAL(10,2) NOT NULL CONSTRAINT CK_Dishes_Price CHECK (Price > 0),
    WeightGrams  INT           NULL CONSTRAINT CK_Dishes_Weight CHECK (WeightGrams IS NULL OR WeightGrams > 0),
    Calories     INT           NULL CONSTRAINT CK_Dishes_Calories CHECK (Calories IS NULL OR Calories BETWEEN 0 AND 5000),
    IsAvailable  BIT           NOT NULL CONSTRAINT DF_Dishes_IsAvailable DEFAULT 1,
    CreatedAt    DATETIME2(3)  NOT NULL CONSTRAINT DF_Dishes_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy    NVARCHAR(100) NULL,
    UpdatedAt    DATETIME2(3)  NULL,
    UpdatedBy    NVARCHAR(100) NULL,
    IsDeleted    BIT           NOT NULL CONSTRAINT DF_Dishes_IsDeleted DEFAULT 0,
    RowVersion   ROWVERSION    NOT NULL,     -- менеджер ресторану й адмін можуть редагувати ціну одночасно
    -- в одному ресторані не може бути двох страв з однаковою назвою
    CONSTRAINT UQ_Dishes_Restaurant_Name UNIQUE (RestaurantId, Name)
);
GO

/* ---------- Categories: довідник ---------- */
IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
CREATE TABLE dbo.Categories (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,  -- INT: довідник маленький
    Name      NVARCHAR(80) NOT NULL CONSTRAINT UQ_Categories_Name UNIQUE,
    Slug      NVARCHAR(80) NOT NULL CONSTRAINT UQ_Categories_Slug UNIQUE,
    SortOrder INT          NOT NULL CONSTRAINT DF_Categories_SortOrder DEFAULT 0
              CONSTRAINT CK_Categories_SortOrder CHECK (SortOrder >= 0),
    CONSTRAINT CK_Categories_Slug CHECK (Slug COLLATE Latin1_General_BIN NOT LIKE '%[^a-z0-9-]%')
);
GO

/* ---------- DishCategories: M:N з власними полями ----------
   AddedAt    — коли страву додали в категорію (напр. «Хіт продажу» з 01.10)
   IsFeatured — показувати страву першою в цій категорії                    */
IF OBJECT_ID(N'dbo.DishCategories', N'U') IS NULL
CREATE TABLE dbo.DishCategories (
    DishId     BIGINT       NOT NULL
               CONSTRAINT FK_DishCategories_Dishes REFERENCES dbo.Dishes(Id) ON DELETE CASCADE,
    CategoryId INT          NOT NULL
               CONSTRAINT FK_DishCategories_Categories REFERENCES dbo.Categories(Id) ON DELETE CASCADE,
    AddedAt    DATETIME2(3) NOT NULL CONSTRAINT DF_DishCategories_AddedAt DEFAULT SYSUTCDATETIME(),
    IsFeatured BIT          NOT NULL CONSTRAINT DF_DishCategories_IsFeatured DEFAULT 0,
    CONSTRAINT PK_DishCategories PRIMARY KEY (DishId, CategoryId)
);
GO

/* =====================================================================
   ІНДЕКСИ під конкретні запити
   (UNIQUE вище: UQ_Restaurants_Slug — сторінка ресторану за slug;
    UQ_Dishes_Restaurant_Name — заборона дублів; UQ_Categories_Slug — сторінка категорії)
   ===================================================================== */

-- Запит: «Меню ресторану» — доступні страви ресторану, впорядковані за назвою.
--   SELECT Name, Price FROM Dishes WHERE RestaurantId=@r AND IsAvailable=1 AND IsDeleted=0 ORDER BY Name
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Dishes_Restaurant_Menu')
CREATE INDEX IX_Dishes_Restaurant_Menu
    ON dbo.Dishes (RestaurantId, Name)
    INCLUDE (Price, WeightGrams)
    WHERE IsDeleted = 0 AND IsAvailable = 1;
GO

-- Запит: «Пошук страви за початком назви» по всіх ресторанах: WHERE Name LIKE N'Піца%'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Dishes_Name')
CREATE INDEX IX_Dishes_Name
    ON dbo.Dishes (Name)
    INCLUDE (RestaurantId, Price)
    WHERE IsDeleted = 0;
GO

-- Запит: «Страви категорії» (PK починається з DishId, тож для пошуку за CategoryId потрібен окремий індекс);
-- спершу IsFeatured, потім нові.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DishCategories_Category')
CREATE INDEX IX_DishCategories_Category
    ON dbo.DishCategories (CategoryId, IsFeatured DESC, AddedAt DESC);
GO

-- Запит: «Ресторани міста» — активні ресторани в місті, за назвою.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Restaurants_City')
CREATE INDEX IX_Restaurants_City
    ON dbo.Restaurants (City, Name)
    INCLUDE (Slug, CuisineType)
    WHERE IsDeleted = 0 AND IsActive = 1;
GO

PRINT N'SmakoViz_Menu: schema OK';
GO
