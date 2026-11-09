namespace ChatApi.Models;

public sealed class ChatSessionEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<ChatMessageEntity> Messages { get; set; } = [];
}
