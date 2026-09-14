using Inspectrol.Core.Catalog;
using Inspectrol.Core.Updates;

namespace Inspectrol.App.Wizard;

// Navigation only moves forward. Going back is offered only while neither the card nor the dashcam has been changed.
internal interface IWizard
{
    WizardSession Session { get; }

    void ShowCard(bool lookAgain);

    void ShowModelPicker();

    void ShowAppUpdate(AppUpdate update);

    void ChooseModel(CatalogModel model);

    void ShowPlan();

    void ShowRecordings();

    void ShowDownload();

    void ShowWrite();

    void ShowCarInstruction();

    // index is zero-based among the questions asked after the user returns from the car.
    void ShowDeviceAction(int index);

    // backToAction is the question to return to if the user reported a failure by mistake.
    void ShowFinish(bool success, string? problem, int? backToAction);

    // While a reason is set, the window refuses to close and shows it.
    void SetBusy(string? reason);

    // Null closes the window without asking.
    void SetCloseQuestion(string? question);

    void CloseProgram();
}
