namespace ChatApi.Models;

public sealed class ChatMessageEntity
{
    public int Id { get; set; }
    public Guid SessionId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ChatSessionEntity Session { get; set; } = null!;
}
