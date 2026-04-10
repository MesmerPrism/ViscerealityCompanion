using System.Diagnostics;
using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

internal sealed class StartupUpdateWindowViewModel : ObservableObject
{
    private readonly StartupUpdateSnapshot _snapshot;
    private bool _isBusy;
    private bool _isCompleted;
    private string _summary;
    private string _detail;
    private string _progressStatus = string.Empty;
    private string _progressDetail = string.Empty;
    private int _progressPercent;
    private string _primaryActionLabel = "Update now";
    private bool _showDismissAction = true;

    public StartupUpdateWindowViewModel(StartupUpdateSnapshot snapshot)
    {
        _snapshot = snapshot;
        _summary = BuildInitialSummary(snapshot);
        _detail = BuildInitialDetail(snapshot);

        PrimaryActionCommand = new AsyncRelayCommand(ExecutePrimaryActionAsync, () => !IsBusy);
        DismissActionCommand = new AsyncRelayCommand(DismissAsync, () => !IsBusy);
        OpenReleasesCommand = new AsyncRelayCommand(OpenReleasesAsync, () => !IsBusy);
    }

    public event EventHandler? RequestClose;

    public AsyncRelayCommand PrimaryActionCommand { get; }
    public AsyncRelayCommand DismissActionCommand { get; }
    public AsyncRelayCommand OpenReleasesCommand { get; }

    public string WindowTitle => "Companion Updates";

    public string Heading
        => _snapshot.App.UpdateAvailable && _snapshot.HasToolingUpdates
            ? "App and tooling updates are available"
            : _snapshot.App.UpdateAvailable
                ? "A Windows package update is available"
                : "Official Quest tooling updates are available";

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowProgress));
                PrimaryActionCommand.RaiseCanExecuteChanged();
                DismissActionCommand.RaiseCanExecuteChanged();
                OpenReleasesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetProperty(ref _isCompleted, value);
    }

    public bool ShowProgress => IsBusy;
    public bool ShowDismissAction => _showDismissAction;
    public bool ShowOpenReleasesAction => _snapshot.App.IsApplicable;

    public string PrimaryActionLabel
    {
        get => _primaryActionLabel;
        private set => SetProperty(ref _primaryActionLabel, value);
    }

    public string ProgressStatus
    {
        get => _progressStatus;
        private set => SetProperty(ref _progressStatus, value);
    }

    public string ProgressDetail
    {
        get => _progressDetail;
        private set => SetProperty(ref _progressDetail, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool ShowAppSection => _snapshot.App.IsApplicable;
    public bool AppUpdateAvailable => _snapshot.App.UpdateAvailable;
    public string AppStatusLabel => _snapshot.App.UpdateAvailable ? "Update ready" : "Current";
    public string AppCurrentVersion => _snapshot.App.CurrentVersion ?? "n/a";
    public string AppAvailableVersion => _snapshot.App.AvailableVersion ?? "n/a";
    public string AppDetail => _snapshot.App.Detail;

    public bool HzdbUpdateAvailable => _snapshot.Tooling.Hzdb.UpdateAvailable;
    public string HzdbStatusLabel => _snapshot.Tooling.Hzdb.UpdateAvailable ? "Update ready" : "Current";
    public string HzdbCurrentVersion => _snapshot.Tooling.Hzdb.InstalledVersion ?? "n/a";
    public string HzdbAvailableVersion => _snapshot.Tooling.Hzdb.AvailableVersion ?? "n/a";
    public string HzdbDetail => $"Managed path: {_snapshot.Tooling.Hzdb.InstallPath}";

    public bool PlatformToolsUpdateAvailable => _snapshot.Tooling.PlatformTools.UpdateAvailable;
    public string PlatformToolsStatusLabel => _snapshot.Tooling.PlatformTools.UpdateAvailable ? "Update ready" : "Current";
    public string PlatformToolsCurrentVersion => _snapshot.Tooling.PlatformTools.InstalledVersion ?? "n/a";
    public string PlatformToolsAvailableVersion => _snapshot.Tooling.PlatformTools.AvailableVersion ?? "n/a";
    public string PlatformToolsDetail => $"Managed path: {_snapshot.Tooling.PlatformTools.InstallPath}";

    private async Task ExecutePrimaryActionAsync()
    {
        if (IsCompleted)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsBusy = true;
        ProgressPercent = 5;
        ProgressStatus = "Preparing updates";
        ProgressDetail = _snapshot.App.UpdateAvailable
            ? "The companion will refresh the managed official Quest tooling cache first, then open Windows App Installer for the app package step."
            : "The companion will refresh the managed official Quest tooling cache in place.";

        string toolingMessage;
        try
        {
            if (_snapshot.HasToolingUpdates)
            {
                using var tooling = new OfficialQuestToolingService();
                var toolingProgress = new Progress<OfficialQuestToolingProgress>(update =>
                {
                    ProgressStatus = update.Status;
                    ProgressDetail = update.Detail;
                    ProgressPercent = Math.Clamp(update.PercentComplete, 5, 95);
                });

                var toolingResult = await tooling.InstallOrUpdateAsync(toolingProgress).ConfigureAwait(true);
                toolingMessage = toolingResult.Detail;
            }
            else
            {
                toolingMessage = "Official Quest tooling was already current.";
            }

            if (_snapshot.App.UpdateAvailable)
            {
                ProgressStatus = "Opening Windows App Installer";
                ProgressDetail = "Downloading the latest .appinstaller file from GitHub Releases and handing off to Windows App Installer.";
                ProgressPercent = 96;

                using var publishedUpdates = new PublishedPreviewUpdateService();
                var appInstallerPath = await publishedUpdates.DownloadLatestAppInstallerAsync().ConfigureAwait(true);
                Process.Start(new ProcessStartInfo
                {
                    FileName = appInstallerPath,
                    UseShellExecute = true
                });

                Summary = "Windows App Installer opened";
                Detail = $"{toolingMessage} Continue the app update in Windows App Installer.";
            }
            else
            {
                Summary = "Official Quest tooling updated";
                Detail = toolingMessage;
            }

            IsCompleted = true;
            PrimaryActionLabel = "Close";
            _showDismissAction = false;
            OnPropertyChanged(nameof(ShowDismissAction));
        }
        catch (Exception exception)
        {
            Summary = "Update failed";
            Detail = exception.Message;
            ProgressStatus = "Update failed";
            ProgressDetail = exception.Message;
            ProgressPercent = 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task DismissAsync()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private Task OpenReleasesAsync()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _snapshot.App.ReleasePageUri,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private static string BuildInitialSummary(StartupUpdateSnapshot snapshot)
    {
        if (snapshot.App.UpdateAvailable && snapshot.HasToolingUpdates)
        {
            return "A newer Windows package and newer official Quest tooling are available.";
        }

        if (snapshot.App.UpdateAvailable)
        {
            return "A newer packaged Windows release is available for this install.";
        }

        return "Meta hzdb and/or Android platform-tools can be refreshed from their published upstream sources.";
    }

    private static string BuildInitialDetail(StartupUpdateSnapshot snapshot)
        => snapshot.App.UpdateAvailable
            ? "Update now refreshes the managed official Quest tooling cache first and then opens Windows App Installer for the Windows package step."
            : "Update now refreshes the managed official Quest tooling cache in place without leaving the app.";
}
