using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aiursoft.AgentKit.Evaluator;

public static class JsonMatcher
{
    public static IReadOnlySet<string> SupportedOperators { get; } = new HashSet<string>(
        ["$exact", "$contains", "$regex", "$oneOf", "$subset", "$unorderedEquals", "$exists", "$var"],
        StringComparer.Ordinal);

    public static bool Matches(JToken expected, JToken actual, IReadOnlyDictionary<string, JToken>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected is JObject expectedObject)
        {
            var properties = expectedObject.Properties().ToArray();
            if (properties.Length == 1 && properties[0].Name.StartsWith('$')) return MatchOperator(properties[0], actual, variables);
            if (actual is not JObject actualObject) return false;
            return properties.All(property => actualObject.Property(property.Name, StringComparison.Ordinal) is { } found && Matches(property.Value, found.Value, variables));
        }
        if (expected is JArray expectedArray)
        {
            if (actual is not JArray actualArray || expectedArray.Count != actualArray.Count) return false;
            return expectedArray.Zip(actualArray).All(pair => Matches(pair.First, pair.Second, variables));
        }
        if (expected.Type != actual.Type) return false;
        return expected.Type switch
        {
            JTokenType.String => string.Equals(expected.Value<string>(), actual.Value<string>(), StringComparison.Ordinal),
            JTokenType.Integer or JTokenType.Float => expected.ToString(Formatting.None) == actual.ToString(Formatting.None),
            JTokenType.Boolean or JTokenType.Null => true,
            _ => expected.ToString(Formatting.None) == actual.ToString(Formatting.None)
        };
    }

    private static bool MatchOperator(JProperty operation, JToken actual, IReadOnlyDictionary<string, JToken>? variables) => operation.Name switch
    {
        "$exact" => JToken.DeepEquals(operation.Value, actual),
        "$contains" => Contains(operation.Value, actual, variables),
        "$regex" => Regex.IsMatch(actual.ToString(), operation.Value.Value<string>()!, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
        "$oneOf" => operation.Value is JArray oneOf && oneOf.Any(item => Matches(item, actual, variables)),
        "$subset" => operation.Value is JArray subset && actual is JArray array && subset.All(item => array.Any(candidate => Matches(item, candidate, variables))),
        "$unorderedEquals" => Unordered(operation.Value, actual, variables),
        "$exists" => operation.Value.Value<bool>() == (actual.Type is not JTokenType.Null and not JTokenType.Undefined),
        "$var" => variables is not null && variables.TryGetValue(operation.Value.Value<string>()!, out var value) && Matches(value, actual, variables),
        _ => false
    };

    private static bool Contains(JToken expected, JToken actual, IReadOnlyDictionary<string, JToken>? variables) => actual switch
    {
        JValue { Type: JTokenType.String } when expected.Type == JTokenType.String => actual.Value<string>()!.Contains(expected.Value<string>()!, StringComparison.OrdinalIgnoreCase),
        JArray array => array.Any(item => Matches(expected, item, variables)),
        JObject when expected is JObject => Matches(expected, actual, variables),
        _ => false
    };

    private static bool Unordered(JToken expected, JToken actual, IReadOnlyDictionary<string, JToken>? variables)
    {
        if (expected is not JArray expectedArray || actual is not JArray actualArray) return false;
        var remaining = actualArray.ToList();
        foreach (var item in expectedArray)
        {
            var index = remaining.FindIndex(candidate => Matches(item, candidate, variables));
            if (index < 0) return false;
            remaining.RemoveAt(index);
        }
        return remaining.Count == 0;
    }
}
