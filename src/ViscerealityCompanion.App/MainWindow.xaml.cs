using System.Windows;
using ViscerealityCompanion.App.ViewModels;

namespace ViscerealityCompanion.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        WindowThemeHelper.Attach(this);
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _ = CheckForStartupUpdatesAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }

    private async Task CheckForStartupUpdatesAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var snapshot = await StartupUpdateService.CheckForUpdatesAsync(AppBuildIdentity.Current, cts.Token).ConfigureAwait(true);
            if (snapshot is null || !IsLoaded)
            {
                return;
            }

            var dialog = new StartupUpdateWindow(new StartupUpdateWindowViewModel(snapshot))
            {
                Owner = this
            };

            dialog.ShowDialog();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }
}
