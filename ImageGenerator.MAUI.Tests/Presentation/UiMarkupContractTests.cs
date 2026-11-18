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
    /// {Binding Typo} compiles clean and silently shows a blank control), so every page's binding
    /// graph is checked here against the view models. One case per page, so a failure names the
    /// page; the root view model is the page's own <c>x:DataType</c>, which compiled bindings
    /// already require — so a page cannot silently change what is being checked.
    ///
    /// A path is walked segment by segment and every segment must be a public instance property of
    /// the type the previous segment resolved to (a collection resolves to its element type). That
    /// covers a hop into a sub-view-model without a hand-kept table — <c>ComfyUi</c>,
    /// <c>InputImages</c>, <c>ProviderFilter</c>, <c>Batch</c> today, and it needs no edit when the
    /// next coordinator lands — which is what makes a refactor's repath machine-checked: leave one
    /// binding on the old owner and the case fails with the path to bind instead.
    ///
    /// Rules, all aimed at the failure mode of pulling state into a sub-view-model:
    /// • every segment resolves, or the failure names the sub-model that does own the member;
    /// • a path must not end on a sub-model itself (binding the object is never what markup means).
    ///
    /// A "sub-model" is a property whose type name ends in <c>Coordinator</c> — the convention the
    /// extractions follow. Out of page scope, and skipped: anything inside a DataTemplate that
    /// declares its own x:DataType (its DataContext is the item), any binding carrying its own
    /// <c>Source=</c>, <c>RelativeSource=</c> or <c>x:DataType</c>, and <c>&lt;Binding&gt;</c>
    /// elements (MultiBinding children) that declare their own <c>x:DataType</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(PagesWithADeclaredViewModel))]
    public void PageLevelBindings_ResolveAgainstTheViewModelGraph(string asset)
    {
        var document = XDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", asset), LoadOptions.SetLineInfo);
        var root = DeclaredViewModel(document, asset);

        var failures = new List<string>();
        var checkedCount = 0;
        foreach (var (site, line, path) in PageLevelBindingPaths(document))
        {
            checkedCount++;
            var failure = UnresolvedSegment(root, path);
            if (failure is not null)
                failures.Add($"{asset} line {line} ({site}): {{Binding {path}}} — {failure}");
        }

        // A scope filter that swallowed the whole page would be "green" and worth nothing. Every
        // page here binds dozens of members, so anything close to zero means the filter or the
        // declared root is wrong, not that the markup is fine.
        checkedCount.Should().BeGreaterThan(10,
            $"{asset} contributed almost no page-level binding to check");

        failures.Should().BeEmpty("MAUI does not validate binding paths at build time, so an " +
            "unresolvable page-level binding is a silently blank control at runtime");
    }

    /// <summary>
    /// Every page whose DataContext is a view model. Adding a page means adding a line here; the
    /// <see cref="DeclaredViewModel"/> assert is what stops an existing page from quietly dropping
    /// out of scope by losing its x:DataType.
    /// </summary>
    public static TheoryData<string> PagesWithADeclaredViewModel() =>
    [
        "MainPage.xaml",
        "SettingsPage.xaml",
        "IdeaToPromptPage.xaml",
        "MutationEnginePage.xaml",
        "IdeogramStructureEditorPage.xaml",
        "GalleryPage.xaml",
        "GalleryItemDetailPage.xaml"
    ];

    /// <summary>Returns the first thing wrong with the path, or null when all of it resolves.</summary>
    private static string? UnresolvedSegment(Type root, string path)
    {
        var segments = path.Split('.');
        var current = root;
        var walked = new List<string>();

        for (var i = 0; i < segments.Length; i++)
        {
            var property = current.GetProperty(segments[i], BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                // The message that pays for itself during an extraction: the member did not vanish,
                // it moved to a sub-model, and the markup has to say so — with the exact path to bind.
                var home = FindMemberOnSubModel(current, segments[i]);
                return home is null
                    ? $"{segments[i]} is not a property of {current.Name}"
                    : $"{segments[i]} is not a property of {current.Name}; bind " +
                      $"{string.Join('.', walked.Append(home))} instead";
            }

            if (i == segments.Length - 1)
                return IsSubModel(property.PropertyType)
                    ? $"ends on the sub-model {property.PropertyType.Name}; bind one of its members, not the object"
                    : null;

            current = ElementType(property.PropertyType);
            walked.Add(segments[i]);
        }

        return null; // unreachable: the last segment always returns
    }

    /// <summary>A sub-model is the convention the extractions follow: a coordinator owned by a view
    /// model. Recognising it by name is what lets this test follow a path into state that moved
    /// without a table that has to be edited every time something is extracted.</summary>
    private static bool IsSubModel(Type type) =>
        type.Namespace == typeof(GeneratorViewModel).Namespace
        && type.Name.EndsWith("Coordinator", StringComparison.Ordinal);

    /// <summary>The element type of a collection, else the type itself (so an ItemsSource path keeps
    /// resolving through a bound collection into its item). <see cref="string"/> is deliberately not
    /// decomposed into <see cref="char"/>.</summary>
    private static Type ElementType(Type type)
    {
        if (type == typeof(string)) return type;
        var enumerable = type.GetInterfaces().Concat([type])
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0] ?? type;
    }

    /// <summary>Breadth-first over the sub-models of <paramref name="from"/> (depth 2, coordinators
    /// only), looking for a member of that name; returns the path to bind, relative to {@from}.</summary>
    private static string? FindMemberOnSubModel(Type from, string member)
    {
        var frontier = new List<(Type Type, string Path)> { (from, string.Empty) };
        for (var depth = 0; depth < 2 && frontier.Count > 0; depth++)
        {
            var next = new List<(Type Type, string Path)>();
            foreach (var (type, prefix) in frontier)
            {
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (string.Equals(property.Name, member, StringComparison.Ordinal) && prefix.Length > 0)
                        return $"{prefix}.{property.Name}";
                    if (!IsSubModel(property.PropertyType)) continue;
                    next.Add((property.PropertyType,
                        prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}"));
                }
            }
            frontier = next;
        }
        return null;
    }

    private static Type DeclaredViewModel(XDocument document, string asset)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
        var declared = (string?)document.Root!.Attribute(x + "DataType");
        declared.Should().NotBeNull(
            $"{asset} must declare x:DataType on its root element: compiled bindings need it, and " +
            "this test reads the binding root from it");

        var colon = declared!.IndexOf(':');
        var (prefix, name) = colon < 0 ? (null, declared) : (declared[..colon], declared[(colon + 1)..]);
        var xmlns = prefix is null
            ? (string?)document.Root.Attribute("xmlns")
            : (string?)document.Root.Attribute(XNamespace.Xmlns + prefix);
        xmlns.Should().NotBeNull($"{asset} declares x:DataType \"{declared}\" with no matching xmlns");

        var clrNamespace = xmlns!
            .Split(';')[0]
            .Replace("clr-namespace:", string.Empty, StringComparison.Ordinal)
            .Replace("using:", string.Empty, StringComparison.Ordinal);
        var type = typeof(GeneratorViewModel).Assembly.GetType($"{clrNamespace}.{name}");
        type.Should().NotBeNull(
            $"{asset}'s x:DataType \"{declared}\" ({clrNamespace}.{name}) is not a type of the app assembly");
        return type!;
    }

    private static IEnumerable<(string Site, int Line, string Path)> PageLevelBindingPaths(XDocument document)
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

            foreach (var (site, binding) in BindingExtensions(element))
            {
                // A binding that names its own source, ancestor or type targets something other
                // than the page's view model, so the page graph says nothing about it.
                if (binding.Contains("RelativeSource", StringComparison.Ordinal)
                    || binding.Contains("Source=", StringComparison.Ordinal)
                    || binding.Contains("x:DataType", StringComparison.Ordinal))
                {
                    continue;
                }

                var path = Regex.Match(binding, @"^\{Binding\s+(?:Path=)?([A-Za-z_][A-Za-z0-9_.]*)");
                if (path.Success)
                    yield return (site, ((IXmlLineInfo)element).LineNumber, path.Groups[1].Value);
            }
        }
    }

    /// <summary>Every binding of one element that is evaluated against the page's view model, each
    /// tagged with the markup site it was written at: every <c>{Binding …}</c> in an attribute value
    /// (brace-matched, so a nested <c>{StaticResource …}</c> or a second binding in the same value
    /// stays in one piece) plus each <c>&lt;Binding&gt;</c> child of a MultiBinding that does not
    /// declare its own type.</summary>
    private static IEnumerable<(string Site, string Binding)> BindingExtensions(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            foreach (var binding in SplitBindings(attribute.Value))
                yield return ($"{element.Name.LocalName}.{attribute.Name.LocalName}", binding);
        }

        if (element.Name.LocalName == "Binding")
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
            var path = (string?)element.Attribute("Path");
            if (path is not null && element.Attribute(x + "DataType") is null)
                yield return ("Binding.Path", $"{{Binding {path}}}");
        }
    }

    private static IEnumerable<string> SplitBindings(string value)
    {
        for (var i = 0; i < value.Length - 1; i++)
        {
            if (!value.AsSpan(i).StartsWith("{Binding".AsSpan(), StringComparison.Ordinal)) continue;

            var depth = 0;
            for (var j = i; j < value.Length; j++)
            {
                if (value[j] == '{') depth++;
                else if (value[j] != '}') continue;
                else depth--;

                if (depth != 0) continue;
                yield return value[i..(j + 1)];
                i = j;
                break;
            }
        }
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
