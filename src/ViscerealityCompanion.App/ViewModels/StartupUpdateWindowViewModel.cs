using ViscerealityCompanion.Core.Services;

namespace ViscerealityCompanion.App.ViewModels;

internal sealed class StartupUpdateWindowViewModel : ObservableObject
{
    private readonly Func<IProgress<OfficialQuestToolingProgress>?, CancellationToken, Task<OfficialQuestToolingInstallResult>> _installToolingAsync;
    private readonly Func<string, IProgress<PackagedAppUpdateProgress>?, CancellationToken, Task> _applyPackagedAppUpdateAsync;
    private PublishedAppUpdateStatus _appStatus;
    private OfficialQuestToolingStatus _toolingStatus;
    private bool _isBusy;
    private bool _isCompleted;
    private string _heading;
    private string _summary;
    private string _detail;
    private string _progressStatus = string.Empty;
    private string _progressDetail = string.Empty;
    private int _progressPercent;
    private string _primaryActionLabel;
    private bool _showDismissAction = true;

    public StartupUpdateWindowViewModel(StartupUpdateSnapshot snapshot)
    {
        _appStatus = snapshot.App;
        _toolingStatus = snapshot.Tooling;
        _installToolingAsync = async (progress, cancellationToken) =>
        {
            using var tooling = new OfficialQuestToolingService();
            return await tooling.InstallOrUpdateAsync(progress, cancellationToken).ConfigureAwait(false);
        };
        _applyPackagedAppUpdateAsync = (appInstallerUri, progress, cancellationToken) =>
            new PackagedAppUpdateInstaller().ApplyPublishedUpdateAsync(appInstallerUri, progress, cancellationToken);
        _heading = BuildInitialHeading(snapshot);
        _summary = BuildInitialSummary(snapshot);
        _detail = BuildInitialDetail(snapshot);
        _primaryActionLabel = BuildInitialPrimaryActionLabel(snapshot);

        PrimaryActionCommand = new AsyncRelayCommand(ExecutePrimaryActionAsync, () => !IsBusy);
        DismissActionCommand = new AsyncRelayCommand(DismissAsync, () => !IsBusy);
        OpenReleasesCommand = new AsyncRelayCommand(OpenReleasesAsync, () => !IsBusy);
    }

