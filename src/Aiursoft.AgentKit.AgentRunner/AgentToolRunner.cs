using System.Diagnostics;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.AgentKit.AgentRunner;

internal sealed class AgentToolRunner
{
    private readonly IReadOnlyDictionary<string, IAgentTool> tools;

    public AgentToolRunner(IEnumerable<IAgentTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var list = tools.Cast<IAgentTool?>().ToArray();
        if (list.Any(tool => tool is null)) throw new ArgumentException("Tools cannot contain null entries.", nameof(tools));

        var nonNullTools = list.Select(tool => tool!).ToArray();
        if (nonNullTools.Any(tool => (object?)tool.Definition is null)) throw new ArgumentException("Every tool must have a definition.", nameof(tools));
        if (nonNullTools.GroupBy(tool => tool.Definition.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Tool names must be unique.", nameof(tools));

        this.tools = nonNullTools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);
    }

    public async ValueTask<(ToolResult[] Results, ToolTrace[] Traces)> RunAsync(
        IReadOnlyList<ToolCall> calls,
        IReadOnlyDictionary<string, ApprovalDecision> decisions,
        int maxConcurrency,
        Func<AgentRunEvent, CancellationToken, ValueTask> publish,
        int iteration,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(maxConcurrency);
        var results = new ToolResult?[calls.Count];
        var traces = new ToolTrace?[calls.Count];
        var tasks = calls.Select((call, index) => RunOneAsync(call, index, decisions, gate, results, traces, publish, iteration, cancellationToken)).ToArray();
        await Task.WhenAll(tasks);
        return (results.Select(result => result!).ToArray(), traces.Select(trace => trace!).ToArray());
    }

    private async Task RunOneAsync(
        ToolCall call,
        int index,
        IReadOnlyDictionary<string, ApprovalDecision> decisions,
        SemaphoreSlim gate,
        ToolResult?[] results,
        ToolTrace?[] traces,
        Func<AgentRunEvent, CancellationToken, ValueTask> publish,
        int iteration,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        var started = Stopwatch.GetTimestamp();

        try
        {
            if (!tools.TryGetValue(call.Name, out var tool))
            {
                results[index] = new ToolResult(call.Id, call.Name, ToolOutcome.Failed, Error: "Tool unavailable.");
                return;
            }

            var decision = decisions[call.Id];
            if (decision == ApprovalDecision.Reject)
            {
                results[index] = new ToolResult(call.Id, call.Name, ToolOutcome.Rejected, Error: "Tool call rejected.");
                return;
            }

            if (decision == ApprovalDecision.Defer)
            {
                results[index] = new ToolResult(call.Id, call.Name, ToolOutcome.Deferred, Error: "Tool call deferred.");
                return;
            }

            await publish(new AgentRunEvent(0, iteration, AgentRunEventKind.ToolExecutionStarted,
                ToolCallId: call.Id, ToolName: call.Name), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (tool is IAgentToolExecutionObserver observer)
                    observer.SetExecutionContext(new AgentToolExecutionContext(call.Id, call.Name, iteration));
                var output = await tool.ExecuteAsync(call.Arguments.DeepClone(), cancellationToken);
                results[index] = new ToolResult(call.Id, call.Name, ToolOutcome.Succeeded, output.DeepClone());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                results[index] = new ToolResult(call.Id, call.Name, ToolOutcome.Failed, Error: "Tool execution failed.");
            }
        }
        finally
        {
            if (results[index] is { } result)
            {
                traces[index] = new ToolTrace(call.Id, call.Name, result.Outcome, Stopwatch.GetElapsedTime(started));
                await publish(new AgentRunEvent(0, iteration, AgentRunEventKind.ToolExecutionCompleted,
                    ToolCallId: call.Id, ToolName: call.Name, ToolOutcome: result.Outcome), cancellationToken);
            }

            gate.Release();
        }
    }
}
