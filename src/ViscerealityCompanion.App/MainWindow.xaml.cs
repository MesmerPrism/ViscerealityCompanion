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
        _ = CheckForStartupUpdatesAsync();
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
    }

    private async Task CheckForStartupUpdatesAsync()
    {
        StartupUpdateDiagnostics.Write(
            $"Startup update check begin. Build={AppBuildIdentity.Current.ShortId}, IsPackaged={AppBuildIdentity.Current.IsPackaged}, Summary=\"{AppBuildIdentity.Current.Summary}\".");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var snapshot = await StartupUpdateService.CheckForUpdatesAsync(AppBuildIdentity.Current, cts.Token).ConfigureAwait(true);
            if (snapshot is null || !IsLoaded)
            {
                StartupUpdateDiagnostics.Write("Startup update check completed with no available updates.");
                return;
            }

            StartupUpdateDiagnostics.Write(
                $"Startup update dialog opening. AppUpdateAvailable={snapshot.App.UpdateAvailable}, CurrentVersion={snapshot.App.CurrentVersion ?? "n/a"}, AvailableVersion={snapshot.App.AvailableVersion ?? "n/a"}, ToolingUpdates={snapshot.HasToolingUpdates}.");
            var dialog = new StartupUpdateWindow(new StartupUpdateWindowViewModel(snapshot))
            {
                Owner = this
            };

            dialog.ShowDialog();
        }
        catch (OperationCanceledException)
        {
            StartupUpdateDiagnostics.Write("Startup update check timed out.");
        }
        catch (Exception exception)
        {
            StartupUpdateDiagnostics.Write($"Startup update check failed: {exception}");
        }
    }
}
