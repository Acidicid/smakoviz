namespace Menu.Domain.Exceptions;

/// <summary>
/// Порушення бізнес-правила самої сутності (ціна ≤ 0, замало порцій, недопустима зміна стану).
/// Кидає сутність. Глобальний обробник API → 409 Conflict.
/// </summary>
public class DomainException(string message) : Exception(message);

/// <summary>Сутність не знайдено → 404.</summary>
public sealed class EntityNotFoundException(string entity, object key)
    : Exception($"{entity} with id '{key}' was not found")
{
    public string Entity { get; } = entity;
    public object Key { get; } = key;
}

/// <summary>
/// Конфлікт зі станом БД: дубль унікального значення (slug, назва страви в ресторані),
/// паралельна зміна (RowVersion), видалення ресторану, у якого є страви → 409.
/// Кидає сервіс BLL (або UoW при DbUpdateException).
/// </summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Помилки валідації вхідних даних (FluentValidation) → 400 ValidationProblemDetails.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
