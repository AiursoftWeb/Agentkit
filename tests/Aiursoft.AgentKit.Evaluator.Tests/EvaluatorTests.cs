using System.Text.Json;
using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Evaluator;
using Aiursoft.AgentKit.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aiursoft.AgentKit.Evaluator.Tests;

[TestClass]
public sealed class EvaluatorTests
{
    [TestMethod]
    public async Task AgentRunEvidencePreservesToolCorrelation()
    {
        var call = new ToolCall("search-1", "search", JsonSerializer.SerializeToElement(new { query = "docs" }));
        var model = new ScriptedModel(
            new AgentModelResponse([new ToolCallBlock(call)], AgentFinishReason.ToolCalls),
            new AgentModelResponse([new TextBlock("Answer")]));
        var run = await new BoundedAgentRunner(model).RunAsync(new AgentRunRequest([], [new EchoTool("search")]));
        using var state = JsonDocument.Parse("{\"documents\":[\"docs\"]}");

        var evidence = AgentRunEvidenceAdapter.ToStepEvidence(
            new EvaluationStepIdentity(0, "subject", "context"),
            run,
            state.RootElement,
            TimeSpan.FromMilliseconds(10));

        Assert.IsTrue(evidence.Valid);
        var trace = evidence.Tools.Single();
        Assert.AreEqual("docs", trace.Parameters.GetProperty("query").GetString());
        Assert.AreEqual("search-1", trace.CallId);
        Assert.AreEqual(ToolOutcome.Succeeded, trace.Outcome);
        Assert.AreEqual(2, trace.Iteration);
        Assert.IsTrue(trace.Elapsed >= TimeSpan.Zero);
        Assert.AreEqual(TimeSpan.FromMilliseconds(10), evidence.Elapsed);
        Assert.AreEqual("Answer", evidence.Response);
    }

    [TestMethod]
    public void JsonMatcherSupportsSubsetAndUnorderedOperators()
    {
        using var subset = JsonDocument.Parse("{\"$subset\":[1,3]}");
        using var unordered = JsonDocument.Parse("{\"$unorderedEquals\":[\"a\",\"b\"]}");
        using var numbers = JsonDocument.Parse("[1,2,3]");
        using var letters = JsonDocument.Parse("[\"b\",\"a\"]");

        Assert.IsTrue(JsonMatcher.Matches(subset.RootElement, numbers.RootElement));
        Assert.IsTrue(JsonMatcher.Matches(unordered.RootElement, letters.RootElement));
    }

    [TestMethod]
    public void RequiredAndForbiddenAssertionsDetermineCaseOutcome()
    {
        using var requiredMatch = JsonDocument.Parse("{\"name\":\"search\",\"minCount\":1}");
        using var forbiddenMatch = JsonDocument.Parse("{\"name\":\"delete\"}");
        using var empty = JsonDocument.Parse("{}");
        var evaluationCase = new EvaluationCase("case", 1, [new EvaluationStep(
            0,
            "user",
            "board",
            new EvaluationExpectation([
                new EvaluationAssertion("search", EvaluationAssertionKinds.Tool, "tools", 10, 0, true, false, requiredMatch.RootElement),
                new EvaluationAssertion("no-delete", EvaluationAssertionKinds.ForbidTool, "safety", 0, -5, true, true, forbiddenMatch.RootElement)
            ]))]);
        var evidence = new EvaluationEvidence([new EvaluationStepEvidence(
            0,
            "user",
            "board",
            [new EvaluationToolTrace("search", empty.RootElement, empty.RootElement, 1, TimeSpan.Zero)],
            empty.RootElement,
            "done",
            1,
            TimeSpan.Zero)]);

        var result = new AssertionEvaluator().Evaluate(evaluationCase, evidence);

        Assert.IsTrue(result.Passed);
        Assert.AreEqual(100d, result.Score);
        Assert.AreEqual("case", result.CaseId);
        Assert.IsNull(result.Error);
        Assert.AreEqual(1, result.Steps!.Count);
        Assert.AreEqual("search", result.Assertions[0].Id);
        Assert.AreEqual(string.Empty, result.Assertions[0].Detail);
    }

    private sealed class EchoTool(string name) : IAgentTool
    {
        public ToolDefinition Definition { get; } = new(name, name);
        public ValueTask<JsonElement> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken) =>
            ValueTask.FromResult(arguments.Clone());
    }

    private sealed class ScriptedModel(params AgentModelResponse[] responses) : IAgentModelClient
    {
        private readonly Queue<AgentModelResponse> responses = new(responses);
        public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(responses.Dequeue());
    }

    [TestMethod]
    public void InvalidEvidenceStaysInWeightedDenominator()
    {
        using var match = JsonDocument.Parse("{\"name\":\"search\"}");
        using var empty = JsonDocument.Parse("{}");
        var assertion = new EvaluationAssertion("search", EvaluationAssertionKinds.Tool, "tools", 10, 0, true, false, match.RootElement);
        var valid = new EvaluationCaseResult("one", 1, true, true, [new EvaluationAssertionResult("search", "tools", true, 10, 10, true, false, "")]);
        var invalid = new EvaluationCaseResult("two", 1, false, false, [new EvaluationAssertionResult("search", "tools", true, 10, 10, true, false, "")]);

        var score = EvaluationScoreCalculator.Calculate([valid, invalid], new Dictionary<string, double> { ["tools"] = 1 });

        Assert.AreEqual(50d, score.Total);
        Assert.IsTrue(score.Incomplete);
        Assert.AreEqual(1, score.Dimensions.Count);
        Assert.AreEqual("tools", score.Dimensions[0].Dimension);
        Assert.AreEqual(50d, score.Dimensions[0].Score);
        Assert.AreEqual(1d, score.Dimensions[0].Weight);
        Assert.AreEqual(2, score.Cases.Count);
        _ = assertion;
        _ = empty;
    }
}
