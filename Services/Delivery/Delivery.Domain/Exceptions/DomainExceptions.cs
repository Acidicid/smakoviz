namespace Delivery.Domain.Exceptions;

/// <summary>
/// Базовий доменний виняток. DAL перетворює на нього SqlException, BLL кидає його
/// при порушенні бізнес-правил, а глобальний обробник в API — перетворює на HTTP-статус.
///   процедура (THROW 5000x) → DAL (catch SqlException) → доменний виняток → ProblemDetails
/// </summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>Ресурс не знайдено → 404. Процедура: THROW 50001.</summary>
public sealed class NotFoundException(string message) : DomainException(message);

/// <summary>
/// Конфлікт зі станом системи → 409: недопустимий перехід статусу (THROW 50002),
/// порушення UNIQUE (SQL 2601/2627).
/// </summary>
public class BusinessConflictException(string message) : DomainException(message);

/// <summary>
/// Запис змінив хтось інший (RowVersion не збігся, THROW 50003) → 409.
/// Оптимістична конкурентність: клієнт має перечитати дані й повторити.
/// </summary>
public sealed class ConcurrencyConflictException(string message) : BusinessConflictException(message);

/// <summary>
/// Різновид бізнес-конфлікту: запит коректний за форматом, але порушує бізнес-правило
/// (страва недоступна, порожнє замовлення, видалення активного замовлення; THROW 50004) → 422.
/// </summary>
public sealed class BusinessRuleException(string message) : BusinessConflictException(message);

/// <summary>Бізнес-валідація в BLL (дані з БД не дозволяють виконати запит) → 400.</summary>
public sealed class ValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : DomainException(message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        errors ?? new Dictionary<string, string[]>();
}
