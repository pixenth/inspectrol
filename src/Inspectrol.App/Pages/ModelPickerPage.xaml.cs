using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Catalog;

namespace Inspectrol.App.Pages;

public partial class ModelPickerPage : UserControl
{
    private sealed record Row(CatalogModel Model, string Name, string Note, bool CanBeChosen);

    private readonly IWizard _wizard;
    private readonly IReadOnlyList<Row> _rows;

    internal ModelPickerPage(IWizard wizard)
    {
        InitializeComponent();
        _wizard = wizard;

        _rows = [.. (wizard.Session.Models ?? [])
            .Where(model => !IsNotADevice(model))
            .OrderBy(model => model.Name, StringComparer.CurrentCulture)
            .Select(ToRow)];

        Show(_rows);

        SecondaryButton.Assign(Strings.Common_Back, () => wizard.ShowCard(lookAgain: false));
        PrimaryButton.Assign(Strings.ModelPickerPage_ThisIsMyModel, Accept, enabled: false);

        Loaded += (_, _) => SearchBox.Focus();
    }

    private static Row ToRow(CatalogModel model)
    {
        var hasDatabase = model.Updates.Any(update => update.Kind == UpdateKind.Database);
        var hasFirmware = model.Updates.Any(update => update.Kind == UpdateKind.Firmware);

        if (!hasDatabase && !hasFirmware)
        {
            return new Row(model, model.Name,
                Strings.ModelPickerPage_NoCardUpdates, false);
        }

        var what = (hasDatabase, hasFirmware) switch
        {
            (true, true) => Strings.ModelPickerPage_DatabaseAndFirmware,
            (true, false) => Strings.ModelPickerPage_DatabaseOnly,
            _ => Strings.ModelPickerPage_FirmwareOnly,
        };

        return new Row(model, model.Name, what, true);
    }

    // The support page lists "APP-файлы" among the models, although it holds app installers.
    private static bool IsNotADevice(CatalogModel model) =>
        model.Name.Contains("APP", StringComparison.OrdinalIgnoreCase)
        && !model.Updates.Any(update => update.Kind is UpdateKind.Database or UpdateKind.Firmware);

    private void Show(IEnumerable<Row> rows) => ModelList.ItemsSource = rows.ToList();

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text.Trim();
        Show(text.Length == 0
            ? _rows
            : _rows.Where(row => row.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        PrimaryButton.IsEnabled = ModelList.SelectedItem is Row { CanBeChosen: true };

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ModelList.SelectedItem is Row { CanBeChosen: true })
            Accept();
    }

    private void Accept()
    {
        if (ModelList.SelectedItem is Row { CanBeChosen: true } row)
            _wizard.ChooseModel(row.Model);
    }
}
