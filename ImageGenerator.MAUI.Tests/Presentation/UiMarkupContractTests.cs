using FluentAssertions;
using System.Text.RegularExpressions;
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
    public void EveryConverterStaticResource_ResolvesFromThePageOrApplicationResources()
    {
        var applicationKeys = ResourceKeys(
            XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "App.xaml")));

        foreach (var asset in PageAssets)
        {
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", asset));
            var availableKeys = applicationKeys
                .Concat(ResourceKeys(document))
                .ToHashSet(StringComparer.Ordinal);
            var references = document.Root!
                .DescendantsAndSelf()
                .Attributes()
                .SelectMany(attribute => ConverterResourceKeys(attribute.Value))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            references.Should().OnlyContain(
                reference => availableKeys.Contains(reference),
                $"every converter StaticResource in {asset} must be declared by the page or App.xaml");
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

    [Fact]
    public void MainPage_Krea2LoraControls_AreBoundAndAutomatable()
    {
        var markup = Markup("MainPage.xaml");
        var strengthSlider = XDocument.Parse(markup)
            .Descendants()
            .Single(element =>
                (string?)element.Attribute("AutomationId") == "Krea2LoraStrengthSlider");

        markup.Should().Contain("ItemsSource=\"{Binding Krea2LoraOptions}\"")
            .And.Contain("SelectedItem=\"{Binding SelectedKrea2Lora}\"")
            .And.Contain("AutomationId=\"Krea2LoraPicker\"")
            .And.Contain("AutomationId=\"Krea2LoraStrengthSlider\"")
            .And.Contain("Command=\"{Binding RefreshKrea2LorasCommand}\"");
        strengthSlider.Name.LocalName.Should().Be("Slider");
        ((string?)strengthSlider.Attribute("HorizontalOptions")).Should().Be("Fill");
        ((string?)strengthSlider.Attribute("MaximumWidthRequest")).Should().Be("320");
    }

    private static string Markup(string asset) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", asset));

    private static HashSet<string> ResourceKeys(XDocument document)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
        return document
            .Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> ConverterResourceKeys(string attributeValue) =>
        Regex.Matches(
                attributeValue,
                @"Converter=\{StaticResource\s+([^\s,}]+)",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value);
}
