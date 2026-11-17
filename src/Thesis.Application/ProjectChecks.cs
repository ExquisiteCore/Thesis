using Thesis.Core;
using Thesis.OpenXml;
using Thesis.Schema;
using Markdig.Syntax;

namespace Thesis.Application;

public static class ProjectChecks
{
    public static List<ProjectIssue> Check(OpenThesisProject project)
    {
        var result = new List<ProjectIssue>(); var content = project.Document.Content;
        if (string.IsNullOrWhiteSpace(content.Title)) result.Add(new("error", "论文题目为空", "请填写题目。"));
        if (string.IsNullOrWhiteSpace(content.AbstractZh)) result.Add(new("warning", "中文摘要为空", "检查学校是否要求摘要。", "abstract.zh"));
        if (content.References.Count == 0) result.Add(new("warning", "尚无参考文献", "请录入引用过的文献。", "references"));
        if (content.Chapters.Count == 0) result.Add(new("error", "尚无正文章节", "先创建章节或导入正文。"));
        foreach (var chapter in content.Chapters)
        {
            if (new[] { chapter.Markdown }.Concat(chapter.Sections.Select(s => s.Markdown)).OfType<string>().Any(m =>
                MarkdownSupport.Parse(m).Descendants<Markdig.Extensions.Mathematics.MathInline>().Any() || MarkdownSupport.Parse(m).Descendants<Markdig.Extensions.Mathematics.MathBlock>().Any()))
                result.Add(new("warning", "LaTeX 公式以源码保留", "当前 Markdown 公式保留 $ / $$ 源码。需要 Word 原生公式时，请从 DOCX 导入公式片段后核对 PDF。", chapter.Id));
            if (string.IsNullOrWhiteSpace(chapter.Title)) result.Add(new("error", "章节标题为空", "请填写章节名称。", chapter.Id));
            if (string.IsNullOrWhiteSpace(chapter.Markdown) && chapter.Paragraphs.Count == 0 && chapter.Blocks.Count == 0 && chapter.Sections.Count == 0)
                result.Add(new("warning", "章节内容为空", chapter.Title, chapter.Id));
        }
        try
        {
            foreach (var asset in ContentPaths.Assets(content)) if (!File.Exists(asset)) result.Add(new("error", "素材缺失", asset));
        }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or InvalidDataException) { result.Add(new("error", "Markdown 资源描述有误", ex.Message)); }
        var template = ThesisProjectStore.Resolve(project, project.Document.TemplatePath);
        if (template is null || !File.Exists(template)) result.Add(new("error", "模板缺失", "选择学校模板，或使用通用模板。"));
        if (ThesisProjectStore.Resolve(project, project.Document.TemplateDesignPath) is { } designPath)
        {
            try
            {
                var design = TemplateLibraryStore.Load(designPath);
                var bodyRole = design.Roles.FirstOrDefault(r => r.Role == "body");
                var unconfirmed = design.Roles.Where(r => r.Confidence < .5 && !r.Confirmed).ToArray();
                if (unconfirmed.Length > 0)
                {
                    var fallback = bodyRole is null ? "通用正文格式" : $"正文格式（{bodyRole.Font}，{bodyRole.Size:g} 磅）";
                    var details = string.Join("；", unconfirmed.Select(role =>
                    {
                        var sample = role.SampleText?.Trim() ?? "";
                        if (sample.Length > 36) sample = sample[..36] + "…";
                        return $"{role.Name}：{(sample.Length == 0 ? "未找到匹配样本段落" : $"样本“{sample}”")}";
                    }));
                    var firstRole = unconfirmed[0];
                    result.Add(new("warning", $"有 {unconfirmed.Length} 项模板格式需要核对",
                        $"{details}。这些位置目前会暂用{fallback}。点击此项可跳转模板工作台，从样本段落重新指定格式。",
                        "template:" + firstRole.Role));
                }
                var directory = Path.Combine(Path.GetTempPath(), "ThesisCheck", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                try
                {
                    var doc = Path.Combine(directory, "check.docx"); ThesisDocumentGenerator.Generate(content, new(), doc);
                    foreach (var requirement in ContentRequirementAudit.Check(doc, design.Requirements).Where(r => !r.Passed))
                        result.Add(new("error", "学校要求未满足：" + requirement.Id, requirement.Message + "\n依据：" + requirement.Evidence));
                }
                finally { Directory.Delete(directory, true); }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Text.Json.JsonException) { result.Add(new("error", "模板或正文检查失败", ex.Message)); }
        }
        return result;
    }
}
