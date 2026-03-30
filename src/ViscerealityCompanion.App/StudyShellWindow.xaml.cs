using System.Windows;
using ViscerealityCompanion.App.ViewModels;
using ViscerealityCompanion.Core.Models;

namespace ViscerealityCompanion.App;

public partial class StudyShellWindow : Window
{
    private readonly StudyShellViewModel _viewModel;

    public StudyShellWindow(StudyShellDefinition study)
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        _viewModel = new StudyShellViewModel(study);
        DataContext = _viewModel;
        Title = $"{study.Label} Study Shell";
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }
}
