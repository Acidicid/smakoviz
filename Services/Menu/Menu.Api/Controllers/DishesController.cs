using Menu.Api.Infrastructure;
using Menu.Bll.Dtos;
using Menu.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Menu.Api.Controllers;

/// <summary>
/// Страви. Тонкий асинхронний контролер: HTTP ↔ сервіс BLL. Ні DbContext, ні бізнес-правил,
/// ні try/catch — помилки обробляє GlobalExceptionHandler.
/// </summary>
[ApiController]
[Route("api/dishes")]
[Produces("application/json")]
public class DishesController(IDishService dishes) : ControllerBase
{
    /// <summary>
    /// Список страв з пагінацією, фільтрацією й сортуванням (усе — в SQL).
    /// Приклад: /api/dishes?search=піца&amp;minPrice=100&amp;sortBy=price&amp;sortDir=desc&amp;page=1&amp;pageSize=5
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DishListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DishListItemDto>>> GetList([FromQuery] DishQuery query, CancellationToken ct)
    {
        var page = await dishes.GetListAsync(query, ct);
        Response.AddPagination(page);
        return Ok(page);
    }

    /// <summary>Картка страви (Eager Loading: ресторан і категорії одним SQL-запитом).</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DishDto>> GetById(long id, CancellationToken ct)
        => Ok(await dishes.GetByIdAsync(id, ct));

    /// <summary>Створити страву. 409 — якщо в ресторані вже є страва з такою назвою.</summary>
    [HttpPost]
    [ProducesResponseType<DishDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DishDto>> Create(CreateDishDto input, CancellationToken ct)
    {
        var created = await dishes.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Змінити ціну (метод сутності ChangePrice). Застарілий RowVersion — 409.</summary>
    [HttpPut("{id:long}/price")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DishDto>> ChangePrice(long id, ChangePriceDto input, CancellationToken ct)
        => Ok(await dishes.ChangePriceAsync(id, input, ct));

    /// <summary>Додати приготовані порції (AddPortions — аналог AddStock).</summary>
    [HttpPost("{id:long}/portions")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DishDto>> AddPortions(long id, PortionsDto input, CancellationToken ct)
        => Ok(await dishes.AddPortionsAsync(id, input.Quantity, ct));

    /// <summary>Списати порції (RemovePortions — аналог RemoveStock). Більше, ніж є, — 409.</summary>
    [HttpPost("{id:long}/portions/remove")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DishDto>> RemovePortions(long id, PortionsDto input, CancellationToken ct)
        => Ok(await dishes.RemovePortionsAsync(id, input.Quantity, ct));

    /// <summary>Увімкнути / зняти страву з продажу.</summary>
    [HttpPut("{id:long}/availability")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DishDto>> SetAvailability(long id, AvailabilityDto input, CancellationToken ct)
        => Ok(await dishes.SetAvailabilityAsync(id, input.IsAvailable, ct));

    /// <summary>Додати страву в категорію (рядок у проміжній таблиці M:N). Повторно — 409.</summary>
    [HttpPost("{id:long}/categories")]
    [ProducesResponseType<DishDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DishDto>> AddToCategory(long id, AddDishToCategoryDto input, CancellationToken ct)
        => Ok(await dishes.AddToCategoryAsync(id, input, ct));

    /// <summary>Прибрати страву з категорії.</summary>
    [HttpDelete("{id:long}/categories/{categoryId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveFromCategory(long id, int categoryId, CancellationToken ct)
    {
        await dishes.RemoveFromCategoryAsync(id, categoryId, ct);
        return NoContent();
    }

    /// <summary>М'яко видалити страву.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await dishes.DeleteAsync(id, ct);
        return NoContent();
    }
}
