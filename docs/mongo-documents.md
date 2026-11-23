# Приклади документів MongoDB (`smakoviz_feedback`)

Нижче — реальні документи, експортовані з бази після `collections.js` + `seed.js` (формат Extended JSON).
У базі `restaurantId`, `dishId`, `orderId`, `customerId` мають тип **long** (Int64, як `BIGINT` у SQL), `rating` — **int**.

Що демонструють документи:
- **гнучка схема** — у колекції `reviews` три типи відгуків з різними полями (`photos`, `comments`, `aspects`, `courier`, `tags`, `deliveryMinutes`) і мінімальний документ лише з оцінкою;
- **embedding** — `author{}` і `comments[]` всередині відгуку;
- **referencing** — `discussionId`, `orderId`, `restaurantId`, `dishId`; `messages.discussionId`;
- **гібрид** — `discussions.recentMessages[]` (вбудоване прев’ю) + повна історія в `messages`;
- **денормалізація** — `restaurantName`, `dishName`, `author.displayName` (див. [duplication-policy.md](duplication-policy.md)).

### reviews — відгук типу `dish`

```json
{
  "_id": {
    "$oid": "66a000000000000000000a01"
  },
  "author": {
    "customerId": 1,
    "displayName": "Олена К."
  },
  "comments": [
    {
      "authorName": "Буковинська хата",
      "text": "Дякуємо! Чекаємо знову 🙂",
      "createdAt": {
        "$date": "2026-10-02T09:15:00Z"
      }
    },
    {
      "authorName": "Андрій М.",
      "text": "Підтверджую, топ",
      "createdAt": {
        "$date": "2026-10-03T18:40:00Z"
      }
    }
  ],
  "createdAt": {
    "$date": "2026-10-01T19:05:00Z"
  },
  "discussionId": {
    "$oid": "66a000000000000000000d01"
  },
  "dishId": 101,
  "dishName": "Бануш з бринзою і шкварками",
  "orderId": 1,
  "orderNumber": "SV-000001",
  "photos": [
    "reviews/a01-1.jpg",
    "reviews/a01-2.jpg"
  ],
  "rating": 5,
  "restaurantId": 1,
  "restaurantName": "Буковинська хата",
  "targetType": "dish",
  "text": "Бануш як у Карпатах, бринза солона в міру. Порція велика."
}
```

### reviews — відгук типу `restaurant`

```json
{
  "_id": {
    "$oid": "66a000000000000000000a03"
  },
  "aspects": {
    "food": 5,
    "packaging": 4,
    "portionSize": 5
  },
  "author": {
    "customerId": 1,
    "displayName": "Олена К."
  },
  "createdAt": {
    "$date": "2026-10-01T19:10:00Z"
  },
  "orderId": 1,
  "orderNumber": "SV-000001",
  "rating": 5,
  "restaurantId": 1,
  "restaurantName": "Буковинська хата",
  "targetType": "restaurant",
  "text": "Усе гаряче, упаковка не протекла."
}
```

### reviews — відгук типу `courier`

```json
{
  "_id": {
    "$oid": "66a000000000000000000a04"
  },
  "author": {
    "customerId": 1,
    "displayName": "Олена К."
  },
  "courier": {
    "courierId": "courier:7",
    "displayName": "Максим"
  },
  "createdAt": {
    "$date": "2026-10-01T19:12:00Z"
  },
  "deliveryMinutes": 32,
  "orderId": 1,
  "orderNumber": "SV-000001",
  "rating": 5,
  "restaurantId": 1,
  "restaurantName": "Буковинська хата",
  "tags": [
    "ввічливий",
    "вчасно",
    "подзвонив заздалегідь"
  ],
  "targetType": "courier"
}
```

### reviews — мінімальний документ (лише оцінка)

```json
{
  "_id": {
    "$oid": "66a000000000000000000a05"
  },
  "author": {
    "customerId": 2,
    "displayName": "Андрій М."
  },
  "createdAt": {
    "$date": "2026-10-12T20:00:00Z"
  },
  "dishId": 201,
  "dishName": "Піца Маргарита 30 см",
  "rating": 3,
  "restaurantId": 2,
  "restaurantName": "Pizza Pronto",
  "targetType": "dish"
}
```

### discussions — гібрид (прев’ю + посилання)

```json
{
  "_id": {
    "$oid": "66a000000000000000000d01"
  },
  "createdAt": {
    "$date": "2026-10-02T10:00:00Z"
  },
  "lastActivityAt": {
    "$date": "2026-10-03T08:30:00Z"
  },
  "messagesCount": 4,
  "recentMessages": [
    {
      "messageId": {
        "$oid": "66a000000000000000000e02"
      },
      "authorName": "Буковинська хата",
      "text": "На кукурудзяній, з домашньою сметаною.",
      "createdAt": {
        "$date": "2026-10-02T10:20:00Z"
      }
    },
    {
      "messageId": {
        "$oid": "66a000000000000000000e03"
      },
      "authorName": "Андрій М.",
      "text": "А можна без шкварок?",
      "createdAt": {
        "$date": "2026-10-03T08:00:00Z"
      }
    },
    {
      "messageId": {
        "$oid": "66a000000000000000000e04"
      },
      "authorName": "Буковинська хата",
      "text": "Так, напишіть у коментарі до позиції.",
      "createdAt": {
        "$date": "2026-10-03T08:30:00Z"
      }
    }
  ],
  "restaurantId": 1,
  "restaurantName": "Буковинська хата",
  "reviewId": {
    "$oid": "66a000000000000000000a01"
  },
  "title": "Питання про бануш"
}
```

### messages — повідомлення за посиланням

```json
{
  "_id": {
    "$oid": "66a000000000000000000e02"
  },
  "author": {
    "displayName": "Буковинська хата",
    "role": "restaurant"
  },
  "createdAt": {
    "$date": "2026-10-02T10:20:00Z"
  },
  "discussionId": {
    "$oid": "66a000000000000000000d01"
  },
  "replyTo": {
    "$oid": "66a000000000000000000e01"
  },
  "text": "На кукурудзяній, з домашньою сметаною."
}
```

### ratingSummaries — read-model

```json
{
  "_id": 1,
  "restaurantName": "Буковинська хата",
  "reviewsCount": 4,
  "avgRating": 4.75,
  "byType": {
    "courier": {
      "count": 1,
      "avg": 5
    },
    "dish": {
      "count": 2,
      "avg": 4.5
    },
    "restaurant": {
      "count": 1,
      "avg": 5
    }
  },
  "updatedAt": {
    "$date": "2026-10-25T13:20:15.248Z"
  }
}
```

