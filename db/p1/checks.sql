/* =====================================================================
   SmakoViz · Проєкт №1 · Запити перевірки (запускати після seed.sql)
   Нічого не ламає: «небезпечні» виклики загорнуті в TRY/CATCH.
   ===================================================================== */

USE SmakoViz_Delivery;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів і обчислюваних колонок
GO
SET NOCOUNT ON;

/* 1. Жодного FK за межі цієї БД: усі FK посилаються лише на таблиці SmakoViz_Delivery */
SELECT fk.name AS ForeignKey,
       OBJECT_NAME(fk.parent_object_id)     AS FromTable,
       OBJECT_NAME(fk.referenced_object_id) AS ToTable
FROM sys.foreign_keys fk
ORDER BY FromTable;

/* 2. Індекси й обмеження, які реально створено */
SELECT t.name AS TableName, i.name AS IndexName, i.type_desc, i.is_unique, i.has_filter, i.filter_definition
FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
WHERE i.name IS NOT NULL
ORDER BY t.name, i.name;

SELECT OBJECT_NAME(parent_object_id) AS TableName, name AS CheckName, definition
FROM sys.check_constraints ORDER BY TableName;

/* 3. CRUD: читання замовлення процедурою (3 набори результатів) */
DECLARE @id BIGINT = (SELECT Id FROM dbo.DeliveryOrders WHERE OrderNumber = N'SV-000001');
EXEC dbo.usp_Order_GetById @OrderId = @id;

/* 4. Пошук + пагінація: замовлення клієнта, сторінка 1 по 2 записи */
DECLARE @PageNumber INT = 1, @PageSize INT = 2;
SELECT o.OrderNumber, o.Status, o.RestaurantName, o.TotalAmount, o.PlacedAt
FROM dbo.DeliveryOrders o
JOIN dbo.Customers c ON c.Id = o.CustomerId
WHERE c.Email = N'olena.kovalchuk@example.com' AND o.IsDeleted = 0
ORDER BY o.PlacedAt DESC
OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

/* 5. Денормалізоване представлення для читання */
SELECT * FROM dbo.vw_OrderSummary ORDER BY PlacedAt;

/* 6. НЕДОПУСТИМИЙ ПЕРЕХІД: відправити кур'єром замовлення, яке ще не оплачене (SV-000004 = Placed).
      Очікується керована помилка 50002, статус лишається Placed. */
DECLARE @placed BIGINT = (SELECT Id FROM dbo.DeliveryOrders WHERE OrderNumber = N'SV-000004');
BEGIN TRY
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @placed, @NewStatus = N'OnTheWay', @ChangedBy = N'check';
    PRINT N'ПОМИЛКА ПЕРЕВІРКИ: перехід мав бути заборонений!';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- XACT_ABORT робить транзакцію «приреченою» — прибираємо її
    PRINT CONCAT(N'OK, керована помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;
SELECT OrderNumber, Status FROM dbo.DeliveryOrders WHERE Id = @placed;   -- все ще Placed

/* 7. Скасування після оплати заборонене (SV-000002 = Cooking) */
DECLARE @cooking BIGINT = (SELECT Id FROM dbo.DeliveryOrders WHERE OrderNumber = N'SV-000002');
BEGIN TRY
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @cooking, @NewStatus = N'Cancelled';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- XACT_ABORT робить транзакцію «приреченою» — прибираємо її
    PRINT CONCAT(N'OK, керована помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

/* 8. Оптимістична конкурентність: передаємо «стару» версію рядка -> 50003 */
BEGIN TRY
    EXEC dbo.usp_Order_ChangeStatus @OrderId = @placed, @NewStatus = N'Confirmed',
         @ExpectedRowVersion = 0x0000000000000001;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;   -- XACT_ABORT робить транзакцію «приреченою» — прибираємо її
    PRINT CONCAT(N'OK, керована помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

/* 9. Знімок: ціна в позиції замовлення не залежить від поточної ціни в копії меню */
SELECT l.DishName, l.UnitPrice AS PriceInOrder, d.Price AS PriceNowInMenuCopy
FROM dbo.OrderLines l JOIN dbo.Dishes d ON d.Id = l.DishId
ORDER BY l.OrderId;

/* 10. Ідемпотентність seed: після повторного запуску seed.sql ці числа мають бути ТІ САМІ */
SELECT (SELECT COUNT(*) FROM dbo.Customers) AS Customers,
       (SELECT COUNT(*) FROM dbo.DeliveryOrders) AS Orders,
       (SELECT COUNT(*) FROM dbo.OrderLines) AS OrderLines;
GO
