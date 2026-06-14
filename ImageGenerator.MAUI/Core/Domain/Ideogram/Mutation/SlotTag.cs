namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Semantic slot vocabulary — dotted <c>namespace.role</c> identifiers attached transiently to an
/// <see cref="Element"/> (via <see cref="Element.SlotTag"/>) so mutation operators know which element
/// a kit phrase or desc edit targets. These never serialize (see the slot-tag-stripping test); they
/// live only in-memory for the duration of a run. Keyword inference of these tags from a caption is a
/// later phase — this type is the constant vocabulary only.
/// </summary>
public static class SlotTag
{
    /// <summary>The recurring focal figure of the scene.</summary>
    public static class Subject
    {
        /// <summary>The subject's face/identity — never text-expressible, out of mutation scope.</summary>
        public const string Identity = "subject.identity";

        /// <summary>The subject's clothing/garment, the primary ornament target.</summary>
        public const string Garment = "subject.garment";

        /// <summary>Items the subject carries or wears beyond the garment.</summary>
        public const string Props = "subject.props";
    }

    /// <summary>Standalone props in the scene.</summary>
    public static class Prop
    {
        /// <summary>Small dangling/decorative ornaments (charms, bells, beads).</summary>
        public const string Charms = "prop.charms";

        /// <summary>A held or placed instrument/tool.</summary>
        public const string Instrument = "prop.instrument";
    }

    /// <summary>Scene-shell decoration.</summary>
    public static class Scene
    {
        /// <summary>Plants, foliage, botanical fill.</summary>
        public const string Flora = "scene.flora";
    }

    /// <summary>Rendered literal text.</summary>
    public static class Text
    {
        /// <summary>A headline / primary text block.</summary>
        public const string Headline = "text.headline";
    }

    /// <summary>Every defined slot tag, for UI pickers and inference fallbacks.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Subject.Identity, Subject.Garment, Subject.Props,
        Prop.Charms, Prop.Instrument,
        Scene.Flora,
        Text.Headline
    ];
}
