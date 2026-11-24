namespace Reviews.Domain.Exceptions;

/// <summary>Порушення бізнес-правила сутності або Value Object (→ 400 Bad Request).</summary>
public class DomainException(string message) : Exception(message);
