using InvestorChat.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace InvestorChat.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasIndex(u => u.UserName).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.UserName).HasMaxLength(50);
            entity.Property(u => u.Email).HasMaxLength(200);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.Property(m => m.UserName).HasMaxLength(50);
            entity.Property(m => m.Content).HasMaxLength(1000);
        });

        base.OnModelCreating(modelBuilder);
    }
}
