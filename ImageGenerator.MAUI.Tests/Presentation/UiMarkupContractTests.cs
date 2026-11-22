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

    /// <summary>
    /// Shared controls are not pages, but they resolve resources the same way - through the page that
    /// hosts them and then application scope - so they join the checks that prove a StaticResource
    /// actually exists. A control no page hosts evaluates no bindings at all, which is why each one is
    /// adopted in the commit that writes it.
    /// </summary>
    private static readonly string[] ControlAssets =
    [
        "ModelPickerRow.xaml"
    ];

    [Fact]
    public void EveryPageMarkup_IsWellFormed_AndAvoidsUnsupportedAccelerators()
    {
        foreach (var asset in PageAssets.Concat(ControlAssets))
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

        foreach (var asset in PageAssets.Concat(ControlAssets))
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

    /// <summary>
    /// The converters a reusable control needs are declared once, in application resources. A per-page
    /// copy is more than duplication: it shadows the application instance for that page only, so the
    /// same control could bind on the page that declares its own copy and show an unbound, permanently
    /// hidden status label on every other page - a failure with no compile error and no runtime warning.
    /// </summary>
    [Fact]
    public void SharedConverters_AreDeclaredOnceInApplicationResources()
    {
        var applicationKeys = ResourceKeys(
            XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "App.xaml")));
        string[] shared = ["NonEmptyStringToBool", "InverseBool"];

        applicationKeys.Should().Contain(shared,
            "a control has no page resources of its own, so application scope is the only place both "
            + "pages and controls reach");

        foreach (var asset in PageAssets)
        {
            var pageKeys = ResourceKeys(
                XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", asset)));

            pageKeys.Should().NotContain(shared,
                $"{asset} must not re-declare a shared converter, or the control that relies on the "
                + "application instance silently breaks on every other page");
        }
    }

    /// <summary>
    /// A role that has handed its whole decision to the row: one binding carries the provider list, the
    /// model list, the refresh, the status and the memory of what was picked, so there is nothing left in
    /// the page to mis-wire.
    ///
    /// <para>
    /// A page that hands over a choice and also binds the per-property shape would answer one question
    /// twice — the picker would show the choice's model and write the other one. The loop over
    /// hand-bound attributes below rejects that. The handles live here deliberately: the row is shared,
    /// the ids are not, because
    /// an id is how a human points at a control — in a bug report, in a diff, in the next page's markup
    /// test. There is no consumer to keep compatible; there is only a name worth getting right.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("IdeogramStructureEditorPage.xaml", "{Binding Enrichment}",
        "EditorEnrichProviderPicker", "EditorEnrichModelPicker",
        "EditorEnrichModelRefreshButton", "EditorEnrichModelStatusLabel")]
    [InlineData("MutationEnginePage.xaml", "{Binding Model}",
        "MutationModelProviderPicker", "MutationOllamaModelPicker",
        "MutationOllamaRefreshButton", "MutationOllamaStatusLabel")]
    [InlineData("IdeaToPromptPage.xaml", "{Binding Vision}",
        "VisionProviderPicker", "IdeaPromptOllamaVisionModelPicker",
        "IdeaPromptOllamaVisionRefreshButton", "IdeaPromptOllamaVisionStatusLabel")]
    [InlineData("IdeaToPromptPage.xaml", "{Binding Writer}",
        "IdeaPromptWriterProviderPicker", "IdeaPromptOllamaModelPicker",
        "IdeaPromptOllamaRefreshButton", "IdeaPromptTierOllamaStatusLabel")]
    public void EachDecision_HandedToTheRowWhole(
        string page, string choice, string providerPickerId, string modelPickerId,
        string refreshButtonId, string statusId)
    {
        var markup = Markup(page);
        // Located by its own handle, not by being the page's only row: Idea to Prompt hosts two decisions,
        // and both readings have to be able to say which row they looked at.
        var row = XDocument.Parse(markup).Descendants()
            .Single(e => e.Name.LocalName == "ModelPickerRow"
                         && (string?)e.Attribute("ModelPickerAutomationId") == modelPickerId);

        ((string?)row.Attribute("Choice")).Should().Be(choice,
            "the page owns which decision this is; everything the decision needs comes from there");

        ((string?)row.Attribute("ModelPickerAutomationId")).Should().Be(modelPickerId);
        ((string?)row.Attribute("RefreshAutomationId")).Should().Be(refreshButtonId);
        ((string?)row.Attribute("StatusAutomationId")).Should().Be(statusId);
        ((string?)row.Attribute("ProviderPickerAutomationId")).Should().Be(providerPickerId,
            "a picker the tests cannot see is a picker nobody can prove works");

        foreach (var handBound in new[] { "Models", "SelectedModel", "RefreshCommand", "Status" })
            row.Attribute(handBound).Should().BeNull(
                $"the choice already supplies {handBound}; binding it here as well gives the page two "
                + "answers to one question, and only one of them is saved");

        Count(markup, modelPickerId).Should().Be(1,
            "the id moves onto the row; a leftover hand-rolled picker would put a second picker on the "
            + "same page under the same handle");
    }

    /// <summary>
    /// UI tests read the writer card's cost sentence by name; it is the row's helper, and the page must
    /// not keep a second sentence answering the same question the row already answers.
    /// </summary>
    [Fact]
    public void TheWritersHelperSitsOnTheRowThatReplacedThePageOwnLabel()
    {
        var markup = Markup("IdeaToPromptPage.xaml");
        var doc = XDocument.Parse(markup);

        var row = doc.Descendants()
            .Single(e => e.Name.LocalName == "ModelPickerRow"
                         && (string?)e.Attribute("ModelPickerAutomationId") == "IdeaPromptOllamaModelPicker");
        ((string?)row.Attribute("HelperAutomationId")).Should().Be("IdeaPromptModelSummaryLabel",
            "the handle survives; what it points at is now the row's own helper");

        Count(markup, "IdeaPromptModelSummaryLabel").Should().Be(1,
            "one sentence about who is billed, in one place");
        Count(markup, "IdeaPromptModelTierPicker").Should().Be(0,
            "the tier picker is what said Local where the card above it said Local (Ollama)");
    }

    /// <summary>
    /// The row is one control now instead of one per provider, so the controls beside it have to keep
    /// their own gate. This switch filters a pricing rule that only exists at OpenRouter: shown while the
    /// row is set to a local server, it offers a filter it cannot apply and hides nothing — and a switch
    /// that does nothing is worse than one that is missing, because it gets clicked.
    /// </summary>
    [Fact]
    public void TheFreeOnlySwitch_OnlyAppearsOverTheListItFilters()
    {
        var doc = XDocument.Parse(Markup("IdeaToPromptPage.xaml"));
        var toggle = doc.Descendants()
            .Single(e => (string?)e.Attribute("AutomationId") == "IdeaPromptOpenRouterVisionFreeOnlySwitch");

        var gated = toggle.Ancestors()
            .FirstOrDefault(a => a.Attribute("IsVisible") is not null);

        ((string?)gated?.Attribute("IsVisible")).Should().Be("{Binding IsOpenRouterVisionProvider}",
            "the switch has to disappear with the list it filters, not stay on screen over Ollama");
    }
    /// <summary>
    /// Enrichment has no queue and no status bar: change the model halfway through a run and the run
    /// keeps the model it had already resolved, with nothing on screen saying so. So this row greys itself
    /// out. The mutation host does not pass IsBusy and never did — that is this page's rule, not the
    /// shared row's, which is why it is asserted here and not in the theory above.
    /// </summary>
    /// <summary>
    /// The enrichment tier defaults to a paid Anthropic model, and the Enrich button is in the same pane
    /// below this section. Collapsed behind a grey word with no chevron, the model choice was invisible to
    /// the person about to spend money on it. Both halves are pinned: open by default, and a disclosure
    /// chevron like the page's three other sections rather than a label that never changes.
    /// </summary>
    [Fact]
    public void EnrichmentSettings_DoNotHideTheModelThatCostsMoney()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
        var markup = Markup("IdeogramStructureEditorPage.xaml");
        var body = XDocument.Parse(markup).Descendants()
            .Single(e => (string?)e.Attribute(x + "Name") == "EnrichmentSettingsBody");

        ((string?)body.Attribute("IsVisible")).Should().NotBe("False",
            "a collapsed section hides which model the Enrich button is about to pay for");

        markup.Should().Contain("x:Reference EnrichmentSettingsBody",
            "the header has to show the chevron that says it opens something");
        markup.Should().Contain("Converter={StaticResource BoolToChevron}, x:DataType=View}",
            "the chevron follows the body's own IsVisible, as it does on the other three sections");
        Count(markup, "Converter={StaticResource BoolToChevron}").Should().Be(4,
            "every accordion header on this page discloses; a section that stops showing its chevron is a"
            + " section nobody finds");
    }

    [Fact]
    public void IdeogramStructureEditorPage_QuietsTheRowMidEnrichment()
    {
        var row = XDocument.Parse(Markup("IdeogramStructureEditorPage.xaml"))
            .Descendants()
            .Single(element => element.Name.LocalName == "ModelPickerRow");

        ((string?)row.Attribute("IsBusy")).Should().Be(
            "{Binding IsEnriching}",
            "a model changed mid-enrichment is ignored, and this page has no status bar to say so");
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

        markup.Should().Contain("IsToggled=\"{Binding Settings.UseSageAttention, Mode=TwoWay}\"")
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

        markup.Should().Contain("ItemsSource=\"{Binding ComfyUi.Krea2LoraChoice.Options}\"")
            .And.Contain("SelectedItem=\"{Binding ComfyUi.Krea2LoraChoice.Selected}\"")
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
    /// Verifies that page-level bindings declared in a XAML page resolve against the view model graph associated with that page.
    /// </summary>
    /// <param name="asset">The identifier or path of the XAML page test asset to inspect.</param>
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

        var colon = declared.IndexOf(':');
        var (prefix, name) = colon < 0 ? (null, declared) : (declared[..colon], declared[(colon + 1)..]);
        var xmlns = prefix is null
            ? (string?)document.Root.Attribute("xmlns")
            : (string?)document.Root.Attribute(XNamespace.Xmlns + prefix);
        xmlns.Should().NotBeNull($"{asset} declares x:DataType \"{declared}\" with no matching xmlns");

        var clrNamespace = xmlns
            .Split(';')[0]
            .Replace("clr-namespace:", string.Empty, StringComparison.Ordinal)
            .Replace("using:", string.Empty, StringComparison.Ordinal);
        var type = typeof(GeneratorViewModel).Assembly.GetType($"{clrNamespace}.{name}");
        type.Should().NotBeNull(
            $"{asset}'s x:DataType \"{declared}\" ({clrNamespace}.{name}) is not a type of the app assembly");
        return type;
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

    private static int Count(string haystack, string needle) =>
        haystack.Split(needle).Length - 1;

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
