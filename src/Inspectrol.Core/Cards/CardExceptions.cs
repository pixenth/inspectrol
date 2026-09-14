namespace Inspectrol.Core.Cards;

public sealed class CardNeedsAdministratorException(string message) : Exception(message);

public sealed class CardNotRemovableException(string message) : Exception(message);
