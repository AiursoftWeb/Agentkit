using System.Text.Json;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.AgentKit;

public enum AgentFinishReason
{
    Stop,
    ToolCalls,
    Length,
    Refusal
}

public enum ApprovalDecision
{
    Execute,
    Defer,
    Reject
}

public enum AgentRunOutcome
{
    Completed,
    ApprovalDeferred,
    MaxIterations,
    Cancelled,
    ModelFailure,
    ApprovalFailure,
    InvalidConfiguration,
    Length,
    Refused
}

public enum AgentRunEventKind
{
    AssistantContent,
    ToolCallProposed,
    ToolExecutionStarted,
    ToolExecutionCompleted,
    RunCompleted
}

/// <summary>
/// A bounded, provider-neutral progress event. It never contains system or user messages,
/// raw tool results, provider bodies, or exception details.
/// </summary>
public sealed record AgentRunEvent(
    long Sequence,
    int Iteration,
    AgentRunEventKind Kind,
    string? Text = null,
    ToolCall? ToolCall = null,
    string? ToolName = null,
    ToolOutcome? ToolOutcome = null,
    AgentRunOutcome? RunOutcome = null,
    bool? HasToolCalls = null)
{
    public AgentRunEvent DeepCopy() => this with
    {
        ToolCall = ToolCall?.DeepCopy()
    };
}

public sealed record ToolDefinition
{
    public string Name { get; }
    public string Description { get; }
    public JsonElement InputSchema { get; }
    public bool RequiresApproval { get; }

    public ToolDefinition(string name, string description, JsonElement inputSchema, bool requiresApproval = false)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tool name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Tool description is required.", nameof(description));

        Name = name;
        Description = description;
        InputSchema = inputSchema.Clone();
        RequiresApproval = requiresApproval;
    }

    public ToolDefinition(string name, string description, bool requiresApproval = false)
        : this(name, description, JsonSerializer.SerializeToElement(new object()), requiresApproval)
    {
    }
}

public interface IAgentTool
{
    ToolDefinition Definition { get; }

    ValueTask<JsonElement> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}

public interface IAgentModelClient
{
    ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken);
}

public sealed record AgentModelRequest
{
    public IReadOnlyList<TranscriptMessage> Transcript { get; }
    public IReadOnlyList<ToolDefinition> Tools { get; }

    public AgentModelRequest(IEnumerable<TranscriptMessage> transcript, IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(tools);
        var messages = transcript.Cast<TranscriptMessage?>().ToArray();
        var descriptors = tools.Cast<ToolDefinition?>().ToArray();
        if (messages.Any(message => message is null)) throw new ArgumentException("Transcript cannot contain null messages.", nameof(transcript));
        if (descriptors.Any(descriptor => descriptor is null)) throw new ArgumentException("Tool descriptors cannot contain null values.", nameof(tools));

        Transcript = messages.Select(message => message!.DeepCopy()).ToArray();
        Tools = descriptors
            .Select(descriptor => new ToolDefinition(descriptor!.Name, descriptor.Description, descriptor.InputSchema, descriptor.RequiresApproval))
            .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record AgentModelResponse
{
    public IReadOnlyList<AgentContentBlock> Content { get; }
    public AgentFinishReason FinishReason { get; }

    public AgentModelResponse(IEnumerable<AgentContentBlock> content, AgentFinishReason finishReason = AgentFinishReason.Stop)
    {
        ArgumentNullException.ThrowIfNull(content);
        var blocks = content.Cast<AgentContentBlock?>().ToArray();
        if (blocks.Any(block => block is null)) throw new ArgumentException("Content cannot contain null blocks.", nameof(content));
        Content = blocks.Select(block => ContentBlockCloner.DeepCopy(block!)).ToArray();
        FinishReason = finishReason;
    }

    public string Text => string.Concat(Content.OfType<TextBlock>().Select(block => block.Text));
    public IReadOnlyList<ToolCall> ToolCalls => Content.OfType<ToolCallBlock>().Select(block => block.Call.DeepCopy()).ToArray();
}

public sealed record AgentRunOptions(
    int MaxConcurrency = 4,
    int MaxIterations = 8,
    Func<ToolCall, CancellationToken, ValueTask<ApprovalDecision>>? Approver = null,
    Func<AgentRunEvent, CancellationToken, ValueTask>? Observer = null)
{
    public int Concurrency => MaxConcurrency > 0 ? MaxConcurrency : throw new ArgumentOutOfRangeException(nameof(MaxConcurrency));
    public int Iterations => MaxIterations > 0 ? MaxIterations : throw new ArgumentOutOfRangeException(nameof(MaxIterations));
}

public sealed record AgentRunRequest(IEnumerable<TranscriptMessage> InitialMessages, IEnumerable<IAgentTool> Tools, AgentRunOptions? Options = null);
public sealed record ToolTrace(string CallId, string ToolName, ToolOutcome Outcome, TimeSpan Duration);
public sealed record AgentRunResult(AgentRunOutcome Outcome, string FinalText, IReadOnlyList<ToolCall> PendingCalls, IReadOnlyList<ToolResult> Results, IReadOnlyList<ToolTrace> Traces, IReadOnlyList<TranscriptMessage> Transcript, int Iterations);

public interface IAgentRunner
{
    ValueTask<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default);
}
