# Foundry Chat

A local chat application with an ASP.NET Core API, Microsoft Agent Framework, SQLite session history, and an Angular UI.

## Requirements

- .NET 10 SDK
- Node.js 22.12 or newer and npm
- An Azure OpenAI-compatible model deployment created in Microsoft Foundry

## Configure the model

The API uses the Microsoft Agent Framework OpenAI integration with API-key authentication. Configure these values in the API process environment; do not commit keys:

```powershell
$env:Foundry__Endpoint = "https://<your-resource>.openai.azure.com"
$env:Foundry__ApiKey = "<your-api-key>"
$env:Foundry__DeploymentName = "<your-deployment-name>"
```

`Foundry__Endpoint` is the Azure OpenAI-compatible resource endpoint for your deployed model, not the Foundry project endpoint. The API targets its `/openai/v1/` route. Microsoft Agent Framework's native Foundry project connector uses Microsoft Entra authentication; use that connector instead if your deployment is configured for project-endpoint authentication rather than an API key.

## Run locally

In the first PowerShell terminal, configure the model variables above and run the API:

```powershell
dotnet run --project .\backend\ChatApi.csproj -- --urls http://localhost:5123
```

The API creates `chat.db` on first run. It is a local SQLite database containing chat sessions and messages.

In a second terminal:

```powershell
cd .\frontend
npm start
```

Open [http://localhost:4200](http://localhost:4200). The Angular development server proxies `/api` requests to the API on port 5123.

## API

- `GET /api/health` — API health
- `GET /api/sessions` — list chat sessions
- `POST /api/sessions` — create a chat session
- `GET /api/sessions/{id}` — get a session and its messages
- `DELETE /api/sessions/{id}` — delete a session and its messages
- `POST /api/sessions/{id}/messages` — save a user message and stream the assistant response as Server-Sent Events (`delta`, `done`, or `error`)

## Validate

```powershell
dotnet test .\backend.tests\ChatApi.Tests.csproj
dotnet build .\backend\ChatApi.csproj
cd .\frontend
npm run build
```
