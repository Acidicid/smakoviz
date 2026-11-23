/* =====================================================================
   SmakoViz · Проєкт №1 · Тестові дані
   Запускати ПІСЛЯ schema.sql і procedures.sql.

   ІДЕМПОТЕНТНО: перед кожною вставкою перевіряється наявність запису
   (за природним ключем — Email, OrderNumber, Id страви), тому повторний
   запуск НЕ створює дублікатів.

   Id страв і ресторанів збігаються з db/p2/seed.sql (БД Меню) —
   це та сама страва, а тут лише її локальна копія.
   ===================================================================== */

USE SmakoViz_Delivery;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів і обчислюваних колонок
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ---------- 1. Локальна копія страв із Меню (upsert: копію завжди можна «досинхронізувати») ---------- */
MERGE dbo.Dishes AS target
USING (VALUES
    (101, 1, N'Бануш з бринзою і шкварками', 189.00, 1),
    (102, 1, N'Борщ з пампушками',            145.00, 1),
    (103, 1, N'Вареники з картоплею',         120.00, 1),
    (104, 1, N'Узвар 0,5 л',                   45.00, 1),
    (201, 2, N'Піца Маргарита 30 см',         210.00, 1),
    (202, 2, N'Піца Чотири сири 30 см',       265.00, 1),
    (203, 2, N'Лимонад домашній 0,4 л',        60.00, 1),
    (301, 3, N'Рол Каліфорнія',               295.00, 1),
    (302, 3, N'Рол Філадельфія',              340.00, 1),
    (303, 3, N'Місо-суп',                      95.00, 0)   -- тимчасово недоступна
) AS src (Id, RestaurantId, Name, Price, IsAvailable)
ON target.Id = src.Id
WHEN MATCHED THEN
    UPDATE SET RestaurantId = src.RestaurantId, Name = src.Name, Price = src.Price,
               IsAvailable = src.IsAvailable, SyncedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (Id, RestaurantId, Name, Price, IsAvailable)
    VALUES (src.Id, src.RestaurantId, src.Name, src.Price, src.IsAvailable);
GO

/* ---------- 2. Клієнти (find-or-create за Email) ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Email = N'olena.kovalchuk@example.com')
    INSERT INTO dbo.Customers (Email, FullName, Phone, CreatedBy)
    VALUES (N'olena.kovalchuk@example.com', N'Олена Ковальчук', N'+380501112233', N'seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Email = N'andrii.melnyk@example.com')
    INSERT INTO dbo.Customers (Email, FullName, Phone, CreatedBy)
    VALUES (N'andrii.melnyk@example.com', N'Андрій Мельник', N'+380672223344', N'seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Email = N'iryna.boiko@example.com')
    INSERT INTO dbo.Customers (Email, FullName, Phone, CreatedBy)
    VALUES (N'iryna.boiko@example.com', N'Ірина Бойко', NULL, N'seed');
GO

/* ---------- 3. Замовлення — через процедури (заодно перевіряємо, що вони працюють) ---------- */

-- SV-000001: Олена, «Буковинська хата», пройшло весь шлях до Delivered
IF NOT EXISTS (SELECT 1 FROM dbo.DeliveryOrders WHERE OrderNumber = N'SV-000001')
BEGIN
    DECLARE @c BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'olena.kovalchuk@example.com');
    DECLARE @o BIGINT;

    EXEC dbo.usp_Order_Create
         @OrderNumber = N'SV-000001', @CustomerId = @c,
         @RestaurantId = 1, @RestaurantName = N'Буковинська хата',
         @Address = N'м. Чернівці, вул. Головна, 12, кв. 5', @ContactPhone = N'+380501112233',
         @CourierNote = N'Код домофону 512', @DeliveryFee = 40.00,
         @CreatedBy = N'seed', @OrderId = @o OUTPUT;

    EXEC dbo.usp_Order_AddLine @OrderId = @o, @DishId = 101, @Quantity = 1, @ChangedBy = N'seed';
    EXEC dbo.usp_Order_AddLine @OrderId = @o, @DishId = 102, @Quantity = 2, @Comment = N'Сметану окремо', @ChangedBy = N'seed';
    EXEC dbo.usp_Order_AddLine @OrderId = @o, @DishId = 104, @Quantity = 2, @ChangedBy = N'seed';

    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Confirmed', @ChangedBy = N'restaurant:1';
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Paid',      @ChangedBy = N'payments';
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Cooking',   @ChangedBy = N'restaurant:1';
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'OnTheWay',  @ChangedBy = N'courier:7';
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Delivered', @ChangedBy = N'courier:7';
END
GO

