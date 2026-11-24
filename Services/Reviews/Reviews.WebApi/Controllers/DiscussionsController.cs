using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Commands.Discussions.CreateDiscussion;
using Reviews.Application.Commands.Discussions.PostMessage;
using Reviews.Application.Common.Models;
using Reviews.Application.Queries.Discussions.GetDiscussionById;
using Reviews.Application.Queries.Discussions.GetDiscussionMessages;

namespace Reviews.WebApi.Controllers;

/// <summary>Обговорення біля ресторанів і відгуків (гібрид: прев'ю в discussions + історія в messages).</summary>
[Route("api/discussions")]
public class DiscussionsController : ApiControllerBase
{
    /// <summary>Обговорення з прев'ю останніх 3 повідомлень.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType<DiscussionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiscussionDto>> GetById(string id, CancellationToken ct)
    {
        var discussion = await Sender.Send(new GetDiscussionByIdQuery(id), ct);
        SetETag(discussion.Version);
        return Ok(discussion);
    }

    /// <summary>Повна історія повідомлень з пагінацією.</summary>
    [HttpGet("{id}/messages")]
    [ProducesResponseType<PagedResult<MessageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<MessageDto>>> GetMessages(string id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize, CancellationToken ct = default)
        => Ok(await Sender.Send(new GetDiscussionMessagesQuery(id, page, pageSize), ct));

    /// <summary>Відкрити обговорення (за бажанням — біля відгуку reviewId) → 201.</summary>
    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreatedResponse>> Create(CreateDiscussionCommand command, CancellationToken ct)
    {
        var id = await Sender.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new CreatedResponse(id));
    }

    /// <summary>Написати повідомлення → 201.</summary>
    [HttpPost("{id}/messages")]
    [ProducesResponseType<MessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MessageDto>> PostMessage(string id, PostMessageRequest request, CancellationToken ct)
    {
        var message = await Sender.Send(new PostMessageCommand(
            id, request.CustomerId, request.AuthorName, request.Role, request.Text, request.ReplyTo), ct);
        return CreatedAtAction(nameof(GetMessages), new { id }, message);
    }
}

/// <summary>Повідомлення в обговорення. Role: customer | restaurant | moderator.</summary>
public sealed record PostMessageRequest(long? CustomerId, string AuthorName, string Role, string Text, string? ReplyTo);
