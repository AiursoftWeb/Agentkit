using System.Diagnostics;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.AgentKit.AgentRunner;

/// <summary>
/// Explicit sequential execution mode for hosts that pause individual tool calls for
/// approval. Unlike <see cref="BoundedAgentRunner"/>, this type never commits an incomplete
/// assistant tool-call batch to a model-visible transcript.
/// </summary>
public sealed class OrderedAgentRunner(IAgentModelClient model)
{
    private readonly IAgentModelClient model = model ?? throw new ArgumentNullException(nameof(model));

    public ValueTask<OrderedAgentRunResult> RunAsync(
        AgentRunRequest request,
        Func<ToolCall, CancellationToken, ValueTask<ApprovalDecision>>? approver = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tools = request.Tools.ToArray();
        var transcript = request.InitialMessages.Select(message => message.DeepCopy()).ToList();
        var options = request.Options ?? new AgentRunOptions();
        TranscriptValidator.Validate(transcript);
        return ContinueAsync(transcript, tools, options.Iterations, 0, approver, [], [], cancellationToken);
    }

    /// <summary>
    /// Resolves exactly one previously deferred call. The returned checkpoint is a new
    /// immutable snapshot, so a host can atomically store it with its approval state.
    /// When every call in the batch has a result, the runner appends one valid tool
    /// message and resumes model execution.
    /// </summary>
    public async ValueTask<OrderedAgentRunResult> ResolveAsync(
        OrderedAgentCheckpoint checkpoint,
        IEnumerable<IAgentTool> tools,
        string callId,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(tools);
        if (decision == ApprovalDecision.Defer)
            throw new ArgumentException("A deferred call must be resolved as execute or reject.", nameof(decision));

        var pending = checkpoint.Resolutions.SingleOrDefault(item => item.Call.Id == callId)
            ?? throw new ArgumentException("Tool call is not part of this checkpoint.", nameof(callId));
        if (pending.Result is not null)
            throw new InvalidOperationException("Tool call was already resolved.");

        var toolList = tools.ToArray();
        var registry = new AgentToolRunner(toolList);
        ToolResult result;
        ToolTrace? trace = null;
        if (decision == ApprovalDecision.Reject)
        {
            result = new ToolResult(pending.Call.Id, pending.Call.Name, ToolOutcome.Rejected, Error: "Tool call rejected.");
        }
        else
        {
            var (results, traces) = await registry.RunAsync(
                [pending.Call],
                new Dictionary<string, ApprovalDecision>(StringComparer.Ordinal) { [pending.Call.Id] = ApprovalDecision.Execute },
                1,
                static (_, _) => ValueTask.CompletedTask,
                checkpoint.Iterations,
                cancellationToken);
            result = results[0];
            trace = traces[0];
        }

        var resolutions = checkpoint.Resolutions
            .Select(item => item.Call.Id == callId ? item with { Result = result.DeepCopy() } : item.DeepCopy())
            .ToArray();
        var resolutionIds = checkpoint.Resolutions.Select(item => item.Call.Id).ToHashSet(StringComparer.Ordinal);
        var completedResults = checkpoint.CompletedResults
            .Where(item => !resolutionIds.Contains(item.CallId))
            .Concat(resolutions.Where(item => item.Result is not null).Select(item => item.Result!))
            .Select(item => item.DeepCopy())
            .ToArray();
        var tracesByCall = checkpoint.CompletedTraces.ToDictionary(item => item.CallId, StringComparer.Ordinal);
        if (trace is not null) tracesByCall[trace.CallId] = trace;
        var completedTraces = checkpoint.CompletedTraces
            .Where(item => !resolutionIds.Contains(item.CallId))
            .Concat(resolutions.Select(item => item.Call.Id)
                .Where(tracesByCall.ContainsKey)
                .Select(call => tracesByCall[call]))
            .ToArray();
        var updated = checkpoint with
        {
            Resolutions = resolutions,
            CompletedResults = completedResults,
            CompletedTraces = completedTraces
        };
        if (resolutions.Any(item => item.Result is null))
            return OrderedAgentRunResult.AwaitingApproval(updated);

        var transcript = checkpoint.Transcript.Select(message => message.DeepCopy()).ToList();
        transcript.Add(TranscriptMessage.Tool(resolutions.Select(item => item.Result!)));
        TranscriptValidator.Validate(transcript);
        return await ContinueAsync(
            transcript,
            toolList,
            checkpoint.MaxIterations,
            checkpoint.Iterations,
            null,
            updated.CompletedResults,
            updated.CompletedTraces,
            cancellationToken);
    }

