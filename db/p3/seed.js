// =====================================================================
// SmakoViz · Проєкт №3 · Тестові дані MongoDB
// Запуск (після collections.js):
//   mongosh "mongodb://localhost:27017" db/p3/seed.js
//
// ІДЕМПОТЕНТНО: кожен документ має фіксований _id і вставляється через
// updateOne({_id}, {$setOnInsert: ...}, {upsert: true}) — find-or-create.
// Повторний запуск нічого не дублює і не перезаписує.
//
// Id ресторанів/страв = Id з БД Меню (db/p2/seed.sql),
// customerId/orderId = Id з БД Доставки (db/p1/seed.sql).
// =====================================================================

db = db.getSiblingDB("smakoviz_feedback");

function upsertAll(collection, docs) {
  let inserted = 0;
  for (const doc of docs) {
    const { _id, ...rest } = doc;
    const res = db[collection].updateOne({ _id }, { $setOnInsert: rest }, { upsert: true });
    inserted += res.upsertedCount;
  }
  print(`${collection}: +${inserted} new, ${db[collection].countDocuments()} total`);
}

const id = (hex) => ObjectId(hex);
const L = (n) => NumberLong(String(n));
const I = (n) => NumberInt(n);

// фіксовані _id, щоб документи могли посилатися один на одного
const DISC_BANUSH   = id("66a000000000000000000d01");
const DISC_DELIVERY = id("66a000000000000000000d02");
// явні _id повідомлень
const MSG = {
  m1: id("66a000000000000000000e01"), m2: id("66a000000000000000000e02"),
  m3: id("66a000000000000000000e03"), m4: id("66a000000000000000000e04"),
  m5: id("66a000000000000000000e05"), m6: id("66a000000000000000000e06")
};

// ---------------------------------------------------------------------
// reviews — РІЗНІ набори полів в одній колекції (гнучка схема)
// ---------------------------------------------------------------------
upsertAll("reviews", [
  {
    // відгук на СТРАВУ: dishId/dishName, фото, вбудовані коментарі, посилання на обговорення
    _id: id("66a000000000000000000a01"),
    targetType: "dish",
    restaurantId: L(1), restaurantName: "Буковинська хата",     // денормалізовано (політика дублювання)
    dishId: L(101), dishName: "Бануш з бринзою і шкварками",     // денормалізовано
    orderId: L(1), orderNumber: "SV-000001",                     // посилання на Доставку (без FK)
    author: { customerId: L(1), displayName: "Олена К." },       // вбудований об'єкт (знімок імені)
    rating: I(5),
    text: "Бануш як у Карпатах, бринза солона в міру. Порція велика.",
    photos: ["reviews/a01-1.jpg", "reviews/a01-2.jpg"],
    comments: [                                                  // вбудований масив (мало й завжди разом з відгуком)
      { authorName: "Буковинська хата", text: "Дякуємо! Чекаємо знову 🙂", createdAt: new Date("2026-10-02T09:15:00Z") },
      { authorName: "Андрій М.", text: "Підтверджую, топ", createdAt: new Date("2026-10-03T18:40:00Z") }
    ],
    discussionId: DISC_BANUSH,                                   // reference на discussions
    createdAt: new Date("2026-10-01T19:05:00Z")
  },
  {
    // відгук на СТРАВУ без фото і без коментарів — інший набір полів
    _id: id("66a000000000000000000a02"),
    targetType: "dish",
    restaurantId: L(1), restaurantName: "Буковинська хата",
    dishId: L(102), dishName: "Борщ з пампушками",
    orderId: L(1), orderNumber: "SV-000001",
    author: { customerId: L(1), displayName: "Олена К." },
    rating: I(4),
    text: "Смачний, але пампушки трохи охололи.",
    createdAt: new Date("2026-10-01T19:07:00Z")
  },
  {
    // відгук на РЕСТОРАН: оцінки за аспектами (вкладений об'єкт), без dishId
    _id: id("66a000000000000000000a03"),
    targetType: "restaurant",
    restaurantId: L(1), restaurantName: "Буковинська хата",
    orderId: L(1), orderNumber: "SV-000001",
    author: { customerId: L(1), displayName: "Олена К." },
    rating: I(5),
    aspects: { food: I(5), packaging: I(4), portionSize: I(5) },
    text: "Усе гаряче, упаковка не протекла.",
    createdAt: new Date("2026-10-01T19:10:00Z")
  },
  {
    // відгук на КУР'ЄРА: зовсім інші поля — courier{}, deliveryMinutes, tags[]
    _id: id("66a000000000000000000a04"),
    targetType: "courier",
    restaurantId: L(1), restaurantName: "Буковинська хата",
    orderId: L(1), orderNumber: "SV-000001",
    author: { customerId: L(1), displayName: "Олена К." },
    rating: I(5),
    courier: { courierId: "courier:7", displayName: "Максим" },
    deliveryMinutes: I(32),
    tags: ["ввічливий", "вчасно", "подзвонив заздалегідь"],
    createdAt: new Date("2026-10-01T19:12:00Z")
  },
  {
    // мінімальний відгук: лише оцінка — без тексту, фото й замовлення
    _id: id("66a000000000000000000a05"),
    targetType: "dish",
    restaurantId: L(2), restaurantName: "Pizza Pronto",
    dishId: L(201), dishName: "Піца Маргарита 30 см",
    author: { customerId: L(2), displayName: "Андрій М." },
    rating: I(3),
    createdAt: new Date("2026-10-12T20:00:00Z")
  },
  {
    _id: id("66a000000000000000000a06"),
    targetType: "restaurant",
    restaurantId: L(3), restaurantName: "Суші Дзен",
    author: { customerId: L(3), displayName: "Ірина Б." },
    rating: I(2),
    aspects: { food: I(3), packaging: I(2) },
    text: "Довго підтверджували замовлення, тому скасувала.",
    discussionId: DISC_DELIVERY,
    createdAt: new Date("2026-10-15T12:30:00Z")
  }
]);

