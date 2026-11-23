using Delivery.Bll.Dtos;
using Delivery.Bll.Services;
using Delivery.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace Delivery.Api.Controllers;

/// <summary>Замовлення доставки: оформлення, позиції, життєвий цикл статусів.</summary>
[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public class OrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>Пошук замовлень (за клієнтом і/або статусом), нові зверху.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> Search(
        [FromQuery] long? customerId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await orders.SearchAsync(customerId, status, page, pageSize, ct));

    /// <summary>Повне замовлення: деталі доставки, позиції, історія статусів.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetById(long id, CancellationToken ct)
        => Ok(await orders.GetByIdAsync(id, ct));

    /// <summary>
    /// Оформити замовлення (одна транзакція: замовлення + деталі + усі позиції).
    /// Заголовок Idempotency-Key (необов'язковий): повтор з тим самим ключем поверне те саме замовлення (200),
    /// а не створить друге.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<OrderDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrderDto>> Place(
        PlaceOrderDto input,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await orders.PlaceOrderAsync(input, idempotencyKey, ct);
        return result.Created
            ? CreatedAtAction(nameof(GetById), new { id = result.Order.Id }, result.Order)
            : Ok(result.Order);
    }

    /// <summary>Додати страву до замовлення (лише в статусі Placed).</summary>
    [HttpPost("{id:long}/lines")]
    [ProducesResponseType<OrderDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrderDto>> AddLine(long id, AddOrderLineDto input, CancellationToken ct)
        => Ok(await orders.AddLineAsync(id, input, ct));

    /// <summary>Підтвердити замовлення. Повторне підтвердження — 409.</summary>
    [HttpPost("{id:long}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Confirm(long id, CancellationToken ct)
    {
        await orders.ConfirmAsync(id, ct);
        return NoContent();
    }

    /// <summary>Скасувати замовлення (лише до оплати; інакше 409).</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancelOrderDto? input, CancellationToken ct)
    {
        await orders.CancelAsync(id, input?.Reason, ct);
        return NoContent();
    }

    /// <summary>
    /// Змінити статус (Paid, Cooking, OnTheWay, Delivered ...). Недопустимий перехід — 409;
    /// застарілий RowVersion — 409.
    /// </summary>
    [HttpPost("{id:long}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatus(long id, ChangeOrderStatusDto input, CancellationToken ct)
    {
        await orders.ChangeStatusAsync(id, input, ct);
        return NoContent();
    }

    /// <summary>М'яко видалити завершене замовлення (Delivered / Cancelled; активне — 422).</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await orders.DeleteAsync(id, ct);
        return NoContent();
    }
}
