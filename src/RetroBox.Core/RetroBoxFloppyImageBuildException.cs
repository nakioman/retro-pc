namespace RetroBox.Core;

public sealed class RetroBoxFloppyImageBuildException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
