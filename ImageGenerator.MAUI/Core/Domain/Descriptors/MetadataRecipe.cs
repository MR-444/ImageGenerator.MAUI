using System.Globalization;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors;

/// <summary>
/// Defensive read helpers for <see cref="Interfaces.IMetadataDescriber.Apply"/>: each call sets a
/// parameter only when the key is present AND parses cleanly, so a missing or hand-edited
/// (unparseable) value leaves the existing parameter untouched. Keeps every descriptor's Apply a
/// flat list of one-liners that mirror its Lines writer.
/// </summary>
internal static class MetadataRecipe
{
    public static void ApplyString(
        this IReadOnlyDictionary<string, string> meta, string key, Action<string> set,
        Func<string, bool>? isValid = null)
    {
        if (meta.TryGetValue(key, out var v) && (isValid is null || isValid(v)))
            set(v);
    }

    public static void ApplyBool(this IReadOnlyDictionary<string, string> meta, string key, Action<bool> set)
    {
        if (meta.TryGetValue(key, out var v) && bool.TryParse(v, out var parsed))
            set(parsed);
    }

    public static void ApplyDouble(this IReadOnlyDictionary<string, string> meta, string key, Action<double> set)
    {
        if (meta.TryGetValue(key, out var v)
            && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            set(parsed);
    }
}
