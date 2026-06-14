namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// A named design grammar: the ornament phrases a look pours into under-specified nouns, keyed by the
/// <see cref="SlotTag"/> they target (e.g. <c>subject.garment</c> → embroidery/harness phrases,
/// <c>prop.charms</c> → locket/medallion phrases). <c>ApplyOrnamentKit</c> injects matching phrases into
/// each element by its resolved slot tag, within the desc word budget.
/// </summary>
/// <param name="Name">Stable identifier, recorded in provenance.</param>
/// <param name="PhrasesBySlot">Slot tag → the phrases to inject into elements carrying that tag.</param>
public sealed record OrnamentKit(
    string Name,
    IReadOnlyDictionary<string, IReadOnlyList<OrnamentPhrase>> PhrasesBySlot);
