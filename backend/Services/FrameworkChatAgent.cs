using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using FrameworkChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace ChatApi.Services;

public sealed class FrameworkChatAgent : IChatAgent
{
    private readonly AIAgent? _agent;

    public FrameworkChatAgent(IConfiguration configuration)
    {
        var endpoint = configuration["Foundry:Endpoint"];
        var apiKey = configuration["Foundry:ApiKey"];
        var deploymentName = configuration["Foundry:DeploymentName"];

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(deploymentName))
        {
            return;
        }

        var normalizedEndpoint = endpoint.TrimEnd('/');
        if (!normalizedEndpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalizedEndpoint += "/openai/v1";
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri($"{normalizedEndpoint}/") });

        _agent = client.GetChatClient(deploymentName)
            .AsAIAgent(
                name: "FoundryChatAssistant",
                instructions: "You are a helpful assistant. Give clear, accurate, and concise answers.");
    }

    public bool IsConfigured => _agent is not null;

    public async IAsyncEnumerable<string> StreamResponseAsync(
        IReadOnlyList<ChatHistoryEntry> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agent = _agent ?? throw new InvalidOperationException(
            "Foundry chat is not configured. Set Foundry:Endpoint, Foundry:ApiKey, and Foundry:DeploymentName.");

        var messages = history.Select(message => new FrameworkChatMessage(
            message.Role == "assistant" ? ChatRole.Assistant : ChatRole.User,
            message.Content));

        await foreach (var update in agent.RunStreamingAsync(
            messages,
            cancellationToken: cancellationToken))
        {
            foreach (var text in update.Contents.OfType<TextContent>())
            {
                if (!string.IsNullOrEmpty(text.Text))
                {
                    yield return text.Text;
                }
            }
        }
    }
}
