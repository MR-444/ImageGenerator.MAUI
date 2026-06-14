namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Deep-clone helper for <see cref="V4JsonPrompt"/>. Operators mutate a clone, never the source.
/// </summary>
public static class CaptionClone
{
    /// <summary>
    /// Returns an independent deep copy of <paramref name="source"/> by round-tripping through the
    /// canonical serializer. Reusing that path means the clone is structurally identical to what would
    /// reach disk — and, by design, any transient <see cref="Element.SlotTag"/> is dropped (operators
    /// re-resolve tags from <see cref="MutationContext"/>, not from the clone).
    /// </summary>
    public static V4JsonPrompt Clone(V4JsonPrompt source) =>
        V4JsonPromptSerializer.Deserialize(V4JsonPromptSerializer.Serialize(source));
}
