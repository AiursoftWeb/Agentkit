using Aiursoft.AgentKit.Messages;
using Newtonsoft.Json.Linq;

namespace Aiursoft.AgentKit.Evaluator;

/// <summary>Application-provided identity and state for one evaluated AgentKit run.</summary>
public sealed record EvaluationStepIdentity(
    int StepIndex,
    string? SubjectId = null,
    string? ContextId = null);

/// <summary>
/// Converts a provider-neutral AgentKit run into deterministic evaluation evidence.
/// Applications own state capture and fixture definitions; this adapter only preserves
/// agent-call correlation and terminal behavior.
/// </summary>
public static class AgentRunEvidenceAdapter
{
    public static EvaluationStepEvidence ToStepEvidence(
        EvaluationStepIdentity identity,
        AgentRunResult run,
        JToken state,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(run);
        var calls = run.Transcript
            .SelectMany(message => message.ToolCalls)
            .ToDictionary(call => call.Id, call => call, StringComparer.Ordinal);
        var results = run.Results.ToDictionary(result => result.CallId, result => result, StringComparer.Ordinal);
        ValidateCorrelations(calls, results, run.Traces);

        var traces = run.Traces.Select(trace =>
        {
            var call = calls[trace.CallId];
            var result = results[trace.CallId];
            return new EvaluationToolTrace(
                trace.ToolName,
                call.Arguments.DeepClone(),
                result.Output?.DeepClone() ?? JValue.CreateNull(),
                run.Iterations,
                trace.Duration,
                trace.CallId,
                result.Outcome);
        }).ToArray();

        var valid = run.Outcome == AgentRunOutcome.Completed;
        return new EvaluationStepEvidence(
            identity.StepIndex,
            identity.SubjectId,
            identity.ContextId,
            traces,
            state.DeepClone(),
            run.FinalText,
            run.Iterations,
            elapsed,
            valid,
            valid ? null : $"Agent run ended with outcome '{run.Outcome}'.");
    }

    public static EvaluationEvidence ToEvidence(
        IReadOnlyList<EvaluationStepEvidence> steps,
        bool valid = true,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return new EvaluationEvidence(steps.ToArray(), valid, error);
    }

    private static void ValidateCorrelations(
        IReadOnlyDictionary<string, ToolCall> calls,
        IReadOnlyDictionary<string, ToolResult> results,
        IReadOnlyList<ToolTrace> traces)
    {
        if (calls.Count != calls.Keys.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("The run transcript contains duplicate tool call identifiers.", nameof(calls));
        foreach (var result in results.Values)
        {
            if (!calls.TryGetValue(result.CallId, out var call) || !string.Equals(call.Name, result.Name, StringComparison.Ordinal))
                throw new ArgumentException("A tool result cannot be correlated to its tool call.", nameof(results));
        }
        foreach (var trace in traces)
        {
            if (!calls.TryGetValue(trace.CallId, out var call) || !results.TryGetValue(trace.CallId, out var result) ||
                !string.Equals(call.Name, trace.ToolName, StringComparison.Ordinal) ||
                !string.Equals(result.Name, trace.ToolName, StringComparison.Ordinal))
                throw new ArgumentException("A tool trace cannot be correlated to a matching tool call and result.", nameof(traces));
        }
    }
}