// ---------------------------------------------------------------------
// messages — повна історія обговорень (може бути тисячі -> окрема колекція)
// ---------------------------------------------------------------------
const msgs = [
  { _id: MSG.m1, discussionId: DISC_BANUSH, author: { customerId: L(1), displayName: "Олена К.", role: "customer" },
    text: "А бануш готують на кукурудзяній крупі чи на манці?", createdAt: new Date("2026-10-02T10:00:00Z") },
  { _id: MSG.m2, discussionId: DISC_BANUSH, author: { displayName: "Буковинська хата", role: "restaurant" },
    text: "На кукурудзяній, з домашньою сметаною.", replyTo: MSG.m1, createdAt: new Date("2026-10-02T10:20:00Z") },
  { _id: MSG.m3, discussionId: DISC_BANUSH, author: { customerId: L(2), displayName: "Андрій М.", role: "customer" },
    text: "А можна без шкварок?", createdAt: new Date("2026-10-03T08:00:00Z") },
  { _id: MSG.m4, discussionId: DISC_BANUSH, author: { displayName: "Буковинська хата", role: "restaurant" },
    text: "Так, напишіть у коментарі до позиції.", replyTo: MSG.m3, createdAt: new Date("2026-10-03T08:30:00Z") },
  { _id: MSG.m5, discussionId: DISC_DELIVERY, author: { customerId: L(3), displayName: "Ірина Б.", role: "customer" },
    text: "Чекала підтвердження 40 хвилин.", createdAt: new Date("2026-10-15T12:35:00Z") },
  { _id: MSG.m6, discussionId: DISC_DELIVERY, author: { displayName: "Модератор SmakoViz", role: "moderator" },
    text: "Передали ресторану, вибачте за незручності.", replyTo: MSG.m5, createdAt: new Date("2026-10-15T13:00:00Z") }
];
upsertAll("messages", msgs);

// ---------------------------------------------------------------------
// discussions — ГІБРИД: вбудовано лише 3 останні повідомлення (для швидкого прев'ю),
// повна історія — у messages за discussionId (reference)
// ---------------------------------------------------------------------
const preview = (list) => list.slice(-3).map(m => ({
  messageId: m._id, authorName: m.author.displayName, text: m.text, createdAt: m.createdAt
}));
const banushMsgs   = msgs.filter(m => m.discussionId.equals(DISC_BANUSH));
const deliveryMsgs = msgs.filter(m => m.discussionId.equals(DISC_DELIVERY));

upsertAll("discussions", [
  {
    _id: DISC_BANUSH,
    restaurantId: L(1), restaurantName: "Буковинська хата",
    reviewId: id("66a000000000000000000a01"),
    title: "Питання про бануш",
    messagesCount: I(banushMsgs.length),
    recentMessages: preview(banushMsgs),
    lastActivityAt: banushMsgs[banushMsgs.length - 1].createdAt,
    createdAt: new Date("2026-10-02T10:00:00Z")
  },
  {
    _id: DISC_DELIVERY,
    restaurantId: L(3), restaurantName: "Суші Дзен",
    reviewId: id("66a000000000000000000a06"),
    title: "Довге підтвердження замовлення",
    messagesCount: I(deliveryMsgs.length),
    recentMessages: preview(deliveryMsgs),
    lastActivityAt: deliveryMsgs[deliveryMsgs.length - 1].createdAt,
    createdAt: new Date("2026-10-15T12:35:00Z")
  }
]);

// ---------------------------------------------------------------------
// ratingSummaries — перебудова read-model через aggregation pipeline.
// $merge ідемпотентний: повторний запуск оновлює ті самі документи.
// ---------------------------------------------------------------------
db.reviews.aggregate([
  { $match: { isDeleted: { $ne: true } } },
  { $group: {
      _id: { restaurantId: "$restaurantId", targetType: "$targetType" },
      restaurantName: { $last: "$restaurantName" },
      count: { $sum: 1 }, sum: { $sum: "$rating" } } },
  { $group: {
      _id: "$_id.restaurantId",
      restaurantName: { $last: "$restaurantName" },
      reviewsCount: { $sum: "$count" },
      ratingSum: { $sum: "$sum" },
      byType: { $push: { k: "$_id.targetType", v: { count: "$count", avg: { $round: [{ $divide: ["$sum", "$count"] }, 2] } } } } } },
  { $project: {
      restaurantName: 1,
      reviewsCount: { $toInt: "$reviewsCount" },
      avgRating: { $toDouble: { $round: [{ $divide: ["$ratingSum", "$reviewsCount"] }, 2] } },
      byType: { $arrayToObject: "$byType" },
      updatedAt: "$$NOW" } },
  { $merge: { into: "ratingSummaries", on: "_id", whenMatched: "replace", whenNotMatched: "insert" } }
]);
print(`ratingSummaries: ${db.ratingSummaries.countDocuments()} total`);

print("smakoviz_feedback: seed OK");
