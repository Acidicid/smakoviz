using Menu.Api.Infrastructure;
using Menu.Bll.Dtos;
using Menu.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Menu.Api.Controllers;

/// <summary>Ресторани-партнери.</summary>
[ApiController]
[Route("api/restaurants")]
[Produces("application/json")]
public class RestaurantsController(IRestaurantService restaurants) : ControllerBase
{
    /// <summary>Список ресторанів з пагінацією, фільтрами (місто, кухня) і сортуванням.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<RestaurantDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<RestaurantDto>>> GetList([FromQuery] RestaurantQuery query, CancellationToken ct)
    {
        var page = await restaurants.GetListAsync(query, ct);
        Response.AddPagination(page);
        return Ok(page);
    }

    /// <summary>Ресторан з профілем і меню (Explicit Loading: окремі SQL-запити).</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<RestaurantDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantDetailsDto>> GetById(long id, [FromQuery] bool onlyAvailable = true,
        CancellationToken ct = default)
        => Ok(await restaurants.GetByIdAsync(id, onlyAvailable, ct));

    /// <summary>Створити ресторан (за бажанням — одразу з профілем). Зайнятий slug — 409.</summary>
    [HttpPost]
    [ProducesResponseType<RestaurantDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RestaurantDetailsDto>> Create(CreateRestaurantDto input, CancellationToken ct)
    {
        var created = await restaurants.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Створити або оновити профіль ресторану (1:1).</summary>
    [HttpPut("{id:long}/profile")]
    [ProducesResponseType<RestaurantDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RestaurantDetailsDto>> SetProfile(long id, RestaurantProfileInputDto input,
        CancellationToken ct)
        => Ok(await restaurants.SetProfileAsync(id, input, ct));

    /// <summary>М'яко видалити ресторан. Якщо в нього є страви — 409.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await restaurants.DeleteAsync(id, ct);
        return NoContent();
    }
}
