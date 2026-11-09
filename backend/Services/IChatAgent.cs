namespace ChatApi.Services;

public sealed record ChatHistoryEntry(string Role, string Content);

public interface IChatAgent
{
    bool IsConfigured { get; }

    IAsyncEnumerable<string> StreamResponseAsync(
        IReadOnlyList<ChatHistoryEntry> history,
        CancellationToken cancellationToken);
}
