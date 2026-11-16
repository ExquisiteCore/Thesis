using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

internal static class OpenXmlFieldInstructions
{
    // Office represents internal links as either w:hyperlink or HYPERLINK fields.
    private static readonly Regex Target = new(@"(\b(?:REF|PAGEREF|NOTEREF)\s+|\bHYPERLINK\b[^\r\n]*?\\l\s+)(?:""([^""]+)""|([^\s\\]+))", RegexOptions.IgnoreCase);

    public static string Rewrite(string instruction, IReadOnlyDictionary<string, string> names) => Target.Replace(instruction, match =>
    {
        var name = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        return names.TryGetValue(name, out var mapped) ? match.Groups[1].Value + "\"" + mapped + "\"" : match.Value;
    });

    public static IEnumerable<string> Targets(OpenXmlElement root)
    {
        foreach (var instruction in Instructions(root))
            foreach (Match match in Target.Matches(instruction))
                yield return match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
    }

    public static void RewriteAll(OpenXmlElement root, Func<string, string> rewrite, Stack<List<FieldCode>>? context = null)
    {
        foreach (var field in root.Descendants<SimpleField>()) field.Instruction = rewrite(field.Instruction?.Value ?? "");
        var stack = context ?? new Stack<List<FieldCode>>();
        foreach (var element in root.Descendants())
        {
            if (element is FieldChar begin && begin.FieldCharType?.Value == FieldCharValues.Begin) stack.Push([]);
            else if (element is FieldCode code)
            {
                if (stack.Count > 0) stack.Peek().Add(code);
                else code.Text = rewrite(code.Text);
            }
            else if (element is FieldChar field && (field.FieldCharType?.Value == FieldCharValues.Separate || field.FieldCharType?.Value == FieldCharValues.End) && stack.Count > 0)
            {
                var codes = field.FieldCharType.Value == FieldCharValues.End ? stack.Pop() : stack.Peek();
                if (codes.Count == 0) continue;
                codes[0].Text = rewrite(string.Concat(codes.Select(c => c.Text)));
                foreach (var remainder in codes.Skip(1)) remainder.Text = "";
                codes.Clear();
            }
        }
    }

    private static IEnumerable<string> Instructions(OpenXmlElement root)
    {
        var stack = new Stack<StringBuilder>();
        foreach (var element in root.Descendants())
        {
            if (element is SimpleField simple) yield return simple.Instruction?.Value ?? "";
            else if (element is FieldCode code)
            {
                if (stack.Count > 0) stack.Peek().Append(code.Text);
                else yield return code.Text;
            }
            else if (element is FieldChar field)
            {
                if (field.FieldCharType?.Value == FieldCharValues.Begin) stack.Push(new());
                else if (stack.Count > 0 && field.FieldCharType?.Value is var kind && (kind == FieldCharValues.Separate || kind == FieldCharValues.End))
                {
                    var instruction = kind == FieldCharValues.End ? stack.Pop() : stack.Peek();
                    yield return instruction.ToString();
                    instruction.Clear();
                }
            }
        }
    }
}
