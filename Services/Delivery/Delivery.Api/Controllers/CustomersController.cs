using Delivery.Bll.Dtos;
using Delivery.Bll.Services;
using Delivery.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace Delivery.Api.Controllers;

/// <summary>
/// Клієнти сервісу доставки. Контролер — тонкий адаптер: прийняти HTTP, викликати сервіс,
/// повернути статус. Без SQL і без бізнес-правил; помилки → глобальний DomainExceptionHandler.
/// </summary>
[ApiController]
[Route("api/delivery/customers")]
[Produces("application/json")]
public class CustomersController(ICustomerService customers) : ControllerBase
{
    /// <summary>Пошук клієнтів за іменем або email з пагінацією.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<CustomerDto>>> Search(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await customers.SearchAsync(q, page, pageSize, ct));

    /// <summary>Клієнт за Id.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(long id, CancellationToken ct)
        => Ok(await customers.GetByIdAsync(id, ct));

    /// <summary>Створити клієнта. 409 — якщо email уже зайнятий.</summary>
    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerDto input, CancellationToken ct)
    {
        var created = await customers.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Оновити клієнта. Потрібен RowVersion з GET; якщо хтось змінив запис раніше — 409.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Update(long id, UpdateCustomerDto input, CancellationToken ct)
        => Ok(await customers.UpdateAsync(id, input, ct));

    /// <summary>М'яке видалення клієнта (IsDeleted = 1).</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }
}
