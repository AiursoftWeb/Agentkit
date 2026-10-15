# Aiursoft AgentKit

Provider-neutral .NET contracts, messages, cloning, and transcript validation for agent applications.

This package contains no model provider, runner implementation, or MCP integration. Its JSON contracts use `Newtonsoft.Json`. Install `Aiursoft.AgentKit.AgentRunner` for tool orchestration.

## How to install

```bash
dotnet add package Aiursoft.AgentKit
```

## Build and test

```bash
dotnet build Aiursoft.AgentKit.sln
dotnet test tests/Aiursoft.AgentKit.Tests/Aiursoft.AgentKit.Tests.csproj
```
