using System.Text.Json;

namespace Aiursoft.AgentKit.Evaluator;

public static class EvaluationAssertionKinds
{
    public const string Tool = "tool";
    public const string ForbidTool = "forbidTool";
    public const string State = "state";
    public const string Response = "response";
    public const string MaxToolCalls = "maxToolCalls";
    public const string MaxIterations = "maxIterations";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Tool,
        ForbidTool,
        State,
        Response,
        MaxToolCalls,
        MaxIterations
    };
}

public sealed record EvaluationAssertion(
    string Id,
    string Kind,
    string Dimension,
    double Points,
    double Penalty,
    bool Required,
    bool HardFail,
    JsonElement Match,
    string Comment = "");

public sealed record EvaluationExpectation(IReadOnlyList<EvaluationAssertion> Assertions);

public sealed record EvaluationStep(
    int Index,
    string? SubjectId,
    string? ContextId,
    EvaluationExpectation Expectation);

public sealed record EvaluationCase(
    string Id,
    double Weight,
    IReadOnlyList<EvaluationStep> Steps);

public sealed record EvaluationToolTrace(
    string Name,
    JsonElement Parameters,
    JsonElement Result,
    int Iteration,
    TimeSpan Elapsed);

public sealed record EvaluationStepEvidence(
    int StepIndex,
    string? SubjectId,
    string? ContextId,
    IReadOnlyList<EvaluationToolTrace> Tools,
    JsonElement State,
    string Response,
    int IterationCount,
    TimeSpan Elapsed,
    bool Valid = true,
    string? Error = null);

public sealed record EvaluationEvidence(
    IReadOnlyList<EvaluationStepEvidence> Steps,
    bool Valid = true,
    string? Error = null);

public sealed record EvaluationAssertionResult(
    string Id,
    string Dimension,
    bool Matched,
    double Earned,
    double Maximum,
    bool Required,
    bool HardFail,
    string Detail);

public sealed record EvaluationCaseResult(
    string CaseId,
    double Weight,
    bool Passed,
    bool Valid,
    IReadOnlyList<EvaluationAssertionResult> Assertions,
    string? Error = null,
    IReadOnlyList<EvaluationStepEvidence>? Steps = null)
{
    public double Score
    {
        get
        {
            if (!Valid) return 0;
            var maximum = Assertions.Sum(assertion => assertion.Maximum);
            return maximum == 0 ? 0 : Math.Clamp(Assertions.Sum(assertion => assertion.Earned) / maximum * 100, 0, 100);
        }
    }
}

public sealed record EvaluationDimensionScore(string Dimension, double Score, double Weight, double Contribution);

public sealed record EvaluationScore(
    double Total,
    bool Incomplete,
    IReadOnlyList<EvaluationDimensionScore> Dimensions,
    IReadOnlyList<EvaluationCaseResult> Cases);
