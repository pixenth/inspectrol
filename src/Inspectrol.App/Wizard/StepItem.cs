namespace Inspectrol.App.Wizard;

internal enum StepState { Done, Current, Upcoming }

internal sealed record StepItem(int Number, string Title, StepState State, string Detail)
{
    public bool HasDetail => Detail.Length > 0;

    // Screen readers announce list items by their string form, which for a record lists every property.
    public override string ToString() => Title;
}
