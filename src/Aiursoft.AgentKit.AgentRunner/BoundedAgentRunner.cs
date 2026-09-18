using Aiursoft.AgentKit.Messages;

namespace Aiursoft.AgentKit.AgentRunner;

public sealed class BoundedAgentRunner : IAgentRunner
{
    private readonly IAgentModelClient model;

    public BoundedAgentRunner(IAgentModelClient model)
    {
        this.model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public async ValueTask<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = request.Options ?? new AgentRunOptions();
        var tools = request.Tools.ToArray();
        var transcript = request.InitialMessages.Select(message => message.DeepCopy()).ToList();
        var results = new List<ToolResult>();
        var traces = new List<ToolTrace>();
        var iterations = 0;
        var finalText = string.Empty;
        long sequence = 0;

        async ValueTask PublishAsync(AgentRunEvent value, CancellationToken token)
        {
            if (options.Observer is null) return;
            var sequenced = value with { Sequence = ++sequence };
            try
            {
                await options.Observer(sequenced.DeepCopy(), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Progress observers must never affect the agent's execution outcome.
            }
        }

        AgentRunResult Build(
            AgentRunOutcome outcome,
            IReadOnlyList<ToolCall>? pending = null) => new(
                outcome,
                finalText,
                (pending ?? []).Select(call => call.DeepCopy()).ToArray(),
                results.Select(result => result.DeepCopy()).ToArray(),
                traces.ToArray(),
                transcript.Select(message => message.DeepCopy()).ToArray(),
                iterations);

        async ValueTask<AgentRunResult> CompleteAsync(
            AgentRunOutcome outcome,
            IReadOnlyList<ToolCall>? pending = null)
        {
            try
            {
                await PublishAsync(new AgentRunEvent(0, iterations, AgentRunEventKind.RunCompleted, RunOutcome: outcome), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Build(AgentRunOutcome.Cancelled);
            }

            return Build(outcome, pending);
        }

        try
        {
            var registry = new AgentToolRunner(tools);
            var descriptors = tools
                .Select(tool => tool.Definition)
                .OrderBy(definition => definition.Name, StringComparer.Ordinal)
                .ToArray();
            TranscriptValidator.Validate(transcript);
            _ = options.Concurrency;
            _ = options.Iterations;
            cancellationToken.ThrowIfCancellationRequested();

            while (iterations < options.Iterations)
            {
                iterations++;
                AgentModelResponse response;
                try
                {
                    response = await model.CompleteAsync(new AgentModelRequest(transcript, descriptors), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return await CompleteAsync(AgentRunOutcome.Cancelled);
                }
                catch
                {
                    return await CompleteAsync(AgentRunOutcome.ModelFailure);
                }

                finalText = response.Text;
                var calls = response.ToolCalls;
                var hasCalls = calls.Count > 0;
                var hasToolBlocks = response.Content.OfType<ToolCallBlock>().Any();

                if (response.FinishReason == AgentFinishReason.Stop && hasToolBlocks ||
                    response.FinishReason == AgentFinishReason.ToolCalls && !hasCalls)
                {
                    return await CompleteAsync(AgentRunOutcome.ModelFailure);
                }

                transcript.Add(TranscriptMessage.Assistant(response.Content));
                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    await PublishAsync(new AgentRunEvent(0, iterations, AgentRunEventKind.AssistantContent,
                        Text: response.Text,
                        HasToolCalls: hasCalls), cancellationToken);
                }

                if (response.FinishReason == AgentFinishReason.Length)
                {
                    return await CompleteAsync(AgentRunOutcome.Length);
                }

                if (response.FinishReason == AgentFinishReason.Refusal)
                {
                    return await CompleteAsync(AgentRunOutcome.Refused);
                }

                if (!hasCalls)
                {
                    return await CompleteAsync(AgentRunOutcome.Completed);
                }

                foreach (var call in calls)
                {
                    await PublishAsync(new AgentRunEvent(0, iterations, AgentRunEventKind.ToolCallProposed, ToolCall: call.DeepCopy(), ToolName: call.Name), cancellationToken);
                }

                IReadOnlyDictionary<string, ApprovalDecision> decisions;
                try
                {
                    decisions = await ResolveDecisionsAsync(calls, tools, options, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return await CompleteAsync(AgentRunOutcome.Cancelled);
                }
                catch
                {
                    return await CompleteAsync(AgentRunOutcome.ApprovalFailure);
                }

                var deferredCalls = calls.Where(call => decisions[call.Id] == ApprovalDecision.Defer).ToArray();
                if (deferredCalls.Length > 0)
                {
                    foreach (var call in deferredCalls)
                    {
                        await PublishAsync(new AgentRunEvent(0, iterations, AgentRunEventKind.ToolExecutionCompleted,
                            ToolCallId: call.Id, ToolName: call.Name, ToolOutcome: ToolOutcome.Deferred), cancellationToken);
                    }
                    return await CompleteAsync(AgentRunOutcome.ApprovalDeferred, deferredCalls);
                }

                (ToolResult[] batchResults, ToolTrace[] batchTraces) = await registry.RunAsync(
                    calls,
                    decisions,
                    options.Concurrency,
                    PublishAsync,
                    iterations,
                    cancellationToken);
                results.AddRange(batchResults.Select(result => result.DeepCopy()));
                traces.AddRange(batchTraces);
                transcript.Add(TranscriptMessage.Tool(batchResults, isMeta: true));

                if (iterations == options.Iterations)
                {
                    return await CompleteAsync(AgentRunOutcome.MaxIterations);
                }
            }

            return await CompleteAsync(AgentRunOutcome.MaxIterations);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CompleteAsync(AgentRunOutcome.Cancelled);
        }
        catch (ArgumentException)
        {
            return await CompleteAsync(AgentRunOutcome.InvalidConfiguration);
        }
    }

    private static async ValueTask<IReadOnlyDictionary<string, ApprovalDecision>> ResolveDecisionsAsync(
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<IAgentTool> tools,
        AgentRunOptions options,
        CancellationToken cancellationToken)
    {
        var decisions = new Dictionary<string, ApprovalDecision>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            var tool = tools.FirstOrDefault(candidate => candidate.Definition.Name == call.Name);
            var decision = options.Approver is not null
                ? await options.Approver(call, cancellationToken)
                : tool?.Definition.RequiresApproval == true
                    ? ApprovalDecision.Defer
                    : ApprovalDecision.Execute;
            decisions.Add(call.Id, decision);
        }

        return decisions;
    }
}
