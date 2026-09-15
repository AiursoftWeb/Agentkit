namespace Aiursoft.AgentKit.Evaluator;

/// <summary>Validates a domain-neutral evaluation definition before it is executed.</summary>
public static class EvaluationDefinitionValidator
{
    public static void Validate(EvaluationCase evaluationCase)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        if (string.IsNullOrWhiteSpace(evaluationCase.Id))
            throw new ArgumentException("Evaluation case id is required.", nameof(evaluationCase));
        if (!double.IsFinite(evaluationCase.Weight) || evaluationCase.Weight <= 0)
            throw new ArgumentException("Evaluation case weight must be finite and positive.", nameof(evaluationCase));
        if (evaluationCase.Steps.Count == 0)
            throw new ArgumentException("Evaluation case requires at least one step.", nameof(evaluationCase));
        if (evaluationCase.Steps.Select(step => step.Index).Distinct().Count() != evaluationCase.Steps.Count)
            throw new ArgumentException("Evaluation step indexes must be unique.", nameof(evaluationCase));

        var assertionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in evaluationCase.Steps)
        {
            foreach (var assertion in step.Expectation.Assertions)
            {
                if (string.IsNullOrWhiteSpace(assertion.Id) || !assertionIds.Add(assertion.Id))
                    throw new ArgumentException("Evaluation assertion ids must be non-empty and unique.", nameof(evaluationCase));
                if (!EvaluationAssertionKinds.All.Contains(assertion.Kind))
                    throw new ArgumentException($"Assertion '{assertion.Id}' has unknown kind '{assertion.Kind}'.", nameof(evaluationCase));
                if (string.IsNullOrWhiteSpace(assertion.Dimension) || !double.IsFinite(assertion.Points) ||
                    !double.IsFinite(assertion.Penalty) || assertion.Points < 0 || assertion.Penalty > 0 ||
                    (assertion.Points == 0 && assertion.Penalty == 0))
                    throw new ArgumentException($"Assertion '{assertion.Id}' has invalid scoring values.", nameof(evaluationCase));
                ValidateMatch(assertion);
            }
        }
    }

    private static void ValidateMatch(EvaluationAssertion assertion)
    {
        if (assertion.Match.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new ArgumentException($"Assertion '{assertion.Id}' has no match value.", nameof(assertion));
        if (assertion.Kind is EvaluationAssertionKinds.MaxToolCalls or EvaluationAssertionKinds.MaxIterations &&
            (assertion.Match.ValueKind != System.Text.Json.JsonValueKind.Number || !assertion.Match.TryGetInt32(out var maximum) || maximum < 0))
            throw new ArgumentException($"Assertion '{assertion.Id}' requires a non-negative integer match value.", nameof(assertion));
    }
}
