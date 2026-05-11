using Thesis.Schema;

namespace Thesis.Cli;

internal static class FinalizationPlanBuilder
{
    public static FinalizationPlan Build(DocumentMap map)
    {
        var reasons = map.FinalizationReasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var required = map.RequiresFinalization || reasons.Count > 0;
        var steps = new List<FinalizationStep>();
        if (reasons.Contains("fields", StringComparer.OrdinalIgnoreCase))
        {
            steps.Add(new FinalizationStep
            {
                Id = "updateFields",
                Capability = "hostApplication",
                Description = "Update DOCX fields with Word/WPS automation or an opened Word-compatible application.",
                Required = true
            });
        }

        if (reasons.Contains("toc", StringComparer.OrdinalIgnoreCase))
        {
            steps.Add(new FinalizationStep
            {
                Id = "updateTableOfContents",
                Capability = "hostApplication",
                Description = "Update table of contents entries and page numbers after all content edits are complete.",
                Required = true
            });
        }

        steps.Add(new FinalizationStep
        {
            Id = "repaginate",
            Capability = "hostApplication",
            Description = "Repaginate in Word/WPS because Open XML does not calculate true page layout.",
            Required = required
        });

        return new FinalizationPlan
        {
            Required = required,
            Reasons = reasons,
            Steps = steps
        };
    }
}