-- SV-000002: Андрій, «Pizza Pronto», зараз готується
IF NOT EXISTS (SELECT 1 FROM dbo.DeliveryOrders WHERE OrderNumber = N'SV-000002')
BEGIN
    DECLARE @c BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'andrii.melnyk@example.com');
    DECLARE @o BIGINT;

    EXEC dbo.usp_Order_Create
         @OrderNumber = N'SV-000002', @CustomerId = @c,
         @RestaurantId = 2, @RestaurantName = N'Pizza Pronto',
         @Address = N'м. Чернівці, вул. Кобилянської, 3', @ContactPhone = N'+380672223344',
         @DeliveryFee = 0, @CreatedBy = N'seed', @OrderId = @o OUTPUT;

    EXEC dbo.usp_Order_AddLine @OrderId = @o, @DishId = 201, @Quantity = 1, @ChangedBy = N'seed';
    EXEC dbo.usp_Order_AddLine @OrderId = @o, @DishId = 203, @Quantity = 2, @ChangedBy = N'seed';

    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Confirmed', @ChangedBy = N'restaurant:2';
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Paid',      @ChangedBy = N'payments';
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Cooking',   @ChangedBy = N'restaurant:2';
END
GO

-- SV-000003: Ірина, «Суші Дзен», скасоване до оплати
IF NOT EXISTS (SELECT 1 FROM dbo.DeliveryOrders WHERE OrderNumber = N'SV-000003')
BEGIN
    DECLARE @c BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'iryna.boiko@example.com');
    DECLARE @o BIGINT;

    EXEC dbo.usp_Order_Create
         @OrderNumber = N'SV-000003', @CustomerId = @c,
         @RestaurantId = 3, @RestaurantName = N'Суші Дзен',
         @Address = N'м. Чернівці, просп. Незалежності, 101', @ContactPhone = N'+380935554466',
         @DeliveryFee = 55.00, @CreatedBy = N'seed', @OrderId = @o OUTPUT;

    EXEC dbo.usp_Order_AddLine @OrderId = @o, @DishId = 301, @Quantity = 2, @ChangedBy = N'seed';

    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Confirmed', @ChangedBy = N'restaurant:3';
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @o, @NewStatus = N'Cancelled', @ChangedBy = N'customer',
         @Reason = N'Клієнт передумав';
END
GO

-- SV-000004: Олена, «Pizza Pronto», щойно оформлене
IF NOT EXISTS (SELECT 1 FROM dbo.DeliveryOrders WHERE OrderNumber = N'SV-000004')
BEGIN
    DECLARE @c BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'olena.kovalchuk@example.com');
    DECLARE @o BIGINT;

    EXEC dbo.usp_Order_Create
         @OrderNumber = N'SV-000004', @CustomerId = @c,
         @RestaurantId = 2, @RestaurantName = N'Pizza Pronto',
         @Address = N'м. Чернівці, вул. Головна, 12, кв. 5', @ContactPhone = N'+380501112233',
         @DeliveryFee = 40.00, @CreatedBy = N'seed', @OrderId = @o OUTPUT;

    EXEC dbo.usp_Order_AddLine @OrderId = @o, @DishId = 202, @Quantity = 1, @Comment = N'Добре пропечена', @ChangedBy = N'seed';
END
GO

PRINT N'SmakoViz_Delivery: seed OK';
SELECT (SELECT COUNT(*) FROM dbo.Customers)          AS Customers,
       (SELECT COUNT(*) FROM dbo.Dishes)             AS Dishes,
       (SELECT COUNT(*) FROM dbo.DeliveryOrders)     AS Orders,
       (SELECT COUNT(*) FROM dbo.OrderLines)         AS OrderLines,
       (SELECT COUNT(*) FROM dbo.OrderStatusHistory) AS HistoryRows;
GO
