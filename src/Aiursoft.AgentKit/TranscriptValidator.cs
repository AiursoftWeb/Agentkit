using Aiursoft.AgentKit.Messages;

namespace Aiursoft.AgentKit;

public static class TranscriptValidator
{
    public static void Validate(IEnumerable<TranscriptMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var pending = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            if (message.Content.Count == 0) throw new ArgumentException("Messages cannot be empty.", nameof(messages));
            if (message.Role is TranscriptRole.System or TranscriptRole.User && message.Content.Any(x => x is not TextBlock)) throw new ArgumentException("System and user messages require text.", nameof(messages));
            if (message.Role == TranscriptRole.Tool)
            {
                var results = message.Content.OfType<ToolResultBlock>().ToArray();
                if (results.Length != message.Content.Count || results.Length == 0) throw new ArgumentException("Tool messages require results.", nameof(messages));
                foreach (var result in results) if (!pending.Remove(result.Result.CallId)) throw new ArgumentException("Orphan or duplicate tool result.", nameof(messages));
            }
            if (message.Role == TranscriptRole.Assistant)
                foreach (var call in message.Content.OfType<ToolCallBlock>()) if (!pending.Add(call.Call.Id)) throw new ArgumentException("Duplicate tool call id.", nameof(messages));
        }
        if (pending.Count != 0) throw new ArgumentException("Transcript contains incomplete historical calls.", nameof(messages));
    }
}