    internal StartupUpdateWindowViewModel(
        StartupUpdateSnapshot snapshot,
        Func<IProgress<OfficialQuestToolingProgress>?, CancellationToken, Task<OfficialQuestToolingInstallResult>> installToolingAsync,
        Func<string, IProgress<PackagedAppUpdateProgress>?, CancellationToken, Task> applyPackagedAppUpdateAsync)
    {
        _appStatus = snapshot.App;
        _toolingStatus = snapshot.Tooling;
        _installToolingAsync = installToolingAsync ?? throw new ArgumentNullException(nameof(installToolingAsync));
        _applyPackagedAppUpdateAsync = applyPackagedAppUpdateAsync ?? throw new ArgumentNullException(nameof(applyPackagedAppUpdateAsync));
        _heading = BuildInitialHeading(snapshot);
        _summary = BuildInitialSummary(snapshot);
        _detail = BuildInitialDetail(snapshot);
        _primaryActionLabel = BuildInitialPrimaryActionLabel(snapshot);

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
    {
        get => _heading;
        private set => SetProperty(ref _heading, value);
    }

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
    public bool ShowOpenReleasesAction => _appStatus.IsApplicable;

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

    public bool ShowAppSection => _appStatus.IsApplicable;
    public bool AppUpdateAvailable => _appStatus.UpdateAvailable;
    public bool AppRequiresGuidedInstaller => _appStatus.RequiresGuidedInstaller;
    public string AppStatusLabel => _appStatus.RequiresGuidedInstaller ? "Use installer" : _appStatus.UpdateAvailable ? "Update ready" : "Current";
    public string AppCurrentVersion => _appStatus.CurrentVersion ?? "n/a";
    public string AppAvailableVersion => _appStatus.AvailableVersion ?? "n/a";
    public string AppDetail => _appStatus.Detail;

    public bool HzdbUpdateAvailable => _toolingStatus.Hzdb.UpdateAvailable;
    public string HzdbStatusLabel => _toolingStatus.Hzdb.UpdateAvailable ? "Update ready" : "Current";
    public string HzdbCurrentVersion => _toolingStatus.Hzdb.InstalledVersion ?? "n/a";
    public string HzdbAvailableVersion => _toolingStatus.Hzdb.AvailableVersion ?? "n/a";
    public string HzdbDetail => $"Managed path: {_toolingStatus.Hzdb.InstallPath}";

    public bool PlatformToolsUpdateAvailable => _toolingStatus.PlatformTools.UpdateAvailable;
    public string PlatformToolsStatusLabel => _toolingStatus.PlatformTools.UpdateAvailable ? "Update ready" : "Current";
    public string PlatformToolsCurrentVersion => _toolingStatus.PlatformTools.InstalledVersion ?? "n/a";
    public string PlatformToolsAvailableVersion => _toolingStatus.PlatformTools.AvailableVersion ?? "n/a";
    public string PlatformToolsDetail => $"Managed path: {_toolingStatus.PlatformTools.InstallPath}";

    private async Task ExecutePrimaryActionAsync()
    {
        if (IsCompleted)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_appStatus.RequiresGuidedInstaller &&
            !_toolingStatus.Hzdb.UpdateAvailable &&
            !_toolingStatus.PlatformTools.UpdateAvailable)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsBusy = true;
        ProgressPercent = 5;
        ProgressStatus = "Preparing updates";
        ProgressDetail = _appStatus.RequiresGuidedInstaller
            ? "The companion will refresh the managed official Quest tooling cache. Finish the Windows package migration from the guided installer or the manual certificate + App Installer path afterward."
            : _appStatus.UpdateAvailable
            ? "The companion will refresh the managed official Quest tooling cache first, then install the published Windows package directly without opening the App Installer UI."
            : "The companion will refresh the managed official Quest tooling cache in place.";

        string toolingMessage;
        try
        {
            if (_toolingStatus.Hzdb.UpdateAvailable || _toolingStatus.PlatformTools.UpdateAvailable)
            {
                var toolingProgress = new Progress<OfficialQuestToolingProgress>(update =>
                {
                    ProgressStatus = update.Status;
                    ProgressDetail = update.Detail;
                    ProgressPercent = Math.Clamp(update.PercentComplete, 5, 95);
                });

                var toolingResult = await _installToolingAsync(toolingProgress, CancellationToken.None).ConfigureAwait(true);
                ApplyToolingStatus(toolingResult.Status);
                toolingMessage = toolingResult.Detail;
            }
            else
            {
                toolingMessage = "Official Quest tooling was already current.";
            }

            if (_appStatus.UpdateAvailable)
            {
                ProgressStatus = "Installing Windows package update";
                ProgressDetail = "Downloading and staging the published MSIX update directly from the App Installer feed.";
                ProgressPercent = 96;

                var appProgress = new Progress<PackagedAppUpdateProgress>(update =>
                {
                    ProgressStatus = update.Status;
                    ProgressDetail = update.Detail;
                    ProgressPercent = Math.Clamp(update.PercentComplete, 1, 99);
                });

                await _applyPackagedAppUpdateAsync(_appStatus.AppInstallerUri, appProgress, CancellationToken.None)
                    .ConfigureAwait(true);
                ApplyPublishedAppUpdateStatus();

                Heading = "Windows package update applied";
                Summary = "Windows package update applied";
                Detail = $"{toolingMessage} If the updated app does not reopen automatically, launch it again from Start.";
            }
            else if (_appStatus.RequiresGuidedInstaller)
            {
                Heading = "Tooling updated";
                Summary = "Windows package migration still needs the guided installer.";
                Detail = $"{toolingMessage} {_appStatus.Detail}";
            }
            else
            {
                Heading = "Official Quest tooling updated";
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
            Heading = "Update failed";
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
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _appStatus.ReleasePageUri,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private void ApplyToolingStatus(OfficialQuestToolingStatus status)
    {
        _toolingStatus = status;
        RefreshDisplayedStatusProperties();
    }

    private void ApplyPublishedAppUpdateStatus()
    {
        var currentVersion = _appStatus.AvailableVersion ?? _appStatus.CurrentVersion;
        _appStatus = _appStatus with
        {
            CurrentVersion = currentVersion,
            UpdateAvailable = false,
            RequiresGuidedInstaller = false,
            Summary = "Windows package is current.",
            Detail = $"Installed package {currentVersion ?? "n/a"} matches the latest published .appinstaller metadata."
        };
        RefreshDisplayedStatusProperties();
    }

    private void RefreshDisplayedStatusProperties()
    {
        OnPropertyChanged(nameof(ShowAppSection));
        OnPropertyChanged(nameof(ShowOpenReleasesAction));
        OnPropertyChanged(nameof(AppUpdateAvailable));
        OnPropertyChanged(nameof(AppRequiresGuidedInstaller));
        OnPropertyChanged(nameof(AppStatusLabel));
        OnPropertyChanged(nameof(AppCurrentVersion));
        OnPropertyChanged(nameof(AppAvailableVersion));
        OnPropertyChanged(nameof(AppDetail));
        OnPropertyChanged(nameof(HzdbUpdateAvailable));
        OnPropertyChanged(nameof(HzdbStatusLabel));
        OnPropertyChanged(nameof(HzdbCurrentVersion));
        OnPropertyChanged(nameof(HzdbAvailableVersion));
        OnPropertyChanged(nameof(HzdbDetail));
        OnPropertyChanged(nameof(PlatformToolsUpdateAvailable));
        OnPropertyChanged(nameof(PlatformToolsStatusLabel));
        OnPropertyChanged(nameof(PlatformToolsCurrentVersion));
        OnPropertyChanged(nameof(PlatformToolsAvailableVersion));
        OnPropertyChanged(nameof(PlatformToolsDetail));
    }

    private static string BuildInitialHeading(StartupUpdateSnapshot snapshot)
        => snapshot.App.RequiresGuidedInstaller && snapshot.HasToolingUpdates
            ? "App migration and tooling updates are available"
            : snapshot.App.RequiresGuidedInstaller
                ? "A Windows package migration is available"
            : snapshot.App.UpdateAvailable && snapshot.HasToolingUpdates
            ? "App and tooling updates are available"
            : snapshot.App.UpdateAvailable
                ? "A Windows package update is available"
                : "Official Quest tooling updates are available";

    private static string BuildInitialSummary(StartupUpdateSnapshot snapshot)
    {
        if (snapshot.App.RequiresGuidedInstaller && snapshot.HasToolingUpdates)
        {
            return "A newer public Windows package is available on a different package family, and newer official Quest tooling is available.";
        }

        if (snapshot.App.RequiresGuidedInstaller)
        {
            return "This installed package cannot self-update to the current public release. Use the guided installer or the manual App Installer path once.";
        }

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
        => snapshot.App.RequiresGuidedInstaller
            ? "The current packaged install is on a different package family than the published release. Use Open Releases for the guided installer, or use the manual certificate + App Installer path."
            : snapshot.App.UpdateAvailable
            ? "Update now refreshes the managed official Quest tooling cache first and then installs the Windows package directly from the published App Installer feed."
            : "Update now refreshes the managed official Quest tooling cache in place without leaving the app.";

    private static string BuildInitialPrimaryActionLabel(StartupUpdateSnapshot snapshot)
        => snapshot.App.RequiresGuidedInstaller && !snapshot.HasToolingUpdates
            ? "Close"
            : snapshot.App.RequiresGuidedInstaller
                ? "Update tooling"
                : "Update now";
}
