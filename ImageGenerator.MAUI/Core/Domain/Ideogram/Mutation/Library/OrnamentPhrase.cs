namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// One injectable ornament fragment plus its drop-priority tier. <c>DescBudget</c> uses
/// <see cref="Category"/> to decide which phrases survive the 60-word cap — no NLP, the category is
/// authored into the kit.
/// </summary>
/// <param name="Text">The phrase to splice into an element desc (e.g. "crossed by thin leather harness straps").</param>
/// <param name="Category">Drop-priority tier under the word budget.</param>
public sealed record OrnamentPhrase(string Text, DescBudgetCategory Category);
