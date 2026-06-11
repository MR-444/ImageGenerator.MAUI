namespace ImageGenerator.MAUI.Core.Domain.ValueObjects;

/// <summary>
/// A live progress update from a generation service to the running job card. Message lands in
/// the card's status label; Percent (0..1) drives its determinate progress bar when present
/// (null = message-only update, the bar keeps its last value / stays hidden).
/// </summary>
public sealed record JobProgress(string Message, double? Percent = null);
