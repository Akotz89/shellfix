namespace Shellfix.Cli;

internal static class Jsonc
{
    public static string StringLiteral(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static JsonRange? FindPropertyValueRange(string content, string name, int startIndex = 0, int? endIndex = null)
    {
        var end = endIndex ?? content.Length;
        var slice = content[startIndex..end];
        var match = Regex.Match(slice, "\"" + Regex.Escape(name) + "\"\\s*:");
        if (!match.Success) { return null; }
        var colon = startIndex + match.Index + match.Value.LastIndexOf(':');
        var valueStart = colon + 1;
        while (valueStart < end && char.IsWhiteSpace(content[valueStart])) { valueStart++; }
        if (valueStart >= end) { throw new InvalidOperationException($"Property '{name}' has no value."); }
        return new JsonRange(startIndex + match.Index, valueStart, FindValueEnd(content, valueStart, end));
    }

    public static string SetObjectProperty(string objectText, string name, string valueText)
    {
        var range = FindPropertyValueRange(objectText, name);
        if (range is not null)
        {
            return objectText[..range.ValueStart] + valueText + objectText[range.ValueEnd..];
        }

        var closeIndex = objectText.LastIndexOf('}');
        if (closeIndex < 0) { throw new InvalidOperationException("Settings object is missing a closing brace."); }
        var beforeClose = objectText[..closeIndex].TrimEnd();
        var body = beforeClose.Length > 1 ? beforeClose[1..].Trim() : "";
        var prefix = body.Length > 0 && !beforeClose.EndsWith(",", StringComparison.Ordinal) ? "," : "";
        var insert = $"{prefix}\r\n  {StringLiteral(name)}: {valueText}\r\n";
        return beforeClose + insert + objectText[closeIndex..];
    }

    private static int FindValueEnd(string content, int start, int end)
    {
        var first = content[start];
        if (first is '{' or '[')
        {
            var open = first;
            var close = first == '{' ? '}' : ']';
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < end; i++)
            {
                var ch = content[i];
                if (inString)
                {
                    if (escaped) { escaped = false; }
                    else if (ch == '\\') { escaped = true; }
                    else if (ch == '"') { inString = false; }
                    continue;
                }
                if (ch == '"') { inString = true; }
                else if (ch == open) { depth++; }
                else if (ch == close && --depth == 0) { return i + 1; }
            }
            throw new InvalidOperationException($"Unclosed JSON value starting at {start}.");
        }

        if (first == '"')
        {
            var escaped = false;
            for (var i = start + 1; i < end; i++)
            {
                var ch = content[i];
                if (escaped) { escaped = false; }
                else if (ch == '\\') { escaped = true; }
                else if (ch == '"') { return i + 1; }
            }
            throw new InvalidOperationException($"Unclosed string value starting at {start}.");
        }

        for (var i = start; i < end; i++)
        {
            var ch = content[i];
            if (ch is ',' or '\r' or '\n' or '}') { return i; }
        }
        return end;
    }
}
