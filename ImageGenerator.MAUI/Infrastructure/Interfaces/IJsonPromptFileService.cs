namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

public interface IJsonPromptFileService
{
    /// <summary>
    /// Writes <paramref name="prettyJson"/> as a .json file into the json-prompts export
    /// directory (created on demand). <paramref name="descriptionForName"/> seeds the
    /// filename; collisions get a _1/_2 suffix. Returns the full path written.
    /// </summary>
    Task<string> SaveAsync(string descriptionForName, string prettyJson, CancellationToken ct = default);
}
