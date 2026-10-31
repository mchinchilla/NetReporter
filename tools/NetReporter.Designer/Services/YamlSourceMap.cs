using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace NetReporter.Designer.Services;

/// <summary>
/// Maps designer paths (<c>bands.N</c>, <c>bands.N.elements.M</c>) to their line range in the YAML
/// text, so the editor can reveal the element selected in the preview and vice versa.
/// </summary>
public static class YamlSourceMap
{
    /// <summary>1-based, inclusive line ranges. Empty when the YAML doesn't parse.</summary>
    public static IReadOnlyDictionary<string, int[]> Build(string yaml)
    {
        var result = new Dictionary<string, int[]>(StringComparer.Ordinal);
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) return result;
            if (!root.Children.TryGetValue(new YamlScalarNode("bands"), out var bandsNode) ||
                bandsNode is not YamlSequenceNode bands) return result;

            var lineCount = CountLines(yaml);
            for (int i = 0; i < bands.Children.Count; i++)
            {
                var band = bands.Children[i];
                result[$"bands.{i}"] = Range(band, lineCount);
                if (band is not YamlMappingNode bandMap ||
                    !bandMap.Children.TryGetValue(new YamlScalarNode("elements"), out var elementsNode) ||
                    elementsNode is not YamlSequenceNode elements) continue;

                for (int j = 0; j < elements.Children.Count; j++)
                    result[$"bands.{i}.elements.{j}"] = Range(elements.Children[j], lineCount);
            }
        }
        catch (YamlException)
        {
            // The preview reports the parse error; no map.
        }
        return result;
    }

    private static int[] Range(YamlNode node, int lineCount)
    {
        var start = (int)node.Start.Line;
        // A block mapping/sequence's own End mark doesn't cover its children: take the deepest end.
        var end = Math.Clamp(LastLine(node), start, Math.Max(start, lineCount));
        return [start, end];
    }

    private static int LastLine(YamlNode node)
    {
        // End is the position right after the node; at column 1 that's the start of the next line.
        var last = (int)node.End.Line - (node.End.Column <= 1 && node.End.Line > node.Start.Line ? 1 : 0);
        switch (node)
        {
            case YamlMappingNode map:
                foreach (var (k, v) in map.Children)
                    last = Math.Max(last, Math.Max(LastLine(k), LastLine(v)));
                break;
            case YamlSequenceNode seq:
                foreach (var child in seq.Children)
                    last = Math.Max(last, LastLine(child));
                break;
        }
        return last;
    }

    private static int CountLines(string text)
    {
        int n = 1;
        foreach (var c in text) if (c == '\n') n++;
        return n;
    }

    /// <summary>Where an exception thrown while parsing or binding the template points to, if anywhere.</summary>
    public static (string Source, int Line, int Column)? Locate(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case YamlException y when y.Start.Line > 0:
                    return ("yaml", (int)y.Start.Line, (int)y.Start.Column);
                case JsonException { LineNumber: { } line } j:
                    return ("json", (int)line + 1, (int)(j.BytePositionInLine ?? 0) + 1);
            }
        }
        return null;
    }
}
