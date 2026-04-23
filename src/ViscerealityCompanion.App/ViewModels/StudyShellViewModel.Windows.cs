using System.Windows;
using ViscerealityCompanion.App;

namespace ViscerealityCompanion.App.ViewModels;

public sealed partial class StudyShellViewModel
{
    private async Task OpenTwinEventsWindowAsync()
    {
        await DispatchAsync(() =>
        {
            if (_twinEventsWindow is { IsLoaded: true })
            {
                if (_twinEventsWindow.WindowState == WindowState.Minimized)
                {
                    _twinEventsWindow.WindowState = WindowState.Normal;
                }

                _twinEventsWindow.Activate();
                return;
            }

            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow;

            var window = new StudyTwinEventsWindow(this)
            {
                Owner = owner
            };
            window.Closed += OnTwinEventsWindowClosed;
            _twinEventsWindow = window;
            window.Show();
            window.Activate();
        }).ConfigureAwait(false);
    }

    private void OnTwinEventsWindowClosed(object? sender, EventArgs e)
    {
        if (_twinEventsWindow is not null)
        {
            _twinEventsWindow.Closed -= OnTwinEventsWindowClosed;
            _twinEventsWindow = null;
        }
    }

    private Task PreviousWorkflowGuideStepAsync()
    {
        WorkflowGuideStepIndex = Math.Max(0, WorkflowGuideStepIndex - 1);
        return Task.CompletedTask;
    }

    private Task NextWorkflowGuideStepAsync()
    {
        if (CanGoToNextWorkflowGuideStep)
        {
            WorkflowGuideStepIndex = Math.Min(WorkflowGuideCatalog.Length - 1, WorkflowGuideStepIndex + 1);
        }

        return Task.CompletedTask;
    }

    private Task CloseWorkflowGuideWindowAsync()
    {
        CloseWorkflowGuideWindow();
        return Task.CompletedTask;
    }

    private async Task OpenWorkflowGuideWindowAsync()
    {
        await DispatchAsync(() =>
        {
            if (_workflowGuideWindow is { IsLoaded: true })
            {
                if (_workflowGuideWindow.WindowState == WindowState.Minimized)
                {
                    _workflowGuideWindow.WindowState = WindowState.Normal;
                }

                _workflowGuideWindow.Activate();
                return;
            }

            WorkflowGuideStepIndex = 0;
            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
                ?? Application.Current?.MainWindow;

            var window = new StudyWorkflowGuideWindow(this)
            {
                Owner = owner
            };
            window.Closed += OnWorkflowGuideWindowClosed;
            _workflowGuideWindow = window;
            window.Show();
            window.Activate();
        }).ConfigureAwait(false);
    }

    private void OnWorkflowGuideWindowClosed(object? sender, EventArgs e)
    {
        if (_workflowGuideWindow is not null)
        {
            _workflowGuideWindow.Closed -= OnWorkflowGuideWindowClosed;
            _workflowGuideWindow = null;
        }
    }

    private void CloseWorkflowGuideWindow()
    {
        if (_workflowGuideWindow is null)
        {
            return;
        }

        _workflowGuideWindow.Closed -= OnWorkflowGuideWindowClosed;
        _workflowGuideWindow.Close();
        _workflowGuideWindow = null;
    }

    private async Task OpenExperimentSessionWindowAsync()
    {
        await DispatchAsync(() =>
        {
            if (_experimentSessionWindow is { IsLoaded: true })
            {
                if (_experimentSessionWindow.WindowState == WindowState.Minimized)
                {
                    _experimentSessionWindow.WindowState = WindowState.Normal;
                }

                _experimentSessionWindow.Activate();
                return;
            }

            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive)
                ?? _workflowGuideWindow
                ?? Application.Current?.MainWindow;

            var window = new StudyExperimentSessionWindow(this)
            {
                Owner = owner
            };
            window.Closed += OnExperimentSessionWindowClosed;
            _experimentSessionWindow = window;
            window.Show();
            window.Activate();
        }).ConfigureAwait(false);
    }

    private void OnExperimentSessionWindowClosed(object? sender, EventArgs e)
    {
        if (_experimentSessionWindow is not null)
        {
            _experimentSessionWindow.Closed -= OnExperimentSessionWindowClosed;
            _experimentSessionWindow = null;
        }
    }

    private void CloseExperimentSessionWindow()
    {
        if (_experimentSessionWindow is null)
        {
            return;
        }

        _experimentSessionWindow.Closed -= OnExperimentSessionWindowClosed;
        _experimentSessionWindow.Close();
        _experimentSessionWindow = null;
    }

    private void OpenClockAlignmentWindow()
    {
        if (_clockAlignmentWindow is { IsLoaded: true })
        {
            if (_clockAlignmentWindow.WindowState == WindowState.Minimized)
            {
                _clockAlignmentWindow.WindowState = WindowState.Normal;
            }

            _clockAlignmentWindow.Activate();
            return;
        }

        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? _workflowGuideWindow
            ?? Application.Current?.MainWindow;

        var window = new StudyClockAlignmentWindow(this)
        {
            Owner = owner
        };
        window.Closed += OnClockAlignmentWindowClosed;
        _clockAlignmentWindow = window;
        window.Show();
        window.Activate();
    }

    private void OnClockAlignmentWindowClosed(object? sender, EventArgs e)
    {
        if (_clockAlignmentWindow is not null)
        {
            _clockAlignmentWindow.Closed -= OnClockAlignmentWindowClosed;
            _clockAlignmentWindow = null;
        }
    }

    private void CloseClockAlignmentWindow()
    {
        if (_clockAlignmentWindow is null)
        {
            return;
        }

        _clockAlignmentWindow.Closed -= OnClockAlignmentWindowClosed;
        _clockAlignmentWindow.Close();
        _clockAlignmentWindow = null;
    }

    private void CloseTwinEventsWindow()
    {
        if (_twinEventsWindow is null)
        {
            return;
        }

        _twinEventsWindow.Closed -= OnTwinEventsWindowClosed;
        _twinEventsWindow.Close();
        _twinEventsWindow = null;
    }
}
