using Newtonsoft.Json.Linq;

namespace Aiursoft.AgentKit.Evaluator;

public interface IEvaluationAssertionEvaluator
{
    EvaluationCaseResult Evaluate(EvaluationCase evaluationCase, EvaluationEvidence evidence);
}

public sealed class AssertionEvaluator : IEvaluationAssertionEvaluator
{
    public EvaluationCaseResult Evaluate(EvaluationCase evaluationCase, EvaluationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        ArgumentNullException.ThrowIfNull(evidence);
        EvaluationDefinitionValidator.Validate(evaluationCase);
        var expectedIndexes = evaluationCase.Steps.Select(step => step.Index).ToHashSet();
        var evidenceIndexes = evidence.Steps.Select(step => step.StepIndex).ToArray();
        var indexesValid = evidenceIndexes.Length == expectedIndexes.Count &&
            evidenceIndexes.Distinct().Count() == evidenceIndexes.Length &&
            evidenceIndexes.All(expectedIndexes.Contains);
        var identitiesValid = indexesValid && evidence.Steps.All(step =>
        {
            var expected = evaluationCase.Steps.Single(item => item.Index == step.StepIndex);
            return string.Equals(expected.SubjectId, step.SubjectId, StringComparison.Ordinal) &&
                   string.Equals(expected.ContextId, step.ContextId, StringComparison.Ordinal);
        });

        var evidenceByIndex = indexesValid
            ? evidence.Steps.ToDictionary(step => step.StepIndex)
            : new Dictionary<int, EvaluationStepEvidence>();
        var results = new List<EvaluationAssertionResult>();
        foreach (var step in evaluationCase.Steps.OrderBy(item => item.Index))
        {
            evidenceByIndex.TryGetValue(step.Index, out var stepEvidence);
            foreach (var assertion in step.Expectation.Assertions)
            {
                if (!EvaluationAssertionKinds.All.Contains(assertion.Kind))
                    throw new ArgumentException($"Assertion '{assertion.Id}' has unknown kind '{assertion.Kind}'.", nameof(evaluationCase));
                var matched = stepEvidence is not null && (assertion.Kind switch
                {
                    EvaluationAssertionKinds.Tool => MatchTool(assertion.Match, stepEvidence.Tools),
                    EvaluationAssertionKinds.ForbidTool => MatchTool(assertion.Match, stepEvidence.Tools),
                    EvaluationAssertionKinds.State => JsonMatcher.Matches(assertion.Match, stepEvidence.State),
                    EvaluationAssertionKinds.Response => JsonMatcher.Matches(assertion.Match, new JValue(stepEvidence.Response)),
                    EvaluationAssertionKinds.MaxToolCalls => stepEvidence.Tools.Count <= assertion.Match.Value<int>(),
                    EvaluationAssertionKinds.MaxIterations => stepEvidence.IterationCount <= assertion.Match.Value<int>(),
                    _ => false
                });
                var forbidden = assertion.Kind == EvaluationAssertionKinds.ForbidTool;
                var success = forbidden ? !matched : matched;
                var earned = forbidden ? (matched ? assertion.Penalty : 0) : (matched ? assertion.Points : 0);
                results.Add(new EvaluationAssertionResult(
                    assertion.Id,
                    assertion.Dimension,
                    success,
                    earned,
                    Math.Max(0, assertion.Points),
                    assertion.Required,
                    assertion.HardFail && !success,
                    stepEvidence?.Error ?? assertion.Comment));
            }
        }

        var maximum = results.Sum(result => result.Maximum);
        var earnedTotal = Math.Max(0, results.Sum(result => result.Earned));
        var valid = evidence.Valid && indexesValid && identitiesValid && evidence.Steps.All(step => step.Valid);
        var passed = valid && results.All(result => !result.Required || result.Matched) &&
            results.All(result => !result.HardFail) && (maximum == 0 || earnedTotal / maximum >= .7);
        var error = evidence.Error ?? evidence.Steps.FirstOrDefault(step => !step.Valid)?.Error;
        if (error is null && !indexesValid) error = "Evidence must contain each evaluation step index exactly once.";
        if (error is null && !identitiesValid) error = "Evidence subject and context identities must match the evaluation step.";
        return new EvaluationCaseResult(evaluationCase.Id, evaluationCase.Weight, passed, valid, results, error, evidence.Steps);
    }

    private static bool MatchTool(JToken match, IReadOnlyList<EvaluationToolTrace> tools)
    {
        if (match is not JObject obj) return false;
        var name = obj.Value<string>("name");
        var candidates = tools.Where(tool => tool.Name == name);
        if (obj["parameters"] is { } parameters)
            candidates = candidates.Where(tool => JsonMatcher.Matches(parameters, tool.Parameters));
        if (obj["result"] is { } result)
            candidates = candidates.Where(tool => JsonMatcher.Matches(result, tool.Result));
        var count = candidates.Count();
        if (obj["minCount"]?.Value<int>() is { } minimum && count < minimum) return false;
        if (obj["maxCount"]?.Value<int>() is { } maximum && count > maximum) return false;
        return count > 0;
    }
}

public static class EvaluationScoreCalculator
{
    public static EvaluationScore Calculate(
        IReadOnlyList<EvaluationCaseResult> cases,
        IReadOnlyDictionary<string, double> weights)
    {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Any(item => string.IsNullOrWhiteSpace(item.Key) || !double.IsFinite(item.Value) || item.Value < 0))
            throw new ArgumentException("Dimension weights must be finite and non-negative.", nameof(weights));

        var dimensions = new List<EvaluationDimensionScore>();
        foreach (var (dimension, weight) in weights.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var assertions = cases.SelectMany(item => item.Assertions
                .Where(assertion => assertion.Dimension == dimension)
                .Select(assertion => (item.Weight, item.Valid, Assertion: assertion))).ToArray();
            var denominator = assertions.Sum(item => item.Weight * item.Assertion.Maximum);
            var earned = assertions.Sum(item => item.Weight * (item.Valid ? item.Assertion.Earned : 0));
            var score = denominator == 0 ? 0 : Math.Clamp(earned / denominator * 100, 0, 100);
            dimensions.Add(new EvaluationDimensionScore(dimension, score, weight, score * weight));
        }
        return new EvaluationScore(dimensions.Sum(item => item.Contribution), cases.Any(item => !item.Valid), dimensions, cases);
    }
}
