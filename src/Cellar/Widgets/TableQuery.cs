using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cellar.Widgets;

/// <summary>How a <see cref="TableQueryTerm"/> compares a cell with its value.</summary>
public enum TableQueryOperator
{
    /// <summary><c>col~text</c>, or a bare word: the cell text contains the value, ignoring case.</summary>
    Contains,

    /// <summary><c>col!~text</c>, or <c>!word</c>: the cell text doesn't contain the value.</summary>
    NotContains,

    /// <summary><c>col=value</c>: equal, as the column's sort key type when the value parses as one (so
    /// <c>age=5m</c> compares durations), else as text ignoring case.</summary>
    Equal,

    /// <summary><c>col!=value</c>.</summary>
    NotEqual,

    /// <summary><c>col&gt;value</c>, compared as the column's sort key type (numbers, durations, dates, enums), or
    /// as numbers or text when the column has none.</summary>
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
}

/// <summary>One condition of a <see cref="TableQuery"/>. <see cref="Column"/> is null for a bare word, which
/// matches any column.</summary>
public sealed record TableQueryTerm(string? Column, TableQueryOperator Operator, string Value);

/// <summary>
/// A small filter language for a <see cref="Table{T}"/>, for a <c>/</c> prompt: space-separated terms that must all
/// match. <c>status=running</c>, <c>status!=ok</c>, <c>name~web</c>, <c>name!~test</c>, <c>restarts&gt;3</c>,
/// <c>age&lt;=10m</c>; a bare word matches rows where any column contains it, and <c>!word</c> rows where none does.
/// Values with spaces go in double quotes (<c>node="node 1"</c>).
/// </summary>
/// <remarks>
/// Columns are found by header or <see cref="TableColumn{T}.Aliases"/>, ignoring case. A term naming a column the
/// table doesn't have is treated as a bare word for its whole text, so typing "a=b" never hides everything because
/// of a typo. Comparisons use each column's <see cref="TableColumn{T}.SortKey"/> when it has one: numbers,
/// <see cref="TimeSpan"/> (<c>90s</c>, <c>5m</c>, <c>1h30m</c>, <c>2d</c>, or <c>00:05:00</c>),
/// <see cref="DateTime"/>/<see cref="DateTimeOffset"/>, enums by name, and other comparable types via
/// <see cref="Convert.ChangeType(object, Type, IFormatProvider)"/>. Otherwise they use the cell's text, as numbers when
/// both sides are numbers.
/// </remarks>
public sealed class TableQuery
{
    private TableQuery(string text, IReadOnlyList<TableQueryTerm> terms)
    {
        Text = text;
        Terms = terms;
    }

    public static TableQuery Empty { get; } = new(string.Empty, []);

    /// <summary>The text the query was parsed from.</summary>
    public string Text { get; }

    public IReadOnlyList<TableQueryTerm> Terms { get; }

    public bool IsEmpty => Terms.Count == 0;

    /// <summary>Parses a query; never fails (anything that isn't <c>column op value</c> is a bare word).</summary>
    public static TableQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        var terms = new List<TableQueryTerm>();
        foreach (var token in Tokenize(text))
        {
            terms.Add(ParseTerm(token));
        }

