namespace Reviews.Application.Common.Exceptions;

/// <summary>Документ не знайдено → 404.</summary>
public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found")
{
    public string Entity { get; } = entity;
    public object Key { get; } = key;
}

/// <summary>Конфлікт: документ змінив хтось інший (Version не збігся) → 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
