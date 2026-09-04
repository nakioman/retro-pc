namespace RetroBox.Core;

public class RetroBoxCatalogException : Exception
{
    public RetroBoxCatalogException(string message)
        : base(message)
    {
    }

    public RetroBoxCatalogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
