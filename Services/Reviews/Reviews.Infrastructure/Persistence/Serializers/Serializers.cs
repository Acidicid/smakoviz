using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Reviews.Domain.ValueObjects;

namespace Reviews.Infrastructure.Persistence.Serializers;

/// <summary>
/// Value Object Rating зберігається просто числом — rating: 5 (як у Лаб. 01 і валідаторі $jsonSchema),
/// а не вкладеним документом { value: 5 }. При читанні число знову проходить валідацію конструктора.
/// </summary>
public sealed class RatingSerializer : SerializerBase<Rating>
{
    public override Rating Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        => new(context.Reader.ReadInt32());

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Rating value)
        => context.Writer.WriteInt32(value.Value);
}

/// <summary>
/// Enum ↔ рядок у camelCase: ReviewTarget.Dish ↔ "dish", MessageRole.Customer ↔ "customer"
/// (саме такі значення дозволяє валідатор колекції з Лаб. 01).
/// </summary>
public sealed class CamelCaseEnumSerializer<TEnum> : SerializerBase<TEnum> where TEnum : struct, Enum
{
    public override TEnum Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var text = context.Reader.ReadString();
        return Enum.TryParse<TEnum>(text, ignoreCase: true, out var value)
            ? value
            : throw new FormatException($"'{text}' is not a valid {typeof(TEnum).Name}");
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEnum value)
    {
        var name = value.ToString();
        context.Writer.WriteString(char.ToLowerInvariant(name[0]) + name[1..]);
    }
}
