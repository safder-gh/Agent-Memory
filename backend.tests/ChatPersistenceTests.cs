using ChatApi.Data;
using ChatApi.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ChatApi.Tests;

public sealed class ChatPersistenceTests
{
    [Fact]
    public async Task SessionAndMessagesPersistAcrossContextsAndDeleteCascades()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseSqlite(connection)
            .Options;

        var sessionId = Guid.NewGuid();
        await using (var writeContext = new ChatDbContext(options))
        {
            await writeContext.Database.EnsureCreatedAsync();
            writeContext.Sessions.Add(new ChatSessionEntity
            {
                Id = sessionId,
                Title = "SQLite test",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Messages =
                [
                    new ChatMessageEntity
                    {
                        Role = "user",
                        Content = "Hello",
                        CreatedAtUtc = DateTime.UtcNow
                    },
                    new ChatMessageEntity
                    {
                        Role = "assistant",
                        Content = "Hi there",
                        CreatedAtUtc = DateTime.UtcNow
                    }
                ]
            });
            await writeContext.SaveChangesAsync();
        }

        await using (var readContext = new ChatDbContext(options))
        {
            var savedSession = await readContext.Sessions
                .Include(session => session.Messages)
                .SingleAsync(session => session.Id == sessionId);

            Assert.Equal("SQLite test", savedSession.Title);
            Assert.Collection(
                savedSession.Messages.OrderBy(message => message.Id),
                message =>
                {
                    Assert.Equal("user", message.Role);
                    Assert.Equal("Hello", message.Content);
                },
                message =>
                {
                    Assert.Equal("assistant", message.Role);
                    Assert.Equal("Hi there", message.Content);
                });

            readContext.Sessions.Remove(savedSession);
            await readContext.SaveChangesAsync();
        }

        await using var verifyContext = new ChatDbContext(options);
        Assert.Empty(await verifyContext.Sessions.ToListAsync());
        Assert.Empty(await verifyContext.Messages.ToListAsync());
    }
}
