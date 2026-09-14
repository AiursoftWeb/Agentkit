using System.Text.Json;
namespace Aiursoft.AgentKit.Messages;

public abstract record AgentContentBlock;
public sealed record TextBlock(string Text) : AgentContentBlock;
public sealed record ToolCallBlock(ToolCall Call) : AgentContentBlock;
public sealed record ToolResultBlock(ToolResult Result) : AgentContentBlock;
public enum ToolOutcome { Succeeded, Deferred, Rejected, Failed }
public sealed record ToolCall
{
    public string Id { get; }
    public string Name { get; }
    public JsonElement Arguments { get; }
    public ToolCall(string id, string name, JsonElement arguments) { if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool call identifiers are required."); Id = id; Name = name; Arguments = arguments.Clone(); }
    public ToolCall DeepCopy() => new(Id, Name, Arguments.Clone());
}
public sealed record ToolResult(string CallId, string Name, ToolOutcome Outcome, JsonElement? Output = null, string? Error = null) { public ToolResult DeepCopy() => new(CallId, Name, Outcome, Output?.Clone(), Error); }

public static class ContentBlockCloner
{
    public static AgentContentBlock DeepCopy(AgentContentBlock block) => block switch
    {
        TextBlock text => text with { },
        ToolCallBlock call => new ToolCallBlock(call.Call.DeepCopy()),
        ToolResultBlock result => new ToolResultBlock(result.Result.DeepCopy()),
        _ => throw new ArgumentException("Unknown content block.", nameof(block))
    };
}
