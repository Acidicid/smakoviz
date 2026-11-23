using AutoMapper;
using FluentValidation;
using Menu.Bll.Dtos;
using Menu.Bll.Specifications;
using Menu.Bll.Validation;
using Menu.Dal.Repositories;
using Menu.Domain.Entities;
using Menu.Domain.Exceptions;

namespace Menu.Bll.Services;

/// <summary>
/// Ресторани. Сервіс не працює з EF напряму — лише через Unit of Work і репозиторії.
/// Правила самої сутності (формат slug, години) перевіряє сутність; правила, що потребують
/// даних з БД (унікальність slug, наявність страв перед видаленням), — сервіс.
/// </summary>
public sealed class RestaurantService(
    IUnitOfWork uow,
    IMapper mapper,
    IValidator<RestaurantQuery> queryValidator,
    IValidator<CreateRestaurantDto> createValidator,
    IValidator<RestaurantProfileInputDto> profileValidator) : IRestaurantService
{
    public async Task<PagedResult<RestaurantDto>> GetListAsync(RestaurantQuery query, CancellationToken ct = default)
    {
        await queryValidator.ValidateOrThrowAsync(query, ct);

        // одні й ті самі критерії: сторінка + загальна кількість (обидва запити виконує БД)
        var items = await uow.Restaurants.ListAsync(new RestaurantsFilterSpec(query), ct);
        var total = await uow.Restaurants.CountAsync(new RestaurantsFilterSpec(query, applyPaging: false), ct);

        return new PagedResult<RestaurantDto>(
            mapper.Map<List<RestaurantDto>>(items), total, query.Page, Paging.ClampPageSize(query.PageSize));
    }

    public async Task<RestaurantDetailsDto> GetByIdAsync(long id, bool onlyAvailableDishes, CancellationToken ct = default)
    {
        // Explicit Loading: ресторан, потім профіль, потім меню — окремими запитами
        var restaurant = await uow.Restaurants.GetWithMenuAsync(id, onlyAvailableDishes, ct)
            ?? throw new EntityNotFoundException(nameof(Restaurant), id);
        return mapper.Map<RestaurantDetailsDto>(restaurant);
    }

    public async Task<RestaurantDetailsDto> CreateAsync(CreateRestaurantDto input, CancellationToken ct = default)
    {
        await createValidator.ValidateOrThrowAsync(input, ct);

        var slug = input.Slug.Trim().ToLowerInvariant();
        if (await uow.Restaurants.ExistsBySlugAsync(slug, ct: ct))
            throw new ConflictException($"Restaurant with slug '{slug}' already exists");

        var restaurant = new Restaurant(input.Name, slug, input.City, input.CuisineType);
        if (input.Profile is { } p)
            restaurant.SetProfile(p.Address, p.Phone, p.OpensAt, p.ClosesAt, p.MinOrderAmount, p.Description);

        await uow.Restaurants.AddAsync(restaurant, ct);
        await uow.SaveChangesAsync(ct);   // один раз: ресторан + профіль в одній транзакції

        return mapper.Map<RestaurantDetailsDto>(restaurant);
    }

    public async Task<RestaurantDetailsDto> SetProfileAsync(long id, RestaurantProfileInputDto input, CancellationToken ct = default)
    {
        await profileValidator.ValidateOrThrowAsync(input, ct);

        var restaurant = await uow.Restaurants.GetWithProfileAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Restaurant), id);

        restaurant.SetProfile(input.Address, input.Phone, input.OpensAt, input.ClosesAt,
            input.MinOrderAmount, input.Description);
        await uow.SaveChangesAsync(ct);

        return await GetByIdAsync(id, onlyAvailableDishes: false, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var restaurant = await uow.Restaurants.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Restaurant), id);

        // бізнес-правило, що потребує БД: ресторан зі стравами видаляти не можна
        if (await uow.Restaurants.HasDishesAsync(id, ct))
            throw new ConflictException($"Restaurant {id} still has dishes. Delete or move them first");

        uow.Restaurants.Delete(restaurant);   // м'яке видалення (IsDeleted = 1) робить MenuDbContext
        await uow.SaveChangesAsync(ct);
    }
}
