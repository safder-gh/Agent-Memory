namespace ChatApi.Models;

public sealed record SendMessageRequest(string Content);

public sealed record SessionSummaryResponse(
    Guid Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ChatMessageResponse(
    string Id,
    string Role,
    string Content,
    DateTime CreatedAtUtc);

public sealed record SessionDetailResponse(
    Guid Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ChatMessageResponse> Messages);
