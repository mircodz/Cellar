using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Cellar.Text;

/// <summary>
/// Reformats JSON and XML for reading, typically before showing it in a <see cref="Widgets.TextViewer"/> with one
/// of the <see cref="TextHighlighters"/>. Text that doesn't parse comes back unchanged, so a viewer can always show
/// <em>something</em> instead of an error.
/// </summary>
public static class PrettyPrint
{
    /// <summary>Indents <paramref name="json"/> two spaces per level, or returns it unchanged if it isn't valid
    /// JSON. Non-ASCII characters are kept as they are rather than escaped, since the result is for people.</summary>
    public static string Json(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
            {
                document.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length).Replace("\r\n", "\n");
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>Indents <paramref name="xml"/> two spaces per level, keeping the declaration, comments and
    /// processing instructions, or returns it unchanged if it isn't well-formed XML.</summary>
    public static string Xml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return xml;
        }

        try
        {
            // Whitespace is kept while parsing so that mixed content (`<b>Hello</b> <b>world</b>`) keeps its spaces;
            // only the line-breaking whitespace that merely indented the source is dropped.
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            document.Nodes().OfType<XText>().Remove();
            if (document.Root is { } root)
            {
                DropIndentation(root);
            }

            string body = document.ToString().Replace("\r\n", "\n");
            return document.Declaration is { } declaration ? declaration + "\n" + body : body;
        }
        catch (XmlException)
        {
            return xml;
        }
    }

    // Removes the text of an element whose text is all indentation (whitespace with a line break); an element with
    // any other text is mixed content, which is kept exactly as written and so isn't re-indented.
    private static void DropIndentation(XElement element)
    {
        if ((string?)element.Attribute(XNamespace.Xml + "space") == "preserve")
        {
            return;
        }

        bool mixed = false;
        for (var node = element.FirstNode; node is not null; node = node.NextNode)
        {
            if (node is XText text && (text is XCData || !IsIndentation(text.Value)))
            {
                mixed = true;
                break;
            }
        }

        if (mixed && element.FirstNode is not XText)
        {
            // The writer only stops indenting at the first text, so an empty one up front keeps it from
            // breaking the line before a leading child.
            element.AddFirst(new XText(string.Empty));
        }

        bool hasChildElements = element.Elements().Any();
        var current = element.FirstNode;
        while (current is not null)
        {
            var next = current.NextNode;
            if (current is XElement child)
            {
                DropIndentation(child);
            }
            else if (hasChildElements && !mixed && current is XText)
            {
                current.Remove();
            }

            current = next;
        }
    }

    private static bool IsIndentation(string text) =>
        text.Contains('\n') && string.IsNullOrWhiteSpace(text);

    /// <summary>Pretty-prints <paramref name="text"/> as JSON if it looks like JSON (starts with <c>{</c> or
    /// <c>[</c>), as XML if it starts with <c>&lt;</c>, and otherwise returns it unchanged.</summary>
    public static string Auto(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.IsEmpty)
        {
            return text;
        }

        return trimmed[0] switch
        {
            '{' or '[' => Json(text),
            '<' => Xml(text),
            _ => text,
        };
    }

    /// <summary>The highlighter that suits <paramref name="text"/>, decided the same way as <see cref="Auto"/>:
    /// <see cref="TextHighlighters.Json"/>, <see cref="TextHighlighters.Xml"/>, or null for plain text.</summary>
    public static Func<string, StyledText>? HighlighterFor(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.IsEmpty)
        {
            return null;
        }

        return trimmed[0] switch
        {
            '{' or '[' => TextHighlighters.Json,
            '<' => TextHighlighters.Xml,
            _ => null,
        };
    }
}
