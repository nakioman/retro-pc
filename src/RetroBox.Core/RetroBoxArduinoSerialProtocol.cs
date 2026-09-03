namespace RetroBox.Core;

public abstract record RetroBoxArduinoSerialEvent;

public sealed record RetroBoxArduinoInsertEvent(string Id, string Mode) : RetroBoxArduinoSerialEvent;

public sealed record RetroBoxArduinoEjectEvent : RetroBoxArduinoSerialEvent;

public sealed record RetroBoxArduinoInitEvent(string Version) : RetroBoxArduinoSerialEvent;

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
    private const string InitPrefix = "INIT ";
    private const string InsertPrefix = "INSERT ";
    private const string ErrorPrefix = "ERROR ";

    public const int DefaultBaudRate = 115200;

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

        if (trimmedLine.StartsWith(InitPrefix, StringComparison.Ordinal))
        {
            return ParseInitEvent(trimmedLine[InitPrefix.Length..]);
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

    public static string BuildPingCommand()
    {
        return "PING";
    }

    public static string BuildStatusCommand()
    {
        return "STATUS";
    }

    public static string BuildTagIdCommand()
    {
        return "TAGID";
    }

    public static NfcResponse ParseResponse(string? line)
    {
        var trimmedLine = line?.Trim();
        if (string.IsNullOrEmpty(trimmedLine))
        {
            return new NfcResponse.Unknown(line);
        }

        if (trimmedLine == "PONG")
        {
            return new NfcResponse.Pong();
        }

        if (trimmedLine == "OK")
        {
            return new NfcResponse.Ok();
        }

        const string TagIdPrefix = "Tag ID: ";
        if (trimmedLine.StartsWith(TagIdPrefix, StringComparison.Ordinal))
        {
            return new NfcResponse.TagId(trimmedLine[TagIdPrefix.Length..]);
        }

        const string ErrorPrefix = "ERROR ";
        if (trimmedLine.StartsWith(ErrorPrefix, StringComparison.Ordinal))
        {
            return new NfcResponse.Error(trimmedLine[ErrorPrefix.Length..]);
        }

        return new NfcResponse.Unknown(line);
    }

    private static RetroBoxArduinoInitEvent ParseInitEvent(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new RetroBoxArduinoSerialProtocolException("Arduino INIT event version is required.");
        }

        return new RetroBoxArduinoInitEvent(version.Trim());
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

public abstract record NfcResponse
{
    public sealed record Pong() : NfcResponse;
    public sealed record Ok() : NfcResponse;
    public sealed record TagId(string Uid) : NfcResponse;
    public sealed record Error(string Message) : NfcResponse;
    public sealed record Unknown(string? Line) : NfcResponse;
}
