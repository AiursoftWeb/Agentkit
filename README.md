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

Run the focused test project that covers your change; the GitLab pipeline runs the complete solution suite with JUnit and Cobertura reporting.

## Validation and release pipeline

The repository follows the Aiursoft template pipeline: `build`, `validate`, `publish`, and `deploy`.

- `lint.sh` runs formatting validation, the shared security baseline, and ReSharper inspection.
- CI tests all projects in `Aiursoft.AgentKit.sln`, aggregates Cobertura coverage, and retains JUnit and coverage artifacts for one day.
- CI builds and packs all four NuGet libraries, retaining `.nupkg` artifacts for one week.
- On `master`, CI publishes packages to the internal Aiursoft feed and NuGet.org. The protected, masked GitLab variables `LOCAL_NUGET_API_KEY` and `NUGET_API_KEY` must be configured before a master release pipeline runs.

For a release-package smoke check, use:

```bash
dotnet build Aiursoft.AgentKit.sln --configuration Release -p:GeneratePackageOnBuild=false
dotnet pack Aiursoft.AgentKit.sln --configuration Release --no-build
```
