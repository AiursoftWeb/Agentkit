# Aiursoft AgentKit

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://gitlab.aiursoft.com/aiursoft/agentkit/-/blob/master/LICENSE)
[![Pipeline stat](https://gitlab.aiursoft.com/aiursoft/agentkit/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/agentkit/-/pipelines)
[![Test Coverage](https://gitlab.aiursoft.com/aiursoft/agentkit/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/agentkit/-/pipelines)
[![NuGet version (Aiursoft.AgentKit)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit/)
[![NuGet version (Aiursoft.AgentKit.AgentRunner)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.AgentRunner.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit.AgentRunner/)
[![NuGet version (Aiursoft.AgentKit.Mcp)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.Mcp.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit.Mcp/)
[![NuGet version (Aiursoft.AgentKit.Evaluator)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.Evaluator.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit.Evaluator/)
[![Man hours](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/aiursoft/agentkit.svg)](https://manhours.aiursoft.com/r/gitlab.aiursoft.com/aiursoft/agentkit.html)

Composable .NET building blocks for agent applications. AgentKit separates provider-neutral contracts, tool execution, MCP integration, and evaluation into four NuGet packages for .NET 10.

## How to install

Install the runtime package from [nuget.org](https://www.nuget.org/packages/Aiursoft.AgentKit.AgentRunner/). It also references the core contracts:

```bash
dotnet add package Aiursoft.AgentKit.AgentRunner
```

Add MCP integration or evaluation when your application needs them:

```bash
dotnet add package Aiursoft.AgentKit.Mcp
dotnet add package Aiursoft.AgentKit.Evaluator
```

Applications that only need the contracts can install the core package directly:

```bash
dotnet add package Aiursoft.AgentKit
```

## Packages and boundaries

- **`Aiursoft.AgentKit`** — provider-neutral contracts, messages, cloning, and transcript validation. Its JSON contracts use `Newtonsoft.Json`.
- **`Aiursoft.AgentKit.AgentRunner`** — the only runtime implementation: `BoundedAgentRunner` for atomic tool batches and `OrderedAgentRunner` for checkpointed per-call approval flows.
- **`Aiursoft.AgentKit.Mcp`** — optional official .NET MCP SDK discovery and in-process invocation bridge. It preserves MCP-generated schemas and does not create a network MCP endpoint.
- **`Aiursoft.AgentKit.Evaluator`** — deterministic domain-neutral JSON matching, evidence assertions, and configurable scoring.

Applications retain provider HTTP clients, authorization, trusted actor injection, queues, conversation persistence, approval presentation, audits, controllers, scenario fixtures, and UI. In particular, a Kanban migration must use `OrderedAgentRunner`; bounded atomic deferral is not a replacement for Kanban mixed read/write tool turns.

Package-specific documentation: [core contracts](src/Aiursoft.AgentKit/README.md), [agent runners](src/Aiursoft.AgentKit.AgentRunner/README.md), [MCP integration](src/Aiursoft.AgentKit.Mcp/README.md), and [evaluation](src/Aiursoft.AgentKit.Evaluator/README.md).

## How to build

Install the .NET 10 SDK, then build the solution:

```bash
dotnet build Aiursoft.AgentKit.sln
```

## How to test

```bash
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

## How to contribute

There are many ways to contribute to the project: logging bugs, submitting merge requests, reporting issues, and suggesting improvements.

Create a personal fork and use feature branches for your changes. See [CONTRIBUTING.md](CONTRIBUTING.md) for validation requirements, and use the [issue tracker](https://gitlab.aiursoft.com/aiursoft/agentkit/-/issues) for bug reports and feature requests.
