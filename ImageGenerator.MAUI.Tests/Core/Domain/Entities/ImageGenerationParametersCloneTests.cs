using System.Reflection;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Entities;

public class ImageGenerationParametersCloneTests
{
    [Fact]
    public void Clone_CopiesEveryPublicReadWriteProperty()
    {
        // Reflection-based completeness check: if someone adds a new [ObservableProperty]
        // and forgets to update Clone(), this test fails.
        var props = typeof(ImageGenerationParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();

        var original = new ImageGenerationParameters();
        foreach (var prop in props) SetSentinel(original, prop);

        var copy = original.Clone();

        foreach (var prop in props)
        {
            var originalValue = prop.GetValue(original);
            var clonedValue = prop.GetValue(copy);
            clonedValue.Should().Be(originalValue, $"Clone() must propagate {prop.Name}");
        }
    }

    [Fact]
    public void Clone_CopiesImagePromptsCollectionIntoFreshInstance()
    {
        var original = new ImageGenerationParameters();
        original.ImagePrompts.Add("one");
        original.ImagePrompts.Add("two");

        var copy = original.Clone();

        copy.ImagePrompts.Should().ContainInOrder("one", "two");
        copy.ImagePrompts.Should().NotBeSameAs(original.ImagePrompts);

        // Mutating the source after cloning must not affect the clone.
        original.ImagePrompts.Add("three");
        copy.ImagePrompts.Should().HaveCount(2);
    }

    private static void SetSentinel(ImageGenerationParameters target, PropertyInfo prop)
    {
        var type = prop.PropertyType;
        object value = type switch
        {
            _ when type == typeof(string) => $"SENTINEL_{prop.Name}",
            _ when type == typeof(bool)   => !(bool)prop.GetValue(target)!,
            _ when type == typeof(int)    => PickIntSentinel(prop),
            _ when type == typeof(long)   => 123_456_789L,
            _ when type == typeof(double) => 0.73,
            _ when type == typeof(ImageOutputFormat) => ImageOutputFormat.Webp,
            _ => throw new InvalidOperationException($"No sentinel defined for {prop.Name}: {type}")
        };
        prop.SetValue(target, value);
    }

    private static int PickIntSentinel(PropertyInfo prop) => prop.Name switch
    {
        // Width/Height have clamp logic — pick values inside the valid range.
        nameof(ImageGenerationParameters.Width)  => ValidationConstants.ImageWidthMin + 7,
        nameof(ImageGenerationParameters.Height) => ValidationConstants.ImageHeightMin + 13,
        nameof(ImageGenerationParameters.SafetyTolerance) => 3,
        nameof(ImageGenerationParameters.OutputQuality) => 42,
        _ => 99
    };
}
