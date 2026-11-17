using System.Xml.Linq;
using Xunit;

public sealed class DesktopShortcutTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Theory]
    [InlineData("新建", "N", "New_Click", "{Binding IsIdle}")]
    [InlineData("打开", "O", "Open_Click", "{Binding IsIdle}")]
    [InlineData("保存", "S", "Save_Click", "{Binding CanEdit}")]
    public void ShortcutBelongsToItsCommandButton(string label, string key, string handler, string enabledBinding)
    {
        var markup = LoadMarkup();
        var shortcut = Assert.Single(markup.Descendants(Presentation + "KeyboardAccelerator"),
            element => (string?)element.Attribute("Key") == key);
        Assert.Equal("Control", (string?)shortcut.Attribute("Modifiers"));
        Assert.Equal(Presentation + "AppBarButton.KeyboardAccelerators", shortcut.Parent!.Name);
        var button = shortcut.Parent.Parent!;
        Assert.Equal(Presentation + "AppBarButton", button.Name);
        Assert.Equal(label, (string?)button.Attribute("Label"));
        Assert.Equal(handler, (string?)button.Attribute("Click"));
        Assert.Equal(enabledBinding, (string?)button.Attribute("IsEnabled"));
        Assert.Contains("Ctrl+" + key, (string?)button.Attribute("ToolTipService.ToolTip") ?? "");
        Assert.Null(shortcut.Attribute("ScopeOwner"));
    }

    [Fact]
    public void ToolbarTooltipsDoNotAdvertiseAnotherCommandsShortcut()
    {
        var markup = LoadMarkup();
        foreach (var button in markup.Descendants(Presentation + "AppBarButton"))
        {
            var tooltip = (string?)button.Attribute("ToolTipService.ToolTip");
            Assert.False(string.IsNullOrWhiteSpace(tooltip));
            if (!button.Descendants(Presentation + "KeyboardAccelerator").Any())
                Assert.DoesNotContain("Ctrl+", tooltip!);
        }
        Assert.All(markup.Descendants(Presentation + "KeyboardAccelerator"), shortcut =>
            Assert.Equal(Presentation + "AppBarButton", shortcut.Parent!.Parent!.Name));
    }

    private static XDocument LoadMarkup()
    {
        using var stream = typeof(DesktopShortcutTests).Assembly.GetManifestResourceStream("Desktop.MainWindow.xaml")!;
        return XDocument.Load(stream);
    }
}
