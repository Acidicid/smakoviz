using Menu.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Menu.Dal.Configurations;

public class RestaurantProfileConfiguration : IEntityTypeConfiguration<RestaurantProfile>
{
    public void Configure(EntityTypeBuilder<RestaurantProfile> builder)
    {
        builder.ToTable("RestaurantProfiles", t =>
        {
            t.HasCheckConstraint("CK_RestaurantProfiles_MinOrder", "[MinOrderAmount] >= 0");
            t.HasCheckConstraint("CK_RestaurantProfiles_Hours", "[OpensAt] <> [ClosesAt]");
        });

        // 1:1 — PK = FK на Restaurants (зв'язок описано в RestaurantConfiguration)
        builder.HasKey(p => p.RestaurantId);
        builder.Property(p => p.RestaurantId).ValueGeneratedNever();

        builder.Property(p => p.Address).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Phone).IsRequired().HasMaxLength(20);
        builder.Property(p => p.OpensAt).HasColumnType("time(0)");
        builder.Property(p => p.ClosesAt).HasColumnType("time(0)");
        builder.Property(p => p.MinOrderAmount).HasColumnType("decimal(18,2)");
        builder.Property(p => p.Description).HasMaxLength(1000);
    }
}
