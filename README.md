# Aiursoft AgentKit

<a name="aiursoft-agentkit"></a>

[![MIT licensed](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/AiursoftWeb/agentkit/blob/master/LICENSE)
[![Pipeline stat](https://badge-proxy.aiursoft.com/aiursoft/agentkit/badges/master/pipeline.svg)](https://gitlab.aiursoft.com/aiursoft/agentkit/-/pipelines)
[![Test Coverage](https://badge-proxy.aiursoft.com/aiursoft/agentkit/badges/master/coverage.svg)](https://gitlab.aiursoft.com/aiursoft/agentkit/-/pipelines)
[![S360 compliance](https://s360.aiursoft.com/badge.svg?project=aiursoft%2Fagentkit)](https://s360.aiursoft.com/SupplyChain?tab=projects&project=aiursoft%2Fagentkit&scope=baseline&ecosystem=all)
[![NuGet version (Aiursoft.AgentKit)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit/)
[![NuGet version (Aiursoft.AgentKit.AgentRunner)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.AgentRunner.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit.AgentRunner/)
[![NuGet version (Aiursoft.AgentKit.Mcp)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.Mcp.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit.Mcp/)
[![NuGet version (Aiursoft.AgentKit.Evaluator)](https://img.shields.io/nuget/vpre/Aiursoft.AgentKit.Evaluator.svg)](https://www.nuget.org/packages/Aiursoft.AgentKit.Evaluator/)
[![Man hours](https://manhours.aiursoft.com/r/github.com/aiursoftweb/agentkit.svg)](https://manhours.aiursoft.com/r/github.com/aiursoftweb/agentkit.html)

Composable .NET building blocks for agent applications. AgentKit separates provider-neutral contracts, tool execution, MCP integration, and evaluation into four NuGet packages for .NET 10.

## Overview

### What is AgentKit?

Composable, provider-neutral building blocks separate message contracts, tool runners, MCP integration and evaluation; applications own the providers and authorization policy.

## Installation

Add the package to your .NET project:

<a name="how-to-install"></a>

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

**Packages and boundaries**

<a name="packages-and-boundaries"></a>

- **`Aiursoft.AgentKit`** — provider-neutral contracts, messages, cloning, and transcript validation. Its JSON contracts use `Newtonsoft.Json`.
- **`Aiursoft.AgentKit.AgentRunner`** — the only runtime implementation: `BoundedAgentRunner` for atomic tool batches and `OrderedAgentRunner` for checkpointed per-call approval flows.
- **`Aiursoft.AgentKit.Mcp`** — optional official .NET MCP SDK discovery and in-process invocation bridge. It preserves MCP-generated schemas and does not create a network MCP endpoint.
- **`Aiursoft.AgentKit.Evaluator`** — deterministic domain-neutral JSON matching, evidence assertions, and configurable scoring.

Applications retain provider HTTP clients, authorization, trusted actor injection, queues, conversation persistence, approval presentation, audits, controllers, scenario fixtures, and UI. In particular, a Kanban migration must use `OrderedAgentRunner`; bounded atomic deferral is not a replacement for Kanban mixed read/write tool turns.

Package-specific documentation: [core contracts](src/Aiursoft.AgentKit/README.md), [agent runners](src/Aiursoft.AgentKit.AgentRunner/README.md), [MCP integration](src/Aiursoft.AgentKit.Mcp/README.md), and [evaluation](src/Aiursoft.AgentKit.Evaluator/README.md).

Requires .NET 10 or later.

## Usage

Start with this complete console `Program.cs` after installing the package:

```csharp
using Aiursoft.AgentKit;
using Aiursoft.AgentKit.Messages;

var transcript = new[]
{
    TranscriptMessage.System("Answer briefly."),
    TranscriptMessage.User("Hello")
};
TranscriptValidator.Validate(transcript);
var copy = transcript.Select(message => message.DeepCopy()).ToArray();
Console.WriteLine($"Validated {copy.Length} messages");
```

## Configuration

AgentKit has no shared `appsettings.json`. Configure providers, endpoints, credentials, execution budgets and approval rules in the consuming application. The core package only validates and copies messages; it does not execute model requests or tools. Choose the runner and register application-owned adapters as described in the [runner guide](src/Aiursoft.AgentKit.AgentRunner/README.md). The four package guides retain their separate contracts and options.

## Development

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

From the repository root, restore, build and test:

```bash
dotnet restore Aiursoft.AgentKit.sln
dotnet build Aiursoft.AgentKit.sln --no-restore
dotnet test Aiursoft.AgentKit.sln --no-restore
```

Run `./lint.sh` for the existing static checks.

**Build details**

<a name="how-to-build"></a>

Install the .NET 10 SDK, then build the solution:

```bash
dotnet build Aiursoft.AgentKit.sln
```

**Focused tests**

<a name="how-to-test"></a>

```bash
dotnet test tests/Aiursoft.AgentKit.Tests/Aiursoft.AgentKit.Tests.csproj --filter FullyQualifiedName~CoreTests
dotnet test tests/Aiursoft.AgentKit.Mcp.Tests/Aiursoft.AgentKit.Mcp.Tests.csproj --filter FullyQualifiedName~McpToolCatalogTests
dotnet test tests/Aiursoft.AgentKit.Evaluator.Tests/Aiursoft.AgentKit.Evaluator.Tests.csproj --filter FullyQualifiedName~EvaluatorTests
```

Run the focused test project that covers your change; the GitLab pipeline runs the complete solution suite with JUnit and Cobertura reporting.

**Validation and release pipeline**

<a name="validation-and-release-pipeline"></a>

The repository follows the Aiursoft template pipeline: `s360`, `build`, `validate`, `publish`, and `deploy`.

- `lint.sh` runs formatting validation, the shared security baseline, and ReSharper inspection.
- CI tests all projects in `Aiursoft.AgentKit.sln`, aggregates Cobertura coverage, and retains JUnit and coverage artifacts for one day.
- CI builds and packs all four NuGet libraries, retaining `.nupkg` artifacts for one week.
- On `master`, CI publishes packages to the internal Aiursoft feed and NuGet.org. The protected, masked GitLab variables `NUPKG_API_KEY` and `NUGET_API_KEY` must be configured before a master release pipeline runs.

For a release-package smoke check, use:

```bash
dotnet build Aiursoft.AgentKit.sln --configuration Release -p:GeneratePackageOnBuild=false
dotnet pack Aiursoft.AgentKit.sln --configuration Release --no-build
```

**Contribution requirements**

<a name="how-to-contribute"></a>

There are many ways to contribute to the project: logging bugs, submitting merge requests, reporting issues, and suggesting improvements.

Create a personal fork and use feature branches for your changes. Keep the core provider-neutral and dependency-light, and add deterministic MSTest coverage for behavior changes. Use the [issue tracker](https://github.com/AiursoftWeb/agentkit/issues) for bug reports and feature requests.

Before submitting a change, run the focused tests that cover it and `./lint.sh`. For package changes, verify Release packaging as described above.

Report suspected security vulnerabilities privately to the Aiursoft maintainers.

## Documentation

Package-specific documentation: [core contracts](src/Aiursoft.AgentKit/README.md), [agent runners](src/Aiursoft.AgentKit.AgentRunner/README.md), [MCP integration](src/Aiursoft.AgentKit.Mcp/README.md), and [evaluation](src/Aiursoft.AgentKit.Evaluator/README.md).

## How to contribute

<a name="how-to-contribute"></a>

There are many ways to contribute to the project: logging bugs, submitting pull requests, reporting issues, and creating suggestions.

Even if you have push rights on the repository, you should create a personal fork and create feature branches there when you need them. This keeps the main repository clean and your workflow cruft out of sight.

We're also interested in your feedback on the future of this project. You can submit a suggestion or feature request through the issue tracker. To make this process more effective, we're asking that these include more information to help define them more clearly.

## License

Licensed under the [MIT License](https://github.com/AiursoftWeb/agentkit/blob/master/LICENSE).
