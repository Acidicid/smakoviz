/* =====================================================================
   SmakoViz · Проєкт №1 · Контекст «Доставка» (DeliveryOrders)
   SQL Server · далі: ADO.NET + Dapper (Лаб. 02)

   Скрипт ідемпотентний: можна запускати повторно — існуючі об'єкти
   не перестворюються.

   Зв'язки всередині БД:
     1:N  Customers      -> DeliveryOrders      (FK CustomerId на боці «багатьох»)
     1:1  DeliveryOrders <-> DeliveryDetails    (FK OrderId = PK залежної таблиці)
     M:N  DeliveryOrders <-> Dishes через OrderLines (проміжна таблиця з двома FK)
     1:N  DeliveryOrders -> OrderStatusHistory

   Посилання на ІНШІ бази — без FOREIGN KEY:
     DeliveryOrders.RestaurantId -> Restaurants.Id у БД Меню (SmakoViz_Menu)
     Dishes.Id                   -> Dishes.Id у БД Меню (той самий Id, локальна копія)

   Первинні ключі: BIGINT IDENTITY — записи створює лише цей сервіс
   (одне джерело вставки), ключ компактний і послідовний (менше фрагментації
   кластерного індексу, ніж у випадкового GUID). Виняток — Dishes:
   Id приходить із БД Меню, тому без IDENTITY.
   ===================================================================== */

IF DB_ID(N'SmakoViz_Delivery') IS NULL
    CREATE DATABASE SmakoViz_Delivery;
GO

USE SmakoViz_Delivery;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів і обчислюваних колонок
GO

/* ---------- Customers: клієнти сервісу доставки (власник — цей контекст) ---------- */
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
CREATE TABLE dbo.Customers (
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    Email      NVARCHAR(256) NOT NULL CONSTRAINT UQ_Customers_Email UNIQUE,
    FullName   NVARCHAR(150) NOT NULL,
    Phone      NVARCHAR(20)  NULL
               CONSTRAINT CK_Customers_Phone CHECK (Phone IS NULL OR Phone LIKE N'+380%'),
    -- аудит + м'яке видалення
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy  NVARCHAR(100) NULL,
    UpdatedAt  DATETIME2(3)  NULL,
    UpdatedBy  NVARCHAR(100) NULL,
    IsDeleted  BIT           NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,
    -- оптимістична конкурентність: клієнт може редагувати профіль з двох пристроїв
    RowVersion ROWVERSION    NOT NULL
);
GO

/* ---------- Dishes: ЛОКАЛЬНА КОПІЯ страв із БД Меню (read-model) ----------
   Власник даних — контекст «Меню». Тут лише копія для швидкої перевірки
   ціни/доступності при оформленні; оновлюється подіями з Меню (Лаб. 08).
   Цей сервіс ці поля НЕ редагує сам (немає dual write).                    */
IF OBJECT_ID(N'dbo.Dishes', N'U') IS NULL
CREATE TABLE dbo.Dishes (
    Id           BIGINT        NOT NULL CONSTRAINT PK_Dishes PRIMARY KEY,  -- той самий Id, що в SmakoViz_Menu
    RestaurantId BIGINT        NOT NULL,           -- посилання на Меню, БЕЗ FK
    Name         NVARCHAR(150) NOT NULL,
    Price        DECIMAL(10,2) NOT NULL CONSTRAINT CK_Dishes_Price CHECK (Price > 0),
    IsAvailable  BIT           NOT NULL CONSTRAINT DF_Dishes_IsAvailable DEFAULT 1,
    SyncedAt     DATETIME2(3)  NOT NULL CONSTRAINT DF_Dishes_SyncedAt DEFAULT SYSUTCDATETIME()
);
GO

