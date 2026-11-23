using Menu.Bll.Dtos;
using Menu.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Menu.Api.Controllers;

/// <summary>Категорії (довідник) і вибірки страв через проміжну сутність M:N.</summary>
[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController(ICategoryService categories) : ControllerBase
{
    /// <summary>Усі категорії + кількість доступних страв у кожній.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryWithStatsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryWithStatsDto>>> GetAll(CancellationToken ct)
        => Ok(await categories.GetAllAsync(ct));

    /// <summary>Категорія за Id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> GetById(int id, CancellationToken ct)
        => Ok(await categories.GetByIdAsync(id, ct));

    /// <summary>Створити категорію. Зайнята назва чи slug — 409.</summary>
    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryDto input, CancellationToken ct)
    {
        var created = await categories.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Страви, додані в категорію за останні N днів (поле AddedAt проміжної сутності).</summary>
    [HttpGet("{slug}/recent")]
    [ProducesResponseType<IReadOnlyList<DishListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DishListItemDto>>> GetRecent(string slug, [FromQuery] int days = 90,
        CancellationToken ct = default)
        => Ok(await categories.GetRecentlyAddedAsync(slug, days, ct));

    /// <summary>Рекомендовані страви категорії (поле IsFeatured проміжної сутності).</summary>
    [HttpGet("{id:int}/featured")]
    [ProducesResponseType<IReadOnlyList<DishListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<DishListItemDto>>> GetFeatured(int id, [FromQuery] int take = 10,
        CancellationToken ct = default)
        => Ok(await categories.GetFeaturedAsync(id, take, ct));
}
