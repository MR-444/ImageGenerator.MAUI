namespace ImageGenerator.MAUI.Shared.Constants;

public static class OutputPaths
{
    public const string FolderName = "ImageGenerator.MAUI";

    // The user's Pictures folder so generated images are easy to find and survive app rebuilds.
    public static string GeneratedImagesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), FolderName);

    // Exported Ideogram structured prompts (pretty-printed .json, portable to the official
    // API / a local Ideogram install). Lives next to the images for the same survive-rebuilds reason.
    public static string JsonPromptsDirectory =>
        Path.Combine(GeneratedImagesDirectory, "json-prompts");
}
