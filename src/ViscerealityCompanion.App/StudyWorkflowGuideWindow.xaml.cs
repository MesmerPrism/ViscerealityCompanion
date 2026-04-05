using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public partial class StudyWorkflowGuideWindow : Window
{
    private readonly StudyShellViewModel _viewModel;

    public StudyWorkflowGuideWindow(StudyShellViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
        DataContext = viewModel;
        Title = $"{viewModel.StudyLabel} Sequential Guide";
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StudyShellViewModel.ValidationCaptureRunning)
            && _viewModel.ValidationCaptureRunning
            && _viewModel.WorkflowGuideShowsValidationCaptureState)
        {
            Dispatcher.BeginInvoke(
                new Action(() => ValidationTimingPanel.BringIntoView()),
                DispatcherPriority.Background);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnClosed;
    }
}