/* ---------- DeliveryOrders: корінь агрегату «Замовлення доставки» ---------- */
IF OBJECT_ID(N'dbo.DeliveryOrders', N'U') IS NULL
CREATE TABLE dbo.DeliveryOrders (
    Id             BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeliveryOrders PRIMARY KEY,
    OrderNumber    NVARCHAR(20)  NOT NULL CONSTRAINT UQ_DeliveryOrders_OrderNumber UNIQUE,   -- номер для клієнта, напр. SV-000001
    -- 1:N Customers -> DeliveryOrders. БЕЗ каскаду: клієнта з історією замовлень видаляти не можна
    CustomerId     BIGINT        NOT NULL
                   CONSTRAINT FK_DeliveryOrders_Customers REFERENCES dbo.Customers(Id),
    RestaurantId   BIGINT        NOT NULL,         -- посилання на Меню, БЕЗ FK
    RestaurantName NVARCHAR(150) NOT NULL,         -- ЗНІМОК назви ресторану на момент замовлення
    Status         NVARCHAR(20)  NOT NULL CONSTRAINT DF_DeliveryOrders_Status DEFAULT N'Placed'
                   CONSTRAINT CK_DeliveryOrders_Status CHECK (Status IN
                       (N'Placed', N'Confirmed', N'Paid', N'Cooking', N'OnTheWay', N'Delivered', N'Cancelled')),
    PlacedAt       DATETIME2(3)  NOT NULL CONSTRAINT DF_DeliveryOrders_PlacedAt DEFAULT SYSUTCDATETIME(),
    ItemsTotal     DECIMAL(10,2) NOT NULL CONSTRAINT DF_DeliveryOrders_ItemsTotal DEFAULT 0
                   CONSTRAINT CK_DeliveryOrders_ItemsTotal CHECK (ItemsTotal >= 0),
    DeliveryFee    DECIMAL(10,2) NOT NULL CONSTRAINT DF_DeliveryOrders_DeliveryFee DEFAULT 0
                   CONSTRAINT CK_DeliveryOrders_DeliveryFee CHECK (DeliveryFee >= 0),
    TotalAmount    AS (ItemsTotal + DeliveryFee) PERSISTED,   -- обчислювана колонка
    -- аудит + м'яке видалення
    CreatedAt      DATETIME2(3)  NOT NULL CONSTRAINT DF_DeliveryOrders_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy      NVARCHAR(100) NULL,
    UpdatedAt      DATETIME2(3)  NULL,
    UpdatedBy      NVARCHAR(100) NULL,
    IsDeleted      BIT           NOT NULL CONSTRAINT DF_DeliveryOrders_IsDeleted DEFAULT 0,
    -- оптимістична конкурентність: статус одночасно змінюють ресторан, кур'єр і клієнт
    RowVersion     ROWVERSION    NOT NULL
);
GO

/* ---------- DeliveryDetails: 1:1 з DeliveryOrders ----------
   FK є водночас PK -> на одне замовлення рівно один рядок деталей.
   Каскад доречний: деталі не існують без замовлення.               */
IF OBJECT_ID(N'dbo.DeliveryDetails', N'U') IS NULL
CREATE TABLE dbo.DeliveryDetails (
    OrderId       BIGINT        NOT NULL CONSTRAINT PK_DeliveryDetails PRIMARY KEY
                  CONSTRAINT FK_DeliveryDetails_DeliveryOrders
                  REFERENCES dbo.DeliveryOrders(Id) ON DELETE CASCADE,
    Address       NVARCHAR(300) NOT NULL,
    ContactPhone  NVARCHAR(20)  NOT NULL
                  CONSTRAINT CK_DeliveryDetails_Phone CHECK (ContactPhone LIKE N'+380%'),
    CourierNote   NVARCHAR(500) NULL,             -- «код домофону 45», «не дзвонити»
    DesiredTime   DATETIME2(0)  NULL              -- NULL = «якнайшвидше»
);
GO

/* ---------- OrderLines: M:N DeliveryOrders <-> Dishes ----------
   Проміжна таблиця з двома FK + власні поля.
   DishName і UnitPrice — ЗНІМОК на момент замовлення: якщо ресторан
   змінить ціну, старе замовлення не «перерахується» (як OrderItem в eShop). */
IF OBJECT_ID(N'dbo.OrderLines', N'U') IS NULL
CREATE TABLE dbo.OrderLines (
    OrderId     BIGINT        NOT NULL
                CONSTRAINT FK_OrderLines_DeliveryOrders REFERENCES dbo.DeliveryOrders(Id) ON DELETE CASCADE,
    DishId      BIGINT        NOT NULL
                CONSTRAINT FK_OrderLines_Dishes REFERENCES dbo.Dishes(Id),   -- без каскаду: копію страви не видаляємо разом з історією
    DishName    NVARCHAR(150) NOT NULL,                                       -- знімок
    UnitPrice   DECIMAL(10,2) NOT NULL CONSTRAINT CK_OrderLines_UnitPrice CHECK (UnitPrice > 0),  -- знімок
    Quantity    INT           NOT NULL CONSTRAINT CK_OrderLines_Quantity CHECK (Quantity BETWEEN 1 AND 50),
    Comment     NVARCHAR(200) NULL,                                           -- «без цибулі»
    CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, DishId)
);
GO

