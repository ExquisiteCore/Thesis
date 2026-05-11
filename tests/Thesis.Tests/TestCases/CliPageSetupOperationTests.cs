internal static partial class Program
{
    static void CliRunExecuteAppliesProfilePageSetup()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var profile = new TemplateProfile
        {
            PageSetup = new ProfilePageSetup
            {
                PageSize = new PageSizeInfo
                {
                    WidthTwips = 10000,
                    HeightTwips = 15000,
                    Orientation = "portrait"
                },
                Margins = new PageMarginInfo
                {
                    TopTwips = 1200,
                    RightTwips = 1300,
                    BottomTwips = 1400,
                    LeftTwips = 1500,
                    HeaderTwips = 600,
                    FooterTwips = 700,
                    GutterTwips = 80
                }
            }
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "execute",
              "options": {
                "createSnapshot": false
              },
              "operations": [
                {
                  "id": "page-setup",
                  "op": "applyProfilePageSetup"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("applied", result.Operations[0].Status);
        var section = OpenXmlDocumentInspector.Inspect(context.Paths.WorkingDocument).Sections[0];
        AssertEqual(10000, section.PageSize!.WidthTwips);
        AssertEqual(15000, section.PageSize.HeightTwips);
        AssertEqual("portrait", section.PageSize.Orientation);
        AssertEqual(1200, section.PageMargin!.TopTwips);
        AssertEqual(1300, section.PageMargin.RightTwips);
        AssertEqual(1400, section.PageMargin.BottomTwips);
        AssertEqual(1500, section.PageMargin.LeftTwips);
        AssertEqual(600, section.PageMargin.HeaderTwips);
        AssertEqual(700, section.PageMargin.FooterTwips);
        AssertEqual(80, section.PageMargin.GutterTwips);
    }

    static void CliRunApplyProfilePageSetupSupportsOverridesAndDryRun()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var before = File.ReadAllBytes(context.Paths.WorkingDocument);
        var profile = new TemplateProfile
        {
            PageSetup = new ProfilePageSetup
            {
                PageSize = new PageSizeInfo { WidthTwips = 10000, HeightTwips = 15000 },
                Margins = new PageMarginInfo { TopTwips = 1200, RightTwips = 1300, BottomTwips = 1400, LeftTwips = 1500 }
            }
        };
        File.WriteAllText(context.Paths.ProfileJson, ThesisJson.Serialize(profile));
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "page-setup",
                  "op": "applyProfilePageSetup",
                  "format": {
                    "topTwips": 1600,
                    "orientation": "landscape"
                  }
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual("preview", result.Operations[0].Status);
        AssertContains(result.Operations[0].Matches[0].PreviewAfter!, "\"topTwips\":1600");
        AssertContains(result.Operations[0].Matches[0].PreviewAfter!, "\"orientation\":\"landscape\"");
        AssertBytesEqual(before, File.ReadAllBytes(context.Paths.WorkingDocument));
    }

    static void CliRunApplyProfilePageSetupReturnsFormatMissing()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedDocxWorkspace(temp.Path);
        var requestPath = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(
            requestPath,
            """
            {
              "schemaVersion": "1.0",
              "mode": "dryRun",
              "operations": [
                {
                  "id": "missing-page-setup",
                  "op": "applyProfilePageSetup"
                }
              ]
            }
            """);

        var (exitCode, result) = RunCli(["run", "--workspace", context.Workspace, "--request", requestPath]);

        AssertEqual(1, exitCode);
        AssertEqual("error", result.Status);
        AssertEqual("profile_page_setup_missing", result.Operations[0].Reason);
        AssertEqual("profile_page_setup_missing", result.Diagnostics[0].Code);
    }
}
