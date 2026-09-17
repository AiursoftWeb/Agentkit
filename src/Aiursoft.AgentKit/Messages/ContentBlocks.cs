using Newtonsoft.Json.Linq;
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
    public JToken Arguments { get; }
    public ToolCall(string id, string name, JToken arguments) { if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool call identifiers are required."); ArgumentNullException.ThrowIfNull(arguments); Id = id; Name = name; Arguments = arguments.DeepClone(); }
    public ToolCall DeepCopy() => new(Id, Name, Arguments.DeepClone());
}
public sealed record ToolResult(string CallId, string Name, ToolOutcome Outcome, JToken? Output = null, string? Error = null) { public ToolResult DeepCopy() => new(CallId, Name, Outcome, Output?.DeepClone(), Error); }

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
