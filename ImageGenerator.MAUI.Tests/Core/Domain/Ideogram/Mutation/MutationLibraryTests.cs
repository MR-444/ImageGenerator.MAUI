using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class MutationLibraryTests
{
    [Fact]
    public void Ctor_DefensivelyCopiesInput_SoLaterMutationOfTheSourceListDoesNotLeakIn()
    {
        var fragments = new List<StyleFragment>
        {
            new("gouache", StyleMath.Clone(MutationTestData.BaseCaption().StyleDescription!)),
        };

        var library = new MutationLibrary(fragments, []);

        // Mutating the original list after construction must not change the library's view.
        fragments.Add(new StyleFragment("sneaked-in", StyleMath.Clone(MutationTestData.BaseCaption().StyleDescription!)));

        library.StyleFragments.Should().HaveCount(1);
        library.FragmentByName("sneaked-in").Should().BeNull();
    }
}
