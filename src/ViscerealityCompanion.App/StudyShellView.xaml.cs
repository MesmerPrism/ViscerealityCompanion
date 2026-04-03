using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public partial class StudyShellView : UserControl
{
    public StudyShellView()
    {
        InitializeComponent();
    }

    private void OnVisualApplyCurrentSessionClick(object sender, RoutedEventArgs e)
        => ExecuteVisualCommand(sender, VisualProfilesTable, static viewModel => viewModel.ApplySelectedCommand);

    private void OnVisualSaveStartupSnapshotClick(object sender, RoutedEventArgs e)
        => ExecuteVisualCommand(sender, VisualProfilesTable, static viewModel => viewModel.SetStartupProfileCommand);

    private void OnControllerApplyCurrentSessionClick(object sender, RoutedEventArgs e)
        => ExecuteControllerCommand(sender, ControllerProfilesTable, static viewModel => viewModel.ApplySelectedCommand);

    private void OnControllerSaveStartupSnapshotClick(object sender, RoutedEventArgs e)
        => ExecuteControllerCommand(sender, ControllerProfilesTable, static viewModel => viewModel.SetStartupProfileCommand);

    private void OnVisualProfilesTablePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (RootScrollViewer is null)
        {
            return;
        }

        e.Handled = true;
        var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        };
        RootScrollViewer.RaiseEvent(forwardedEvent);
    }

    private static void CommitGridEdits(DataGrid? grid)
    {
        if (grid is null)
        {
            return;
        }

        switch (Keyboard.FocusedElement)
        {
            case TextBox textBox:
                BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
                break;
            case ToggleButton toggleButton:
                BindingOperations.GetBindingExpression(toggleButton, ToggleButton.IsCheckedProperty)?.UpdateSource();
                break;
        }

        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        Keyboard.ClearFocus();
    }

    private static void ExecuteVisualCommand(
        object sender,
        DataGrid? grid,
        Func<SussexVisualProfilesWorkspaceViewModel, ICommand> resolveCommand)
    {
        CommitGridEdits(grid);
        var viewModel = grid?.DataContext as SussexVisualProfilesWorkspaceViewModel
            ?? (sender as FrameworkElement)?.DataContext as SussexVisualProfilesWorkspaceViewModel;
        if (viewModel is null)
        {
            return;
        }

        var command = resolveCommand(viewModel);
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static void ExecuteControllerCommand(
        object sender,
        DataGrid? grid,
        Func<SussexControllerBreathingProfilesWorkspaceViewModel, ICommand> resolveCommand)
    {
        CommitGridEdits(grid);
        var viewModel = grid?.DataContext as SussexControllerBreathingProfilesWorkspaceViewModel
            ?? (sender as FrameworkElement)?.DataContext as SussexControllerBreathingProfilesWorkspaceViewModel;
        if (viewModel is null)
        {
            return;
        }

        var command = resolveCommand(viewModel);
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
