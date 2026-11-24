using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.IdGenerators;
using Reviews.Domain.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence.Serializers;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// BSON-мапінг доменних класів БЕЗ атрибутів у Domain (домен не знає, як його зберігають).
/// Реєструється ОДИН раз при старті (AddInfrastructure), до першого звернення до БД.
///
/// Результат у БД — структура з Лаб. 01:
///   { _id, targetType: "dish", restaurantId, restaurantName, dishId, dishName,
///     author: { customerId, displayName }, rating: 5, aspects: {...}, photos: [...],
///     comments: [ { authorName, text, createdAt } ], createdAt, updatedAt, version }
/// </summary>
public static class MongoMappings
{
    private static readonly object Sync = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Sync)
        {
            if (_registered) return;   // повторна реєстрація кинула б виняток (напр. у тестах)

            // ---- конвенції для ВСІХ типів (у т.ч. DTO для проєкцій) ----
            ConventionRegistry.Register("smakoviz", new ConventionPack
            {
                new CamelCaseElementNameConvention(),     // RestaurantName → restaurantName
                new IgnoreExtraElementsConvention(true),  // зайві поля старих документів не ламають читання
                new IgnoreIfNullConvention(true)          // null-поля не пишуться (гнучка схема: dishId лише у відгуку на страву)
            }, _ => true);

            // ---- власні серіалізатори: VO та enum ----
            BsonSerializer.RegisterSerializer(new RatingSerializer());
            BsonSerializer.RegisterSerializer(new CamelCaseEnumSerializer<ReviewTarget>());
            BsonSerializer.RegisterSerializer(new CamelCaseEnumSerializer<MessageRole>());

            // ---- базова сутність ----
            BsonClassMap.RegisterClassMap<BaseEntity>(cm =>
            {
                cm.AutoMap();
                cm.SetIsRootClass(false);   // без поля-дискримінатора "_t" у документах
                cm.MapIdMember(e => e.Id).SetIdGenerator(ObjectIdGenerator.Instance);
            });

            // ---- агрегати: приватні поля колекцій мапляться під публічними іменами ----
            BsonClassMap.RegisterClassMap<Review>(cm =>
            {
                cm.AutoMap();                                         // властивості з private set
                cm.MapField("_comments").SetElementName("comments");  // приватне поле → масив comments
                cm.MapField("_photos").SetElementName("photos");
            });

            BsonClassMap.RegisterClassMap<Discussion>(cm =>
            {
                cm.AutoMap();
                cm.MapField("_recentMessages").SetElementName("recentMessages");
            });

            BsonClassMap.RegisterClassMap<Message>(cm => cm.AutoMap());

            // ---- вкладений документ масиву comments: незмінний клас, створення через конструктор ----
            BsonClassMap.RegisterClassMap<Comment>(cm =>
            {
                cm.MapProperty(c => c.AuthorName);
                cm.MapProperty(c => c.Text);
                cm.MapProperty(c => c.CreatedAt);
                cm.MapCreator(c => new Comment(c.AuthorName, c.Text, c.CreatedAt));
            });

            // ---- Value Objects: лише get-властивості → створення через конструктор (з валідацією) ----
            BsonClassMap.RegisterClassMap<Author>(cm =>
            {
                cm.MapProperty(a => a.CustomerId);
                cm.MapProperty(a => a.DisplayName);
                cm.MapCreator(a => new Author(a.CustomerId, a.DisplayName));
            });

            BsonClassMap.RegisterClassMap<RatingAspects>(cm =>
            {
                cm.MapProperty(a => a.Food);
                cm.MapProperty(a => a.Packaging);
                cm.MapProperty(a => a.PortionSize);
                cm.MapCreator(a => new RatingAspects(a.Food, a.Packaging, a.PortionSize));
            });

            BsonClassMap.RegisterClassMap<MessageAuthor>(cm =>
            {
                cm.MapProperty(a => a.CustomerId);
                cm.MapProperty(a => a.DisplayName);
                cm.MapProperty(a => a.Role);
                cm.MapCreator(a => new MessageAuthor(a.CustomerId, a.DisplayName, a.Role));
            });

            BsonClassMap.RegisterClassMap<MessagePreview>(cm =>
            {
                cm.MapProperty(p => p.MessageId);
                cm.MapProperty(p => p.AuthorName);
                cm.MapProperty(p => p.Text);
                cm.MapProperty(p => p.CreatedAt);
                cm.MapCreator(p => new MessagePreview(p.MessageId, p.AuthorName, p.Text, p.CreatedAt));
            });

            _registered = true;
        }
    }

    /// <summary>Безпечний парсинг рядкового ObjectId (невалідні рядки відсікає валідатор ще раніше).</summary>
    public static ObjectId ToObjectId(string id) => ObjectId.TryParse(id, out var oid) ? oid : ObjectId.Empty;
}
