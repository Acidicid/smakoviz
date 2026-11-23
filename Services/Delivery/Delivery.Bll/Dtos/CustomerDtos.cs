using System.ComponentModel.DataAnnotations;

namespace Delivery.Bll.Dtos;

/// <summary>Клієнт у відповіді API. RowVersion — base64; його треба повернути в PUT.</summary>
public record CustomerDto(
    long Id,
    string Email,
    string FullName,
    string? Phone,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string RowVersion);

/// <summary>
/// Вхід для створення клієнта. Атрибути DataAnnotations — модельна валідація на рівні API:
/// некоректний запит відхиляється з 400 ще до виклику сервісу.
/// </summary>
public class CreateCustomerDto
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    /// <summary>Український номер у форматі +380XXXXXXXXX (як CHECK у БД).</summary>
    [RegularExpression(@"^\+380\d{9}$", ErrorMessage = "Phone must look like +380XXXXXXXXX")]
    public string? Phone { get; set; }
}

public class UpdateCustomerDto : CreateCustomerDto
{
    /// <summary>RowVersion з останнього GET — захист від затирання чужих змін.</summary>
    [Required]
    public string RowVersion { get; set; } = "";
}