    private async ValueTask<OrderedAgentRunResult> ContinueAsync(
        List<TranscriptMessage> transcript,
        IReadOnlyList<IAgentTool> tools,
        int maxIterations,
        int iterations,
        Func<ToolCall, CancellationToken, ValueTask<ApprovalDecision>>? approver,
        IReadOnlyList<ToolResult> priorResults,
        IReadOnlyList<ToolTrace> priorTraces,
        CancellationToken cancellationToken)
    {
        var registry = new AgentToolRunner(tools);
        var descriptors = tools.Select(tool => tool.Definition)
            .OrderBy(definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
        var results = priorResults.Select(result => result.DeepCopy()).ToList();
        var traces = priorTraces.ToList();

        try
        {
            while (iterations < maxIterations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                iterations++;
                AgentModelResponse response;
                try
                {
                    response = await model.CompleteAsync(new AgentModelRequest(transcript, descriptors), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return OrderedAgentRunResult.Cancelled(transcript, results, traces, iterations);
                }
                catch
                {
                    return OrderedAgentRunResult.ModelFailure(transcript, results, traces, iterations);
                }

                var calls = response.ToolCalls;
                var hasToolBlocks = response.Content.OfType<ToolCallBlock>().Any();
                if (response.FinishReason == AgentFinishReason.Stop && hasToolBlocks ||
                    response.FinishReason == AgentFinishReason.ToolCalls && calls.Count == 0)
                    return OrderedAgentRunResult.ModelFailure(transcript, results, traces, iterations);

                transcript.Add(TranscriptMessage.Assistant(response.Content));
                if (response.FinishReason == AgentFinishReason.Length)
                    return OrderedAgentRunResult.Length(transcript, results, traces, iterations, response.Text);
                if (response.FinishReason == AgentFinishReason.Refusal)
                    return OrderedAgentRunResult.Refused(transcript, results, traces, iterations, response.Text);
                if (calls.Count == 0)
                    return OrderedAgentRunResult.Completed(transcript, results, traces, iterations, response.Text);

                var resolutions = new List<OrderedToolResolution>(calls.Count);
                foreach (var call in calls)
                {
                    var decision = approver is null
                        ? tools.FirstOrDefault(tool => tool.Definition.Name == call.Name)?.Definition.RequiresApproval == true
                            ? ApprovalDecision.Defer
                            : ApprovalDecision.Execute
                        : await approver(call, cancellationToken);
                    if (decision == ApprovalDecision.Defer)
                    {
                        resolutions.Add(new OrderedToolResolution(call.DeepCopy(), null));
                        continue;
                    }

                    if (decision == ApprovalDecision.Reject)
                    {
                        var rejected = new ToolResult(call.Id, call.Name, ToolOutcome.Rejected, Error: "Tool call rejected.");
                        resolutions.Add(new OrderedToolResolution(call.DeepCopy(), rejected));
                        results.Add(rejected.DeepCopy());
                        continue;
                    }

                    var started = Stopwatch.GetTimestamp();
                    ToolResult result;
                    var tool = tools.FirstOrDefault(candidate => candidate.Definition.Name == call.Name);
                    if (tool is null)
                    {
                        result = new ToolResult(call.Id, call.Name, ToolOutcome.Failed, Error: "Tool unavailable.");
                    }
                    else
                    {
                        try
                        {
                            var output = await tool.ExecuteAsync(call.Arguments.Clone(), cancellationToken);
                            result = new ToolResult(call.Id, call.Name, ToolOutcome.Succeeded, output.Clone());
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            result = new ToolResult(call.Id, call.Name, ToolOutcome.Failed, Error: "Tool execution failed.");
                        }
                    }
                    resolutions.Add(new OrderedToolResolution(call.DeepCopy(), result));
                    results.Add(result.DeepCopy());
                    traces.Add(new ToolTrace(call.Id, call.Name, result.Outcome, Stopwatch.GetElapsedTime(started)));
                }

                if (resolutions.Any(item => item.Result is null))
                {
                    return OrderedAgentRunResult.AwaitingApproval(new OrderedAgentCheckpoint(
                        transcript,
                        resolutions,
                        results,
                        traces,
                        iterations,
                        maxIterations));
                }

                transcript.Add(TranscriptMessage.Tool(resolutions.Select(item => item.Result!)));
            }
            return OrderedAgentRunResult.MaxIterations(transcript, results, traces, iterations);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OrderedAgentRunResult.Cancelled(transcript, results, traces, iterations);
        }
    }
}

/// <summary>Frozen incomplete tool turn. It must not be sent to a model until fully resolved.</summary>
public sealed record OrderedAgentCheckpoint(
    IReadOnlyList<TranscriptMessage> Transcript,
    IReadOnlyList<OrderedToolResolution> Resolutions,
    IReadOnlyList<ToolResult> CompletedResults,
    IReadOnlyList<ToolTrace> CompletedTraces,
    int Iterations,
    int MaxIterations)
{
    public OrderedAgentCheckpoint DeepCopy() => new(
        Transcript.Select(message => message.DeepCopy()).ToArray(),
        Resolutions.Select(resolution => resolution.DeepCopy()).ToArray(),
        CompletedResults.Select(result => result.DeepCopy()).ToArray(),
        CompletedTraces.ToArray(),
        Iterations,
        MaxIterations);
}

public sealed record OrderedToolResolution(ToolCall Call, ToolResult? Result)
{
    public OrderedToolResolution DeepCopy() => new(Call.DeepCopy(), Result?.DeepCopy());
}

public enum OrderedAgentRunOutcome
{
    Completed,
    AwaitingApproval,
    MaxIterations,
    Cancelled,
    ModelFailure,
    Length,
    Refused
}

public sealed record OrderedAgentRunResult(
    OrderedAgentRunOutcome Outcome,
    string FinalText,
    OrderedAgentCheckpoint? Checkpoint,
    IReadOnlyList<ToolResult> Results,
    IReadOnlyList<ToolTrace> Traces,
    IReadOnlyList<TranscriptMessage> Transcript,
    int Iterations)
{
    public static OrderedAgentRunResult AwaitingApproval(OrderedAgentCheckpoint checkpoint) =>
        new(OrderedAgentRunOutcome.AwaitingApproval, string.Empty, checkpoint.DeepCopy(), [], [], checkpoint.Transcript.Select(message => message.DeepCopy()).ToArray(), checkpoint.Iterations);
    public static OrderedAgentRunResult Completed(IReadOnlyList<TranscriptMessage> transcript, IReadOnlyList<ToolResult> results, IReadOnlyList<ToolTrace> traces, int iterations, string text) =>
        new(OrderedAgentRunOutcome.Completed, text, null, results.Select(result => result.DeepCopy()).ToArray(), traces.ToArray(), transcript.Select(message => message.DeepCopy()).ToArray(), iterations);
    public static OrderedAgentRunResult MaxIterations(IReadOnlyList<TranscriptMessage> transcript, IReadOnlyList<ToolResult> results, IReadOnlyList<ToolTrace> traces, int iterations) =>
        new(OrderedAgentRunOutcome.MaxIterations, string.Empty, null, results.Select(result => result.DeepCopy()).ToArray(), traces.ToArray(), transcript.Select(message => message.DeepCopy()).ToArray(), iterations);
    public static OrderedAgentRunResult Cancelled(IReadOnlyList<TranscriptMessage> transcript, IReadOnlyList<ToolResult> results, IReadOnlyList<ToolTrace> traces, int iterations) =>
        new(OrderedAgentRunOutcome.Cancelled, string.Empty, null, results.Select(result => result.DeepCopy()).ToArray(), traces.ToArray(), transcript.Select(message => message.DeepCopy()).ToArray(), iterations);
    public static OrderedAgentRunResult ModelFailure(IReadOnlyList<TranscriptMessage> transcript, IReadOnlyList<ToolResult> results, IReadOnlyList<ToolTrace> traces, int iterations) =>
        new(OrderedAgentRunOutcome.ModelFailure, string.Empty, null, results.Select(result => result.DeepCopy()).ToArray(), traces.ToArray(), transcript.Select(message => message.DeepCopy()).ToArray(), iterations);
    public static OrderedAgentRunResult Length(IReadOnlyList<TranscriptMessage> transcript, IReadOnlyList<ToolResult> results, IReadOnlyList<ToolTrace> traces, int iterations, string text) =>
        new(OrderedAgentRunOutcome.Length, text, null, results.Select(result => result.DeepCopy()).ToArray(), traces.ToArray(), transcript.Select(message => message.DeepCopy()).ToArray(), iterations);
    public static OrderedAgentRunResult Refused(IReadOnlyList<TranscriptMessage> transcript, IReadOnlyList<ToolResult> results, IReadOnlyList<ToolTrace> traces, int iterations, string text) =>
        new(OrderedAgentRunOutcome.Refused, text, null, results.Select(result => result.DeepCopy()).ToArray(), traces.ToArray(), transcript.Select(message => message.DeepCopy()).ToArray(), iterations);
}
