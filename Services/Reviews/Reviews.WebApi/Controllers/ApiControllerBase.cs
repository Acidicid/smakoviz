using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Reviews.WebApi.Controllers;

/// <summary>
/// Базовий контролер (за зразком jasontaylordev/CleanArchitecture): дає доступ до ISender (MediatR).
/// Контролери лише перетворюють HTTP на команду/запит і відправляють її — жодної логіки,
/// жодного IMongoCollection.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>Версія документа з заголовка If-Match ("3" або 3); null — якщо заголовка немає.</summary>
    protected int? IfMatchVersion()
    {
        var raw = Request.Headers.IfMatch.ToString().Trim().Trim('"');
        if (raw.StartsWith("W/", StringComparison.Ordinal)) raw = raw[2..].Trim('"');
        return int.TryParse(raw, out var version) ? version : null;
    }

    /// <summary>ETag = номер версії документа (для подальшого If-Match).</summary>
    protected void SetETag(int version) => Response.Headers.ETag = $"\"{version}\"";
}
