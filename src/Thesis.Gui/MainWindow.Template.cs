using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Thesis.Application;
using Thesis.OpenXml;
using Thesis.Schema;

namespace Thesis.Gui;

public sealed partial class MainWindow
{
    private async void RebuildTemplateOutline_Click(object sender, RoutedEventArgs e)
        => await RunAsync(() => viewModel.RebuildTemplateOutlineAsync());
    private TemplateDesign? editingDesign;
    private TemplateDesignRole? editingRole;
    private bool loadingRole;
    private void LoadTemplateEditor()
    {
        editingRole = null;
        editingDesign = viewModel.Design is null ? null : ThesisJson.Deserialize<TemplateDesign>(ThesisJson.Serialize(viewModel.Design));
        if (editingDesign is not null)
            foreach (var role in editingDesign.Roles.Where(r => r.Name == r.Role)) role.Name = TemplateDesigner.RoleName(role.Role);
        DesignName.Text = editingDesign?.Name ?? "尚未导入模板";
        RoleList.ItemsSource = editingDesign?.Roles;
        TemplateNotes.Text = string.Join("\n\n", editingDesign?.ReviewNotes ?? []);
        TemplateReviewSummary.Text = editingDesign is null ? "导入模板后，选择左侧角色即可核对和调整。"
            : $"共 {editingDesign.Roles.Count} 类格式规则，{editingDesign.Roles.Count(r => !r.Confirmed)} 类待核对。低置信度项目请重新选择样本段落。";
        if (editingDesign is null) return;
        MarginTop.Value = editingDesign.MarginTopMm; MarginBottom.Value = editingDesign.MarginBottomMm;
        MarginLeft.Value = editingDesign.MarginLeftMm; MarginRight.Value = editingDesign.MarginRightMm;
        ApplyMargins.IsChecked = editingDesign.ApplyMarginsToAllSections;
        RoleList.SelectedIndex = editingDesign.Roles.FindIndex(r => r.Role == "body");
    }
    private void SaveRoleFields()
    {
        if (editingRole is null || loadingRole) return;
        editingRole.Font = RoleFont.Text; editingRole.LatinFont = RoleLatinFont.Text; editingRole.Size = RoleSize.Value;
        editingRole.LineSpacing = RoleLine.Value; editingRole.FirstLineCharacters = RoleIndent.Value;
        editingRole.Alignment = (RoleAlign.SelectedItem as ComboBoxItem)?.Tag as string ?? "left"; editingRole.Bold = RoleBold.IsChecked == true;
        editingRole.OverrideFormat = RoleOverride.IsChecked == true; editingRole.Confirmed = RoleConfirmed.IsChecked == true;
    }
    private void Role_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveRoleFields(); editingRole = RoleList.SelectedItem as TemplateDesignRole;
        if (editingRole is null) return;
        loadingRole = true;
        try
        {
            RoleFont.Text = editingRole.Font; RoleLatinFont.Text = editingRole.LatinFont;
            RoleSize.Value = editingRole.Size; RoleLine.Value = editingRole.LineSpacing; RoleIndent.Value = editingRole.FirstLineCharacters;
            RoleAlign.SelectedItem = RoleAlign.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == editingRole.Alignment); RoleBold.IsChecked = editingRole.Bold;
            RoleOverride.IsChecked = editingRole.OverrideFormat; RoleConfirmed.IsChecked = editingRole.Confirmed;
            RoleEvidence.Text = $"识别角色：{editingRole.Name} · 样本块 {editingRole.SampleIndex} · 置信度 {editingRole.Confidence:P0}\n样本文本：{editingRole.SampleText}";
        }
        finally { loadingRole = false; }
        UpdateRoleSample();
    }
    private void UpdateRoleSample()
    {
        if (RoleSample is null || RoleFont is null || RoleSize is null || RoleBold is null) return;
        RoleSample.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(RoleFont.Text) ? "宋体" : RoleFont.Text);
        RoleSample.FontSize = double.IsFinite(RoleSize.Value) ? Math.Clamp(RoleSize.Value * 4 / 3, 7, 96) : 16;
        RoleSample.FontWeight = RoleBold.IsChecked == true ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        RoleSample.TextAlignment = ((RoleAlign?.SelectedItem as ComboBoxItem)?.Tag as string) switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            "both" or "distribute" => TextAlignment.Justify,
            _ => TextAlignment.Left
        };
        RoleSample.LineHeight = RoleSample.FontSize * (RoleLine is not null && double.IsFinite(RoleLine.Value) ? Math.Clamp(RoleLine.Value, 0.5, 5) : 1.5);
        RoleSample.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        RoleSample.Text = editingRole?.SampleText is { Length: > 0 } text ? text : "排版样例：论文的内容、结构与格式。";
    }
    private void RolePreview_Changed(object sender, RoutedEventArgs e) => UpdateRoleSample();
    private void RoleNumber_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e) => UpdateRoleSample();
    private async void ImportTemplate_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var path = await OpenFileAsync("导入模板或合格论文", ".docx"); if (path is null) return;
        var design = await Task.Run(() => TemplateDesigner.Inspect(path));
        await viewModel.UseDesignAsync(design); LoadTemplateEditor();
    });
    private async void DefaultTemplate_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    { await viewModel.UseDesignAsync(await Task.Run(() => TemplateLibraryStore.Default())); LoadTemplateEditor(); });
    private async void Library_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var templates = TemplateLibraryStore.List();
        var list = new ListView { ItemsSource = templates.Select(t => t.Name).ToList(), Height = 300, MinWidth = 360 };
        if (await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "我的模板库", Content = list, PrimaryButtonText = "使用", CloseButtonText = "取消" }.ShowAsync() == ContentDialogResult.Primary && list.SelectedIndex >= 0)
        { await viewModel.UseDesignAsync(TemplateLibraryStore.Load(templates[list.SelectedIndex].Path)); LoadTemplateEditor(); }
    });
    private async Task SaveDesignAsync(bool library)
    {
        if (editingDesign is null) return;
        SaveRoleFields(); editingDesign.Name = DesignName.Text;
        editingDesign.MarginTopMm = MarginTop.Value; editingDesign.MarginBottomMm = MarginBottom.Value;
        editingDesign.MarginLeftMm = MarginLeft.Value; editingDesign.MarginRightMm = MarginRight.Value;
        editingDesign.ApplyMarginsToAllSections = ApplyMargins.IsChecked == true;
        editingDesign.Version++;
        await viewModel.UseDesignAsync(editingDesign, library); LoadTemplateEditor();
    }
    private async void SaveTemplate_Click(object sender, RoutedEventArgs e) => await RunAsync(() => SaveDesignAsync(false));
    private async void SaveLibrary_Click(object sender, RoutedEventArgs e) => await RunAsync(() => SaveDesignAsync(true));
    private async void ChooseSample_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (editingRole is null || editingDesign is null) return;
        var preview = await Task.Run(() => DocumentPreviewReader.Read(editingDesign.SourcePath));
        var candidates = preview.Blocks.Where(b => b.Kind is "paragraph" or "heading").ToArray();
        var list = new ListView { ItemsSource = candidates.Select(b => $"{b.SourceIndex} · {b.Text}").ToArray(), Height = 420, MaxWidth = 620 };
        if (await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "选择模板中的样本段落", Content = list, PrimaryButtonText = "使用样本", CloseButtonText = "取消" }.ShowAsync() != ContentDialogResult.Primary || list.SelectedIndex < 0) return;
        var selected = candidates[list.SelectedIndex];
        editingRole.SampleIndex = selected.SourceIndex; editingRole.SampleText = selected.Text; editingRole.Confidence = 1;
        editingRole.Confirmed = true; RoleConfirmed.IsChecked = true; RoleOverride.IsChecked = false;
        RoleEvidence.Text = $"手动指定样本块 {selected.SourceIndex}\n{selected.Text}"; UpdateRoleSample();
    });
    private async void TemplatePreview_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (viewModel.TemplatePath is not { } path) return;
        quickPreview = await Task.Run(() => DocumentPreviewReader.Read(path));
        pdfPreview = null; previewPage = 0; ShowPage("preview"); await DrawPreviewAsync();
    });
    private async void CoverFields_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "通用模板使用 {{学校}} 等字段。自定义模板可添加原文与替换值；题目和作者自动取自论文属性。", TextWrapping = TextWrapping.Wrap });
        var fields = new List<(TextBox Key, TextBox Value)>();
        void Row(string key, string value)
        {
            var a = new TextBox { Header = "模板中的原文 / 占位符", Text = key }; var b = new TextBox { Header = "本篇论文的内容", Text = value };
            panel.Children.Add(a); panel.Children.Add(b); fields.Add((a, b));
        }
        foreach (var key in new[] { "{{学校}}", "{{学号}}", "{{导师}}", "{{日期}}" }.Concat(viewModel.CoverFields.Keys).Distinct()) Row(key, viewModel.CoverFields.GetValueOrDefault(key, ""));
        var add = new Button { Content = "添加字段" }; add.Click += (_, _) => Row("", ""); panel.Children.Add(add);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "封面字段", Content = new ScrollViewer { Content = panel, MaxHeight = 500 }, PrimaryButtonText = "保存", CloseButtonText = "取消" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var values = new Dictionary<string, string>();
            foreach (var field in fields.Where(f => !string.IsNullOrWhiteSpace(f.Key.Text)))
                if (!values.TryAdd(field.Key.Text, field.Value.Text)) throw new InvalidDataException("封面原文字段重复：" + field.Key.Text);
            viewModel.SetCoverFields(values);
        }
    });
    private async void Requirements_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (editingDesign is null) return;
        var rules = ThesisJson.Deserialize<List<ContentRequirement>>(ThesisJson.Serialize(editingDesign.Requirements));
        var list = new ComboBox { ItemsSource = rules, DisplayMemberPath = "Id", HorizontalAlignment = HorizontalAlignment.Stretch, Header = "已有要求" };
        var id = new TextBox { Header = "要求名称" };
        var kind = new ComboBox { Header = "检查类型", ItemsSource = new[] { "sectionCharacters", "keywords", "journalReferences", "requiredText" }, SelectedIndex = 0 };
        var start = new TextBox { Header = "起始标题（精确匹配，可留空）" }; var end = new TextBox { Header = "结束标题（可留空）" };
        var min = new NumberBox { Header = "最少数量 / 字符数", Minimum = 0 }; var max = new NumberBox { Header = "最多数量（空为不限）", Minimum = 0 };
        var text = new TextBox { Header = "必须包含的文字（requiredText 使用）" }; var evidence = new TextBox { Header = "学校要求的依据", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var tip = new TextBlock { Text = "sectionCharacters：区间字数；keywords：关键词数量；journalReferences：期刊文献篇数。标题应与最终文档一致。", TextWrapping = TextWrapping.Wrap };
        void LoadRule()
        {
            if (list.SelectedItem is not ContentRequirement rule) return;
            id.Text = rule.Id; kind.SelectedItem = rule.Kind; start.Text = rule.StartAnchor ?? ""; end.Text = rule.EndAnchor ?? "";
            min.Value = rule.Minimum ?? double.NaN; max.Value = rule.Maximum ?? double.NaN; text.Text = rule.Text ?? ""; evidence.Text = rule.Evidence;
        }
        list.SelectionChanged += (_, _) => LoadRule();
        var add = new Button { Content = "添加 / 更新当前要求" };
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(id.Text) || string.IsNullOrWhiteSpace(evidence.Text)) { tip.Text = "请填写名称和学校要求依据。"; return; }
            var rule = new ContentRequirement { Id = id.Text, Kind = (string)kind.SelectedItem, StartAnchor = Blank(start.Text), EndAnchor = Blank(end.Text),
                Minimum = double.IsFinite(min.Value) ? (int)min.Value : null, Maximum = double.IsFinite(max.Value) ? (int)max.Value : null, Text = Blank(text.Text), Evidence = evidence.Text };
            if (rule.Minimum > rule.Maximum) { tip.Text = "最少数量应小于或等于最多数量。"; return; }
            rules.RemoveAll(r => r.Id == rule.Id); rules.Add(rule); list.ItemsSource = rules.ToArray(); list.SelectedItem = rule; tip.Text = "已加入。点击“完成”后，再保存模板设计。";
        };
        var remove = new Button { Content = "删除选中的要求" }; remove.Click += (_, _) => { if (list.SelectedItem is ContentRequirement rule) { rules.Remove(rule); list.ItemsSource = rules.ToArray(); } };
        var panel = new StackPanel { Spacing = 10 };
        foreach (var control in new UIElement[] { tip, list, id, kind, start, end, min, max, text, evidence, add, remove }) panel.Children.Add(control);
        if (await new ContentDialog { XamlRoot = Root.XamlRoot, Title = "内容要求", Content = new ScrollViewer { Content = panel, MaxHeight = 520 }, PrimaryButtonText = "完成", CloseButtonText = "取消" }.ShowAsync() == ContentDialogResult.Primary) editingDesign.Requirements = rules;
    });
    private static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
