using System.Text.Json;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
namespace Aiursoft.AgentKit.Tests;

[TestClass]
public sealed class CoreTests
{
    private sealed class Tool : IAgentTool { public ToolDefinition Definition { get; } public int CallCount; private readonly bool fail; public Tool(string name, bool approval = false, bool fail = false) { Definition = new(name, name, approval); this.fail = fail; } public ValueTask<JsonElement> ExecuteAsync(JsonElement a, CancellationToken c) { CallCount++; if (fail) throw new InvalidOperationException("secret"); return ValueTask.FromResult(a.Clone()); } }
    private sealed class Model : IAgentModelClient { private readonly Queue<AgentModelResponse> responses; public List<AgentModelRequest> Requests = []; public Model(params AgentModelResponse[] r) { responses = new(r); } public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest q, CancellationToken c) { Requests.Add(q); return ValueTask.FromResult(responses.Count > 0 ? responses.Dequeue() : new AgentModelResponse([new TextBlock("done")])); } }
    private sealed class ContextTool(string name) : IAgentTool, IAgentToolExecutionObserver { public ToolDefinition Definition { get; } = new(name, name); public AgentToolExecutionContext? Context { get; private set; } public void SetExecutionContext(AgentToolExecutionContext context) => Context = context; public ValueTask<JsonElement> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken) { Assert.IsNotNull(Context); return ValueTask.FromResult(arguments.Clone()); } }
    static ToolCall Call(string id, string name = "echo") => new(id, name, JsonDocument.Parse("{\"x\":1}").RootElement);
    static AgentModelResponse Calls(params ToolCall[] calls) => new(calls.Select(x => (AgentContentBlock)new ToolCallBlock(x)), AgentFinishReason.ToolCalls);
    [TestMethod] public async Task PlainText() { var r = await new BoundedAgentRunner(new Model(new AgentModelResponse([new TextBlock("ok")]))).RunAsync(new AgentRunRequest([], [])); Assert.AreEqual(AgentRunOutcome.Completed, r.Outcome); }
    [TestMethod] public async Task SingleTool() { var r = await new BoundedAgentRunner(new Model(Calls(Call("a")), new AgentModelResponse([new TextBlock("done")]))).RunAsync(new AgentRunRequest([], [new Tool("echo")])); Assert.AreEqual(ToolOutcome.Succeeded, r.Results[0].Outcome); Assert.AreEqual(ToolOutcome.Succeeded, r.Traces[0].Outcome); Assert.AreEqual(1, r.Transcript.Count(x => x.Role == TranscriptRole.Tool)); }
    [TestMethod] public async Task ParallelOrderOneMessage() { var m = new Model(Calls(Call("b"), Call("a")), new AgentModelResponse([new TextBlock("done")])); var r = await new BoundedAgentRunner(m).RunAsync(new AgentRunRequest([], [new Tool("echo")])); CollectionAssert.AreEqual(new[] { "b", "a" }, r.Results.Select(x => x.CallId).ToArray()); Assert.AreEqual(1, r.Transcript.Count(x => x.Role == TranscriptRole.Tool)); }
    [TestMethod] public async Task SortedDescriptors() { var m = new Model(new AgentModelResponse([new TextBlock("done")])); await new BoundedAgentRunner(m).RunAsync(new AgentRunRequest([], [new Tool("z"), new Tool("a")])); CollectionAssert.AreEqual(new[] { "a", "z" }, m.Requests[0].Tools.Select(x => x.Name).ToArray()); }
    [TestMethod] public async Task UnknownSiblingRecovery() { var r = await new BoundedAgentRunner(new Model(Calls(Call("u", "missing"), Call("ok", "ok")), new AgentModelResponse([new TextBlock("done")]))).RunAsync(new AgentRunRequest([], [new Tool("ok")])); Assert.AreEqual(2, r.Results.Count); Assert.AreEqual(ToolOutcome.Failed, r.Results[0].Outcome); Assert.AreEqual(ToolOutcome.Succeeded, r.Results[1].Outcome); }
    [TestMethod] public async Task ExceptionRedacted() { var r = await new BoundedAgentRunner(new Model(Calls(Call("x")), new AgentModelResponse([new TextBlock("done")]))).RunAsync(new AgentRunRequest([], [new Tool("echo", fail: true)])); Assert.IsFalse(r.Results[0].Error?.Contains("secret") == true); }
    [TestMethod] public async Task ModelFailure() { var r = await new BoundedAgentRunner(new ThrowingModel()).RunAsync(new AgentRunRequest([], [])); Assert.AreEqual(AgentRunOutcome.ModelFailure, r.Outcome); }
    private sealed class ThrowingModel : IAgentModelClient { public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest r, CancellationToken c) => throw new InvalidOperationException("secret"); }
    [TestMethod] public async Task ExactMax() { var r = await new BoundedAgentRunner(new Model(Calls(Call("a")), Calls(Call("b")))).RunAsync(new AgentRunRequest([], [new Tool("echo")], new(MaxIterations: 2))); Assert.AreEqual(AgentRunOutcome.MaxIterations, r.Outcome); Assert.AreEqual(2, r.Iterations); }
    [TestMethod] public async Task ApprovalAtomic() { var a = new Tool("echo"); var b = new Tool("ask", approval: true); var r = await new BoundedAgentRunner(new Model(Calls(Call("a"), Call("b", "ask")))).RunAsync(new AgentRunRequest([], [a, b])); Assert.AreEqual(AgentRunOutcome.ApprovalDeferred, r.Outcome); Assert.AreEqual("b", r.PendingCalls.Single().Id); Assert.AreEqual("ask", r.PendingCalls.Single().Name); Assert.AreEqual(0, a.CallCount); Assert.AreEqual(0, r.Transcript.Count(x => x.Role == TranscriptRole.Tool)); }
    [TestMethod] public async Task DuplicateNames() { var result = await new BoundedAgentRunner(new Model()).RunAsync(new AgentRunRequest([], [new Tool("x"), new Tool("x")])); Assert.AreEqual(AgentRunOutcome.InvalidConfiguration, result.Outcome); }
    [TestMethod] public void TranscriptInvalid() { Assert.Throws<ArgumentException>(() => TranscriptValidator.Validate([TranscriptMessage.Tool([new ToolResult("x", "e", ToolOutcome.Failed)])])); Assert.Throws<ArgumentException>(() => TranscriptValidator.Validate([TranscriptMessage.Assistant([new ToolCallBlock(Call("x"))])])); }
    [TestMethod] public void JsonClone() { ToolCall call; using (var document = JsonDocument.Parse("{\"x\":2}")) { call = new ToolCall("x", "e", document.RootElement.Clone()); } Assert.AreEqual(2, call.Arguments.GetProperty("x").GetInt32()); }
    [TestMethod] public async Task FinishReasons() { var l = await new BoundedAgentRunner(new Model(new AgentModelResponse([new TextBlock("x")], AgentFinishReason.Length))).RunAsync(new AgentRunRequest([], [])); var f = await new BoundedAgentRunner(new Model(new AgentModelResponse([new TextBlock("x")], AgentFinishReason.Refusal))).RunAsync(new AgentRunRequest([], [])); Assert.AreEqual(AgentRunOutcome.Length, l.Outcome); Assert.AreEqual(AgentRunOutcome.Refused, f.Outcome); }
    [TestMethod]
    public async Task ApprovalCallbackRunsOnceAndRejectPairs()
    {
        var callbackCount = 0;
        var model = new Model(Calls(Call("a")), new AgentModelResponse([new TextBlock("done")]));
        var result = await new BoundedAgentRunner(new AgentModelClientWrapper(model)).RunAsync(new AgentRunRequest([], [new Tool("echo")], new(Approver: (_, _) => { callbackCount++; return ValueTask.FromResult(ApprovalDecision.Reject); })));
        Assert.AreEqual(1, callbackCount);
        Assert.AreEqual(ToolOutcome.Rejected, result.Results[0].Outcome);
    }

    [TestMethod]
    public async Task CancellationBeforeModelReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new BoundedAgentRunner(new Model(new AgentModelResponse([new TextBlock("ignored")]))).RunAsync(new AgentRunRequest([], []), cancellation.Token);
        Assert.AreEqual(AgentRunOutcome.Cancelled, result.Outcome);
    }

    private sealed class AgentModelClientWrapper : IAgentModelClient
    {
        private readonly IAgentModelClient _inner;
        public AgentModelClientWrapper(IAgentModelClient inner) => _inner = inner;
        public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken) => _inner.CompleteAsync(request, cancellationToken);
    }

    [TestMethod]
    public async Task CancellationDuringModelReturnsActualIterations()
    {
        using var cancellation = new CancellationTokenSource();
        var model = new CancellingModel(cancellation);
        var result = await new BoundedAgentRunner(model).RunAsync(new AgentRunRequest([], []), cancellation.Token);
        Assert.AreEqual(AgentRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(1, result.Iterations);
    }

    [TestMethod]
    public async Task CancellationDuringToolReturnsActualIterations()
    {
        using var cancellation = new CancellationTokenSource();
        var tool = new CancellingTool(cancellation);
        var result = await new BoundedAgentRunner(new Model(Calls(Call("x")))).RunAsync(new AgentRunRequest([], [tool]), cancellation.Token);
        Assert.AreEqual(AgentRunOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(1, result.Iterations);
    }

    [TestMethod]
    public void ResponseDeepCopiesBlocksAndRejectsNulls()
    {
        AgentModelResponse response;
        using (var document = JsonDocument.Parse("{\"x\":3}"))
        {
            response = new AgentModelResponse([new ToolCallBlock(new ToolCall("x", "echo", document.RootElement.Clone()))]);
        }
        Assert.AreEqual(3, response.ToolCalls[0].Arguments.GetProperty("x").GetInt32());
        Assert.Throws<ArgumentException>(() => new AgentModelResponse(new AgentContentBlock[] { null! }));
        Assert.Throws<ArgumentException>(() => new AgentModelRequest(new TranscriptMessage[] { null! }, []));
        Assert.Throws<ArgumentException>(() => new AgentModelRequest([], new ToolDefinition[] { null! }));
    }

    [TestMethod]
    public async Task StopWithCallsIsModelFailure()
    {
        var response = new AgentModelResponse([new ToolCallBlock(Call("x"))]);
        var result = await new BoundedAgentRunner(new Model(response)).RunAsync(new AgentRunRequest([], []));
        Assert.AreEqual(AgentRunOutcome.ModelFailure, result.Outcome);
    }

    [TestMethod]
    public void TranscriptRoleValidationRejectsToolContentInUserMessage()
    {
        Assert.Throws<ArgumentException>(() => TranscriptValidator.Validate([
            new TranscriptMessage(TranscriptRole.User, [new ToolCallBlock(Call("x"))])
        ]));
    }

    [TestMethod]
    public async Task ObserverPublishesCausalToolActivityWithClonedArguments()
    {
        ToolCall call;
        using (var document = JsonDocument.Parse("{\"query\":\"quartz\"}"))
        {
            call = new ToolCall("search-1", "echo", document.RootElement.Clone());
        }
        var events = new List<AgentRunEvent>();
        var model = new Model(
            new AgentModelResponse([new TextBlock("I will search."), new ToolCallBlock(call)], AgentFinishReason.ToolCalls),
            new AgentModelResponse([new TextBlock("Answer")]));

        var result = await new BoundedAgentRunner(model).RunAsync(new AgentRunRequest(
            [],
            [new Tool("echo")],
            new AgentRunOptions(Observer: (agentEvent, _) =>
            {
                events.Add(agentEvent);
                return ValueTask.CompletedTask;
            })));

        CollectionAssert.AreEqual(
            new[]
            {
                AgentRunEventKind.AssistantContent,
                AgentRunEventKind.ToolCallProposed,
                AgentRunEventKind.ToolExecutionStarted,
                AgentRunEventKind.ToolExecutionCompleted,
                AgentRunEventKind.AssistantContent,
                AgentRunEventKind.RunCompleted
            },
            events.Select(agentEvent => agentEvent.Kind).ToArray());
        CollectionAssert.AreEqual(
            Enumerable.Range(1, events.Count).Select(value => (long)value).ToArray(),
            events.Select(agentEvent => agentEvent.Sequence).ToArray());
        var assistantContent = events.Single(agentEvent => agentEvent.Kind == AgentRunEventKind.AssistantContent && agentEvent.Iteration == 1);
        Assert.AreEqual("I will search.", assistantContent.Text);
        Assert.IsTrue(assistantContent.HasToolCalls);
        Assert.AreEqual("echo", events.Single(agentEvent => agentEvent.Kind == AgentRunEventKind.ToolCallProposed).ToolName);
        Assert.AreEqual("search-1", events.Single(agentEvent => agentEvent.Kind == AgentRunEventKind.ToolExecutionStarted).ToolCallId);
        Assert.AreEqual("echo", events.Single(agentEvent => agentEvent.Kind == AgentRunEventKind.ToolExecutionStarted).ToolName);
        var completedTool = events.Single(agentEvent => agentEvent.Kind == AgentRunEventKind.ToolExecutionCompleted);
        Assert.AreEqual("search-1", completedTool.ToolCallId);
        Assert.AreEqual("echo", completedTool.ToolName);
        Assert.AreEqual(ToolOutcome.Succeeded, completedTool.ToolOutcome);
        Assert.AreEqual("quartz", events.Single(agentEvent => agentEvent.Kind == AgentRunEventKind.ToolCallProposed)
            .ToolCall!.Arguments.GetProperty("query").GetString());
        Assert.AreEqual(AgentRunOutcome.Completed, events.Single(agentEvent => agentEvent.Kind == AgentRunEventKind.RunCompleted).RunOutcome);
        Assert.AreEqual(AgentRunOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    public async Task ToolExecutionContextIsSuppliedBeforeExecution()
    {
        var tool = new ContextTool("echo");
        var result = await new BoundedAgentRunner(new Model(Calls(Call("context-1")), new AgentModelResponse([new TextBlock("done")]))).RunAsync(new AgentRunRequest([], [tool]));

        Assert.AreEqual(AgentRunOutcome.Completed, result.Outcome);
        Assert.IsNotNull(tool.Context);
        Assert.AreEqual("context-1", tool.Context.ToolCallId);
        Assert.AreEqual("echo", tool.Context.ToolName);
        Assert.AreEqual(1, tool.Context.Iteration);
    }

    [TestMethod]
    public async Task ObserverFailureDoesNotChangeAgentOutcome()
    {
        var result = await new BoundedAgentRunner(new Model(new AgentModelResponse([new TextBlock("ok")]))).RunAsync(
            new AgentRunRequest([], [], new AgentRunOptions(Observer: (_, _) =>
                ValueTask.FromException(new InvalidOperationException("observer failure")))));

        Assert.AreEqual(AgentRunOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    public async Task OrderedRunnerExecutesReadBeforeDeferringWrite()
    {
        var read = new Tool("read");
        var write = new Tool("write", approval: true);
        var runner = new OrderedAgentRunner(new Model(Calls(Call("read-1", "read"), Call("write-1", "write"))));

        var result = await runner.RunAsync(new AgentRunRequest([], [read, write]));

        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, result.Outcome);
        Assert.AreEqual(1, read.CallCount);
        Assert.AreEqual(0, write.CallCount);
        Assert.IsNotNull(result.Checkpoint);
        Assert.AreEqual(ToolOutcome.Succeeded, result.Checkpoint.Resolutions.Single(item => item.Call.Id == "read-1").Result!.Outcome);
        Assert.IsNull(result.Checkpoint.Resolutions.Single(item => item.Call.Id == "write-1").Result);
        Assert.AreEqual(0, result.Transcript.Count(message => message.Role == TranscriptRole.Tool));
    }

    [TestMethod]
    public async Task OrderedRunnerResolvesOnlyPendingCallThenContinues()
    {
        var read = new Tool("read");
        var write = new Tool("write", approval: true);
        var model = new Model(
            Calls(Call("read-1", "read"), Call("write-1", "write")),
            new AgentModelResponse([new TextBlock("done")]));
        var runner = new OrderedAgentRunner(model);
        var paused = await runner.RunAsync(new AgentRunRequest([], [read, write]));

        var resumed = await runner.ResolveAsync(paused.Checkpoint!, [read, write], "write-1", ApprovalDecision.Execute);

        Assert.AreEqual(OrderedAgentRunOutcome.Completed, resumed.Outcome);
        Assert.AreEqual("done", resumed.FinalText);
        Assert.AreEqual(2, resumed.Iterations);
        Assert.AreEqual(1, read.CallCount, "The executed read must not replay after approval.");
        Assert.AreEqual(1, write.CallCount);
        Assert.AreEqual(1, resumed.Transcript.Count(message => message.Role == TranscriptRole.Tool));
        CollectionAssert.AreEqual(new[] { "read-1", "write-1" }, resumed.Transcript.Single(message => message.Role == TranscriptRole.Tool).ToolResults.Select(result => result.CallId).ToArray());
        CollectionAssert.AreEqual(new[] { "read-1", "write-1" }, resumed.Results.Select(result => result.CallId).ToArray());
        CollectionAssert.AreEqual(new[] { "read-1", "write-1" }, resumed.Traces.Select(trace => trace.CallId).ToArray());
    }

    [TestMethod]
    public async Task OrderedRunnerKeepsMultiplePendingResolutionsInCallOrder()
    {
        var write = new Tool("write", approval: true);
        var runner = new OrderedAgentRunner(new Model(
            Calls(Call("write-1", "write"), Call("write-2", "write")),
            new AgentModelResponse([new TextBlock("done")])));
        var paused = await runner.RunAsync(new AgentRunRequest([], [write]));

        var afterSecond = await runner.ResolveAsync(paused.Checkpoint!, [write], "write-2", ApprovalDecision.Reject);
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, afterSecond.Outcome);
        Assert.AreEqual(ToolOutcome.Rejected, afterSecond.Checkpoint!.Resolutions.Single(item => item.Call.Id == "write-2").Result!.Outcome);

        var resumed = await runner.ResolveAsync(afterSecond.Checkpoint, [write], "write-1", ApprovalDecision.Execute);

        Assert.AreEqual(OrderedAgentRunOutcome.Completed, resumed.Outcome);
        CollectionAssert.AreEqual(new[] { "write-1", "write-2" }, resumed.Results.Select(result => result.CallId).ToArray());
        CollectionAssert.AreEqual(new[] { "write-1", "write-2" }, resumed.Transcript.Single(message => message.Role == TranscriptRole.Tool).ToolResults.Select(result => result.CallId).ToArray());
        Assert.AreEqual(1, write.CallCount);
        Assert.AreEqual(1, resumed.Traces.Count);
    }

    [TestMethod]
    public async Task OrderedRunnerRejectsPendingCallWithoutExecutingIt()
    {
        var write = new Tool("write", approval: true);
        var runner = new OrderedAgentRunner(new Model(Calls(Call("write-1", "write")), new AgentModelResponse([new TextBlock("done")])));
        var paused = await runner.RunAsync(new AgentRunRequest([], [write]));

        var resumed = await runner.ResolveAsync(paused.Checkpoint!, [write], "write-1", ApprovalDecision.Reject);

        Assert.AreEqual(OrderedAgentRunOutcome.Completed, resumed.Outcome);
        Assert.AreEqual(0, write.CallCount);
        Assert.AreEqual(ToolOutcome.Rejected, resumed.Transcript.Single(message => message.Role == TranscriptRole.Tool).ToolResults.Single().Outcome);
    }

    private sealed class CancellingModel : IAgentModelClient
    {
        private readonly CancellationTokenSource _source;
        public CancellingModel(CancellationTokenSource source) => _source = source;
        public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken)
        { _source.Cancel(); cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(new AgentModelResponse([new TextBlock("never")])); }
    }

    private sealed class CancellingTool : IAgentTool
    {
        private readonly CancellationTokenSource _source;
        public CancellingTool(CancellationTokenSource source) { _source = source; Definition = new ToolDefinition("echo", "echo"); }
        public ToolDefinition Definition { get; }
        public ValueTask<JsonElement> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
        { _source.Cancel(); cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(arguments); }
    }

}
