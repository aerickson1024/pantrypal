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

    public DbSet<FoodTag> FoodTags => Set<FoodTag>();

    public DbSet<FoodItemTag> FoodItemTags => Set<FoodItemTag>();

    public DbSet<Purchase> Purchases => Set<Purchase>();

    public DbSet<User> Users => Set<User>();

    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id)
                .HasColumnName("id")
                .HasColumnType("bigint unsigned");
            entity.Property(user => user.Email)
                .HasColumnName("email")
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(user => user.FirstName)
                .HasColumnName("first_name")
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(user => user.LastName)
                .HasColumnName("last_name")
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(user => user.PasswordHash)
                .HasColumnName("password_hash")
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(user => user.ProtectedGeminiApiKey)
                .HasColumnName("gemini_api_key_protected")
                .HasColumnType("text");
            entity.Property(user => user.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp")
                .IsRequired();
            entity.HasIndex(user => user.Email)
                .IsUnique()
                .HasDatabaseName("uq_users_email");
        });

        modelBuilder.Entity<InviteCode>(entity =>
        {
            entity.ToTable("invite_codes");
            entity.HasKey(inviteCode => inviteCode.Id);
            entity.Property(inviteCode => inviteCode.Id)
                .HasColumnName("id")
                .HasColumnType("bigint unsigned");
            entity.Property(inviteCode => inviteCode.CodeHash)
                .HasColumnName("code_hash")
                .HasColumnType("char(64)")
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(inviteCode => inviteCode.UsedByUserId)
                .HasColumnName("used_by_user_id")
                .HasColumnType("bigint unsigned");
            entity.Property(inviteCode => inviteCode.UsedAt)
                .HasColumnName("used_at")
                .HasColumnType("timestamp");
            entity.Property(inviteCode => inviteCode.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp")
                .IsRequired();
            entity.HasIndex(inviteCode => inviteCode.CodeHash)
                .IsUnique()
                .HasDatabaseName("uq_invite_codes_code_hash");
        });

        modelBuilder.Entity<FoodItem>(entity =>
        {
            entity.ToTable("food_items");
            entity.HasKey(foodItem => foodItem.Id);
            entity.Property(foodItem => foodItem.Id)
                .HasColumnName("id")
                .HasColumnType("bigint unsigned")
                .ValueGeneratedOnAdd();
            entity.Property(foodItem => foodItem.UserId)
                .HasColumnName("user_id")
                .HasColumnType("bigint unsigned");
            entity.Property(foodItem => foodItem.Name)
                .HasColumnName("name")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex(foodItem => new { foodItem.UserId, foodItem.Name })
                .IsUnique()
                .HasDatabaseName("uq_food_items_user_name");
        });

        modelBuilder.Entity<FoodTag>(entity =>
        {
            entity.ToTable("food_tags");
            entity.HasKey(tag => tag.Id);
            entity.Property(tag => tag.Id)
                .HasColumnName("id")
                .HasColumnType("bigint unsigned")
                .ValueGeneratedOnAdd();
            entity.Property(tag => tag.UserId)
                .HasColumnName("user_id")
                .HasColumnType("bigint unsigned");
            entity.Property(tag => tag.Name)
                .HasColumnName("name")
                .HasMaxLength(100)
                .IsRequired();
            entity.HasIndex(tag => new { tag.UserId, tag.Name })
                .IsUnique()
                .HasDatabaseName("uq_food_tags_user_name");
        });

        modelBuilder.Entity<FoodItemTag>(entity =>
        {
            entity.ToTable("food_item_tags");
            entity.HasKey(link => new { link.FoodItemId, link.FoodTagId });
            entity.Property(link => link.FoodItemId)
                .HasColumnName("food_item_id")
                .HasColumnType("bigint unsigned");
            entity.Property(link => link.FoodTagId)
                .HasColumnName("food_tag_id")
                .HasColumnType("bigint unsigned");
            entity.HasIndex(link => link.FoodTagId)
                .HasDatabaseName("ix_food_item_tags_food_tag_id");
            entity.HasOne(link => link.FoodItem)
                .WithMany(foodItem => foodItem.Tags)
                .HasForeignKey(link => link.FoodItemId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.FoodTag)
                .WithMany(tag => tag.FoodItems)
                .HasForeignKey(link => link.FoodTagId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(alias => alias.UserId)
                .HasColumnName("user_id")
                .HasColumnType("bigint unsigned");
            entity.Property(alias => alias.Alias)
                .HasColumnName("alias")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex(alias => new { alias.UserId, alias.Alias })
                .IsUnique()
                .HasDatabaseName("uq_food_item_aliases_user_alias");
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
            entity.Property(purchase => purchase.UserId)
                .HasColumnName("user_id")
                .HasColumnType("bigint unsigned");
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
            entity.HasIndex(purchase => new { purchase.UserId, purchase.Hash, purchase.LineNumber })
                .IsUnique()
                .HasDatabaseName("uq_purchases_user_hash_line");
            entity.HasIndex(purchase => purchase.FoodItemId)
                .HasDatabaseName("ix_purchases_food_item_id");
            entity.HasOne(purchase => purchase.FoodItem)
                .WithMany()
                .HasForeignKey(purchase => purchase.FoodItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}