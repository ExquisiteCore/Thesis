internal static partial class Program
{
    static void CliValidateSuggestsProfileRoleFixes()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            StyleRoles =
            [
                new ProfileStyleRole
                {
                    Role = "abstract.zh",
                    Format = new ParagraphFormatSample
                    {
                        StyleId = "Heading1",
                        Alignment = "center",
                        SpacingAfterTwips = 120,
                        RunFormat = new RunFormatSample
                        {
                            Bold = true,
                            FontSizeHalfPoints = "28",
                            EastAsiaFont = "黑体"
                        }
                    },
                    Evidence =
                    [
                        new ProfileParagraphEvidence { ParagraphIndex = 0, StyleId = "Title", TextPreview = "中文摘要" }
                    ]
                }
            ]
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));

        var (exitCode, result) = RunCli(["validate", "--workspace", context.Workspace]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(true, result.Validation is not null);
        AssertEqual(false, result.Validation!.Compliant);
        AssertEqual(true, result.Validation.Diagnostics.Any(diagnostic => diagnostic.Code == "profile_role_format_mismatch"));
        AssertEqual(true, result.Validation.SuggestedOperations.Any(operation =>
            operation.Op == "applyProfileRole"
            && operation.Role == "abstract.zh"
            && operation.Target?["type"]?.GetValue<string>() == "paragraphIndex"
            && operation.Target?["index"]?.GetValue<int>() == 0));
    }
}
