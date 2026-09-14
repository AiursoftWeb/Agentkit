# Aiursoft AgentKit

Composable .NET building blocks for agent applications.

## Packages and boundaries

- **`Aiursoft.AgentKit`** — provider-neutral contracts, messages, cloning, and transcript validation. It uses only the .NET BCL and `System.Text.Json`.
- **`Aiursoft.AgentKit.AgentRunner`** — the only runtime implementation: `BoundedAgentRunner` for atomic tool batches and `OrderedAgentRunner` for checkpointed per-call approval flows.
- **`Aiursoft.AgentKit.Mcp`** — optional official .NET MCP SDK discovery and in-process invocation bridge. It preserves MCP-generated schemas and does not create a network MCP endpoint.
- **`Aiursoft.AgentKit.Evaluator`** — deterministic domain-neutral JSON matching, evidence assertions, and configurable scoring.

Applications retain provider HTTP clients, authorization, trusted actor injection, queues, conversation persistence, approval presentation, audits, controllers, scenario fixtures, and UI. In particular, a Kanban migration must use `OrderedAgentRunner`; bounded atomic deferral is not a replacement for Kanban mixed read/write tool turns.

## Build and focused tests

```bash
dotnet build Aiursoft.AgentKit.sln
dotnet test tests/Aiursoft.AgentKit.Tests/Aiursoft.AgentKit.Tests.csproj --filter FullyQualifiedName~CoreTests
dotnet test tests/Aiursoft.AgentKit.Mcp.Tests/Aiursoft.AgentKit.Mcp.Tests.csproj --filter FullyQualifiedName~McpToolCatalogTests
dotnet test tests/Aiursoft.AgentKit.Evaluator.Tests/Aiursoft.AgentKit.Evaluator.Tests.csproj --filter FullyQualifiedName~EvaluatorTests
```
