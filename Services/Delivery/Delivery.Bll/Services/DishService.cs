using AutoMapper;
using Delivery.Bll.Dtos;
using Delivery.Dal.Abstractions;
using Delivery.Domain.Exceptions;
using Delivery.Domain.Models;

namespace Delivery.Bll.Services;

/// <summary>
/// Робота з локальною копією страв. Власник даних — «Меню»; цей сервіс лише читає копію
/// і приймає синхронізацію (без dual write: самі ціни тут ніхто не редагує).
/// </summary>
public sealed class DishService(IUnitOfWork uow, IMapper mapper) : IDishService
{
    public async Task<DishDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var dish = await uow.Dishes.GetByIdAsync(id, ct)
            ?? throw new NotFoundException($"Dish {id} not found");
        return mapper.Map<DishDto>(dish);
    }

    public async Task<IReadOnlyList<DishDto>> GetByRestaurantAsync(long restaurantId, bool onlyAvailable,
        CancellationToken ct = default)
    {
        if (restaurantId < 1)
            throw new ValidationException("Invalid restaurant id",
                new Dictionary<string, string[]> { ["restaurantId"] = ["restaurantId must be >= 1"] });

        var dishes = await uow.Dishes.GetByRestaurantAsync(restaurantId, onlyAvailable, ct);
        return mapper.Map<List<DishDto>>(dishes);
    }

    public async Task<DishDto> UpsertAsync(long id, UpsertDishDto input, CancellationToken ct = default)
    {
        if (id < 1)
            throw new ValidationException("Invalid dish id",
                new Dictionary<string, string[]> { ["id"] = ["Dish id must be >= 1 (the same id as in the Menu service)"] });

        await uow.Dishes.UpsertAsync(new Dish
        {
            Id = id,
            RestaurantId = input.RestaurantId,
            Name = input.Name.Trim(),
            Price = input.Price,
            IsAvailable = input.IsAvailable
        }, ct);

        return await GetByIdAsync(id, ct);
    }
}
