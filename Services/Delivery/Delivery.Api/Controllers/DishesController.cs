using Delivery.Bll.Dtos;
using Delivery.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Delivery.Api.Controllers;

/// <summary>Локальна копія страв із сервісу «Меню».</summary>
[ApiController]
[Route("api/dishes")]
[Produces("application/json")]
public class DishesController(IDishService dishes) : ControllerBase
{
    /// <summary>Страви ресторану (за замовчуванням — лише доступні).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DishDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<DishDto>>> GetByRestaurant(
        [FromQuery] long restaurantId, [FromQuery] bool onlyAvailable = true, CancellationToken ct = default)
        => Ok(await dishes.GetByRestaurantAsync(restaurantId, onlyAvailable, ct));

    /// <summary>Страва за Id (той самий Id, що в «Меню»).</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DishDto>> GetById(long id, CancellationToken ct)
        => Ok(await dishes.GetByIdAsync(id, ct));

    /// <summary>
    /// Синхронізувати копію страви з «Меню» (створити або оновити).
    /// У Лаб. 08 це замінить обробник події DishChanged.
    /// </summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DishDto>> Upsert(long id, UpsertDishDto input, CancellationToken ct)
        => Ok(await dishes.UpsertAsync(id, input, ct));
}
