using System.Diagnostics;

namespace RetroBox.Core;

public sealed record RetroBoxProcessSpec(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Emulators and their tools print diagnostics to stdout; discarding it keeps the appliance console clean.</summary>
    public bool DiscardStandardOutput { get; init; }
}

public interface IRetroBoxProcessRunner
{
    /// <summary>Runs the process to completion and returns its exit code.</summary>
    int Run(RetroBoxProcessSpec spec);

    /// <summary>Starts the process in the background; disposing the handle stops it.</summary>
    IDisposable Start(RetroBoxProcessSpec spec);
}

public sealed class RetroBoxProcessRunner : IRetroBoxProcessRunner
{
    public int Run(RetroBoxProcessSpec spec)
    {
        using var process = StartProcess(spec);
        var stdout = spec.DiscardStandardOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult(string.Empty);
        process.WaitForExit();
        _ = stdout.GetAwaiter().GetResult();
        return process.ExitCode;
    }

    public IDisposable Start(RetroBoxProcessSpec spec)
    {
        var process = StartProcess(spec);
        if (spec.DiscardStandardOutput)
        {
            _ = process.StandardOutput.ReadToEndAsync();
        }

        return new BackgroundProcess(process);
    }

    private static Process StartProcess(RetroBoxProcessSpec spec)
    {
        var startInfo = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = spec.DiscardStandardOutput,
        };
        if (spec.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = spec.WorkingDirectory;
        }

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in spec.Environment)
        {
            startInfo.Environment[name] = value;
        }

        return Process.Start(startInfo)
            ?? throw new IOException($"Could not start '{spec.FileName}'.");
    }

    private sealed class BackgroundProcess(Process process) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
