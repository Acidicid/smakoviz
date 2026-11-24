using System.Diagnostics;
using AutoMapper;
using FluentValidation;
using Menu.Bll.Dtos;
using Menu.Bll.Specifications;
using Menu.Bll.Validation;
using Menu.Dal.Repositories;
using Menu.Domain.Entities;
using Menu.Domain.Exceptions;

using Microsoft.Extensions.Logging;

namespace Menu.Bll.Services;

/// <summary>
/// Страви. Бізнес-правила самої страви (ціна &gt; 0, вистачає порцій) перевіряє сутність Dish і кидає
/// DomainException (→ 409); сервіс перевіряє правила, для яких потрібні інші дані з БД
/// (ресторан і категорії існують, назва унікальна в ресторані) і зберігає зміни ОДНИМ SaveChangesAsync.
/// </summary>
public sealed class DishService(
    IUnitOfWork uow,
    IMapper mapper,
    IValidator<DishQuery> queryValidator,
    IValidator<CreateDishDto> createValidator,
    IValidator<ChangePriceDto> priceValidator,
    ILogger<DishService> logger) : IDishService
{
    /// <summary>Власні spans бізнес-операцій (Лаб. 05), підключені в ServiceDefaults через AddSource("SmakoViz.*").</summary>
    public static readonly ActivitySource ActivitySource = new("SmakoViz.Menu");

    public async Task<PagedResult<DishListItemDto>> GetListAsync(DishQuery query, CancellationToken ct = default)
    {
        await queryValidator.ValidateOrThrowAsync(query, ct);

        // фільтри, сортування і Skip/Take — у специфікації; база повертає лише потрібну сторінку
        var items = await uow.Dishes.ListAsync(new DishesFilterSpec(query), ct);
        var total = await uow.Dishes.CountAsync(new DishesFilterSpec(query, applyPaging: false), ct);

        return new PagedResult<DishListItemDto>(
            mapper.Map<List<DishListItemDto>>(items), total, query.Page, Paging.ClampPageSize(query.PageSize));
    }

    public async Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default)
        => mapper.Map<DishDto>(await LoadAsync(id, ct));

    public async Task<DishDto> CreateAsync(CreateDishDto input, CancellationToken ct = default)
    {
        await createValidator.ValidateOrThrowAsync(input, ct);

        if (await uow.Restaurants.GetByIdAsync(input.RestaurantId, ct) is null)
            throw new EntityNotFoundException(nameof(Restaurant), input.RestaurantId);

        var name = input.Name.Trim();
        if (await uow.Dishes.ExistsByNameAsync(input.RestaurantId, name, ct: ct))
            throw new ConflictException($"Restaurant {input.RestaurantId} already has a dish named '{name}'");

        foreach (var categoryId in input.CategoryIds)
            if (await uow.Categories.GetByIdAsync(categoryId, ct) is null)
                throw new EntityNotFoundException(nameof(Category), categoryId);

        var dish = new Dish(input.RestaurantId, name, input.Price, input.Description, input.WeightGrams, input.Calories);
        if (input.InitialPortions > 0)
            dish.AddPortions(input.InitialPortions);

        var now = DateTime.UtcNow;
        foreach (var categoryId in input.CategoryIds)
            dish.AddToCategory(categoryId, isFeatured: false, now);

        await uow.Dishes.AddAsync(dish, ct);
        await uow.SaveChangesAsync(ct);   // страва + зв'язки з категоріями — одна транзакція

        return await GetByIdAsync(dish.Id, ct);
    }

    public async Task<DishDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("ChangeDishPrice");
        activity?.SetTag("dish.id", id);

        await priceValidator.ValidateOrThrowAsync(input, ct);

        var dish = await LoadAsync(id, ct);
        var oldPrice = dish.Price;
        if (input.RowVersion is not null)
            uow.Dishes.SetExpectedRowVersion(dish, Convert.FromBase64String(input.RowVersion));

        dish.ChangePrice(input.NewPrice);    // правило «ціна > 0 і змінилась» — у сутності
        await uow.SaveChangesAsync(ct);      // чужа зміна між GET і PUT → ConflictException (409)

        activity?.SetTag("dish.new_price", dish.Price);
        logger.LogInformation("Dish {DishId} of restaurant {RestaurantId} price changed {OldPrice} -> {NewPrice}",
            dish.Id, dish.RestaurantId, oldPrice, dish.Price);

        // У Лаб. 08 тут публікуватиметься подія DishPriceChanged для копії в сервісі «Доставка»
        return mapper.Map<DishDto>(dish);
    }

    public async Task<DishDto> AddPortionsAsync(long id, int quantity, CancellationToken ct = default)
    {
        var dish = await LoadAsync(id, ct);
        dish.AddPortions(quantity);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<DishDto>(dish);
    }

    public async Task<DishDto> RemovePortionsAsync(long id, int quantity, CancellationToken ct = default)
    {
        var dish = await LoadAsync(id, ct);
        dish.RemovePortions(quantity);       // «Only N portions available» → DomainException → 409
        await uow.SaveChangesAsync(ct);
        return mapper.Map<DishDto>(dish);
    }

    public async Task<DishDto> SetAvailabilityAsync(long id, bool isAvailable, CancellationToken ct = default)
    {
        var dish = await LoadAsync(id, ct);
        if (isAvailable) dish.MarkAvailable();
        else dish.MarkUnavailable();
        await uow.SaveChangesAsync(ct);
        return mapper.Map<DishDto>(dish);
    }

    public async Task<DishDto> AddToCategoryAsync(long id, AddDishToCategoryDto input, CancellationToken ct = default)
    {
        var dish = await LoadAsync(id, ct);   // з категоріями — сутність сама перевірить дубль

        if (await uow.Categories.GetByIdAsync(input.CategoryId, ct) is null)
            throw new EntityNotFoundException(nameof(Category), input.CategoryId);

        dish.AddToCategory(input.CategoryId, input.IsFeatured, DateTime.UtcNow);
        await uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<DishDto> RemoveFromCategoryAsync(long id, int categoryId, CancellationToken ct = default)
    {
        var dish = await LoadAsync(id, ct);
        dish.RemoveFromCategory(categoryId);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<DishDto>(dish);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var dish = await uow.Dishes.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Dish), id);
        uow.Dishes.Delete(dish);             // м'яке видалення
        await uow.SaveChangesAsync(ct);
    }

    /// <summary>Eager Loading: страва + ресторан + категорії одним запитом (відстежується для змін).</summary>
    private async Task<Dish> LoadAsync(long id, CancellationToken ct)
        => await uow.Dishes.GetWithDetailsAsync(id, ct)
           ?? throw new EntityNotFoundException(nameof(Dish), id);
}
