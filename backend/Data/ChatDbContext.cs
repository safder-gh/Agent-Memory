using ChatApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApi.Data;

public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<ChatSessionEntity> Sessions => Set<ChatSessionEntity>();
    public DbSet<ChatMessageEntity> Messages => Set<ChatMessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatSessionEntity>()
            .HasMany(session => session.Messages)
            .WithOne(message => message.Session)
            .HasForeignKey(message => message.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ChatMessageEntity>()
            .HasIndex(message => new { message.SessionId, message.Id });
    }
}
