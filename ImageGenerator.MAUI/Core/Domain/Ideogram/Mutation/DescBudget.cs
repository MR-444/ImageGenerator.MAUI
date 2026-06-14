using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Enforces Ideogram's 60-word element-desc cap when ornament phrases are spliced into a desc.
/// <see cref="V4JsonPromptValidator"/> does NOT check word counts (only the reference-only verifier
/// does), so this is the sole guard. It trims by whole authored phrase, never by NLP: the base desc is
/// protected, and candidate phrases drop highest-tier-first per <see cref="DescBudgetCategory"/>.
/// </summary>
public static class DescBudget
{
    /// <summary>Ideogram's hard cap; <c>desc.split()</c> word count must not exceed this.</summary>
    public const int MaxWords = 60;

    /// <summary>
    /// Word count matching Python's <c>len(s.split())</c> — split on any whitespace run, ignore empties.
    /// </summary>
    public static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>
    /// Splices as many <paramref name="candidates"/> as fit into <paramref name="protectedDesc"/> without
    /// exceeding <paramref name="maxWords"/>. The base is never trimmed; candidates are admitted lowest
    /// drop-priority first (style markers before environmental micro-detail) so that, when over budget, the
    /// highest tiers are the ones left out. Kept phrases are emitted in their authored order. Returns the
    /// unchanged base when nothing fits, or <c>null</c> when the base alone already exceeds the cap.
    /// </summary>
    public static string? Fit(
        string protectedDesc,
        IReadOnlyList<OrnamentPhrase> candidates,
        int maxWords = MaxWords)
    {
        var baseDesc = (protectedDesc ?? string.Empty).Trim();
        var used = CountWords(baseDesc);
        if (used > maxWords)
            return null;

        // Select by keep-priority (lower category value kept first; stable within a tier by original
        // index), but remember each kept phrase's original position so output preserves authored order.
        var ordered = candidates
            .Select((phrase, index) => (phrase, index))
            .OrderBy(x => x.phrase.Category)
            .ThenBy(x => x.index);

        var keptIndices = new SortedSet<int>();
        foreach (var (phrase, index) in ordered)
        {
            var cost = CountWords(phrase.Text);
            if (cost == 0 || used + cost > maxWords)
                continue;
            keptIndices.Add(index);
            used += cost;
        }

        if (keptIndices.Count == 0)
            return baseDesc.Length == 0 ? null : baseDesc;

        var segments = new List<string> { baseDesc.TrimEnd('.', ' ') };
        segments.AddRange(keptIndices.Select(i => candidates[i].Text.Trim()));
        return string.Join(", ", segments);
    }
}
