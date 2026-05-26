using System.Text;

namespace Shellfix.Core;

public static class CommandTokenizer
{
    public static List<string> ParseCommandArgs(string input)
    {
        var args = new List<string>();
        var i = 0;
        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i])) { i++; }
            if (i >= input.Length) { break; }

            var token = new StringBuilder();
            if (input[i] == '"' || input[i] == '\'')
            {
                var quote = input[i++];
                ReadQuoted(input, ref i, token, quote);
            }
            else
            {
                while (i < input.Length && !char.IsWhiteSpace(input[i]))
                {
                    if (input[i] == '"' || input[i] == '\'')
                    {
                        var quote = input[i++];
                        ReadQuoted(input, ref i, token, quote);
                    }
                    else
                    {
                        token.Append(input[i++]);
                    }
                }
            }

            if (token.Length > 0)
            {
                args.Add(token.ToString());
            }
        }

        return args;
    }

    public static bool TryReadCommandToken(string input, ref int index, out string token)
    {
        token = "";
        SkipWhitespace(input, ref index);
        if (index >= input.Length)
        {
            return false;
        }

        var sb = new StringBuilder();
        var quote = input[index] is '"' or '\'' ? input[index++] : '\0';
        while (index < input.Length)
        {
            var ch = input[index];
            if (quote == '\0')
            {
                if (char.IsWhiteSpace(ch))
                {
                    break;
                }

                sb.Append(ch);
                index++;
                continue;
            }

            if (ch == quote)
            {
                index++;
                break;
            }

            if (quote == '"' && ch == '\\' && index + 1 < input.Length && input[index + 1] == '"')
            {
                sb.Append('"');
                index += 2;
                continue;
            }

            sb.Append(ch);
            index++;
        }

        token = sb.ToString();
        return token.Length > 0;
    }

    public static bool TryReadInlinePayload(string input, ref int index, out string payload)
    {
        payload = "";
        SkipWhitespace(input, ref index);
        if (index >= input.Length)
        {
            return false;
        }

        if (input[index] != '"' && input[index] != '\'')
        {
            return TryReadCommandToken(input, ref index, out payload);
        }

        var quote = input[index++];
        var sb = new StringBuilder();
        while (index < input.Length)
        {
            var ch = input[index];
            if (quote == '"' && ch == '\\' && index + 1 < input.Length && input[index + 1] == '"')
            {
                sb.Append('"');
                index += 2;
                continue;
            }

            if (ch == quote)
            {
                index++;
                payload = sb.ToString();
                return true;
            }

            sb.Append(ch);
            index++;
        }

        return false;
    }

    public static bool IsBufferedCommandComplete(string command)
    {
        var inSingle = false;
        var inDouble = false;
        var escaped = false;

        foreach (var ch in command)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inDouble)
            {
                escaped = true;
                continue;
            }

            if (ch == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
        }

        return !inSingle && !inDouble;
    }

    public static void SkipWhitespace(string input, ref int index)
    {
        while (index < input.Length && char.IsWhiteSpace(input[index]))
        {
            index++;
        }
    }

    public static string NormalizeCommandName(string commandName)
    {
        var fileName = Path.GetFileName(commandName.Trim('"', '\''));
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(withoutExtension)
            ? fileName.ToLowerInvariant()
            : withoutExtension.ToLowerInvariant();
    }

    private static void ReadQuoted(string input, ref int index, StringBuilder token, char quote)
    {
        while (index < input.Length)
        {
            if (quote == '"' && input[index] == '\\' && index + 1 < input.Length && input[index + 1] == '"')
            {
                token.Append('"');
                index += 2;
                continue;
            }

            if (input[index] == quote)
            {
                index++;
                break;
            }

            token.Append(input[index++]);
        }
    }
}
