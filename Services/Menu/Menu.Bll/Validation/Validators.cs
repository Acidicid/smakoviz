using FluentValidation;
using Menu.Bll.Dtos;
using Menu.Bll.Specifications;

namespace Menu.Bll.Validation;

// Складні правила — тут, в окремих класах FluentValidation (прості Required/StringLength — на DTO).
// Реєструються в Program.cs через AddValidatorsFromAssemblyContaining і викликаються ЯВНО
// в сервісах (ValidateOrThrowAsync), без застарілого AddFluentValidation.

public sealed class CreateRestaurantDtoValidator : AbstractValidator<CreateRestaurantDto>
{
    public CreateRestaurantDtoValidator()
    {
        RuleFor(x => x.Slug)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Slug may contain only lowercase latin letters, digits and single hyphens, e.g. 'pizza-pronto'");

        RuleFor(x => x.Profile!)
            .SetValidator(new RestaurantProfileInputDtoValidator())
            .When(x => x.Profile is not null);
    }
}

public sealed class RestaurantProfileInputDtoValidator : AbstractValidator<RestaurantProfileInputDto>
{
    public RestaurantProfileInputDtoValidator()
    {
        RuleFor(x => x.Phone)
            .Matches(@"^\+380\d{9}$")
            .WithMessage("Phone must look like +380XXXXXXXXX");

        RuleFor(x => x.ClosesAt)
            .NotEqual(x => x.OpensAt)
            .WithMessage("Closing time must differ from opening time");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);
    }
}

public sealed class CreateDishDtoValidator : AbstractValidator<CreateDishDto>
{
    public CreateDishDtoValidator()
    {
        RuleFor(x => x.Price)
            .GreaterThan(0)
            .LessThanOrEqualTo(100_000)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Price must be between 0.01 and 100000 with at most 2 decimal places");

        RuleFor(x => x.WeightGrams).GreaterThan(0).When(x => x.WeightGrams is not null);
        RuleFor(x => x.Calories).InclusiveBetween(0, 5000).When(x => x.Calories is not null);
        RuleFor(x => x.InitialPortions).InclusiveBetween(0, 1000);

        RuleFor(x => x.CategoryIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Category ids must be unique")
            .Must(ids => ids.Count <= 5)
            .WithMessage("A dish can be in at most 5 categories");
        RuleForEach(x => x.CategoryIds).GreaterThan(0);
    }
}

public sealed class ChangePriceDtoValidator : AbstractValidator<ChangePriceDto>
{
    public ChangePriceDtoValidator()
    {
        RuleFor(x => x.NewPrice)
            .GreaterThan(0)
            .LessThanOrEqualTo(100_000)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);

        RuleFor(x => x.RowVersion)
            .Must(v => BeBase64RowVersion(v!))
            .When(x => x.RowVersion is not null)
            .WithMessage("RowVersion must be the base64 value returned by GET");
    }

    private static bool BeBase64RowVersion(string value)
    {
        var buffer = new byte[8];
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 8;
    }
}

public sealed class CreateCategoryDtoValidator : AbstractValidator<CreateCategoryDto>
{
    public CreateCategoryDtoValidator()
    {
        RuleFor(x => x.Slug)
            .Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Slug may contain only lowercase latin letters, digits and single hyphens");
    }
}

/// <summary>Сортування — лише за дозволеними полями; діапазон цін — коректний.</summary>
public sealed class DishQueryValidator : AbstractValidator<DishQuery>
{
    public DishQueryValidator()
    {
        RuleFor(x => x.SortBy)
            .Must(s => DishesFilterSpec.SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"sortBy must be one of: {string.Join(", ", DishesFilterSpec.SortFields)}");

        RuleFor(x => x.SortDir)
            .Must(BeSortDirection)
            .WithMessage("sortDir must be 'asc' or 'desc'");

        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).When(x => x.MinPrice is not null);

        RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice!.Value)
            .When(x => x.MinPrice is not null && x.MaxPrice is not null)
            .WithMessage("maxPrice must be greater than or equal to minPrice");
    }

    internal static bool BeSortDirection(string dir) =>
        string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
}

public sealed class RestaurantQueryValidator : AbstractValidator<RestaurantQuery>
{
    public RestaurantQueryValidator()
    {
        RuleFor(x => x.SortBy)
            .Must(s => RestaurantsFilterSpec.SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"sortBy must be one of: {string.Join(", ", RestaurantsFilterSpec.SortFields)}");

        RuleFor(x => x.SortDir)
            .Must(DishQueryValidator.BeSortDirection)
            .WithMessage("sortDir must be 'asc' or 'desc'");
    }
}
