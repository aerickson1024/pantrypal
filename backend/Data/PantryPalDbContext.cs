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

    public DbSet<Purchase> Purchases => Set<Purchase>();

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

        modelBuilder.Entity<Purchase>(entity =>
        {
            entity.ToTable("purchases", table =>
            {
                table.HasCheckConstraint("chk_purchases_line_number", "`line_number` > 0");
                table.HasCheckConstraint("chk_purchases_quantity", "`quantity` > 0");
                table.HasCheckConstraint("chk_purchases_cost", "`cost_per_item` >= 0");
            });
            entity.HasKey(purchase => purchase.Id);
            entity.Property(purchase => purchase.Id)
                .HasColumnName("id")
                .HasColumnType("bigint unsigned")
                .ValueGeneratedOnAdd();
            entity.Property(purchase => purchase.Hash)
                .HasColumnName("hash")
                .HasColumnType("char(64)")
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(purchase => purchase.LineNumber)
                .HasColumnName("line_number")
                .HasColumnType("int unsigned")
                .IsRequired();
            entity.Property(purchase => purchase.FoodItemId)
                .HasColumnName("food_item_id")
                .HasColumnType("bigint unsigned")
                .IsRequired();
            entity.Property(purchase => purchase.PurchaseDate)
                .HasColumnName("purchase_date")
                .HasConversion(
                    date => date.ToDateTime(TimeOnly.MinValue),
                    dateTime => DateOnly.FromDateTime(dateTime))
                .HasColumnType("date")
                .IsRequired();
            entity.Property(purchase => purchase.Quantity)
                .HasColumnName("quantity")
                .HasColumnType("decimal(18,3)")
                .IsRequired();
            entity.Property(purchase => purchase.CostPerItem)
                .HasColumnName("cost_per_item")
                .HasColumnType("decimal(18,2)")
                .IsRequired();
            entity.Property(purchase => purchase.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAdd();
            entity.HasIndex(purchase => new { purchase.Hash, purchase.LineNumber })
                .IsUnique()
                .HasDatabaseName("uq_purchases_hash_line");
            entity.HasIndex(purchase => purchase.FoodItemId)
                .HasDatabaseName("ix_purchases_food_item_id");
            entity.HasOne(purchase => purchase.FoodItem)
                .WithMany()
                .HasForeignKey(purchase => purchase.FoodItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}