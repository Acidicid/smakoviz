/* =====================================================================
   SmakoViz · Проєкт №1 · Збережувані процедури (6 шт.)
   Запускати ПІСЛЯ schema.sql. Ідемпотентно (CREATE OR ALTER).

   | Процедура               | Що робить                                   | Тип         |
   |-------------------------|---------------------------------------------|-------------|
   | usp_Customer_Upsert     | створити клієнта або оновити за email       | CRUD        |
   | usp_Order_Create        | замовлення + деталі доставки + історія      | бізнес, TX  |
   | usp_Order_AddLine       | додати страву (знімок назви й ціни)         | бізнес, TX  |
   | usp_Order_GetById       | замовлення, позиції, історія (3 набори)     | CRUD (read) |
   | usp_Order_ChangeStatus  | зміна статусу з перевіркою переходу         | бізнес, TX  |
   | usp_Order_SoftDelete    | м'яке видалення завершеного замовлення      | CRUD        |

   Керовані помилки (THROW) — їх DAL у Лаб. 02 перетворює на HTTP-коди:
     50001  замовлення / клієнта / страву не знайдено          -> 404
     50002  недопустимий перехід статусу                        -> 409
     50003  конфлікт конкурентності (RowVersion змінився)        -> 409
     50004  порушення бізнес-правила (порожнє замовлення, страва
            недоступна, інший ресторан, видалення активного)     -> 422

   Життєвий цикл замовлення:
     Placed -> Confirmed -> Paid -> Cooking -> OnTheWay -> Delivered
     Cancelled — лише з Placed або Confirmed (тобто ДО оплати).
   ===================================================================== */

USE SmakoViz_Delivery;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів і обчислюваних колонок
GO

/* ---------- 1. Створити або оновити клієнта (ключ — email) ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_Customer_Upsert
    @Email      NVARCHAR(256),
    @FullName   NVARCHAR(150),
    @Phone      NVARCHAR(20)  = NULL,
    @ChangedBy  NVARCHAR(100) = NULL,
    @CustomerId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

        SELECT @CustomerId = Id
        FROM dbo.Customers WITH (UPDLOCK, HOLDLOCK)   -- блокуємо «діапазон», щоб два паралельні виклики не створили двох клієнтів
        WHERE Email = @Email;

        IF @CustomerId IS NULL
        BEGIN
            INSERT INTO dbo.Customers (Email, FullName, Phone, CreatedBy)
            VALUES (@Email, @FullName, @Phone, @ChangedBy);
            SET @CustomerId = SCOPE_IDENTITY();
        END
        ELSE
        BEGIN
            UPDATE dbo.Customers
            SET FullName  = @FullName,
                Phone     = @Phone,
                IsDeleted = 0,
                UpdatedAt = SYSUTCDATETIME(),
                UpdatedBy = @ChangedBy
            WHERE Id = @CustomerId;
        END

    COMMIT TRANSACTION;
END
GO

/* ---------- 2. Створити замовлення (шапка + деталі доставки + перший запис історії) ----------
   Усе або нічого: якщо впаде вставка деталей, замовлення теж не залишиться. */
CREATE OR ALTER PROCEDURE dbo.usp_Order_Create
    @OrderNumber    NVARCHAR(20),
    @CustomerId     BIGINT,
    @RestaurantId   BIGINT,
    @RestaurantName NVARCHAR(150),     -- знімок назви з Меню
    @Address        NVARCHAR(300),
    @ContactPhone   NVARCHAR(20),
    @CourierNote    NVARCHAR(500) = NULL,
    @DesiredTime    DATETIME2(0)  = NULL,
    @DeliveryFee    DECIMAL(10,2) = 0,
    @CreatedBy      NVARCHAR(100) = NULL,
    @OrderId        BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId AND IsDeleted = 0)
        THROW 50001, N'Customer not found', 1;

    BEGIN TRANSACTION;

        INSERT INTO dbo.DeliveryOrders (OrderNumber, CustomerId, RestaurantId, RestaurantName, DeliveryFee, CreatedBy)
        VALUES (@OrderNumber, @CustomerId, @RestaurantId, @RestaurantName, @DeliveryFee, @CreatedBy);

        SET @OrderId = SCOPE_IDENTITY();

        INSERT INTO dbo.DeliveryDetails (OrderId, Address, ContactPhone, CourierNote, DesiredTime)
        VALUES (@OrderId, @Address, @ContactPhone, @CourierNote, @DesiredTime);

        INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy, Reason)
        VALUES (@OrderId, NULL, N'Placed', @CreatedBy, N'Order created');

    COMMIT TRANSACTION;
