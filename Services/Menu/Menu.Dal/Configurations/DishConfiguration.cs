using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Menu.Dal.Configurations;

public class DishConfiguration : IEntityTypeConfiguration<Dish>
{
    public void Configure(EntityTypeBuilder<Dish> builder)
    {
        builder.ToTable("Dishes", t =>
        {
            t.HasCheckConstraint("CK_Dishes_Price", "[Price] > 0");
            t.HasCheckConstraint("CK_Dishes_AvailablePortions", "[AvailablePortions] >= 0");
            t.HasCheckConstraint("CK_Dishes_Weight", "[WeightGrams] IS NULL OR [WeightGrams] > 0");
            t.HasCheckConstraint("CK_Dishes_Calories", "[Calories] IS NULL OR [Calories] BETWEEN 0 AND 5000");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).UseIdentityColumn();

        builder.Property(d => d.Name).IsRequired().HasMaxLength(150);
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.Property(d => d.Price).HasColumnType("decimal(18,2)");
        builder.Property(d => d.IsAvailable).IsRequired();
        builder.Property(d => d.AvailablePortions).IsRequired();
        builder.ConfigureAudit();

        // В одному ресторані не може бути двох «живих» страв з однаковою назвою
        // (фільтр — щоб м'яко видалена страва не блокувала нову з тією ж назвою).
        // Цей самий індекс обслуговує запит «меню ресторану за назвою».
        builder.HasIndex(d => new { d.RestaurantId, d.Name })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // пошук страви за назвою по всіх ресторанах (DishesFilterSpec: Search)
        builder.HasIndex(d => d.Name);
        // фільтр за ціновим діапазоном + сортування за ціною
        builder.HasIndex(d => d.Price);

        builder.Navigation(d => d.DishCategories).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
