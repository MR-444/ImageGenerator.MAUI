namespace ImageGenerator.MAUI.Tests;

// OutputPaths.SetRootOverride mutates a process-global static. Tests that touch it
// share this collection so xUnit never runs them concurrently with each other — without this,
// one class's override could leak into another's enumeration mid-run. No other test reads
// OutputPaths, so serialising just these classes is sufficient.
[CollectionDefinition("OutputPathsState")]
public sealed class OutputPathsStateCollection { }
