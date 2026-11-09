using System.Text;
using System.Text.Json;
using ChatApi.Data;
using ChatApi.Models;
using ChatApi.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ChatDatabase")
    ?? "Data Source=chat.db";
var frontendOrigin = builder.Configuration["FrontendOrigin"] ?? "http://localhost:4200";

builder.Services.AddDbContext<ChatDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<IChatAgent, FrameworkChatAgent>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(frontendOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
    await database.Database.EnsureCreatedAsync();
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/sessions", async (ChatDbContext database, CancellationToken cancellationToken) =>
{
    var sessions = await database.Sessions
        .AsNoTracking()
        .OrderByDescending(session => session.UpdatedAtUtc)
        .Select(session => new SessionSummaryResponse(
            session.Id,
            session.Title,
            session.CreatedAtUtc,
            session.UpdatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(sessions);
});

app.MapPost("/api/sessions", async (ChatDbContext database, CancellationToken cancellationToken) =>
{
    var session = new ChatSessionEntity
    {
        Title = "New chat",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    database.Sessions.Add(session);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/sessions/{session.Id}", new SessionDetailResponse(
        session.Id,
        session.Title,
        session.CreatedAtUtc,
        session.UpdatedAtUtc,
        []));
});

app.MapGet("/api/sessions/{sessionId:guid}", async (
    Guid sessionId,
    ChatDbContext database,
    CancellationToken cancellationToken) =>
{
    var session = await database.Sessions
        .AsNoTracking()
        .Include(item => item.Messages)
        .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);

    if (session is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new SessionDetailResponse(
        session.Id,
        session.Title,
        session.CreatedAtUtc,
        session.UpdatedAtUtc,
        session.Messages
            .OrderBy(message => message.Id)
            .Select(message => new ChatMessageResponse(
                message.Id.ToString(),
                message.Role,
                message.Content,
                message.CreatedAtUtc))
            .ToList()));
});

app.MapDelete("/api/sessions/{sessionId:guid}", async (
    Guid sessionId,
    ChatDbContext database,
    CancellationToken cancellationToken) =>
{
    var session = await database.Sessions.FindAsync([sessionId], cancellationToken);
    if (session is null)
    {
        return Results.NotFound();
    }

    database.Sessions.Remove(session);
    await database.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPost("/api/sessions/{sessionId:guid}/messages", async Task<IResult> (
    Guid sessionId,
    SendMessageRequest request,
    ChatDbContext database,
    IChatAgent agent,
    HttpContext context,
    ILogger<Program> logger) =>
{
    if (string.IsNullOrWhiteSpace(request.Content))
    {
        return Results.BadRequest(new { message = "Message content is required." });
    }

    if (!agent.IsConfigured)
    {
        return Results.Problem(
            "Configure Foundry:Endpoint, Foundry:ApiKey, and Foundry:DeploymentName before sending messages.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var session = await database.Sessions
        .SingleOrDefaultAsync(item => item.Id == sessionId, context.RequestAborted);

    if (session is null)
    {
        return Results.NotFound();
    }

    var now = DateTime.UtcNow;
    var userMessage = new ChatMessageEntity
    {
        SessionId = session.Id,
        Role = "user",
        Content = request.Content.Trim(),
        CreatedAtUtc = now
    };

    if (session.Title == "New chat")
    {
        var title = request.Content.Trim().ReplaceLineEndings(" ");
        session.Title = title.Length <= 60 ? title : $"{title[..57]}...";
    }

    session.UpdatedAtUtc = now;
    database.Messages.Add(userMessage);
    await database.SaveChangesAsync(context.RequestAborted);

    var history = await database.Messages
        .AsNoTracking()
        .Where(message => message.SessionId == session.Id)
        .OrderBy(message => message.Id)
        .Select(message => new ChatHistoryEntry(message.Role, message.Content))
        .ToListAsync(context.RequestAborted);

    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    var assistantText = new StringBuilder();
    try
    {
        await foreach (var text in agent.StreamResponseAsync(history, context.RequestAborted))
        {
            assistantText.Append(text);
            await WriteEventAsync(context.Response, "delta", new { text }, context.RequestAborted);
        }

        if (assistantText.Length == 0)
        {
            throw new InvalidOperationException("The model returned an empty response.");
        }

        var assistantMessage = new ChatMessageEntity
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = assistantText.ToString(),
            CreatedAtUtc = DateTime.UtcNow
        };
        database.Messages.Add(assistantMessage);
        session.UpdatedAtUtc = assistantMessage.CreatedAtUtc;
        await database.SaveChangesAsync(context.RequestAborted);

        await WriteEventAsync(context.Response, "done", new
        {
            messageId = assistantMessage.Id.ToString(),
            createdAtUtc = assistantMessage.CreatedAtUtc
        }, context.RequestAborted);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Chat response failed for session {SessionId}", session.Id);
        await WriteEventAsync(context.Response, "error", new
        {
            message = "The assistant could not complete this response. Check the API logs and Foundry configuration."
        }, CancellationToken.None);
    }

    return Results.Empty;
});

app.Run();

static async Task WriteEventAsync<T>(
    HttpResponse response,
    string eventName,
    T payload,
    CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(payload);
    await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

public partial class Program { }
