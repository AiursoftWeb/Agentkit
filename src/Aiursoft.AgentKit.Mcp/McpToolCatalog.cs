using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Aiursoft.AgentKit;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.AgentKit.Mcp;

/// <summary>
/// Discovers official MCP server tools and adapts them to AgentKit without changing the
/// MCP-generated schema or requiring hosts to expose a network MCP endpoint.
/// </summary>
public sealed class McpToolCatalog
{
    private readonly IServiceProvider services;
    private readonly Func<MethodInfo, bool> requiresApproval;
    private readonly Func<MethodInfo, IEnumerable<object>> metadataFactory;
    private readonly IReadOnlyList<McpToolRegistration> registrations;

    public McpToolCatalog(
        IServiceProvider services,
        IEnumerable<Assembly> assemblies,
        IReadOnlySet<string>? enabledToolNames = null,
        Func<MethodInfo, bool>? requiresApproval = null,
        Func<MethodInfo, IEnumerable<object>>? metadataFactory = null)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        ArgumentNullException.ThrowIfNull(assemblies);
        this.requiresApproval = requiresApproval ?? (_ => false);
        this.metadataFactory = metadataFactory ?? (_ => []);

        var discovered = Discover(assemblies);
        ValidateEnabledToolNames(discovered, enabledToolNames);
        registrations = discovered
            .Where(item => enabledToolNames is null || enabledToolNames.Contains(item.Name))
            .Select(CreateRegistration)
            .ToArray();
    }

    public IReadOnlyList<McpToolRegistration> Tools => registrations;

    public static IReadOnlyList<string> GetRegisteredToolNames(IEnumerable<Assembly> assemblies) =>
        Discover(assemblies).Select(item => item.Name).ToArray();

    public McpToolRegistration? Get(string name) =>
        registrations.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));

    public async ValueTask<McpToolSession> CreateSessionAsync(
        Func<IServiceProvider, CancellationToken, ValueTask>? initializeScope = null,
        CancellationToken cancellationToken = default)
    {
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            if (initializeScope is not null)
                await initializeScope(scope.ServiceProvider, cancellationToken);
            return new McpToolSession(scope, registrations);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private McpToolRegistration CreateRegistration(DiscoveredTool discovered)
    {
        var metadata = metadataFactory(discovered.Method).ToList();
        if (this.requiresApproval(discovered.Method))
            metadata.Add(new ApprovalRequiredMetadata());
        var tool = McpServerTool.Create(
            method: discovered.Method,
            createTargetFunc: context => context.Services!.GetRequiredService(discovered.Type),
            options: new McpServerToolCreateOptions
            {
                Name = discovered.Name,
                Description = discovered.Method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Services = services,
                Metadata = metadata
            });
        var protocol = tool.ProtocolTool;
        return new McpToolRegistration(
            protocol.Name,
            protocol.Title,
            protocol.Description,
            protocol.InputSchema,
            this.requiresApproval(discovered.Method),
            discovered.Type,
            discovered.Method,
            tool);
    }

    private static IReadOnlyList<DiscoveredTool> Discover(IEnumerable<Assembly> assemblies)
    {
        var discovered = assemblies.Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => new { Type = type, Method = method, Attribute = method.GetCustomAttribute<McpServerToolAttribute>() }))
            .Where(item => item.Attribute is not null)
            .Select(item => new DiscoveredTool(item.Attribute!.Name ?? item.Method.Name, item.Type, item.Method))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var duplicate = discovered.GroupBy(item => item.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate MCP tool name '{duplicate}'.");
        return discovered;
    }

    private static void ValidateEnabledToolNames(
        IReadOnlyList<DiscoveredTool> discovered,
        IReadOnlySet<string>? enabledToolNames)
    {
        if (enabledToolNames is null) return;
        var names = discovered.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = enabledToolNames.Where(name => !names.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException($"Unknown MCP tool name(s): {string.Join(", ", unknown)}.", nameof(enabledToolNames));
    }

    private sealed record DiscoveredTool(string Name, Type Type, MethodInfo Method);
}

internal sealed record ApprovalRequiredMetadata;

public sealed record McpToolRegistration(
    string Name,
    string? Title,
    string? Description,
    JsonElement InputSchema,
    bool RequiresApproval,
    Type SourceType,
    MethodInfo SourceMethod,
    McpServerTool Tool)
{
    public ToolDefinition Definition { get; } = new(
        Name,
        Description ?? Title ?? Name,
        InputSchema,
        RequiresApproval);
}

/// <summary>One scoped MCP activation for one agent turn.</summary>
public sealed class McpToolSession : IAsyncDisposable
{
    private readonly AsyncServiceScope scope;
    private readonly IReadOnlyList<IAgentTool> tools;

    internal McpToolSession(AsyncServiceScope scope, IReadOnlyList<McpToolRegistration> registrations)
    {
        this.scope = scope;
        tools = registrations.Select(registration => (IAgentTool)new McpAgentTool(registration, scope.ServiceProvider)).ToArray();
    }

    public IReadOnlyList<IAgentTool> Tools => tools;
    public IServiceProvider Services => scope.ServiceProvider;

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

internal sealed class McpAgentTool(McpToolRegistration registration, IServiceProvider scopedServices) : IAgentTool
{
    public ToolDefinition Definition { get; } = registration.Definition;

    public async ValueTask<JsonElement> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("MCP tool arguments must be an object.", nameof(arguments));

        var jsonArguments = arguments.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        var request = new RequestContext<CallToolRequestParams>(
            server: InProcessMcpServer.Instance,
            jsonRpcRequest: new JsonRpcRequest { Method = "tools/call" },
            parameters: new CallToolRequestParams { Name = registration.Name, Arguments = jsonArguments })
        {
            Services = scopedServices
        };
        var result = await registration.Tool.InvokeAsync(request, cancellationToken);
        return JsonSerializer.SerializeToElement(result, result.GetType());
    }
}
