using MakroChef.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MakroChef.Data;

public class MakroChefDbContext(DbContextOptions<MakroChefDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<McpToken> McpTokens => Set<McpToken>();
    public DbSet<ProductNutrition> ProductNutritions => Set<ProductNutrition>();
    public DbSet<ReceiptSnapshot> ReceiptSnapshots => Set<ReceiptSnapshot>();
    public DbSet<McpCall> McpCalls => Set<McpCall>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.SilpoUserId).IsUnique();
        });

        modelBuilder.Entity<McpToken>(e =>
        {
            e.HasIndex(t => t.UserId).IsUnique();
        });

        modelBuilder.Entity<ProductNutrition>(e =>
        {
            e.HasIndex(p => p.ProductId).IsUnique();
        });

        modelBuilder.Entity<ReceiptSnapshot>(e =>
        {
            e.HasIndex(r => new { r.UserId, r.SourceOrderId }).IsUnique();
        });

        modelBuilder.Entity<McpCall>(e =>
        {
            e.HasIndex(c => c.CreatedAt);
        });
    }
}