END
GO

/* ---------- 3. Додати страву до замовлення ----------
   Назва і ціна беруться з локальної копії Dishes і ЗАПИСУЮТЬСЯ ЯК ЗНІМОК
   (політика дублювання: OrderLines.DishName/UnitPrice не оновлюються).
   Повторне додавання тієї ж страви збільшує кількість.                    */
CREATE OR ALTER PROCEDURE dbo.usp_Order_AddLine
    @OrderId   BIGINT,
    @DishId    BIGINT,
    @Quantity  INT,
    @Comment   NVARCHAR(200) = NULL,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(20), @RestaurantId BIGINT;

        SELECT @Status = Status, @RestaurantId = RestaurantId
        FROM dbo.DeliveryOrders WITH (UPDLOCK)
        WHERE Id = @OrderId AND IsDeleted = 0;

        IF @Status IS NULL
            THROW 50001, N'Order not found', 1;
        IF @Status <> N'Placed'
            THROW 50004, N'Dishes can be added only while the order is Placed', 1;

        DECLARE @DishName NVARCHAR(150), @Price DECIMAL(10,2), @DishRestaurant BIGINT, @IsAvailable BIT;

        SELECT @DishName = Name, @Price = Price, @DishRestaurant = RestaurantId, @IsAvailable = IsAvailable
        FROM dbo.Dishes
        WHERE Id = @DishId;

        IF @DishName IS NULL
            THROW 50001, N'Dish not found', 1;
        IF @IsAvailable = 0
            THROW 50004, N'Dish is not available now', 1;
        IF @DishRestaurant <> @RestaurantId
            THROW 50004, N'Dish belongs to another restaurant', 1;

        IF EXISTS (SELECT 1 FROM dbo.OrderLines WHERE OrderId = @OrderId AND DishId = @DishId)
            UPDATE dbo.OrderLines
            SET Quantity = Quantity + @Quantity,
                Comment  = COALESCE(@Comment, Comment)
            WHERE OrderId = @OrderId AND DishId = @DishId;
        ELSE
            INSERT INTO dbo.OrderLines (OrderId, DishId, DishName, UnitPrice, Quantity, Comment)
            VALUES (@OrderId, @DishId, @DishName, @Price, @Quantity, @Comment);

        -- перерахунок суми зі знімків цін (а не з поточних цін меню)
        UPDATE dbo.DeliveryOrders
        SET ItemsTotal = (SELECT SUM(UnitPrice * Quantity) FROM dbo.OrderLines WHERE OrderId = @OrderId),
            UpdatedAt  = SYSUTCDATETIME(),
            UpdatedBy  = @ChangedBy
        WHERE Id = @OrderId;

    COMMIT TRANSACTION;
END
GO

/* ---------- 4. Отримати замовлення: 3 набори результатів ----------
   1) шапка + деталі доставки, 2) позиції, 3) історія статусів.
   У Dapper читається через QueryMultiple.                        */
CREATE OR ALTER PROCEDURE dbo.usp_Order_GetById
    @OrderId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.DeliveryOrders WHERE Id = @OrderId AND IsDeleted = 0)
        THROW 50001, N'Order not found', 1;

    SELECT o.Id, o.OrderNumber, o.CustomerId, o.RestaurantId, o.RestaurantName, o.Status,
           o.PlacedAt, o.ItemsTotal, o.DeliveryFee, o.TotalAmount, o.RowVersion,
           d.Address, d.ContactPhone, d.CourierNote, d.DesiredTime
    FROM dbo.DeliveryOrders o
    LEFT JOIN dbo.DeliveryDetails d ON d.OrderId = o.Id
    WHERE o.Id = @OrderId;

    SELECT DishId, DishName, UnitPrice, Quantity, UnitPrice * Quantity AS LineTotal, Comment
    FROM dbo.OrderLines
    WHERE OrderId = @OrderId
    ORDER BY DishName;

    SELECT FromStatus, ToStatus, ChangedAt, ChangedBy, Reason
    FROM dbo.OrderStatusHistory
    WHERE OrderId = @OrderId
    ORDER BY ChangedAt, Id;
