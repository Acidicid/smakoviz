using Menu.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Menu.Dal.Configurations;

/// <summary>Спільна конфігурація колонок аудиту, м'якого видалення й RowVersion.</summary>
internal static class AuditableConfiguration
{
    public static void ConfigureAudit<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.CreatedBy).HasMaxLength(100);
        builder.Property(e => e.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.UpdatedBy).HasMaxLength(100);
        builder.Property(e => e.IsDeleted).IsRequired();

        // ROWVERSION: SQL Server сам змінює значення при кожному UPDATE,
        // EF додає його у WHERE → паралельний запис дає DbUpdateConcurrencyException
        builder.Property(e => e.RowVersion).IsRowVersion();

        // глобальний фільтр: «видалені» рядки не потрапляють у жоден LINQ-запит
        builder.HasQueryFilter(e => !e.IsDeleted);
    }
}
