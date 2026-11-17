using System.Xml.Linq;
using Xunit;

public sealed class DesktopShortcutTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Theory]
    [InlineData("新建", "N", "New_Click", "NewShortcut_Invoked", "{Binding IsIdle}")]
    [InlineData("打开", "O", "Open_Click", "OpenShortcut_Invoked", "{Binding IsIdle}")]
    [InlineData("保存", "S", "Save_Click", "SaveShortcut_Invoked", "{Binding CanEdit}")]
    public void ShortcutIsScopedToTheWindowAndRunsItsCommand(string label, string key, string clickHandler, string shortcutHandler, string enabledBinding)
    {
        var markup = LoadMarkup();
        var shortcut = Assert.Single(markup.Descendants(Presentation + "KeyboardAccelerator"),
            element => (string?)element.Attribute("Key") == key);
        Assert.Equal("Control", (string?)shortcut.Attribute("Modifiers"));
        Assert.Equal("Grid.KeyboardAccelerators", shortcut.Parent!.Name.LocalName);
        var root = shortcut.Parent.Parent!;
        Assert.Equal(Presentation + "Grid", root.Name);
        Assert.Equal("Hidden", (string?)root.Attribute("KeyboardAcceleratorPlacementMode"));
        Assert.Equal("Root", (string?)root.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name"));
        Assert.Equal(shortcutHandler, (string?)shortcut.Attribute("Invoked"));

        var button = Assert.Single(markup.Descendants(Presentation + "AppBarButton"), element => (string?)element.Attribute("Label") == label);
        Assert.Equal(Presentation + "AppBarButton", button.Name);
        Assert.Equal(clickHandler, (string?)button.Attribute("Click"));
        Assert.Equal(enabledBinding, (string?)button.Attribute("IsEnabled"));
        Assert.Contains("Ctrl+" + key, (string?)button.Attribute("ToolTipService.ToolTip") ?? "");
        Assert.Empty(button.Descendants(Presentation + "KeyboardAccelerator"));
    }

    [Fact]
    public void ToolbarTooltipsDoNotAdvertiseAnotherCommandsShortcut()
    {
        var markup = LoadMarkup();
        foreach (var button in markup.Descendants(Presentation + "AppBarButton"))
        {
            var tooltip = (string?)button.Attribute("ToolTipService.ToolTip");
            Assert.False(string.IsNullOrWhiteSpace(tooltip));
            if ((string?)button.Attribute("Label") is not ("新建" or "打开" or "保存"))
                Assert.DoesNotContain("Ctrl+", tooltip!);
        }
        Assert.All(markup.Descendants(Presentation + "KeyboardAccelerator"), shortcut =>
            Assert.Equal("Grid.KeyboardAccelerators", shortcut.Parent!.Name.LocalName));
    }

    [Fact]
    public void WritingWorkspaceUsesNativeRichTextWithCompactSourceFallback()
    {
        var markup = LoadMarkup();
        var editor = Assert.Single(markup.Descendants(Presentation + "RichEditBox"));
        Assert.Equal("BodyEditor", (string?)editor.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name"));
        Assert.Equal("BodyEditor_SelectionChanged", (string?)editor.Attribute("SelectionChanged"));
        Assert.Contains(markup.Descendants(Presentation + "TextBox"), element =>
            (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "SourceBodyEditor");
        Assert.Contains(markup.Descendants(Presentation + "Button"), element =>
            (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "UndoEditorButton");
        Assert.Contains(markup.Descendants(Presentation + "Button"), element =>
            (string?)element.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name") == "RedoEditorButton");
    }

    private static XDocument LoadMarkup()
    {
        using var stream = typeof(DesktopShortcutTests).Assembly.GetManifestResourceStream("Desktop.MainWindow.xaml")!;
        return XDocument.Load(stream);
    }
}
