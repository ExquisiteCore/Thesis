using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Thesis.OpenXml;

internal static class TemplateSampleText
{
    // Text boxes are independent stories. Their notes must not become the anchor paragraph's title or font sample.
    public static string FlowText(OpenXmlElement block)
        => string.Concat(block.Descendants<Text>().Where(t => !t.Ancestors<TextBoxContent>().Any()).Select(t => t.Text)).Trim();

    public static Run? DominantRun(Paragraph paragraph)
        => paragraph.Descendants<Run>()
            .Where(run => run.Ancestors<Paragraph>().FirstOrDefault() == paragraph && run.Elements<Text>().Any())
            .OrderByDescending(run => string.Concat(run.Elements<Text>().Select(t => t.Text)).Length).FirstOrDefault();

    public static bool IsInstruction(string text)
    {
        var compact = Regex.Replace(text, @"\s+", "", RegexOptions.CultureInvariant);
        return Regex.IsMatch(compact, @"排版[：:]|段前.*段后|^(说明[：:]|【举例】)|参考格式|必须修改|不要出现雷同|建议采用|每章标题|关于论文中的表格|中文的括号|可选择分布式存储|以上标题内容|所选关键词必须|题目与毕设平台保持一致");
    }

    public static void RemoveInstructionShapes(OpenXmlElement block)
    {
        var containers = block.Descendants<TextBoxContent>()
            .Where(box => IsInstruction(box.InnerText))
            .Select(box => box.Ancestors<AlternateContent>().FirstOrDefault()
                ?? (OpenXmlElement?)box.Ancestors<Drawing>().FirstOrDefault()
                ?? box.Ancestors<DocumentFormat.OpenXml.Vml.Shape>().FirstOrDefault())
            .OfType<OpenXmlElement>().Distinct().ToArray();
        foreach (var shape in containers) shape.Remove();
    }
}
