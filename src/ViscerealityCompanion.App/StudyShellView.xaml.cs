using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    private void OnVisualSaveAsNewProfileClick(object sender, RoutedEventArgs e)
        => ExecuteVisualCommand(sender, VisualProfilesTable, static viewModel => viewModel.SaveAsNewProfileCommand);

    private void OnVisualSaveSelectedProfileClick(object sender, RoutedEventArgs e)
        => ExecuteVisualCommand(sender, VisualProfilesTable, static viewModel => viewModel.SaveSelectedCommand);

    private void OnVisualSaveStartupSnapshotClick(object sender, RoutedEventArgs e)
        => ExecuteVisualCommand(sender, VisualProfilesTable, static viewModel => viewModel.SetStartupProfileCommand);

    private void OnControllerApplyCurrentSessionClick(object sender, RoutedEventArgs e)
        => ExecuteControllerCommand(sender, ControllerProfilesTable, static viewModel => viewModel.ApplySelectedCommand);

    private void OnControllerSaveStartupSnapshotClick(object sender, RoutedEventArgs e)
        => ExecuteControllerCommand(sender, ControllerProfilesTable, static viewModel => viewModel.SetStartupProfileCommand);

    private void OnStudyPhaseTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabControl ||
            !ReferenceEquals(e.OriginalSource, tabControl) ||
            !ReferenceEquals(tabControl.SelectedItem, ConditionsTab))
        {
            return;
        }

        Dispatcher.BeginInvoke(
            new Action(() => RefreshConditionProfileOptions(ConditionsTab)),
            DispatcherPriority.ContextIdle);
    }

    private void OnProfileBooleanToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
        {
            return;
        }

        checkBox.Dispatcher.BeginInvoke(() =>
        {
            BindingOperations.GetBindingExpression(checkBox, ToggleButton.IsCheckedProperty)?.UpdateSource();
            var grid = FindAncestor<DataGrid>(checkBox);
            grid?.CommitEdit(DataGridEditingUnit.Cell, true);
            grid?.CommitEdit(DataGridEditingUnit.Row, true);
        }, DispatcherPriority.Background);
    }

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

    private static void RefreshConditionProfileOptions(object sender)
    {
        var viewModel = (sender as FrameworkElement)?.DataContext as SussexConditionWorkspaceViewModel;
        if (viewModel?.RefreshCommand.CanExecute(null) == true)
        {
            viewModel.RefreshCommand.Execute(null);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? node)
        where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }
}
