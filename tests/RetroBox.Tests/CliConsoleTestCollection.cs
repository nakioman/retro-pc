namespace RetroBox.Tests;

// Console.In/Console.Out/Console.Error are process-global statics, and xUnit runs different
// test classes in parallel by default. Every class here invokes a CliCommandFactory command
// through Invoke() without redirecting every stream on every call (CliHelpSmokeTests goes
// further and redirects Console.In/Console.Error for the duration of a test), so a CLI
// invocation from one class can read or write another class's console state mid-test. Sharing
// this collection makes xUnit run their tests sequentially against each other - collections
// are xUnit's unit of parallelism, so this is the standard fix for cross-test global state.
[CollectionDefinition(Name)]
public sealed class CliConsoleTestCollection
{
    public const string Name = "CLI console tests";
}
