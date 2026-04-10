using System.ComponentModel;
using System.Windows;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

partial class StartupUpdateWindow : Window
{
    private readonly StartupUpdateWindowViewModel _viewModel;

    internal StartupUpdateWindow(StartupUpdateWindowViewModel viewModel)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        _viewModel = viewModel;
        _viewModel.RequestClose += OnRequestClose;
        DataContext = _viewModel;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void OnRequestClose(object? sender, EventArgs e)
        => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }
}
