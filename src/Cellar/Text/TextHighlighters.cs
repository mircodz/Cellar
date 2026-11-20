using System;
using Cellar.Primitives;
using Cellar.Theming;

namespace Cellar.Text;

/// <summary>
/// Line highlighters for <see cref="Widgets.TextViewer.Highlighter"/>: they color one line of JSON or XML with the
/// current theme. They're forgiving tokenizers rather than parsers, so a broken or partial document still gets
/// colored as far as it makes sense. Each line is colored on its own; pretty-printed text (see
/// <see cref="PrettyPrint"/>) keeps every string, tag and attribute on one line, which is what they expect.
/// </summary>
public static class TextHighlighters
{
    /// <summary>Colors a line of JSON: keys in the accent color, string values, numbers and
    /// <c>true</c>/<c>false</c>/<c>null</c> each in their own, and punctuation dimmed.</summary>
    public static StyledText Json(string line)
    {
        var theme = Theme.Current;
        var text = new StyledText();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            int start = i;
            if (c == '"')
            {
                i = StringEnd(line, i);
                int next = i;
                while (next < line.Length && char.IsWhiteSpace(line[next]))
                {
                    next++;
                }

                bool key = next < line.Length && line[next] == ':';
                text.Append(line[start..i], new Style(key ? theme.Accent : theme.Success, Color.Default));
            }
            else if (c == '-' || char.IsDigit(c))
            {
                i++;
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    i++;
                }

                text.Append(line[start..i], new Style(theme.Info, Color.Default));
            }
            else if (char.IsLetter(c))
            {
                while (i < line.Length && char.IsLetter(line[i]))
                {
                    i++;
                }

                string word = line[start..i];
                bool literal = word is "true" or "false" or "null";
                text.Append(word, literal ? new Style(theme.Secondary, Color.Default) : theme.TextStyle);
            }
            else if (char.IsWhiteSpace(c))
            {
                while (i < line.Length && char.IsWhiteSpace(line[i]))
                {
                    i++;
                }

                text.Append(line[start..i], theme.TextStyle);
            }
            else
            {
                i++;
                text.Append(line[start..i], c is '{' or '}' or '[' or ']' or ',' or ':' ? theme.MutedStyle : theme.TextStyle);
            }
        }

        return text;
    }

    /// <summary>Colors a line of XML: element names in the accent color, attribute names, attribute values,
    /// comments and processing instructions each in their own, text plain, and the angle brackets dimmed.</summary>
    public static StyledText Xml(string line)
    {
        var theme = Theme.Current;
        var punctuation = theme.MutedStyle;
        var name = new Style(theme.Accent, Color.Default);
        var attribute = new Style(theme.Info, Color.Default);
        var value = new Style(theme.Success, Color.Default);
        var comment = new Style(theme.Muted, Color.Default).Italic;
        var text = new StyledText();
        int i = 0;
        while (i < line.Length)
        {
            int start = i;
            if (Starts(line, i, "<!--"))
            {
                i = Until(line, i + 4, "-->");
                text.Append(line[start..i], comment);
            }
            else if (Starts(line, i, "<![CDATA["))
            {
                text.Append("<![CDATA[", punctuation);
                int content = Math.Min(i + 9, line.Length);
                int close = line.IndexOf("]]>", content, StringComparison.Ordinal);
                text.Append(line[content..(close < 0 ? line.Length : close)], theme.TextStyle);
                if (close >= 0)
                {
                    text.Append("]]>", punctuation);
                }

                i = close < 0 ? line.Length : close + 3;
            }
            else if (Starts(line, i, "<?"))
            {
                i = Until(line, i + 2, "?>");
                text.Append(line[start..i], comment);
            }
            else if (line[i] == '<')
            {
                i++;
                if (i < line.Length && line[i] is '/' or '!')
                {
                    i++;
                }

                text.Append(line[start..i], punctuation);
                i = Tag(line, i, text, name, attribute, value, punctuation);
            }
            else
            {
                while (i < line.Length && line[i] != '<')
                {
                    i++;
                }

                text.Append(line[start..i], theme.TextStyle);
            }
        }

        return text;
    }

    // The inside of a tag after "<", "</" or "<!": its name, attributes and the closing "/>" or ">".
    private static int Tag(string line, int i, StyledText text, Style name, Style attribute, Style value, Style punctuation)
    {
        int start = i;
        while (i < line.Length && IsNameChar(line[i]))
        {
            i++;
        }

        text.Append(line[start..i], name);
        while (i < line.Length)
        {
            char c = line[i];
            start = i;
            if (c == '>')
            {
                text.Append(">", punctuation);
                return i + 1;
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '>')
            {
                text.Append("/>", punctuation);
                return i + 2;
            }

            if (c is '"' or '\'')
            {
                int end = line.IndexOf(c, i + 1);
                i = end < 0 ? line.Length : end + 1;
                text.Append(line[start..i], value);
            }
            else if (IsNameChar(c))
            {
                while (i < line.Length && IsNameChar(line[i]))
                {
                    i++;
                }

                text.Append(line[start..i], attribute);
            }
            else
            {
                i++;
                text.Append(line[start..i], char.IsWhiteSpace(c) ? Theme.Current.TextStyle : punctuation);
            }
        }

        return i;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is ':' or '_' or '-' or '.';

    private static bool Starts(string line, int at, string token) =>
        at >= 0 && string.CompareOrdinal(line, at, token, 0, token.Length) == 0;

    // The index just past the next "token" from "from", or the end of the line.
    private static int Until(string line, int from, string token)
    {
        int at = line.IndexOf(token, Math.Min(from, line.Length), StringComparison.Ordinal);
        return at < 0 ? line.Length : at + token.Length;
    }

    // The index just past a JSON string's closing quote (or the end of the line), skipping escaped quotes.
    private static int StringEnd(string line, int open)
    {
        int i = open + 1;
        while (i < line.Length)
        {
            if (line[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (line[i] == '"')
            {
                return i + 1;
            }

            i++;
        }

        return line.Length;
    }
}
