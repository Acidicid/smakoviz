using AutoMapper;
using MongoDB.Bson;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Common.Mappings;

/// <summary>
/// Мапінг «доменна сутність → DTO» (використовують обробники команд).
/// Обробляє BSON-типи (ObjectId → string), Value Objects (Rating → int, Author → AuthorDto),
/// enum → рядок як у БД, а також вкладені документи й масиви (comments, photos, recentMessages).
/// </summary>
public class ReviewProfile : Profile
{
    public ReviewProfile()
    {
        // ---- BSON-типи ----
        CreateMap<ObjectId, string>().ConvertUsing(id => id.ToString());
        CreateMap<ObjectId?, string?>().ConvertUsing(id => id.HasValue ? id.Value.ToString() : null);

        // ---- Value Objects і enum ----
        CreateMap<Rating, int>().ConvertUsing(r => r.Value);
        CreateMap<ReviewTarget, string>().ConvertUsing(t => EnumText.ToText(t));
        CreateMap<MessageRole, string>().ConvertUsing(r => EnumText.ToText(r));
        CreateMap<Author, AuthorDto>();
        CreateMap<RatingAspects, AspectsDto>();
        CreateMap<MessagePreview, MessagePreviewDto>();

        // ---- вкладені документи / масиви ----
        CreateMap<Comment, CommentDto>();

        // ---- сутності ----
        CreateMap<Review, ReviewDetailsDto>();             // Photos, Comments — масиви; Author, Aspects — вкладені
        CreateMap<Review, ReviewListItemDto>();            // AuthorName ← Author.DisplayName, CommentsCount ← Comments.Count
        CreateMap<Discussion, DiscussionDto>();
        CreateMap<Message, MessageDto>()
            .ForMember(d => d.AuthorName, o => o.MapFrom(s => s.Author.DisplayName))
            .ForMember(d => d.Role, o => o.MapFrom(s => s.Author.Role));
    }
}
