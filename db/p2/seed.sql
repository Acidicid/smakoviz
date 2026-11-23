/* =====================================================================
   SmakoViz · Проєкт №2 · Довідникові та тестові дані
   Запускати ПІСЛЯ schema.sql.

   ІДЕМПОТЕНТНО: MERGE ... WHEN NOT MATCHED вставляє лише те, чого ще немає.
   Id задаються явно (IDENTITY_INSERT), бо ці самі Id використовують
   копії/посилання в інших базах (SmakoViz_Delivery, MongoDB).
   ===================================================================== */

USE SmakoViz_Menu;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів і обчислюваних колонок
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

/* ---------- Категорії (довідник) ---------- */
SET IDENTITY_INSERT dbo.Categories ON;
MERGE dbo.Categories AS t
USING (VALUES
    (1, N'Перші страви',    N'soups',      10),
    (2, N'Основні страви',  N'mains',      20),
    (3, N'Піца',            N'pizza',      30),
    (4, N'Суші та роли',    N'sushi',      40),
    (5, N'Напої',           N'drinks',     50),
    (6, N'Вегетаріанське',  N'vegetarian', 60),
    (7, N'Хіт продажу',     N'bestseller',  0)
) AS s (Id, Name, Slug, SortOrder)
ON t.Id = s.Id
WHEN NOT MATCHED THEN
    INSERT (Id, Name, Slug, SortOrder) VALUES (s.Id, s.Name, s.Slug, s.SortOrder);
SET IDENTITY_INSERT dbo.Categories OFF;
GO

/* ---------- Ресторани ---------- */
SET IDENTITY_INSERT dbo.Restaurants ON;
MERGE dbo.Restaurants AS t
USING (VALUES
    (1, N'Буковинська хата', N'bukovynska-khata', N'Чернівці', N'Українська'),
    (2, N'Pizza Pronto',     N'pizza-pronto',     N'Чернівці', N'Італійська'),
    (3, N'Суші Дзен',        N'sushi-zen',        N'Чернівці', N'Японська')
) AS s (Id, Name, Slug, City, CuisineType)
ON t.Id = s.Id
WHEN NOT MATCHED THEN
    INSERT (Id, Name, Slug, City, CuisineType, CreatedBy)
    VALUES (s.Id, s.Name, s.Slug, s.City, s.CuisineType, N'seed');
SET IDENTITY_INSERT dbo.Restaurants OFF;
GO

/* ---------- Профілі ресторанів (1:1) ---------- */
MERGE dbo.RestaurantProfiles AS t
USING (VALUES
    (1, N'вул. Ольги Кобилянської, 20', N'+380372555001', '09:00', '22:00', 150.00, N'Бануш, борщ і вареники за бабусиними рецептами'),
    (2, N'вул. Головна, 45',            N'+380372555002', '10:00', '23:00', 200.00, N'Неаполітанська піца з дров''яної печі'),
    (3, N'просп. Незалежності, 52',     N'+380372555003', '11:00', '23:30', 300.00, NULL)
) AS s (RestaurantId, Address, Phone, OpensAt, ClosesAt, MinOrderAmount, Description)
ON t.RestaurantId = s.RestaurantId
WHEN NOT MATCHED THEN
    INSERT (RestaurantId, Address, Phone, OpensAt, ClosesAt, MinOrderAmount, Description)
    VALUES (s.RestaurantId, s.Address, s.Phone, s.OpensAt, s.ClosesAt, s.MinOrderAmount, s.Description);
GO

/* ---------- Страви (1:N від ресторану) ---------- */
SET IDENTITY_INSERT dbo.Dishes ON;
MERGE dbo.Dishes AS t
USING (VALUES
    (101, 1, N'Бануш з бринзою і шкварками', 189.00, 350, 640, 1),
    (102, 1, N'Борщ з пампушками',            145.00, 400, 310, 1),
    (103, 1, N'Вареники з картоплею',         120.00, 300, 450, 1),
    (104, 1, N'Узвар 0,5 л',                   45.00, 500, 120, 1),
    (201, 2, N'Піца Маргарита 30 см',         210.00, 450, 980, 1),
    (202, 2, N'Піца Чотири сири 30 см',       265.00, 480, 1150, 1),
    (203, 2, N'Лимонад домашній 0,4 л',        60.00, 400, 150, 1),
    (301, 3, N'Рол Каліфорнія',               295.00, 260, 420, 1),
    (302, 3, N'Рол Філадельфія',              340.00, 280, 480, 1),
    (303, 3, N'Місо-суп',                      95.00, 300,  80, 0)
) AS s (Id, RestaurantId, Name, Price, WeightGrams, Calories, IsAvailable)
ON t.Id = s.Id
WHEN NOT MATCHED THEN
    INSERT (Id, RestaurantId, Name, Price, WeightGrams, Calories, IsAvailable, CreatedBy)
    VALUES (s.Id, s.RestaurantId, s.Name, s.Price, s.WeightGrams, s.Calories, s.IsAvailable, N'seed');
SET IDENTITY_INSERT dbo.Dishes OFF;
GO

/* ---------- Страва <-> Категорія (M:N з власними полями) ---------- */
MERGE dbo.DishCategories AS t
USING (VALUES
    (101, 2, '2026-09-01', 1), (101, 7, '2026-10-01', 1),
    (102, 1, '2026-09-01', 1), (102, 7, '2026-10-01', 0),
    (103, 2, '2026-09-01', 0), (103, 6, '2026-09-01', 1),
    (104, 5, '2026-09-01', 0),
    (201, 3, '2026-09-05', 1), (201, 6, '2026-09-05', 0),
    (202, 3, '2026-09-05', 0), (202, 6, '2026-09-05', 0),
    (203, 5, '2026-09-05', 0),
    (301, 4, '2026-09-10', 1), (302, 4, '2026-09-10', 0), (302, 7, '2026-10-01', 0),
    (303, 1, '2026-09-10', 0)
) AS s (DishId, CategoryId, AddedAt, IsFeatured)
ON t.DishId = s.DishId AND t.CategoryId = s.CategoryId
WHEN NOT MATCHED THEN
    INSERT (DishId, CategoryId, AddedAt, IsFeatured)
    VALUES (s.DishId, s.CategoryId, s.AddedAt, s.IsFeatured);
GO

PRINT N'SmakoViz_Menu: seed OK';
SELECT (SELECT COUNT(*) FROM dbo.Restaurants)    AS Restaurants,
       (SELECT COUNT(*) FROM dbo.Dishes)         AS Dishes,
       (SELECT COUNT(*) FROM dbo.Categories)     AS Categories,
       (SELECT COUNT(*) FROM dbo.DishCategories) AS DishCategories;
GO
