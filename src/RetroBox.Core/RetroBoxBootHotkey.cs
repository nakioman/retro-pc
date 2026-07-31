namespace RetroBox.Core;

public interface IRetroBoxConsoleInput
{
    bool KeyAvailable { get; }

    ConsoleKeyInfo ReadKey(bool intercept);
}

public interface IRetroBoxBootClock
{
    DateTimeOffset Now { get; }

    void Sleep(TimeSpan duration);
}

public interface IRetroBoxBootHotkeyDetector
{
    bool IsSelectorRequested();
}

public sealed class RetroBoxConsoleInput : IRetroBoxConsoleInput
{
    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
}

public sealed class RetroBoxBootClock : IRetroBoxBootClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public void Sleep(TimeSpan duration) => Thread.Sleep(duration);
}

public sealed class RetroBoxBootHotkeyDetector(
    IRetroBoxConsoleInput input,
    IRetroBoxBootClock clock) : IRetroBoxBootHotkeyDetector
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    public bool IsSelectorRequested()
    {
        var deadline = clock.Now + Window;
        while (clock.Now < deadline)
        {
            bool keyAvailable;
            try
            {
                keyAvailable = input.KeyAvailable;
            }
            catch (InvalidOperationException)
            {
                // Non-interactive invocations (pipes, tests, and CI) have no console key stream.
                return false;
            }

            if (keyAvailable)
            {
                try
                {
                    if (input.ReadKey(intercept: true).Key == ConsoleKey.F12)
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    return false;
                }

                continue;
            }

            var remaining = deadline - clock.Now;
            clock.Sleep(remaining > TimeSpan.FromMilliseconds(10)
                ? TimeSpan.FromMilliseconds(10)
                : remaining);
        }

        return false;
    }
}
