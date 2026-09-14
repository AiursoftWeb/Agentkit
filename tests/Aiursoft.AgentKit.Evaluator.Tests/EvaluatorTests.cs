using System.Text.Json;
using Aiursoft.AgentKit.Evaluator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aiursoft.AgentKit.Evaluator.Tests;

[TestClass]
public sealed class EvaluatorTests
{
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
        _ = assertion;
        _ = empty;
    }
}
