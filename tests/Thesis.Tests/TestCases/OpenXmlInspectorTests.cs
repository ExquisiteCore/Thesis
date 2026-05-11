internal static partial class Program
{
    static void OpenXmlInspectorReadsDocumentMap()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "fixture.docx");
        WriteFixtureDocx(docx);

        var map = OpenXmlDocumentInspector.Inspect(docx);

        AssertEqual("1.0", map.SchemaVersion);
        AssertEqual(Path.GetFullPath(docx), map.Path);
        AssertEqual(true, map.RequiresFinalization);
        AssertEqual(true, map.FinalizationReasons.Contains("fields", StringComparer.Ordinal));

        AssertEqual(7, map.Paragraphs.Count);
        AssertEqual("中文摘要", map.Paragraphs[0].Text);
        AssertEqual("Title", map.Paragraphs[0].StyleId);
        AssertEqual("第一章 绪论", map.Paragraphs[1].Text);
        AssertEqual("Heading1", map.Paragraphs[1].StyleId);
        AssertEqual("列表项", map.Paragraphs[2].Text);
        AssertEqual("1", map.Paragraphs[2].Numbering!.NumberingId);
        AssertEqual("0", map.Paragraphs[2].Numbering!.Level);

        AssertEqual(true, map.Styles.Any(style => style.StyleId == "Heading1" && style.Name == "heading 1" && style.Type == "paragraph"));
        AssertEqual(true, map.Numbering.Any(numbering =>
            numbering.NumberingId == "1"
            && numbering.AbstractNumberingId == "0"
            && numbering.Levels.Any(level => level.Level == "0" && level.Format == "decimal" && level.Text == "%1.")));
        AssertEqual(1, map.Sections.Count);
        AssertEqual(11906, map.Sections[0].PageSize!.WidthTwips);
        AssertEqual(16838, map.Sections[0].PageSize!.HeightTwips);
        AssertEqual(1440, map.Sections[0].PageMargin!.TopTwips);
        AssertEqual(true, map.Sections[0].Headers.Any(header => header.Type == "default" && header.RelationshipId == "rIdHeader1"));

        AssertEqual(1, map.Tables.Count);
        AssertEqual(2, map.Tables[0].RowCount);
        AssertEqual(2, map.Tables[0].CellCounts[0]);
        AssertContains(map.Tables[0].TextPreview, "A1");
        AssertContains(map.Tables[0].TextPreview, "B2");
    }

    static void OpenXmlInspectorReadsParagraphAndRunFormatSamples()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "formatted-fixture.docx");
        WriteFormattedFixtureDocx(docx);

        var map = OpenXmlDocumentInspector.Inspect(docx);
        var paragraph = map.Paragraphs[0];

        AssertEqual("Heading1", paragraph.StyleId);
        AssertEqual("Heading1", paragraph.Format.StyleId);
        AssertEqual("center", paragraph.Format.Alignment);
        AssertEqual(240, paragraph.Format.SpacingBeforeTwips);
        AssertEqual(120, paragraph.Format.SpacingAfterTwips);
        AssertEqual("360", paragraph.Format.LineSpacing);
        AssertEqual("auto", paragraph.Format.LineSpacingRule);
        AssertEqual(480, paragraph.Format.FirstLineIndentTwips);
        AssertEqual(240, paragraph.Format.LeftIndentTwips);
        AssertEqual(120, paragraph.Format.RightIndentTwips);
        AssertEqual(true, paragraph.Format.RunFormat!.Bold);
        AssertEqual("28", paragraph.Format.RunFormat.FontSizeHalfPoints);
        AssertEqual("Times New Roman", paragraph.Format.RunFormat.AsciiFont);
        AssertEqual("宋体", paragraph.Format.RunFormat.EastAsiaFont);
        AssertEqual("Times New Roman", paragraph.Runs[0].AsciiFont);
        AssertEqual("宋体", paragraph.Runs[0].EastAsiaFont);

        AssertEqual(false, map.Paragraphs[1].Format.RunFormat!.Bold);
        var emptyRunFormat = new RunFormatSample();
        AssertEqual((bool?)null, emptyRunFormat.Bold);
        AssertEqual((bool?)null, emptyRunFormat.Italic);
    }

    static void OpenXmlInspectorFallsBackToComplexScriptFontSize()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "complex-size.docx");
        WriteComplexScriptSizeFixtureDocx(docx);

        var map = OpenXmlDocumentInspector.Inspect(docx);

        AssertEqual("21", map.Paragraphs[0].Format.RunFormat!.FontSizeHalfPoints);
        AssertEqual("21", map.Paragraphs[0].Runs[0].FontSizeHalfPoints);
    }

    static void OpenXmlInspectorReadsStyleUsageAndOutlineFacts()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "formatted.docx");
        WriteFormattedFixtureDocx(docx);

        var map = OpenXmlDocumentInspector.Inspect(docx);

        var heading = map.Paragraphs.Single(paragraph => paragraph.Text == "第一章 绪论");
        AssertEqual("Heading1", heading.StyleId);
        AssertEqual(0, heading.OutlineLevel);

        var headingStyle = map.Styles.Single(style => style.StyleId == "Heading1");
        AssertEqual(true, headingStyle.UsageCount > 0);
    }

    static void OpenXmlInspectorReadsOutlineFactsFromStyleDefinitions()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "style-outline.docx");
        WriteFixtureDocx(docx);

        var map = OpenXmlDocumentInspector.Inspect(docx);

        var heading = map.Paragraphs.Single(paragraph => paragraph.Text == "第一章 绪论");
        AssertEqual("Heading1", heading.StyleId);
        AssertEqual(0, heading.OutlineLevel);
    }

    static void OpenXmlInspectorReadsTableFormatSamples()
    {
        using var temp = new TempDirectory();
        var docx = Path.Combine(temp.Path, "formatted-table-fixture.docx");
        WriteFormattedFixtureDocx(docx);

        var map = OpenXmlDocumentInspector.Inspect(docx);
        var table = map.Tables[0];

        AssertEqual(8640, table.Format.WidthTwips);
        AssertEqual("dxa", table.Format.WidthType);
        AssertEqual("center", table.Format.Alignment);
        AssertEqual(2, table.Format.GridColumnWidthsTwips.Count);
        AssertEqual(4320, table.Format.GridColumnWidthsTwips[0]);
        AssertEqual(4320, table.Format.GridColumnWidthsTwips[1]);
        AssertEqual("single", table.Format.Borders!.Top!.Value);
        AssertEqual("12", table.Format.Borders.Top.Size);
        AssertEqual("000000", table.Format.Borders.Top.Color);
        AssertEqual("single", table.Format.Borders.Bottom!.Value);
        AssertEqual("12", table.Format.Borders.Bottom.Size);
        AssertEqual("single", table.Format.Borders.InsideHorizontal!.Value);
        AssertEqual("4", table.Format.Borders.InsideHorizontal.Size);
        AssertEqual("nil", table.Format.Borders.InsideVertical!.Value);
        AssertEqual(60, table.Format.CellMargins!.TopTwips);
        AssertEqual(120, table.Format.CellMargins.LeftTwips);
        AssertEqual(60, table.Format.CellMargins.BottomTwips);
        AssertEqual(120, table.Format.CellMargins.RightTwips);
        AssertEqual(1, table.Format.HeaderRowCount);
        AssertEqual("center", table.Format.FirstCellParagraphFormat!.Alignment);
        AssertEqual(true, table.Format.FirstCellParagraphFormat.RunFormat!.Bold);
        AssertEqual("21", table.Format.FirstCellParagraphFormat.RunFormat.FontSizeHalfPoints);
        AssertEqual("宋体", table.Format.FirstCellParagraphFormat.RunFormat.EastAsiaFont);
    }

    static void CliInspectIncludesDocumentMapForDocxWorkspaces()
    {
        using var temp = new TempDirectory();
        var sourceDoc = Path.Combine(temp.Path, "source.docx");
        var profile = Path.Combine(temp.Path, "input-profile.json");
        var workspace = Path.Combine(temp.Path, ".thesis");

        WriteFixtureDocx(sourceDoc);
        File.WriteAllText(profile, "{}");

        var init = SessionInitializer.Initialize(sourceDoc, profile, workspace);
        AssertEqual("success", init.Status);

        var (exitCode, result) = RunCli(["inspect", "--workspace", workspace]);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(Path.Combine(Path.GetFullPath(workspace), "working.docx"), result.DocumentMap!.Path);
        AssertEqual(7, result.DocumentMap.Paragraphs.Count);
        AssertEqual(1, result.DocumentMap.Tables.Count);

        var rawJson = RunCliRaw(["inspect", "--workspace", workspace]).Output;
        AssertContains(rawJson, "\"documentMap\"");
        AssertContains(rawJson, "\"requiresFinalization\":true");
        AssertContains(rawJson, "\"finalizationReasons\":[\"fields\",\"toc\"]");
        AssertContains(rawJson, "\"numberingId\":\"1\"");
        AssertContains(rawJson, "\"levels\":[");
        AssertDoesNotContain(rawJson, "\"DocumentMap\"");
    }

    static void CliInspectReportsJsonWarningWhenDocumentMapUnavailable()
    {
        using var temp = new TempDirectory();
        var context = CreateInitializedWorkspace(temp.Path);

        var (exitCode, output) = RunCliRaw(["inspect", "--workspace", context.Workspace]);
        var result = ThesisJson.Deserialize<CliResult>(output);

        AssertEqual(0, exitCode);
        AssertEqual("success", result.Status);
        AssertEqual(null, result.DocumentMap);
        AssertEqual(1, result.Diagnostics.Count);
        AssertEqual("warning", result.Diagnostics[0].Severity);
        AssertEqual("document_map_unavailable", result.Diagnostics[0].Code);
        AssertEqual(context.Paths.WorkingDocument, result.Diagnostics[0].Path);
        AssertContains(output, "\"diagnostics\":[");
        AssertContains(output, "\"code\":\"document_map_unavailable\"");
        AssertContains(output, "\"path\":\"");
    }

}