/* ---------- OrderStatusHistory: 1:N, журнал переходів статусу ---------- */
IF OBJECT_ID(N'dbo.OrderStatusHistory', N'U') IS NULL
CREATE TABLE dbo.OrderStatusHistory (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderStatusHistory PRIMARY KEY,
    OrderId     BIGINT        NOT NULL
                CONSTRAINT FK_OrderStatusHistory_DeliveryOrders REFERENCES dbo.DeliveryOrders(Id) ON DELETE CASCADE,
    FromStatus  NVARCHAR(20)  NULL,               -- NULL для першого запису (створення)
    ToStatus    NVARCHAR(20)  NOT NULL,
    ChangedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_OrderStatusHistory_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedBy   NVARCHAR(100) NULL,
    Reason      NVARCHAR(300) NULL,
    CONSTRAINT CK_OrderStatusHistory_Diff CHECK (FromStatus IS NULL OR FromStatus <> ToStatus)
);
GO

/* =====================================================================
   ІНДЕКСИ — кожен під конкретний запит (без «сліпого індексування»).
   UNIQUE-обмеження вище теж створюють індекси:
     UQ_Customers_Email          -> вхід/пошук клієнта за email
     UQ_DeliveryOrders_OrderNumber -> пошук замовлення за номером, який диктує клієнт
   ===================================================================== */

-- Запит: «Мої замовлення» — замовлення клієнта з певним статусом, нові зверху.
--   SELECT ... FROM DeliveryOrders WHERE CustomerId=@c AND Status=@s AND IsDeleted=0 ORDER BY PlacedAt DESC
-- Фільтрований (лише живі) + покриваючий (INCLUDE) -> без звернень до таблиці.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryOrders_Customer_Status')
CREATE INDEX IX_DeliveryOrders_Customer_Status
    ON dbo.DeliveryOrders (CustomerId, Status, PlacedAt DESC)
    INCLUDE (OrderNumber, RestaurantName, TotalAmount)
    WHERE IsDeleted = 0;
GO

-- Запит: «Черга ресторану» — активні замовлення ресторану в статусі Paid/Cooking, найстаріші першими.
--   SELECT ... WHERE RestaurantId=@r AND Status IN ('Paid','Cooking') AND IsDeleted=0 ORDER BY PlacedAt
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryOrders_Restaurant_Queue')
CREATE INDEX IX_DeliveryOrders_Restaurant_Queue
    ON dbo.DeliveryOrders (RestaurantId, PlacedAt)
    INCLUDE (OrderNumber, Status)
    WHERE IsDeleted = 0 AND Status IN (N'Paid', N'Cooking');
GO

-- Запит: «Диспетчер кур'єрів» — усі замовлення в дорозі, найстаріші першими.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DeliveryOrders_OnTheWay')
CREATE INDEX IX_DeliveryOrders_OnTheWay
    ON dbo.DeliveryOrders (PlacedAt)
    INCLUDE (OrderNumber, RestaurantName)
    WHERE IsDeleted = 0 AND Status = N'OnTheWay';
GO

-- Запит: «Популярні страви» / «у яких замовленнях є ця страва» (PK починається з OrderId,
-- тому пошук за DishId без цього індексу — повний перегляд).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrderLines_DishId')
CREATE INDEX IX_OrderLines_DishId
    ON dbo.OrderLines (DishId)
    INCLUDE (Quantity);
GO

-- Запит: «Таймлайн замовлення» — історія статусів одного замовлення в хронологічному порядку.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrderStatusHistory_Order')
CREATE INDEX IX_OrderStatusHistory_Order
    ON dbo.OrderStatusHistory (OrderId, ChangedAt);
GO

-- Запит: «Меню ресторану з локальної копії» при оформленні — доступні страви ресторану.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Dishes_Restaurant_Available')
CREATE INDEX IX_Dishes_Restaurant_Available
    ON dbo.Dishes (RestaurantId)
    INCLUDE (Name, Price)
    WHERE IsAvailable = 1;
GO

/* =====================================================================
   ПРЕДСТАВЛЕННЯ (view) для читання — денормалізований «звіт по замовленню»,
   щоб API не робив кілька JOIN-ів самостійно.
   ===================================================================== */
CREATE OR ALTER VIEW dbo.vw_OrderSummary
AS
SELECT  o.Id, o.OrderNumber, o.Status, o.PlacedAt,
        c.Id AS CustomerId, c.FullName AS CustomerName, c.Email AS CustomerEmail,
        o.RestaurantId, o.RestaurantName,
        d.Address, d.ContactPhone, d.DesiredTime,
        o.ItemsTotal, o.DeliveryFee, o.TotalAmount,
        (SELECT COUNT(*) FROM dbo.OrderLines l WHERE l.OrderId = o.Id) AS LinesCount
FROM    dbo.DeliveryOrders o
JOIN    dbo.Customers c        ON c.Id = o.CustomerId
LEFT JOIN dbo.DeliveryDetails d ON d.OrderId = o.Id
WHERE   o.IsDeleted = 0;
GO

PRINT N'SmakoViz_Delivery: schema OK';
GO
