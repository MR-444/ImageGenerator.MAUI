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

        paths.Should().NotBeEmpty("a row with no bindings would pass this test for the wrong reason");
        paths.Should().OnlyContain(
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
    [InlineData("ModelPickerAutomationId")]
    [InlineData("RefreshAutomationId")]
    [InlineData("StatusAutomationId")]
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
            .Should().Be(Microsoft.Maui.Controls.BindingMode.TwoWay);
    }
}