END
GO

/* ---------- 5. ЗМІНА СТАТУСУ — транзакційна, з перевіркою переходу ----------
   - явна транзакція + XACT_ABORT: будь-яка помилка відкочує все;
   - UPDLOCK: рядок замовлення заблоковано до COMMIT, тож кур'єр і ресторан
     не змінять статус «одночасно»;
   - @ExpectedRowVersion (необов'язковий): якщо клієнт API передав версію,
     яку бачив, а рядок уже змінився — помилка 50003 (оптимістична конкурентність);
   - недопустимий перехід -> керована помилка 50002, дія НЕ виконується.      */
CREATE OR ALTER PROCEDURE dbo.usp_Order_ChangeStatus
    @OrderId            BIGINT,
    @NewStatus          NVARCHAR(20),
    @ChangedBy          NVARCHAR(100) = NULL,
    @Reason             NVARCHAR(300) = NULL,
    @ExpectedRowVersion BINARY(8)     = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

        DECLARE @Current NVARCHAR(20), @Version BINARY(8), @Lines INT;

        SELECT @Current = Status, @Version = RowVersion
        FROM dbo.DeliveryOrders WITH (UPDLOCK)
        WHERE Id = @OrderId AND IsDeleted = 0;

        IF @Current IS NULL
            THROW 50001, N'Order not found', 1;

        IF @ExpectedRowVersion IS NOT NULL AND @ExpectedRowVersion <> @Version
            THROW 50003, N'Order was changed by someone else. Reload and try again', 1;

        -- таблиця допустимих переходів (єдине місце, де описано життєвий цикл)
        IF NOT EXISTS (
            SELECT 1
            FROM (VALUES
                (N'Placed',    N'Confirmed'),
                (N'Placed',    N'Cancelled'),
                (N'Confirmed', N'Paid'),
                (N'Confirmed', N'Cancelled'),
                (N'Paid',      N'Cooking'),
                (N'Cooking',   N'OnTheWay'),
                (N'OnTheWay',  N'Delivered')
            ) AS t(FromStatus, ToStatus)
            WHERE t.FromStatus = @Current AND t.ToStatus = @NewStatus)
        BEGIN
            DECLARE @Msg NVARCHAR(200) =
                CONCAT(N'Transition ', @Current, N' -> ', @NewStatus, N' is not allowed');
            THROW 50002, @Msg, 1;
        END

        -- бізнес-правило: порожнє замовлення не можна підтвердити
        IF @NewStatus = N'Confirmed'
        BEGIN
            SELECT @Lines = COUNT(*) FROM dbo.OrderLines WHERE OrderId = @OrderId;
            IF @Lines = 0
                THROW 50004, N'Cannot confirm an empty order', 1;
        END

        UPDATE dbo.DeliveryOrders
        SET Status    = @NewStatus,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @ChangedBy
        WHERE Id = @OrderId;

        INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy, Reason)
        VALUES (@OrderId, @Current, @NewStatus, @ChangedBy, @Reason);

    COMMIT TRANSACTION;
END
GO

/* ---------- 6. М'яке видалення (лише завершених замовлень) ---------- */
CREATE OR ALTER PROCEDURE dbo.usp_Order_SoftDelete
    @OrderId   BIGINT,
    @ChangedBy NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

        DECLARE @Status NVARCHAR(20);

        SELECT @Status = Status
        FROM dbo.DeliveryOrders WITH (UPDLOCK)
        WHERE Id = @OrderId AND IsDeleted = 0;

        IF @Status IS NULL
            THROW 50001, N'Order not found', 1;
        IF @Status NOT IN (N'Delivered', N'Cancelled')
            THROW 50004, N'Only Delivered or Cancelled orders can be deleted', 1;

        UPDATE dbo.DeliveryOrders
        SET IsDeleted = 1,
            UpdatedAt = SYSUTCDATETIME(),
            UpdatedBy = @ChangedBy
        WHERE Id = @OrderId;

    COMMIT TRANSACTION;
END
GO

PRINT N'SmakoViz_Delivery: procedures OK';
GO
