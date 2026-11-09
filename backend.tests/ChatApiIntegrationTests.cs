using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using ChatApi.Data;
using ChatApi.Models;
using ChatApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChatApi.Tests;

public sealed class ChatApiIntegrationTests
{
    [Fact]
    public async Task SessionsCanBeCreatedListedStreamedAndDeleted()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsync("/api/sessions", content: null);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(created);
        Assert.Equal("New chat", created.Title);

        using var streamResponse = await client.PostAsJsonAsync(
            $"/api/sessions/{created.Id}/messages",
            new SendMessageRequest("Hello"));

        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
        var stream = await streamResponse.Content.ReadAsStringAsync();
        Assert.Contains("event: delta", stream);
        Assert.Contains("\"text\":\"Hello\"", stream);
        Assert.Contains("event: done", stream);
        Assert.Equal(1, factory.Agent.LastHistory?.Count);
        Assert.Equal("Hello", factory.Agent.LastHistory?[0].Content);

        var session = await client.GetFromJsonAsync<SessionResponse>(
            $"/api/sessions/{created.Id}");
        Assert.NotNull(session);
        Assert.Equal("Hello", session.Title);
        Assert.Collection(
            session.Messages,
            message => Assert.Equal(("user", "Hello"), (message.Role, message.Content)),
            message => Assert.Equal(("assistant", "Hello from the test agent."), (message.Role, message.Content)));

        var sessions = await client.GetFromJsonAsync<List<SessionSummaryResponse>>("/api/sessions");
        Assert.NotNull(sessions);
        Assert.Single(sessions);

        var deleteResponse = await client.DeleteAsync($"/api/sessions/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/sessions/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task EmptyMessagesAreRejectedWithoutCallingTheAgent()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var sessionResponse = await client.PostAsync("/api/sessions", content: null);
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(session);

        var response = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/messages",
            new SendMessageRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(factory.Agent.LastHistory);
    }

    [Fact]
    public async Task MissingModelConfigurationReturnsServiceUnavailableWithoutSavingTheMessage()
    {
        using var factory = new TestApiFactory(agentConfigured: false);
        using var client = factory.CreateClient();
        var sessionResponse = await client.PostAsync("/api/sessions", content: null);
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(session);

        var response = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/messages",
            new SendMessageRequest("Hello"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Foundry:Endpoint", await response.Content.ReadAsStringAsync());
        var savedSession = await client.GetFromJsonAsync<SessionResponse>(
            $"/api/sessions/{session.Id}");
        Assert.NotNull(savedSession);
        Assert.Empty(savedSession.Messages);
        Assert.Null(factory.Agent.LastHistory);
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public FakeChatAgent Agent { get; }

        public TestApiFactory(bool agentConfigured = true)
        {
            Agent = new FakeChatAgent(agentConfigured);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            _connection.Open();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ChatDbContext>>();
                services.RemoveAll<ChatDbContext>();
                services.AddDbContext<ChatDbContext>(options => options.UseSqlite(_connection));
                services.RemoveAll<IChatAgent>();
                services.AddSingleton<IChatAgent>(Agent);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _connection.Dispose();
            }
        }
    }

    private sealed class FakeChatAgent : IChatAgent
    {
        public bool IsConfigured { get; }
        public IReadOnlyList<ChatHistoryEntry>? LastHistory { get; private set; }

        public FakeChatAgent(bool isConfigured)
        {
            IsConfigured = isConfigured;
        }

        public async IAsyncEnumerable<string> StreamResponseAsync(
            IReadOnlyList<ChatHistoryEntry> history,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastHistory = history;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return "Hello";
            yield return " from the test agent.";
        }
    }

    private sealed record SessionResponse(
        Guid Id,
        string Title,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        List<MessageResponse> Messages);

    private sealed record SessionSummaryResponse(
        Guid Id,
        string Title,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record MessageResponse(
        string Id,
        string Role,
        string Content,
        DateTime CreatedAtUtc);
}
