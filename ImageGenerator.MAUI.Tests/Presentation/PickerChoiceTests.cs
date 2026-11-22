using FluentAssertions;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Tests.Presentation;

/// <summary>
/// The rule every two-way-bound Picker relies on: only a real pick reaches onPicked. The pushes
/// faked here are the ones a WinUI Picker makes on its own.
/// </summary>
public class PickerChoiceTests
{
    private readonly List<string> _picks = [];

    private PickerChoice Create(params string[] options)
    {
        var choice = new PickerChoice(_picks.Add);
        choice.SetOptions(options);
        return choice;
    }

    private static int CountSelectedChanges(PickerChoice choice, Action act)
    {
        var count = 0;
        choice.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PickerChoice.Selected)) count++; };
        act();
        return count;
    }

    [Fact]
    public void Selected_AMemberValue_IsAPick()
    {
        var choice = Create("a", "b");

        choice.Selected = "b";

        choice.Selected.Should().Be("b");
        _picks.Should().Equal("b");
    }

    [Fact]
    public void Selected_TheCurrentValueAgain_IsNotAPick()
    {
        var choice = Create("a", "b");
        choice.Selected = "b";

        choice.Selected = "b";

        _picks.Should().Equal("b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-offered")]
    public void Selected_NullOrANonMember_IsIgnored(string? push)
    {
        var choice = Create("a", "b");
        choice.Show("a");

        choice.Selected = push;

        choice.Selected.Should().Be("a");
        _picks.Should().BeEmpty();
    }

    [Fact]
    public void Selected_IgnoredPush_ReannouncesTheValueSoThePickerShowsItAgain()
    {
        var choice = Create("a", "b");
        choice.Show("a");

        CountSelectedChanges(choice, () => choice.Selected = null).Should().Be(1);
    }

    [Fact]
    public void Selected_IgnoredPush_StaysQuietWhenTheValueIsNotInTheList()
    {
        // The Picker cannot show such a value; announcing it would only make it push null again.
        var choice = Create("a", "b");
        choice.Show("gone");

        CountSelectedChanges(choice, () => choice.Selected = null).Should().Be(0);
    }

    [Fact]
    public void SetOptions_PushesDuringTheSwap_AreNotPicks()
    {
        var choice = Create("a", "b");
        choice.Show("a");
        choice.PropertyChanged += (_, e) =>
        {
            // A Picker that resets to its first row, from inside the swap.
            if (e.PropertyName == nameof(PickerChoice.Options)) choice.Selected = choice.Options[0];
        };

        choice.SetOptions(["c", "a"]);

        choice.Selected.Should().Be("a");
        _picks.Should().BeEmpty();
    }

    [Fact]
    public void SetOptions_AnnouncesTheValueAgain_BecauseTheSwapResetThePicker()
    {
        var choice = Create("a", "b");
        choice.Show("a");

        CountSelectedChanges(choice, () => choice.SetOptions(["a", "c"])).Should().Be(1);
    }

    [Fact]
    public void SetOptions_Null_GivesAnEmptyList()
    {
        var choice = Create("a");

        choice.SetOptions(null);

        choice.Options.Should().BeEmpty();
    }

    [Fact]
    public void Show_IsNeverAPick()
    {
        var choice = Create("a", "b");

        choice.Show("b");

        choice.Selected.Should().Be("b");
        _picks.Should().BeEmpty();
    }
}
