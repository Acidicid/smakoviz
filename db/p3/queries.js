// =====================================================================
// SmakoViz · Проєкт №3 · Приклади запитів і агрегацій (для перевірки та захисту)
//   mongosh "mongodb://localhost:27017" db/p3/queries.js
// =====================================================================

db = db.getSiblingDB("smakoviz_feedback");
const show = (title, x) => { print(`\n=== ${title}`); printjson(x); };

// 1. Гнучка схема: документи однієї колекції мають РІЗНІ набори полів
show("reviews: різні набори полів",
  db.reviews.aggregate([
    { $project: { _id: 0, targetType: 1, fields: { $map: { input: { $objectToArray: "$$ROOT" }, in: "$$this.k" } } } }
  ]).toArray());

// 2. Відгуки ресторану, нові зверху, з пагінацією (індекс ix_reviews_restaurant_created)
const page = 1, size = 3;
show("reviews ресторану #1, сторінка 1",
  db.reviews.find({ restaurantId: NumberLong("1") }, { targetType: 1, rating: 1, text: 1, createdAt: 1 })
    .sort({ createdAt: -1 }).skip((page - 1) * size).limit(size).toArray());

// 3. Embedding: відгук разом з автором і коментарями — ОДНИМ читанням
show("embedding: відгук + коментарі", db.reviews.findOne({ "comments.0": { $exists: true } }, { author: 1, comments: 1 }));

// 4. Referencing: повна історія обговорення ($lookup з messages)
show("referencing: обговорення + усі повідомлення",
  db.discussions.aggregate([
    { $match: { title: "Питання про бануш" } },
    { $lookup: { from: "messages", localField: "_id", foreignField: "discussionId", as: "allMessages",
                 pipeline: [{ $sort: { createdAt: 1 } }, { $project: { _id: 0, "author.displayName": 1, text: 1 } }] } },
    { $project: { title: 1, messagesCount: 1, allMessages: 1 } }
  ]).toArray());

// 5. Гібрид: прев'ю з вбудованих recentMessages без звернення до messages
show("гібрид: прев'ю обговорень ресторану", db.discussions.find({}, { title: 1, messagesCount: 1, "recentMessages.text": 1 }).toArray());

// 6. Найкращі страви за середньою оцінкою (aggregation pipeline)
show("топ страв",
  db.reviews.aggregate([
    { $match: { targetType: "dish" } },
    { $group: { _id: "$dishId", dishName: { $first: "$dishName" }, avg: { $avg: "$rating" }, count: { $sum: 1 } } },
    { $sort: { avg: -1, count: -1 } }
  ]).toArray());

// 7. Read-model з середніми оцінками ресторанів (заповнюється в seed.js через $merge)
show("ratingSummaries", db.ratingSummaries.find().toArray());

// 8. Повнотекстовий пошук
show("пошук 'бринза'", db.reviews.find({ $text: { $search: "бринза" } }, { text: 1 }).toArray());

// 9. Оновлення денормалізованого поля подією з Меню (так зробить обробник події в Лаб. 08):
//    ресторан перейменовано -> оновлюємо копії назви в усіх колекціях Відгуків.
//    (закоментовано, щоб не змінювати seed-дані)
// db.reviews.updateMany({ restaurantId: NumberLong("2") }, { $set: { restaurantName: "Pizza Pronto Centro" } });
// db.discussions.updateMany({ restaurantId: NumberLong("2") }, { $set: { restaurantName: "Pizza Pronto Centro" } });

// 10. Валідатор працює: оцінка 7 відхиляється
try {
  db.reviews.insertOne({ targetType: "dish", restaurantId: NumberLong("1"), restaurantName: "x",
                         rating: NumberInt(7), author: { customerId: NumberLong("1"), displayName: "x" }, createdAt: new Date() });
  print("\nПОМИЛКА: невалідний документ вставився!");
} catch (e) {
  print("\n=== валідатор: OK, документ з rating=7 відхилено");
}

// 11. Індекси
show("індекси reviews", db.reviews.getIndexes().map(i => i.name));
