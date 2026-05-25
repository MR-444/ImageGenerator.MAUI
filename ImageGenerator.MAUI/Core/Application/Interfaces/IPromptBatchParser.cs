namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Parses a textfile of image prompts into a list. Prompts are separated by a blank
/// line, a '#' comment line, or a line containing only "---". Comment and blank content
/// is dropped, never emitted; empty chunks are dropped. Consecutive non-blank lines are
/// joined into a single multi-line prompt. Throws <see cref="PromptBatchTooLargeException"/>
/// when the parsed count exceeds <see cref="PromptBatchParserLimits.MaxPromptsPerBatch"/>.
/// </summary>
public interface IPromptBatchParser
{
    IReadOnlyList<string> Parse(string fileContents);
}

public static class PromptBatchParserLimits
{
    // Hard cap to protect against picking the wrong file by accident. Stated user range
    // is 10–60; 100 leaves headroom without enabling runaway batches.
    public const int MaxPromptsPerBatch = 100;
}

public sealed class PromptBatchTooLargeException : Exception
{
    public int PromptCount { get; }
    public int MaxAllowed { get; }

    public PromptBatchTooLargeException(int promptCount, int maxAllowed)
        : base($"Prompt file contains {promptCount} prompts, exceeding the cap of {maxAllowed}.")
    {
        PromptCount = promptCount;
        MaxAllowed = maxAllowed;
    }
}
