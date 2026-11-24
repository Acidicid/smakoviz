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
                FixEntityMap(cm);
                cm.MapField("_comments").SetElementName("comments");  // приватне поле → масив comments
                cm.MapField("_photos").SetElementName("photos");
            });

            BsonClassMap.RegisterClassMap<Discussion>(cm =>
            {
                cm.AutoMap();
                FixEntityMap(cm);
                cm.MapField("_recentMessages").SetElementName("recentMessages");
            });

            BsonClassMap.RegisterClassMap<Message>(cm =>
            {
                cm.AutoMap();
                FixEntityMap(cm);
            });

            // ---- вкладений документ масиву comments: незмінний клас, створення через конструктор ----
            BsonClassMap.RegisterClassMap<Comment>(cm =>
            {
                cm.MapProperty(c => c.AuthorName);
                cm.MapProperty(c => c.Text);
                cm.MapProperty(c => c.CreatedAt);
                CamelCase(cm);
                cm.MapCreator(c => new Comment(c.AuthorName, c.Text, c.CreatedAt));
            });

            // ---- Value Objects: лише get-властивості → створення через конструктор (з валідацією) ----
            BsonClassMap.RegisterClassMap<Author>(cm =>
            {
                cm.MapProperty(a => a.CustomerId);
                cm.MapProperty(a => a.DisplayName);
                CamelCase(cm);
                cm.MapCreator(a => new Author(a.CustomerId, a.DisplayName));
            });

            BsonClassMap.RegisterClassMap<RatingAspects>(cm =>
            {
                cm.MapProperty(a => a.Food);
                cm.MapProperty(a => a.Packaging);
                cm.MapProperty(a => a.PortionSize);
                CamelCase(cm);
                cm.MapCreator(a => new RatingAspects(a.Food, a.Packaging, a.PortionSize));
            });

            BsonClassMap.RegisterClassMap<MessageAuthor>(cm =>
            {
                cm.MapProperty(a => a.CustomerId);
                cm.MapProperty(a => a.DisplayName);
                cm.MapProperty(a => a.Role);
                CamelCase(cm);
                cm.MapCreator(a => new MessageAuthor(a.CustomerId, a.DisplayName, a.Role));
            });

            BsonClassMap.RegisterClassMap<MessagePreview>(cm =>
            {
                cm.MapProperty(p => p.MessageId);
                cm.MapProperty(p => p.AuthorName);
                cm.MapProperty(p => p.Text);
                cm.MapProperty(p => p.CreatedAt);
                CamelCase(cm);
                cm.MapCreator(p => new MessagePreview(p.MessageId, p.AuthorName, p.Text, p.CreatedAt));
            });

            _registered = true;
        }
    }

    /// <summary>
    /// Нові версії MongoDB.Driver (3.14+) під час AutoMap() нащадка підхоплюють і УСПАДКОВАНІ властивості
    /// (Id, CreatedAt…), які вже змаплені в BaseEntity, і get-only властивості (Photos, Comments) → помилка «element name '_id' is already being used».
    /// Тому з карти нащадка прибираємо все, що оголошено не в ньому самому: ці поля серіалізує карта BaseEntity.
    /// </summary>
    private static void FixEntityMap(BsonClassMap cm)
    {
        foreach (var m in cm.DeclaredMemberMaps
                     .Where(m => m.MemberInfo.DeclaringType != cm.ClassType
                                 // а також обчислювані властивості без setter-а (Comments => _comments.AsReadOnly()):
                                 // їх серіалізує приватне поле (_comments → "comments"), інакше ім'я елемента дублюється
                                 || m.MemberInfo is System.Reflection.PropertyInfo { SetMethod: null })
                     .ToList())
            cm.UnmapMember(m.MemberInfo);

        // Нові версії драйвера вважають сутність із private set «незмінною» і самі додають creator map
        // з публічного конструктора (у Review — 12 параметрів, які не збігаються з полями) → помилка
        // «Creator map ... has 12 arguments, but none are configured». Прибираємо автоматичні creator map-и
        // і явно кажемо: створюй об'єкт приватним конструктором без параметрів, далі заповнюй поля.
        var creatorMapsField = typeof(BsonClassMap)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .FirstOrDefault(f => f.FieldType == typeof(List<BsonCreatorMap>));
        (creatorMapsField?.GetValue(cm) as List<BsonCreatorMap>)?.Clear();

        var parameterless = cm.ClassType.GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            Type.EmptyTypes);
        if (parameterless is not null) cm.MapConstructor(parameterless);
    }

    /// <summary>
    /// Карти, задані вручну (без AutoMap), не проходять конвенції — імена полів лишилися б PascalCase.
    /// Робимо їх camelCase, як у документах Лаб. 01, і дозволяємо зайві поля.
    /// </summary>
    private static void CamelCase(BsonClassMap cm)
    {
        foreach (var m in cm.DeclaredMemberMaps)
            m.SetElementName(char.ToLowerInvariant(m.MemberName[0]) + m.MemberName[1..]);
        cm.SetIgnoreExtraElements(true);
    }

    /// <summary>Безпечний парсинг рядкового ObjectId (невалідні рядки відсікає валідатор ще раніше).</summary>
    public static ObjectId ToObjectId(string id) => ObjectId.TryParse(id, out var oid) ? oid : ObjectId.Empty;
}
