using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aiursoft.AgentKit.Evaluator;

public static class JsonMatcher
{
    public static IReadOnlySet<string> SupportedOperators { get; } = new HashSet<string>(
        ["$exact", "$contains", "$regex", "$oneOf", "$subset", "$unorderedEquals", "$exists", "$var"],
        StringComparer.Ordinal);

    public static bool Matches(JsonElement expected, JsonElement actual, IReadOnlyDictionary<string, JsonElement>? variables = null)
    {
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var properties = expected.EnumerateObject().ToArray();
            if (properties.Length == 1 && properties[0].Name.StartsWith('$'))
                return MatchOperator(properties[0], actual, variables);
            if (actual.ValueKind != JsonValueKind.Object) return false;
            return properties.All(property => actual.TryGetProperty(property.Name, out var found) && Matches(property.Value, found, variables));
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            if (actual.ValueKind != JsonValueKind.Array) return false;
            var expectedItems = expected.EnumerateArray().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            return expectedItems.Length == actualItems.Length && expectedItems.Zip(actualItems).All(pair => Matches(pair.First, pair.Second, variables));
        }
        return expected.ValueKind == actual.ValueKind && expected.ValueKind switch
        {
            JsonValueKind.String => string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => expected.GetRawText() == actual.GetRawText(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => expected.GetRawText() == actual.GetRawText()
        };
    }

    private static bool MatchOperator(JsonProperty operation, JsonElement actual, IReadOnlyDictionary<string, JsonElement>? variables) => operation.Name switch
    {
        "$exact" => JsonElement.DeepEquals(operation.Value, actual),
        "$contains" => Contains(operation.Value, actual, variables),
        "$regex" => Regex.IsMatch(actual.ToString(), operation.Value.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        "$oneOf" => operation.Value.EnumerateArray().Any(item => Matches(item, actual, variables)),
        "$subset" => operation.Value.EnumerateArray().All(item => actual.ValueKind == JsonValueKind.Array && actual.EnumerateArray().Any(candidate => Matches(item, candidate, variables))),
        "$unorderedEquals" => Unordered(operation.Value, actual, variables),
        "$exists" => operation.Value.GetBoolean() == (actual.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined),
        "$var" => variables is not null && variables.TryGetValue(operation.Value.GetString()!, out var value) && Matches(value, actual, variables),
        _ => false
    };

    private static bool Contains(JsonElement expected, JsonElement actual, IReadOnlyDictionary<string, JsonElement>? variables) => actual.ValueKind switch
    {
        JsonValueKind.String when expected.ValueKind == JsonValueKind.String => actual.GetString()!.Contains(expected.GetString()!, StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Array => actual.EnumerateArray().Any(item => Matches(expected, item, variables)),
        JsonValueKind.Object when expected.ValueKind == JsonValueKind.Object => Matches(expected, actual, variables),
        _ => false
    };

    private static bool Unordered(JsonElement expected, JsonElement actual, IReadOnlyDictionary<string, JsonElement>? variables)
    {
        if (expected.ValueKind != JsonValueKind.Array || actual.ValueKind != JsonValueKind.Array) return false;
        var remaining = actual.EnumerateArray().ToList();
        foreach (var item in expected.EnumerateArray())
        {
            var index = remaining.FindIndex(candidate => Matches(item, candidate, variables));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        return remaining.Count == 0;
    }
}
