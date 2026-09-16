using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Aiursoft.AgentKit.Mcp.Tests;

[TestClass]
public sealed class McpToolCatalogTests
{
    [TestMethod]
    public async Task CatalogUsesMcpSchemaAndScopedTarget()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedTool>();
        using var provider = services.BuildServiceProvider();
        var catalog = new McpToolCatalog(
            provider,
            [typeof(ScopedTool).Assembly],
            new HashSet<string>(StringComparer.Ordinal) { "echo" });

        Assert.AreEqual(1, catalog.Tools.Count);
        var registration = catalog.Tools[0];
        Assert.AreEqual("echo", registration.Name);
        Assert.AreEqual("Returns its input.", registration.Description);
        Assert.AreEqual(registration.Description ?? registration.Title ?? registration.Name, registration.Definition.Description);
        Assert.AreEqual(typeof(ScopedTool), registration.SourceType);
        Assert.AreEqual(nameof(ScopedTool.Echo), registration.SourceMethod.Name);
        StringAssert.Contains(registration.InputSchema.GetRawText(), "text");
        Assert.IsFalse(registration.RequiresApproval);

        await using var session = await catalog.CreateSessionAsync();
        using var arguments = JsonDocument.Parse("{\"text\":\"hello\"}");
        var output = await session.Tools.Single().ExecuteAsync(arguments.RootElement, CancellationToken.None);
        StringAssert.Contains(output.GetRawText(), "hello");
    }

    [TestMethod]
    public void CatalogRejectsUnknownAllowlistEntry()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedTool>();
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
        {
            using var provider = services.BuildServiceProvider();
            _ = new McpToolCatalog(
                provider,
                [typeof(ScopedTool).Assembly],
                new HashSet<string>(StringComparer.Ordinal) { "missing" });
        });

        Assert.AreEqual("enabledToolNames", exception.ParamName);
        StringAssert.Contains(exception.Message, "Unknown MCP tool name(s): missing.");
    }

    [McpServerToolType]
    public sealed class ScopedTool
    {
        [McpServerTool(Name = "echo")]
        [System.ComponentModel.Description("Returns its input.")]
        public string Echo(string text) => text;
    }
}
