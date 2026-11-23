using AutoMapper;
using FluentValidation;
using Menu.Bll.Dtos;
using Menu.Bll.Validation;
using Menu.Dal.Repositories;
using Menu.Domain.Entities;
using Menu.Domain.Exceptions;

namespace Menu.Bll.Services;

public sealed class CategoryService(
    IUnitOfWork uow,
    IMapper mapper,
    IValidator<CreateCategoryDto> createValidator) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryWithStatsDto>> GetAllAsync(CancellationToken ct = default)
    {
        var categories = await uow.Categories.GetAllAsync(ct);                        // довідник — кілька рядків
        var counts = await uow.DishCategories.CountAvailableDishesByCategoryAsync(ct); // GROUP BY по проміжній таблиці

        return categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryWithStatsDto(c.Id, c.Name, c.Slug, c.SortOrder,
                counts.TryGetValue(c.Id, out var n) ? n : 0))
            .ToList();
    }

    public async Task<CategoryDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var category = await uow.Categories.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Category), id);
        return mapper.Map<CategoryDto>(category);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryDto input, CancellationToken ct = default)
    {
        await createValidator.ValidateOrThrowAsync(input, ct);

        var slug = input.Slug.Trim().ToLowerInvariant();
        var name = input.Name.Trim();
        if (await uow.Categories.ExistsBySlugOrNameAsync(slug, name, ct))
            throw new ConflictException($"Category '{name}' or slug '{slug}' already exists");

        var category = new Category(name, slug, input.SortOrder);
        await uow.Categories.AddAsync(category, ct);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<CategoryDto>(category);
    }

    public async Task<IReadOnlyList<DishListItemDto>> GetRecentlyAddedAsync(string slug, int days, CancellationToken ct = default)
    {
        if (days is < 1 or > 365)
            throw new Menu.Domain.Exceptions.ValidationException(new Dictionary<string, string[]> { ["days"] = ["days must be between 1 and 365"] });

        if (await uow.Categories.GetBySlugAsync(slug, ct) is null)
            throw new EntityNotFoundException(nameof(Category), slug);

        var dishes = await uow.DishCategories.GetRecentlyAddedAsync(slug, DateTime.UtcNow.AddDays(-days), ct);
        return mapper.Map<List<DishListItemDto>>(dishes);
    }

    public async Task<IReadOnlyList<DishListItemDto>> GetFeaturedAsync(int id, int take, CancellationToken ct = default)
    {
        if (await uow.Categories.GetByIdAsync(id, ct) is null)
            throw new EntityNotFoundException(nameof(Category), id);

        var dishes = await uow.DishCategories.GetFeaturedAsync(id, Math.Clamp(take, 1, 50), ct);
        return mapper.Map<List<DishListItemDto>>(dishes);
    }
}
