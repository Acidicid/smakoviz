using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Menu.Dal.Configurations;

public class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        builder.ToTable("Restaurants", t =>
            // slug — лише малі латинські літери, цифри й дефіс (бінарне порівняння, бо колація нечутлива до регістру)
            t.HasCheckConstraint("CK_Restaurants_Slug", "[Slug] COLLATE Latin1_General_BIN NOT LIKE '%[^a-z0-9-]%'"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityColumn();

        builder.Property(r => r.Name).IsRequired().HasMaxLength(150);
        builder.Property(r => r.Slug).IsRequired().HasMaxLength(80);
        builder.Property(r => r.City).IsRequired().HasMaxLength(80);
        builder.Property(r => r.CuisineType).IsRequired().HasMaxLength(50);
        builder.Property(r => r.IsActive).IsRequired();
        builder.ConfigureAudit();

        // сторінка ресторану за адресою /restaurants/{slug}
        builder.HasIndex(r => r.Slug).IsUnique();
        // «ресторани міста» за назвою (фільтр і сортування в RestaurantsFilterSpec)
        builder.HasIndex(r => new { r.City, r.Name });

        // 1:1 — профіль; RestaurantId у профілі є водночас PK і FK; профіль не існує без ресторану
        builder.HasOne(r => r.Profile)
            .WithOne()
            .HasForeignKey<RestaurantProfile>(p => p.RestaurantId)
            .OnDelete(DeleteBehavior.Cascade);

        // 1:N — страви. Без каскаду: ресторан видаляється м'яко, а страви потрібні для історії
        builder.HasMany(r => r.Dishes)
            .WithOne(d => d.Restaurant)
            .HasForeignKey(d => d.RestaurantId)
            .OnDelete(DeleteBehavior.Restrict);

        // колекція відкрита лише на читання — EF пише в приватне поле _dishes
        builder.Navigation(r => r.Dishes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
