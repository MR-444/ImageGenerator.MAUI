using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using ImageGenerator.MAUI.Presentation.Views.Controls;

namespace ImageGenerator.MAUI.Tests.Presentation;

/// <summary>
/// The row's own binding boundary, checked without rendering it.
///
/// Instantiating a MAUI control in this suite is not available: the first `new Label()` reaches WinUI
/// and dies in a static initializer (RPC_E_WRONGTHREAD), and a bare test host cannot start WindowsAppSDK
/// at all. So the boundary these tests hold is the one the bug actually crossed - *whose properties are
/// these* - by reading the built XAML and the control type, both of which are enough to state it, and by
/// leaving the pixel itself to the manual pass.
/// </summary>
public class ModelPickerRowTests
{
    private static readonly string MarkupPath =
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "ModelPickerRow.xaml");

    private static XElement Root() => XDocument.Load(MarkupPath).Root!.Elements().Single();

    /// <summary>
    /// A ContentView's content inherits the binding context of whatever hosts it. Every page here hosts
    /// the row on a view model that has no `Models` and no `RefreshCommand`, so unless the content names
    /// its own context the row asks the page for those and finds neither: an empty list beside a button
    /// that does nothing, compiling clean and passing every other markup test. This is the line that was
    /// missing, pinned.
    /// </summary>
    [Fact]
    public void Row_NamesTheObjectItsOwnElementsResolveAgainst()
    {
        var context = (string?)Root().Attribute("BindingContext");

        context.Should().NotBeNull(
            "the row's content has to say whose properties it binds; inherited from a page it would bind "
            + "that page's view model, which has none of the row's members");
        context.Should().Contain(nameof(ModelPickerRow));
    }

    /// <summary>
    /// Every path bound inside the row must be something the row itself exposes. Read the other way, this
    /// is the list of members a host has to be handed: a path that happens to exist on some page view
    /// model instead is the same defect wearing a different hat, and it would bind fine on that one page.
    /// </summary>
    [Fact]
    public void EveryBindingInsideTheRow_TargetsAPropertyTheRowItselfDeclares()
    {
        var declared = typeof(ModelPickerRow)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        // The second group peeks the next character: `{Binding Source=…}` names a source rather than a
        // path, and the only reliable way to tell them apart is what follows the word.
        var paths = System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(MarkupPath), @"\{Binding\s+([A-Za-z_][A-Za-z0-9_]*)(.)?")
            .Where(m => m.Groups[2].Value != "=")
            .Select(m => m.Groups[1].Value)
            .Distinct();

        var enumerable = paths.ToList();
        enumerable.Should().NotBeEmpty("a row with no bindings would pass this test for the wrong reason");
        enumerable.Should().OnlyContain(
            p => declared.Contains(p),
            "the row is its own binding source, so a path it cannot resolve is a silent hole at run time");
    }

    /// <summary>
    /// The four things a host must be able to hand a row, as real bindable properties rather than
    /// CLR-only conveniences: without `BindableProperty` backing, a `{Binding}` from a page - which is how
    /// every page here passes state in - does not move.
    /// </summary>
    [Theory]
    [InlineData("TitleText")]
    [InlineData("PickerTitle")]
    [InlineData("Models")]
    [InlineData("SelectedModel")]
    [InlineData("RefreshCommand")]
    [InlineData("Helper")]
    [InlineData("Status")]
    [InlineData("IsBusy")]
    [InlineData("ModelPickerAutomationId")]
    [InlineData("RefreshAutomationId")]
    [InlineData("StatusAutomationId")]
    [InlineData("Choice")]
    [InlineData("ProviderPickerAutomationId")]
    [InlineData("HelperAutomationId")]
    public void Hosts_CanSetThisThroughABinding(string property)
    {
        typeof(ModelPickerRow)
            .GetField($"{property}Property", BindingFlags.Public | BindingFlags.Static)
            .Should().NotBeNull($"{property} is set from page markup, so it has to be a bindable property");
    }

    /// <summary>
    /// A model choice that only travels one way is a model the user never picked: the row has to write
    /// back. Default is TwoWay precisely so a host cannot forget `Mode=TwoWay` and lose the pick.
    /// </summary>
    [Fact]
    public void ThePickedModel_TravelsBothWays()
    {
        ModelPickerRow.SelectedModelProperty.DefaultBindingMode
            .Should().Be(BindingMode.TwoWay);
    }

    /// <summary>
    /// A row whose host offered one provider gets no provider picker: a one-entry picker is a label
    /// wearing a control, and every row that grows one is a row where the user can click and change
    /// nothing. The picker writes by position, so the entries cannot be re-matched by spelling.
    /// </summary>
    [Fact]
    public void TheProviderPicker_AppearsOnlyWhenThereIsMoreThanOneProvider()
    {
        var markup = File.ReadAllText(MarkupPath);

        markup.Should().Contain("IsVisible=\"{Binding ShowProviderPicker}\"",
            "one provider is not a choice, and a picker offering one thing is a label dressed as a control");
        markup.Should().Contain("SelectedIndex=\"{Binding ProviderIndex, Mode=TwoWay}\"",
            "the picker selects by position, so two providers spelled alike cannot select each other");
        markup.Should().Contain("ItemsSource=\"{Binding ProviderLabels}\"",
            "the entries are spelled by ModelProviderLabels, never by an enum's ToString");
    }

    /// <summary>
    /// One set of elements for both wiring shapes. Two copies of the same picker in one control is the
    /// duplication this control exists to remove, and the copy kept "just for the migration" is the copy
    /// that outlives it.
    /// </summary>
    [Fact]
    public void ThereIsExactlyOneModelPickerAndOneRefreshButton()
    {
        var markup = File.ReadAllText(MarkupPath);

        System.Text.RegularExpressions.Regex.Matches(markup, "<Picker").Count
            .Should().Be(2, "one for the provider, one for the model");
        System.Text.RegularExpressions.Regex.Matches(markup, "<Button").Count
            .Should().Be(1, "the model picker and its Refresh are written once, whichever shape fed them");
    }

    /// <summary>
    /// A handle a host can set and no element carries is worse than no handle: the markup test here finds
    /// the attribute and passes, the UI test outside this repository finds no control, and both sides
    /// believe the picker is on screen.
    /// </summary>
    [Theory]
    [InlineData("ModelPickerAutomationId")]
    [InlineData("ProviderPickerAutomationId")]
    [InlineData("RefreshAutomationId")]
    [InlineData("StatusAutomationId")]
    [InlineData("HelperAutomationId")]
    public void EveryHandleAHostCanSet_SitsOnAnElement(string handle)
    {
        File.ReadAllText(MarkupPath).Should().Contain(
            "AutomationId=\"{Binding " + handle + "}\"",
            "a settable id that nothing binds is a handle nobody can click");
    }
}
