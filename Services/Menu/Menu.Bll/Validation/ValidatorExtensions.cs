using FluentValidation;

namespace Menu.Bll.Validation;

internal static class ValidatorExtensions
{
    /// <summary>
    /// Явний виклик валідатора. Помилки → доменний ValidationException зі словником
    /// «поле → повідомлення», який глобальний обробник віддає як 400 ValidationProblemDetails.
    /// </summary>
    public static async Task ValidateOrThrowAsync<T>(this IValidator<T> validator, T instance, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(instance, ct);
        if (!result.IsValid)
            throw new Menu.Domain.Exceptions.ValidationException(
                new Dictionary<string, string[]>(result.ToDictionary()));
    }
}
