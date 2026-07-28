namespace RetroBox.Core;

public abstract record RetroBoxArduinoSerialEvent;

public sealed record RetroBoxArduinoInsertEvent(string Id, string Mode) : RetroBoxArduinoSerialEvent;

public sealed record RetroBoxArduinoEjectEvent : RetroBoxArduinoSerialEvent;

public sealed record RetroBoxArduinoErrorEvent(string Message) : RetroBoxArduinoSerialEvent;

public sealed class RetroBoxArduinoSerialProtocolException : Exception
{
    public RetroBoxArduinoSerialProtocolException(string message)
        : base(message)
    {
    }
}

public static class RetroBoxArduinoSerialProtocol
{
    private const string InsertPrefix = "INSERT ";
    private const string ErrorPrefix = "ERROR ";

    public static RetroBoxArduinoSerialEvent ParseEvent(string? line)
    {
        var trimmedLine = line?.Trim();
        if (string.IsNullOrEmpty(trimmedLine))
        {
            throw new RetroBoxArduinoSerialProtocolException("Arduino serial event is required.");
        }

        if (trimmedLine == "EJECT")
        {
            return new RetroBoxArduinoEjectEvent();
        }

        if (trimmedLine.StartsWith(InsertPrefix, StringComparison.Ordinal))
        {
            return ParseInsertEvent(trimmedLine[InsertPrefix.Length..]);
        }

        if (trimmedLine.StartsWith(ErrorPrefix, StringComparison.Ordinal))
        {
            return ParseErrorEvent(trimmedLine[ErrorPrefix.Length..]);
        }

        throw new RetroBoxArduinoSerialProtocolException($"Malformed Arduino serial event '{trimmedLine}'.");
    }

    public static string BuildWriteCommand(string id, string mode)
    {
        RequireValidId(id);
        RequireValidMode(mode, id);

        return $"WRITE {id},{mode}";
    }

    public static string BuildReadCommand()
    {
        return "READ";
    }

    private static RetroBoxArduinoInsertEvent ParseInsertEvent(string payload)
    {
        var commaIndex = payload.IndexOf(',');
        var id = payload;
        var mode = RetroBoxFloppyCatalogRules.ReadOnlyMode;

        if (commaIndex >= 0)
        {
            var modePayload = payload[(commaIndex + 1)..];
            if (modePayload.Contains(','))
            {
                throw new RetroBoxArduinoSerialProtocolException($"Malformed Arduino INSERT event '{payload}'.");
            }

            id = payload[..commaIndex];
            mode = modePayload;
        }

        RequireValidId(id);
        RequireValidMode(mode, id);

        return new RetroBoxArduinoInsertEvent(id, mode);
    }

    private static RetroBoxArduinoErrorEvent ParseErrorEvent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new RetroBoxArduinoSerialProtocolException("Arduino ERROR event message is required.");
        }

        return new RetroBoxArduinoErrorEvent(message);
    }

    private static void RequireValidId(string id)
    {
        if (RetroBoxCatalogRules.IsValidId(id))
        {
            return;
        }

        throw new RetroBoxArduinoSerialProtocolException(
            $"Arduino floppy ID '{id}' must contain only lowercase ASCII letters, digits, and single hyphens, and must start and end with a letter or digit.");
    }

    private static void RequireValidMode(string mode, string id)
    {
        if (RetroBoxFloppyCatalogRules.IsValidMode(mode))
        {
            return;
        }

        throw new RetroBoxArduinoSerialProtocolException(
            $"Invalid Arduino floppy mode '{mode}' for floppy '{id}'.");
    }
}
