// =====================================================================
// SmakoViz · Проєкт №3 · Контекст «Відгуки» (Feedback) · MongoDB
// Далі: мікросервіс на MongoDB (Лаб. 04)
//
// Запуск:  mongosh "mongodb://localhost:27017" db/p3/collections.js
// Ідемпотентно: колекція створюється, якщо її ще немає, інакше їй
// оновлюється валідатор (collMod). Індекси createIndex теж ідемпотентні.
//
// 4 колекції:
//   reviews          — відгуки (на страву / ресторан / кур'єра): РІЗНА структура в одній колекції
//                      embedding: author{}, comments[]   referencing: discussionId, orderId, restaurantId
//   discussions      — ГІБРИД: вбудовані останні 3 повідомлення + посилання на повну історію в messages
//   messages         — повідомлення обговорень (необмежено багато -> окрема колекція, referencing)
//   ratingSummaries  — read-model: середні оцінки ресторанів, будується aggregation pipeline ($merge)
// =====================================================================

db = db.getSiblingDB("smakoviz_feedback");

function ensureCollection(name, validator) {
  const exists = db.getCollectionNames().includes(name);
  if (!exists) {
    db.createCollection(name, { validator, validationLevel: "moderate", validationAction: "error" });
    print(`created  ${name}`);
  } else {
    db.runCommand({ collMod: name, validator, validationLevel: "moderate", validationAction: "error" });
    print(`updated  ${name} (validator)`);
  }
}

// ---------- reviews ----------
// Обов'язкові лише спільні поля; решта (photos, dishId, courier, aspects...) — залежить від типу відгуку.
ensureCollection("reviews", {
  $jsonSchema: {
    bsonType: "object",
    required: ["targetType", "restaurantId", "restaurantName", "rating", "author", "createdAt"],
    properties: {
      targetType:     { enum: ["dish", "restaurant", "courier"] },
      restaurantId:   { bsonType: "long", description: "Id ресторану з БД Меню (посилання без FK)" },
      restaurantName: { bsonType: "string", description: "денормалізовано з Меню, оновлюється подіями" },
      dishId:         { bsonType: "long" },
      dishName:       { bsonType: "string", description: "денормалізовано з Меню, оновлюється подіями" },
      orderId:        { bsonType: "long", description: "Id замовлення з БД Доставки (підтверджена покупка)" },
      orderNumber:    { bsonType: "string" },
      rating:         { bsonType: "int", minimum: 1, maximum: 5 },
      text:           { bsonType: "string", maxLength: 2000 },
      photos:         { bsonType: "array", maxItems: 5, items: { bsonType: "string" } },
      author: {
        bsonType: "object",
        required: ["customerId", "displayName"],
        properties: {
          customerId:  { bsonType: "long" },
          displayName: { bsonType: "string" }
        }
      },
      comments: {
        bsonType: "array",
        maxItems: 50,   // embedding лише поки масив обмежений; довгі розмови — у discussions/messages
        items: {
          bsonType: "object",
          required: ["authorName", "text", "createdAt"],
          properties: {
            authorName: { bsonType: "string" },
            text:       { bsonType: "string", maxLength: 1000 },
            createdAt:  { bsonType: "date" }
          }
        }
      },
      discussionId: { bsonType: "objectId" },
      createdAt:    { bsonType: "date" },
      updatedAt:    { bsonType: "date" },
      isDeleted:    { bsonType: "bool" }
    }
  }
});

// ---------- discussions ----------
ensureCollection("discussions", {
  $jsonSchema: {
    bsonType: "object",
    required: ["restaurantId", "title", "messagesCount", "recentMessages", "createdAt"],
    properties: {
      restaurantId:   { bsonType: "long" },
      restaurantName: { bsonType: "string" },
      reviewId:       { bsonType: "objectId" },
      title:          { bsonType: "string", maxLength: 200 },
      messagesCount:  { bsonType: "int", minimum: 0 },
      recentMessages: {                       // гібрид: лише «хвіст» розмови вбудовано
        bsonType: "array",
        maxItems: 3,
        items: {
          bsonType: "object",
          required: ["messageId", "authorName", "text", "createdAt"],
          properties: {
            messageId:  { bsonType: "objectId" },
            authorName: { bsonType: "string" },
            text:       { bsonType: "string" },
            createdAt:  { bsonType: "date" }
          }
        }
      },
      lastActivityAt: { bsonType: "date" },
      createdAt:      { bsonType: "date" }
    }
  }
});

// ---------- messages ----------
ensureCollection("messages", {
  $jsonSchema: {
    bsonType: "object",
    required: ["discussionId", "author", "text", "createdAt"],
    properties: {
      discussionId: { bsonType: "objectId" },
      author: {
        bsonType: "object",
        required: ["displayName", "role"],
        properties: {
          customerId:  { bsonType: "long" },
          displayName: { bsonType: "string" },
          role:        { enum: ["customer", "restaurant", "moderator"] }
        }
      },
      text:      { bsonType: "string", maxLength: 2000 },
      replyTo:   { bsonType: "objectId" },
      createdAt: { bsonType: "date" }
    }
  }
});

// ---------- ratingSummaries (read-model, заповнюється aggregation pipeline) ----------
ensureCollection("ratingSummaries", {
  $jsonSchema: {
    bsonType: "object",
    required: ["_id", "restaurantName", "reviewsCount", "avgRating"],
    properties: {
      _id:            { bsonType: "long", description: "= restaurantId" },
      restaurantName: { bsonType: "string" },
      reviewsCount:   { bsonType: "int" },
      avgRating:      { bsonType: "double" },
      byType:         { bsonType: "object" },
      updatedAt:      { bsonType: "date" }
    }
  }
});

// =====================================================================
// ІНДЕКСИ — кожен під конкретний запит
// =====================================================================

// «Відгуки ресторану, нові зверху» — сторінка ресторану
db.reviews.createIndex({ restaurantId: 1, createdAt: -1 }, { name: "ix_reviews_restaurant_created" });

// «Відгуки на страву, найкращі зверху» — лише документи, де є dishId (partial index)
db.reviews.createIndex(
  { dishId: 1, rating: -1 },
  { name: "ix_reviews_dish_rating", partialFilterExpression: { dishId: { $exists: true } } }
);

// «Мої відгуки» — профіль клієнта
db.reviews.createIndex({ "author.customerId": 1, createdAt: -1 }, { name: "ix_reviews_author" });

// Бізнес-правило: один відгук на одну ціль у межах замовлення (не можна «накрутити»)
db.reviews.createIndex(
  { orderId: 1, targetType: 1, dishId: 1 },
  { name: "ux_reviews_order_target", unique: true, partialFilterExpression: { orderId: { $exists: true } } }
);

// Повнотекстовий пошук по тексту відгуку (українська не має стемера -> "none")
db.reviews.createIndex({ text: "text" }, { name: "tx_reviews_text", default_language: "none" });

// «Обговорення ресторану, найактивніші зверху»
db.discussions.createIndex({ restaurantId: 1, lastActivityAt: -1 }, { name: "ix_discussions_restaurant_activity" });

// «Повна історія обговорення з пагінацією» — головний запит до messages
db.messages.createIndex({ discussionId: 1, createdAt: 1 }, { name: "ix_messages_discussion_created" });

print("smakoviz_feedback: collections & indexes OK");
