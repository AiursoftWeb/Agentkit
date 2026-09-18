namespace Aiursoft.AgentKit.Messages;

public enum TranscriptRole { System, User, Assistant, Tool }

public sealed record TranscriptMessage(TranscriptRole Role, IReadOnlyList<AgentContentBlock> Content, bool IsMeta = false)
{
    public static TranscriptMessage System(string text) => new(TranscriptRole.System, [new TextBlock(text)]);
    public static TranscriptMessage User(string text) => new(TranscriptRole.User, [new TextBlock(text)]);
    public static TranscriptMessage Assistant(IEnumerable<AgentContentBlock> content) => new(TranscriptRole.Assistant, CopyContent(content));
    public static TranscriptMessage Tool(IEnumerable<ToolResult> results, bool isMeta = false) => new(TranscriptRole.Tool, results.Select(x => (AgentContentBlock)new ToolResultBlock(x.DeepCopy())).ToArray(), isMeta);
    public IReadOnlyList<ToolCall> ToolCalls => Content.OfType<ToolCallBlock>().Select(x => x.Call.DeepCopy()).ToArray();
    public IReadOnlyList<ToolResult> ToolResults => Content.OfType<ToolResultBlock>().Select(x => x.Result.DeepCopy()).ToArray();
    public TranscriptMessage DeepCopy() => new(Role, CopyContent(Content), IsMeta);
    private static AgentContentBlock CopyContent(AgentContentBlock block) => block switch { TextBlock x => x with { }, ToolCallBlock x => new ToolCallBlock(x.Call.DeepCopy()), ToolResultBlock x => new ToolResultBlock(x.Result.DeepCopy()), _ => throw new ArgumentException("Unknown content block.") };
    private static IReadOnlyList<AgentContentBlock> CopyContent(IEnumerable<AgentContentBlock> content) => content.Select(CopyContent).ToArray();
}
