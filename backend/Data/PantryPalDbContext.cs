using Microsoft.EntityFrameworkCore;

namespace PantryPal.Api.Data;

public sealed class PantryPalDbContext : DbContext
{
    public PantryPalDbContext(DbContextOptions<PantryPalDbContext> options)
        : base(options)
    {
    }

    public DbSet<FoodItem> FoodItems => Set<FoodItem>();

    public DbSet<FoodItemAlias> FoodItemAliases => Set<FoodItemAlias>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FoodItem>(entity =>
        {
            entity.ToTable("food_items");
            entity.HasKey(foodItem => foodItem.Id);
            entity.Property(foodItem => foodItem.Id)
                .HasColumnName("id")
                .HasColumnType("bigint unsigned")
                .ValueGeneratedOnAdd();
            entity.Property(foodItem => foodItem.Name)
                .HasColumnName("name")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex(foodItem => foodItem.Name)
                .IsUnique()
                .HasDatabaseName("uq_food_items_name");
        });

        modelBuilder.Entity<FoodItemAlias>(entity =>
        {
            entity.ToTable("food_item_aliases");
            entity.HasKey(alias => alias.Id);
            entity.Property(alias => alias.Id)
                .HasColumnName("id")
                .HasColumnType("bigint unsigned")
                .ValueGeneratedOnAdd();
            entity.Property(alias => alias.FoodItemId)
                .HasColumnName("food_item_id")
                .HasColumnType("bigint unsigned")
                .IsRequired();
            entity.Property(alias => alias.Alias)
                .HasColumnName("alias")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex(alias => alias.Alias)
                .IsUnique()
                .HasDatabaseName("uq_food_item_aliases_alias");
            entity.HasIndex(alias => alias.FoodItemId)
                .HasDatabaseName("ix_food_item_aliases_food_item_id");
            entity.HasOne(alias => alias.FoodItem)
                .WithMany(foodItem => foodItem.Aliases)
                .HasForeignKey(alias => alias.FoodItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}