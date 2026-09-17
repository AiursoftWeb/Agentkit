using Newtonsoft.Json.Linq;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;

namespace Aiursoft.AgentKit.Evaluator.Tests;

[TestClass]
public sealed class EvaluatorTests
{
    [TestMethod]
    public async Task AgentRunEvidencePreservesToolCorrelation()
    {
        var call = new ToolCall("search-1", "search", JToken.FromObject(new { query = "docs" }));
        var model = new ScriptedModel(
            new AgentModelResponse([new ToolCallBlock(call)], AgentFinishReason.ToolCalls),
            new AgentModelResponse([new TextBlock("Answer")]));
        var run = await new BoundedAgentRunner(model).RunAsync(new AgentRunRequest([], [new EchoTool("search")]));
        var state = JToken.Parse("{\"documents\":[\"docs\"]}");

        var evidence = AgentRunEvidenceAdapter.ToStepEvidence(
            new EvaluationStepIdentity(0, "subject", "context"),
            run,
            state,
            TimeSpan.FromMilliseconds(10));

        Assert.IsTrue(evidence.Valid);
        var trace = evidence.Tools.Single();
        Assert.AreEqual("docs", trace.Parameters["query"]?.ToObject<string>());
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
        var subset = JToken.Parse("{\"$subset\":[1,3]}");
        var unordered = JToken.Parse("{\"$unorderedEquals\":[\"a\",\"b\"]}");
        var numbers = JToken.Parse("[1,2,3]");
        var letters = JToken.Parse("[\"b\",\"a\"]");

        Assert.IsTrue(JsonMatcher.Matches(subset, numbers));
        Assert.IsTrue(JsonMatcher.Matches(unordered, letters));
    }

    [TestMethod]
    public void RequiredAndForbiddenAssertionsDetermineCaseOutcome()
    {
        var requiredMatch = JToken.Parse("{\"name\":\"search\",\"minCount\":1}");
        var forbiddenMatch = JToken.Parse("{\"name\":\"delete\"}");
        var empty = JToken.Parse("{}");
        var evaluationCase = new EvaluationCase("case", 1, [new EvaluationStep(
            0,
            "user",
            "board",
            new EvaluationExpectation([
                new EvaluationAssertion("search", EvaluationAssertionKinds.Tool, "tools", 10, 0, true, false, requiredMatch),
                new EvaluationAssertion("no-delete", EvaluationAssertionKinds.ForbidTool, "safety", 0, -5, true, true, forbiddenMatch)
            ]))]);
        var evidence = new EvaluationEvidence([new EvaluationStepEvidence(
            0,
            "user",
            "board",
            [new EvaluationToolTrace("search", empty, empty, 1, TimeSpan.Zero)],
            empty,
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
        public ValueTask<JToken> ExecuteAsync(JToken arguments, CancellationToken cancellationToken) =>
            ValueTask.FromResult(arguments.DeepClone());
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
        var match = JToken.Parse("{\"name\":\"search\"}");
        var empty = JToken.Parse("{}");
        var assertion = new EvaluationAssertion("search", EvaluationAssertionKinds.Tool, "tools", 10, 0, true, false, match);
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
