using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ViscerealityCompanion.PreviewInstaller;

internal sealed class InstallerStatusForm : Form
{
    private readonly Func<IProgress<InstallerProgressUpdate>, CancellationToken, Task<InstallerCompletionResult>> _installAsync;
    private readonly string _releasePageUri;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly PictureBox _logoBox;
    private readonly Label _eyebrowLabel;
    private readonly Label _titleLabel;
    private readonly Label _summaryLabel;
    private readonly Label _detailLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _footerLabel;
    private readonly Button _openReleaseButton;
    private readonly Button _retryButton;
    private readonly Button _closeButton;
    private bool _started;
    private string? _lastAppInstallerPath;

    public InstallerStatusForm(
        Func<IProgress<InstallerProgressUpdate>, CancellationToken, Task<InstallerCompletionResult>> installAsync,
        string releasePageUri)
    {
        _installAsync = installAsync ?? throw new ArgumentNullException(nameof(installAsync));
        _releasePageUri = releasePageUri ?? throw new ArgumentNullException(nameof(releasePageUri));

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(6, 10, 16);
        ForeColor = Color.White;
        ClientSize = new Size(760, 470);
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(24);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Viscereality Companion Guided Setup";

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 16, 26),
            Padding = new Padding(28)
        };
        panel.Paint += OnPanelPaint;
        Controls.Add(panel);

        _logoBox = new PictureBox
        {
            Dock = DockStyle.Top,
            Height = 150,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = LoadEmbeddedLogo()
        };
        panel.Controls.Add(_logoBox);

        _eyebrowLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 28,
            ForeColor = Color.FromArgb(52, 210, 255),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "SUSSEX-FOCUSED RESEARCH PREVIEW",
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(_eyebrowLabel);

        _titleLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 42,
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            Text = "Installing Viscereality Companion",
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(_titleLabel);

        _summaryLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.White,
            Text = "Preparing guided setup",
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(_summaryLabel);

        _detailLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 72,
            Font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(204, 214, 232),
            Text = "The bootstrapper will trust the preview certificate and then open Windows App Installer for the final install step.",
            TextAlign = ContentAlignment.TopLeft
        };
        panel.Controls.Add(_detailLabel);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 14,
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 5,
            Margin = new Padding(0, 0, 0, 18)
        };
        panel.Controls.Add(_progressBar);

        _footerLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 60,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(148, 166, 196),
            Text = "After this window opens Windows App Installer, confirm the update there. This bootstrapper only stages the certificate and package metadata.",
            TextAlign = ContentAlignment.TopLeft
        };
        panel.Controls.Add(_footerLabel);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        panel.Controls.Add(buttonRow);

        _closeButton = BuildButton("Close", Color.FromArgb(20, 27, 40), Color.FromArgb(26, 180, 255));
        _closeButton.Click += (_, _) => Close();
        _closeButton.Visible = false;
        buttonRow.Controls.Add(_closeButton);

        _retryButton = BuildButton("Open App Installer Again", Color.FromArgb(20, 27, 40), Color.FromArgb(26, 180, 255));
        _retryButton.Click += (_, _) => RetryOpenAppInstaller();
        _retryButton.Visible = false;
        buttonRow.Controls.Add(_retryButton);

        _openReleaseButton = BuildButton("Open Releases", Color.FromArgb(20, 27, 40), Color.FromArgb(255, 103, 177));
        _openReleaseButton.Click += (_, _) => OpenReleasePage();
        _openReleaseButton.Visible = false;
        buttonRow.Controls.Add(_openReleaseButton);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_started)
        {
            return;
        }

        _started = true;
        _ = RunInstallAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _logoBox.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task RunInstallAsync()
    {
        var progress = new Progress<InstallerProgressUpdate>(ApplyProgressUpdate);

        try
        {
            var result = await _installAsync(progress, _cancellation.Token).ConfigureAwait(true);
            _lastAppInstallerPath = result.AppInstallerPath;
            _summaryLabel.Text = "Windows App Installer opened";
            _detailLabel.Text = "Finish the install or update in the Windows App Installer window. If that window did not appear, you can reopen it from here.";
            _footerLabel.Text = "The package metadata is already staged. This bootstrapper does not perform the final install itself.";
            _retryButton.Visible = true;
            _closeButton.Visible = true;
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception exception)
        {
            _summaryLabel.Text = "Guided setup could not finish";
            _summaryLabel.ForeColor = Color.FromArgb(255, 124, 191);
            _detailLabel.Text = exception.Message;
            _footerLabel.Text = "If the latest preview release is not reachable yet, open the GitHub releases page and download the certificate and MSIX manually.";
            _progressBar.Value = 0;
            _openReleaseButton.Visible = true;
            _closeButton.Visible = true;
        }
    }

    private void ApplyProgressUpdate(InstallerProgressUpdate update)
    {
        _summaryLabel.ForeColor = Color.White;
        _summaryLabel.Text = update.Status;
        _detailLabel.Text = update.Detail;
        _progressBar.Value = Math.Clamp(update.PercentComplete, _progressBar.Minimum, _progressBar.Maximum);
    }

    private void RetryOpenAppInstaller()
    {
        if (string.IsNullOrWhiteSpace(_lastAppInstallerPath) || !File.Exists(_lastAppInstallerPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _lastAppInstallerPath,
            UseShellExecute = true
        });
    }

    private void OpenReleasePage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _releasePageUri,
            UseShellExecute = true
        });
    }

    private static Button BuildButton(string text, Color backColor, Color borderColor)
    {
        var button = new Button
        {
            AutoSize = true,
            BackColor = backColor,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Height = 38,
            Margin = new Padding(10, 0, 0, 0),
            Padding = new Padding(14, 0, 14, 0),
            Text = text
        };
        button.FlatAppearance.BorderColor = borderColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 40, 58);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(26, 36, 52);
        return button;
    }

    private static Image? LoadEmbeddedLogo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("ViscerealityCompanion.PreviewInstaller.Assets.viscereality-wordmark.png");
        return stream is null ? null : Image.FromStream(stream);
    }

    private static void OnPanelPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel)
        {
            return;
        }

        using var outlinePen = new Pen(Color.FromArgb(26, 180, 255), 1.6f);
        using var accentPen = new Pen(Color.FromArgb(255, 103, 177), 1.2f);
        var outlineRect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
        var accentRect = new Rectangle(10, 10, panel.Width - 21, panel.Height - 21);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(outlinePen, outlineRect);
        e.Graphics.DrawRectangle(accentPen, accentRect);
    }
}
