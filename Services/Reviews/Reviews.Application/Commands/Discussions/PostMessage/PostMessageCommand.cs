using AutoMapper;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Commands.Discussions.PostMessage;

/// <summary>
/// Написати в обговорення. Змінює ДВІ колекції (гібридна модель):
///   discussions — лічильник і прев'ю останніх 3 повідомлень (з перевіркою версії);
///   messages    — сам документ повідомлення.
/// </summary>
public sealed record PostMessageCommand(
    string DiscussionId,
    long? CustomerId,
    string AuthorName,
    string Role,
    string Text,
    string? ReplyTo = null) : ICommand<MessageDto>;

public sealed class PostMessageCommandHandler(
    IDiscussionRepository discussions,
    IMessageRepository messages,
    IMapper mapper) : IRequestHandler<PostMessageCommand, MessageDto>
{
    /// <summary>Скільки разів повторити оновлення прев'ю, якщо паралельно написав хтось інший.</summary>
    private const int MaxAttempts = 3;

    public async Task<MessageDto> Handle(PostMessageCommand cmd, CancellationToken ct)
    {
        var discussionId = ObjectId.Parse(cmd.DiscussionId);
        EnumText.TryParse<MessageRole>(cmd.Role, out var role);

        var message = new Message(
            discussionId,
            new MessageAuthor(cmd.CustomerId, cmd.AuthorName, role),
            cmd.Text,
            cmd.ReplyTo is null ? null : ObjectId.Parse(cmd.ReplyTo));

        // Оптимістична конкурентність: два повідомлення одночасно → одне з оновлень отримає конфлікт версій;
        // перечитуємо обговорення і пробуємо ще раз (до 3 разів).
        for (var attempt = 1; ; attempt++)
        {
            var discussion = await discussions.GetByIdAsync(discussionId, ct)
                ?? throw new NotFoundException(nameof(Discussion), cmd.DiscussionId);

            discussion.RegisterMessage(message);
            try
            {
                await discussions.UpdateAsync(discussion, ct);
                break;
            }
            catch (ConflictException) when (attempt < MaxAttempts)
            {
                // хтось оновив обговорення між читанням і записом — повторюємо зі свіжою версією
            }
        }

        await messages.AddAsync(message, ct);
        return mapper.Map<MessageDto>(message);
    }
}

public sealed class PostMessageCommandValidator : AbstractValidator<PostMessageCommand>
{
    public PostMessageCommandValidator()
    {
        RuleFor(x => x.DiscussionId).MustBeObjectId();
        RuleFor(x => x.ReplyTo).MustBeObjectIdIfPresent();
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).NotEmpty().MustBeEnum<PostMessageCommand, MessageRole>();
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Message.MaxTextLength);
        RuleFor(x => x.CustomerId)
            .NotNull().GreaterThan(0)
            .When(x => EnumText.TryParse<MessageRole>(x.Role, out var r) && r == MessageRole.Customer)
            .WithMessage("customerId is required for a customer message");
    }
}
