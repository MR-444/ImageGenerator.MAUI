using FluentAssertions;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ImageGenerator.MAUI.Presentation.ViewModels;

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

        markup.Should().Contain("ItemsSource=\"{Binding ComfyUi.Krea2LoraOptions}\"")
            .And.Contain("SelectedItem=\"{Binding ComfyUi.SelectedKrea2Lora}\"")
            .And.Contain("AutomationId=\"Krea2LoraPicker\"")
            .And.Contain("AutomationId=\"Krea2LoraStrengthSlider\"")
            .And.Contain("Command=\"{Binding ComfyUi.RefreshKrea2LorasCommand}\"");
        strengthSlider.Name.LocalName.Should().Be("Slider");
        ((string?)strengthSlider.Attribute("HorizontalOptions")).Should().Be("Fill");
        ((string?)strengthSlider.Attribute("MaximumWidthRequest")).Should().Be("320");
    }

    [Fact]
    public void MainPage_UpscaleFactorCard_IsVisibleOnlyWhileTheFactorCanApply()
    {
        var markup = Markup("MainPage.xaml");
        var picker = XDocument.Parse(markup)
            .Descendants()
            .Single(element => (string?)element.Attribute("AutomationId") == "UpscaleFactorPicker");
        var card = (string?)picker.Parent!.Attribute("IsVisible");

        card.Should().Be("{Binding ComfyUi.IsUpscaleFactorVisible}",
            "a factor whose upscale slot lives in the chain workflow edits nothing while " +
            "\"Upscale after render\" is off, and offered it there it produced false UpscaleBy metadata");
        markup.Should().NotContain("IsVisible=\"{Binding ComfyUi.SupportsUpscaleFactor}\"");
    }

    /// <summary>
    /// The build does not validate MAUI binding paths (XamlSourceGen is off in this project — a
    /// {Binding Typo} compiles clean and silently shows a blank control), so the binding graph is
    /// checked here against the view models instead. Two rules, both aimed at the failure mode of
    /// pulling state into a sub-view-model: a path under a sub-model prefix must exist on that
    /// sub-model, and a member that only exists on a sub-model must never be bound bare at page
    /// level. Templates that declare their own x:DataType are skipped — their DataContext is the
    /// item, not the page.
    /// </summary>
    [Fact]
    public void MainPage_PageLevelBindings_ResolveAgainstTheViewModelGraph()
    {
        var document = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "MainPage.xaml"),
            LoadOptions.SetLineInfo);

        var subModels = new (string Prefix, Type Type)[]
        {
            ("ComfyUi", typeof(ComfyUiWorkflowCoordinator)),
            ("InputImages", typeof(InputImagesCoordinator)),
            ("ProviderFilter", typeof(ProviderFilterCoordinator)),
            ("Batch", typeof(BatchCoordinator)),
        };
        var pageProperties = PropertyNames(typeof(GeneratorViewModel));
        var subModelMembers = subModels
            .SelectMany(entry => PropertyNames(entry.Type).Select(name => (name, entry.Prefix)))
            .ToHashSet();

        var failures = new List<string>();
        foreach (var (attribute, line, path) in PageLevelBindingPaths(document))
        {
            var segments = path.Split('.', 2);
            var root = segments[0];
            var subModel = subModels.FirstOrDefault(entry =>
                string.Equals(entry.Prefix, root, StringComparison.Ordinal));

            if (subModel.Type is not null)
            {
                if (segments.Length < 2)
                {
                    failures.Add($"line {line}: {root} is bound with no member after it");
                }
                else if (!PropertyNames(subModel.Type).Contains(segments[1]))
                {
                    failures.Add($"line {line}: {path} is not a property of {subModel.Type.Name}");
                }
                continue;
            }

            // A bare page-level binding is only legitimate while the member still lives on the
            // page view model. If it lives on a sub-model, the binding silently lost its target
            // when that state was extracted.
            if (!pageProperties.Contains(root))
            {
                var home = subModelMembers.FirstOrDefault(m => string.Equals(m.name, root, StringComparison.Ordinal));
                failures.Add(home.name is null
                    ? $"line {line}: {path} resolves on neither {nameof(GeneratorViewModel)} nor a sub-model"
                    : $"line {line}: {path} must be bound as {home.Prefix}.{root} — it moved to {home.Prefix}");
            }
        }

        failures.Should().BeEmpty("MAUI does not validate binding paths at build time, so an " +
            "unresolvable page-level binding is a silently blank control at runtime");
    }

    private static IEnumerable<(string Attribute, int Line, string Path)> PageLevelBindingPaths(XDocument document)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
        foreach (var element in document.Descendants())
        {
            // Inside a DataTemplate that declares its own x:DataType the DataContext is the item.
            if (element.Ancestors().Any(a =>
                    a.Name.LocalName == "DataTemplate" && a.Attribute(x + "DataType") is not null))
            {
                continue;
            }

            foreach (var attribute in element.Attributes())
            {
                foreach (Match match in Regex.Matches(attribute.Value, @"\{Binding\s+(?!Path=)([A-Za-z_][A-Za-z0-9_.]*)"))
                {
                    // A binding that carries its own x:DataType targets something other than the page.
                    var tail = attribute.Value.Substring(match.Index, Math.Min(200, attribute.Value.Length - match.Index));
                    if (tail.Contains("x:DataType", StringComparison.Ordinal)) continue;
                    yield return (attribute.Name.LocalName, ((IXmlLineInfo)element).LineNumber, match.Groups[1].Value);
                }
            }
        }
    }

    private static HashSet<string> PropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

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
