using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Menu.Dal.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", t =>
        {
            t.HasCheckConstraint("CK_Categories_SortOrder", "[SortOrder] >= 0");
            t.HasCheckConstraint("CK_Categories_Slug", "[Slug] COLLATE Latin1_General_BIN NOT LIKE '%[^a-z0-9-]%'");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).UseIdentityColumn();   // INT: довідник маленький
        builder.Property(c => c.Name).IsRequired().HasMaxLength(80);
        builder.Property(c => c.Slug).IsRequired().HasMaxLength(80);

        builder.HasIndex(c => c.Name).IsUnique();
        builder.HasIndex(c => c.Slug).IsUnique();

        builder.Navigation(c => c.DishCategories).UsePropertyAccessMode(PropertyAccessMode.Field);

        // ---- МОДЕЛЬНИЙ (статичний) СІДІНГ довідника з фіксованими Id ----
        // Потрапляє в міграцію як INSERT; повторне застосування міграцій дублів не створює.
        // Анонімні об'єкти — бо конструктор і setter-и сутності закриті.
        // Id ті самі, що в db/p2/seed.sql (Лаб. 01).
        builder.HasData(
            new { Id = 1, Name = "Перші страви", Slug = "soups", SortOrder = 10 },
            new { Id = 2, Name = "Основні страви", Slug = "mains", SortOrder = 20 },
            new { Id = 3, Name = "Піца", Slug = "pizza", SortOrder = 30 },
            new { Id = 4, Name = "Суші та роли", Slug = "sushi", SortOrder = 40 },
            new { Id = 5, Name = "Напої", Slug = "drinks", SortOrder = 50 },
            new { Id = 6, Name = "Вегетаріанське", Slug = "vegetarian", SortOrder = 60 },
            new { Id = 7, Name = "Хіт продажу", Slug = "bestseller", SortOrder = 0 });
    }
}
