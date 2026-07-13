using FluentAssertions;
using System.Xml.Linq;

namespace ImageGenerator.MAUI.Tests.Presentation;

public class UiMarkupContractTests
{
    private static readonly string[] PageAssets =
    [
        "MainPage.xaml", "IdeaToPromptPage.xaml", "MutationEnginePage.xaml",
        "IdeogramStructureEditorPage.xaml", "GalleryPage.xaml",
        "GalleryItemDetailPage.xaml", "SettingsPage.xaml"
    ];

    [Fact]
    public void EveryPageMarkup_IsWellFormed_AndAvoidsUnsupportedAccelerators()
    {
        foreach (var asset in PageAssets)
        {
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", asset));
            document.Root.Should().NotBeNull($"{asset} must load as XAML/XML");
            document.Descendants().Should().NotContain(
                element => element.Name.LocalName.Contains("KeyboardAccelerator", StringComparison.Ordinal),
                $"{asset} must not use the KeyboardAccelerators property that fails in the MAUI runtime loader");
        }
    }

    [Fact]
    public void StudioShell_ExposesCreateGalleryAndSettingsRail()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "AppShell.xaml"));
        var titles = document.Descendants()
            .Where(element => element.Name.LocalName == "FlyoutItem")
            .Select(element => (string?)element.Attribute("Title"))
            .ToList();

        titles.Should().Equal("CREATE", "GALLERY", "SETTINGS");
    }

    [Fact]
    public void RedesignedPages_ExposePrimaryWorkflowContracts()
    {
        Markup("MutationEnginePage.xaml").Should().Contain("MutationAiModeButton").And.Contain("MutationDeterministicModeButton");
        Markup("SettingsPage.xaml").Should().Contain("SettingsAiServicesCategory").And.Contain("SettingsOutputCategory");
        Markup("GalleryItemDetailPage.xaml").Should().Contain("DetailPromptTab").And.Contain("DetailRawTab");
        Markup("GalleryPage.xaml").Should().Contain("Breed variants");
    }

    [Fact]
    public void SettingsPage_SageAttentionSwitch_IsBoundAccessibleAndAutomatable()
    {
        var markup = Markup("SettingsPage.xaml");

        markup.Should().Contain("IsToggled=\"{Binding UseSageAttention, Mode=TwoWay}\"")
            .And.Contain("SemanticProperties.Description=\"Use SageAttention for compatible ComfyUI workflows\"")
            .And.Contain("AutomationId=\"UseSageAttentionSwitch\"");
    }

    private static string Markup(string asset) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", asset));
}