        return new TableQuery(text, terms);
    }

    public override string ToString() => Text;

    /// <summary>Whether <paramref name="row"/> matches every term, evaluated against <paramref name="columns"/>.</summary>
    public bool Matches<T>(T row, IReadOnlyList<TableColumn<T>> columns)
    {
        string?[]? texts = null;
        string Cell(int c)
        {
            texts ??= new string?[columns.Count];
            return texts[c] ??= columns[c].Render(row).PlainText;
        }

        foreach (var term in Terms)
        {
            int column = term.Column is null ? -1 : FindColumn(columns, term.Column);
            if (column < 0)
            {
                // A bare word, or a column the table doesn't have: look for the text anywhere.
                string needle = term.Column is null ? term.Value : Raw(term);
                bool negate = term.Column is null && term.Operator == TableQueryOperator.NotContains;
                bool found = false;
                for (int c = 0; c < columns.Count && !found; c++)
                {
                    found = Cell(c).Contains(needle, StringComparison.OrdinalIgnoreCase);
                }

                if (found == negate)
                {
                    return false;
                }

                continue;
            }

            if (!Evaluate(term, columns[column].SortKey?.Invoke(row), Cell(column)))
            {
                return false;
            }
        }

        return true;
    }

    private static string Raw(TableQueryTerm term) => term.Column + term.Operator switch
    {
        TableQueryOperator.Contains => "~",
        TableQueryOperator.NotContains => "!~",
        TableQueryOperator.Equal => "=",
        TableQueryOperator.NotEqual => "!=",
        TableQueryOperator.Greater => ">",
        TableQueryOperator.GreaterOrEqual => ">=",
        TableQueryOperator.Less => "<",
        _ => "<=",
    } + term.Value;

    private static int FindColumn<T>(IReadOnlyList<TableColumn<T>> columns, string name)
    {
        for (int c = 0; c < columns.Count; c++)
        {
            if (string.Equals(columns[c].Header.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }

            foreach (var alias in columns[c].Aliases)
            {
                if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
        }

        return -1;
    }

    private static bool Evaluate(TableQueryTerm term, IComparable? key, string text)
    {
        switch (term.Operator)
        {
            case TableQueryOperator.Contains:
                return text.Contains(term.Value, StringComparison.OrdinalIgnoreCase);
            case TableQueryOperator.NotContains:
                return !text.Contains(term.Value, StringComparison.OrdinalIgnoreCase);
            case TableQueryOperator.Equal:
            case TableQueryOperator.NotEqual:
            {
                bool equal = Compare(key, text, term.Value, typedOnly: true) is { } typed
                    ? typed == 0
                    : string.Equals(text.Trim(), term.Value, StringComparison.OrdinalIgnoreCase);
                return equal == (term.Operator == TableQueryOperator.Equal);
            }

            default:
            {
                if (Compare(key, text, term.Value, typedOnly: false) is not { } cmp)
                {
                    return false;
                }

                return term.Operator switch
                {
                    TableQueryOperator.Greater => cmp > 0,
                    TableQueryOperator.GreaterOrEqual => cmp >= 0,
                    TableQueryOperator.Less => cmp < 0,
                    _ => cmp <= 0,
                };
            }
        }
    }

    // Compares the cell with the value: as the sort key's type when the value parses as it, else (for ordering) as
    // numbers when both sides are numbers, else as text. Null when the two can't be compared.
    private static int? Compare(IComparable? key, string text, string value, bool typedOnly)
    {
        switch (key)
        {
            case string keyText:
                return string.Compare(keyText.Trim(), value, StringComparison.OrdinalIgnoreCase);
            case double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort:
                if (TryNumber(value, out double number))
                {
                    return Convert.ToDouble(key, CultureInfo.InvariantCulture).CompareTo(number);
                }

                break;
            case not null when TryConvert(value, key, out var converted):
                try
                {
                    return key.CompareTo(converted);
                }
                catch (ArgumentException)
                {
                    return null;
                }

            case null when TryNumber(text, out double left) && TryNumber(value, out double right):
                return left.CompareTo(right);
            case null when TryDuration(text, out var leftSpan) && TryDuration(value, out var rightSpan):
                return leftSpan.CompareTo(rightSpan);
        }

        return typedOnly ? null : string.Compare(text.Trim(), value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvert(string value, IComparable key, out object? converted)
    {
        converted = null;
        switch (key)
        {
            case TimeSpan:
                if (TryDuration(value, out var span))
                {
                    converted = span;
                    return true;
                }

                return false;
            case DateTime:
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                {
                    converted = date;
                    return true;
                }

                return false;
            case DateTimeOffset:
                if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var offset))
                {
                    converted = offset;
                    return true;
                }

                return false;
            case Enum:
                if (Enum.TryParse(key.GetType(), value, ignoreCase: true, out var parsed) && parsed is not null && Enum.IsDefined(key.GetType(), parsed))
                {
                    converted = parsed;
                    return true;
                }

                return false;
            case bool:
                if (bool.TryParse(value, out bool flag))
                {
                    converted = flag;
                    return true;
                }

                return false;
            default:
                try
                {
                    converted = Convert.ChangeType(value, key.GetType(), CultureInfo.InvariantCulture);
                    return converted is not null;
                }
                catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
                {
                    return false;
                }
        }
    }

    private static bool TryNumber(string text, out double number) =>
        double.TryParse(text.Trim().TrimEnd('%'), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number);

    /// <summary>Parses a duration: <c>90s</c>, <c>5m</c>, <c>1h30m</c>, <c>2d</c>, <c>250ms</c>, <c>1w</c>, or a
    /// <see cref="TimeSpan"/> like <c>00:05:00</c>.</summary>
    public static bool TryDuration(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var s = text.Trim();
        if (s.Length == 0)
        {
            return false;
        }

        if (s.Contains(':', StringComparison.Ordinal))
        {
            return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out duration);
        }

        int i = 0;
        bool any = false;
        while (i < s.Length)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
            {
                i++;
            }

            if (i == start || !double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            {
                return false;
            }

            int unitStart = i;
            while (i < s.Length && char.IsLetter(s[i]))
            {
                i++;
            }

            var unit = s[unitStart..i].ToLowerInvariant();
            TimeSpan? part = unit switch
            {
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" or "sec" or "secs" => TimeSpan.FromSeconds(amount),
                "m" or "min" or "mins" => TimeSpan.FromMinutes(amount),
                "h" or "hr" or "hrs" => TimeSpan.FromHours(amount),
                "d" or "day" or "days" => TimeSpan.FromDays(amount),
                "w" => TimeSpan.FromDays(7 * amount),
                _ => null,
            };
            if (part is null)
            {
                return false;
            }

            duration += part.Value;
            any = true;
        }

        return any;
    }

    private static TableQueryTerm ParseTerm(string token)
    {
        // column op value, where the column is a run of characters that can't be an operator.
        int i = 0;
        while (i < token.Length && token[i] is not ('=' or '!' or '<' or '>' or '~'))
        {
            i++;
        }

        if (i > 0 && i < token.Length)
        {
            string column = token[..i];
            string rest = token[i..];
            (TableQueryOperator op, int length)? found = rest switch
            {
                _ when rest.StartsWith("!=", StringComparison.Ordinal) => (TableQueryOperator.NotEqual, 2),
                _ when rest.StartsWith("!~", StringComparison.Ordinal) => (TableQueryOperator.NotContains, 2),
                _ when rest.StartsWith(">=", StringComparison.Ordinal) => (TableQueryOperator.GreaterOrEqual, 2),
                _ when rest.StartsWith("<=", StringComparison.Ordinal) => (TableQueryOperator.LessOrEqual, 2),
                _ when rest.StartsWith('=') => (TableQueryOperator.Equal, 1),
                _ when rest.StartsWith('~') => (TableQueryOperator.Contains, 1),
                _ when rest.StartsWith('>') => (TableQueryOperator.Greater, 1),
                _ when rest.StartsWith('<') => (TableQueryOperator.Less, 1),
                _ => null,
            };
            if (found is { } f)
            {
                return new TableQueryTerm(column, f.op, Unquote(rest[f.length..]));
            }
        }

        if (token.Length > 1 && token[0] == '!')
        {
            return new TableQueryTerm(null, TableQueryOperator.NotContains, Unquote(token[1..]));
        }

        return new TableQueryTerm(null, TableQueryOperator.Contains, Unquote(token));
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    // Splits on whitespace outside double quotes; quotes stay on the token so a value can be told from a column.
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                current.Append(c);
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
