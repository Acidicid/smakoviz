using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Menu.Dal.Configurations;

/// <summary>
/// M:N Dish ↔ Category через ЯВНУ проміжну сутність: складений ключ + власні поля.
/// Автоматичне M:N в EF Core (skip navigations) не дало б зберегти AddedAt і IsFeatured.
/// </summary>
public class DishCategoryConfiguration : IEntityTypeConfiguration<DishCategory>
{
    public void Configure(EntityTypeBuilder<DishCategory> builder)
    {
        builder.ToTable("DishCategories");

        builder.HasKey(dc => new { dc.DishId, dc.CategoryId });   // складений ключ

        builder.Property(dc => dc.AddedAt)
            .HasColumnType("datetime2(3)")
            .HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(dc => dc.IsFeatured).IsRequired();

        builder.HasOne(dc => dc.Dish)
            .WithMany(d => d.DishCategories)
            .HasForeignKey(dc => dc.DishId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(dc => dc.Category)
            .WithMany(c => c.DishCategories)
            .HasForeignKey(dc => dc.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // PK починається з DishId, тож для «страв категорії» (DishCategoryRepository) потрібен окремий індекс:
        // спершу рекомендовані, потім нові
        builder.HasIndex(dc => new { dc.CategoryId, dc.IsFeatured, dc.AddedAt });

        // зв'язок для страви, видаленої м'яко, теж ховаємо (узгоджено з фільтром Dish)
        builder.HasQueryFilter(dc => !dc.Dish.IsDeleted);
    }
}
