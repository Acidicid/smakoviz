# Політика дублювання даних між базами

Дублювання в мікросервісах — **свідоме рішення**: воно роз'єднує сервіси й прибирає синхронні виклики при читанні.
Ціна — eventual consistency (копія може коротко відставати від оригіналу).

Правила:
1. У кожного дубльованого поля є **рівно один власник**. Редагує його лише власник.
2. **Знімок** фіксується в момент події і не оновлюється ніколи.
3. **Копія для читання** оновлюється лише **подіями** власника (Лаб. 08, RabbitMQ).
4. **Dual write заборонений**: жоден сервіс не пише одне й те саме поле в дві бази.
5. Між базами — лише Id без `FOREIGN KEY`.

## Таблиця

| Поле | Де зберігається | Джерело (власник даних) | Політика узгодженості | Навіщо дублюємо |
|------|-----------------|-------------------------|-----------------------|-----------------|
| `OrderLines.DishName`, `OrderLines.UnitPrice` | БД Доставки `SmakoViz_Delivery` (db/p1) | `Dishes` у БД Меню | **знімок** на момент додавання — не оновлюється | чек має показувати ціну, за яку купили, навіть якщо меню змінилось |
| `DeliveryOrders.RestaurantName` | БД Доставки (db/p1) | `Restaurants` у БД Меню | **знімок** на момент оформлення | історія замовлень показує назву без виклику Меню |
| `Dishes.Name`, `Dishes.Price`, `Dishes.IsAvailable`, `Dishes.RestaurantId` | БД Доставки (db/p1), локальна копія | `Dishes` у БД Меню | **копія**, оновлюється подіями `DishChanged`, `DishPriceChanged` | перевірка ціни й доступності при оформленні без синхронного виклику |
| `reviews.restaurantName`, `discussions.restaurantName` | MongoDB Відгуків (db/p3) | `Restaurants` у БД Меню | **копія**, подія `RestaurantRenamed` → `updateMany` | сторінка відгуків без звернень до Меню |
| `reviews.dishName` | MongoDB Відгуків (db/p3) | `Dishes` у БД Меню | **копія**, подія `DishChanged` | те саме |
| `reviews.author.displayName`, `messages.author.displayName` | MongoDB Відгуків (db/p3) | `Customers` у БД Доставки | **копія**, подія `CustomerRenamed` | показати автора без виклику Доставки |
| `reviews.orderNumber` | MongoDB Відгуків (db/p3) | `DeliveryOrders` у БД Доставки | **знімок** (номер незмінний) | позначка «підтверджена покупка» |
| `ratingSummaries.avgRating`, `reviewsCount`, `byType` | MongoDB Відгуків (db/p3) | `reviews` (та сама БД) | **read-model**, перераховується `aggregate(... $merge)` | швидкий рейтинг без агрегації на кожен запит |

## Поля-посилання між базами (без FK)

| Поле | У базі | Посилається на |
|------|--------|----------------|
| `DeliveryOrders.RestaurantId` | `SmakoViz_Delivery` | `Restaurants.Id` у `SmakoViz_Menu` |
| `Dishes.Id`, `Dishes.RestaurantId` | `SmakoViz_Delivery` | `Dishes.Id`, `Restaurants.Id` у `SmakoViz_Menu` |
| `reviews.restaurantId`, `reviews.dishId` | `smakoviz_feedback` | `SmakoViz_Menu` |
| `reviews.orderId`, `reviews.author.customerId` | `smakoviz_feedback` | `SmakoViz_Delivery` |

Перевірка: у кожній SQL-базі `SELECT * FROM sys.foreign_keys` показує лише FK на таблиці тієї ж бази (`db/p1/checks.sql`, пункт 1).

## Приклад допустимого і неприпустимого дублювання

- ✅ Ціна страви в `OrderLines.UnitPrice` — знімок, власник ціни (Меню) один, копія не редагується.
- ❌ Якби адмін міг змінити ціну страви і в Меню, і напряму в `SmakoViz_Delivery.Dishes` — дві копії редагуються незалежно, невідомо, яка правильна (dual write).
