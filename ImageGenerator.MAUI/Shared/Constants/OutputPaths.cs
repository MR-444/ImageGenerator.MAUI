namespace ImageGenerator.MAUI.Shared.Constants;

public static class OutputPaths
{
    public const string FolderName = "ImageGenerator.MAUI";

    // The user's Pictures folder so generated images are easy to find and survive app rebuilds.
    public static string GeneratedImagesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), FolderName);
}
