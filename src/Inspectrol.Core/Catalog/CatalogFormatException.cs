namespace Inspectrol.Core.Catalog;

// The message is shown to the user.
public sealed class CatalogFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);
