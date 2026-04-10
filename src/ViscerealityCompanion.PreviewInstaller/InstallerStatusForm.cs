using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ViscerealityCompanion.PreviewInstaller;

internal sealed class InstallerStatusForm : Form
{
    private static readonly Color AppBackgroundColor = Color.FromArgb(0, 0, 0);
    private static readonly Color ShellBackgroundColor = Color.FromArgb(2, 6, 11);
    private static readonly Color StatusPanelBackgroundColor = Color.FromArgb(4, 29, 41);
    private static readonly Color StatusPanelSuccessBackgroundColor = Color.FromArgb(3, 29, 17);
    private static readonly Color StatusPanelFailureBackgroundColor = Color.FromArgb(38, 6, 20);
    private static readonly Color LineColor = Color.FromArgb(24, 69, 106);
    private static readonly Color InkColor = Color.FromArgb(250, 253, 255);
    private static readonly Color MutedColor = Color.FromArgb(208, 219, 234);
    private static readonly Color AccentColor = Color.FromArgb(0, 232, 255);
    private static readonly Color AccentSoftColor = Color.FromArgb(4, 29, 41);
    private static readonly Color SuccessColor = Color.FromArgb(0, 255, 153);
    private static readonly Color FailureColor = Color.FromArgb(255, 77, 152);
    private static readonly Color ButtonBackgroundColor = Color.FromArgb(4, 11, 18);
    private static readonly Color ButtonHoverColor = Color.FromArgb(14, 24, 36);

    private readonly Func<IProgress<InstallerProgressUpdate>, CancellationToken, Task<InstallerCompletionResult>> _installAsync;
    private readonly string _releasePageUri;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Panel _shellPanel;
    private readonly Panel _statusPanel;
    private readonly Panel _titleBarPanel;
    private readonly PictureBox _logoBox;
    private readonly Label _titleLabel;
    private readonly Label _introLabel;
    private readonly Label _windowTitleLabel;
    private readonly Label _summaryLabel;
    private readonly Label _detailLabel;
    private readonly Panel _progressTrack;
    private readonly Panel _progressFill;
    private readonly Label _progressValueLabel;
    private readonly Label _footerLabel;
    private readonly Button _titleBarCloseButton;
    private readonly Button _openReleaseButton;
    private readonly Button _retryButton;
    private readonly Button _closeButton;

    private int _disposeState;
    private bool _started;
    private string? _lastAppInstallerPath;
    private int _progressPercent = 5;
    private Color _statusAccentColor = AccentColor;

    public InstallerStatusForm(
        Func<IProgress<InstallerProgressUpdate>, CancellationToken, Task<InstallerCompletionResult>> installAsync,
        string releasePageUri)
    {
        _installAsync = installAsync ?? throw new ArgumentNullException(nameof(installAsync));
        _releasePageUri = releasePageUri ?? throw new ArgumentNullException(nameof(releasePageUri));

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = LineColor;
        ClientSize = new Size(900, 560);
        DoubleBuffered = true;
        Font = CreateUiFont(10.5f, FontStyle.Regular);
        FormBorderStyle = FormBorderStyle.None;
        ForeColor = InkColor;
        MinimumSize = new Size(900, 560);
        Padding = new Padding(1);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Viscereality Companion Preview Setup";
        var framePanel = new Panel
        {
            BackColor = AppBackgroundColor,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 16)
        };
        Controls.Add(framePanel);

        _titleBarCloseButton = new Button
        {
            BackColor = AppBackgroundColor,
            Dock = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            Font = CreateUiFont(10f, FontStyle.Bold),
            ForeColor = InkColor,
            Margin = new Padding(0),
            TabStop = false,
            Text = "X",
            Width = 46
        };
        _titleBarCloseButton.FlatAppearance.BorderSize = 0;
        _titleBarCloseButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(64, 18, 28);
        _titleBarCloseButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 14, 22);
        _titleBarCloseButton.Click += (_, _) => Close();

        _windowTitleLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            Font = CreateUiFont(10.5f, FontStyle.Regular),
            ForeColor = MutedColor,
            Margin = new Padding(0),
            Padding = new Padding(0, 12, 0, 0),
            Text = Text
        };

        _titleBarPanel = new Panel
        {
            BackColor = AppBackgroundColor,
            Dock = DockStyle.Top,
            Height = 42,
            Margin = new Padding(0),
            Padding = new Padding(14, 0, 0, 0)
        };
        _titleBarPanel.Controls.Add(_windowTitleLabel);
        _titleBarPanel.Controls.Add(_titleBarCloseButton);
        _titleBarPanel.MouseDown += (_, e) => BeginWindowDrag(e);
        _windowTitleLabel.MouseDown += (_, e) => BeginWindowDrag(e);
        _titleBarPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(LineColor, 1f);
            e.Graphics.DrawLine(pen, 0, _titleBarPanel.Height - 1, _titleBarPanel.Width, _titleBarPanel.Height - 1);
        };
        framePanel.Controls.Add(_titleBarPanel);

        _shellPanel = new Panel
        {
            AutoScroll = true,
            BackColor = ShellBackgroundColor,
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 20, 22, 20)
        };
        _shellPanel.Paint += (_, e) => DrawPanelBorder(e.Graphics, _shellPanel.ClientRectangle, LineColor);
        framePanel.Controls.Add(_shellPanel);

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Margin = new Padding(0),
            WrapContents = false
        };

        _closeButton = BuildButton("Close", isPrimary: false, AccentColor);
        _closeButton.Click += (_, _) => Close();
        _closeButton.Visible = false;
        buttonRow.Controls.Add(_closeButton);

        _retryButton = BuildButton("Open App Installer Again", isPrimary: true, AccentColor);
        _retryButton.Click += (_, _) => RetryOpenAppInstaller();
        _retryButton.Visible = false;
        buttonRow.Controls.Add(_retryButton);

        _openReleaseButton = BuildButton("Open Releases", isPrimary: false, AccentColor);
        _openReleaseButton.Click += (_, _) => OpenReleasePage();
        _openReleaseButton.Visible = false;
        buttonRow.Controls.Add(_openReleaseButton);

        _shellPanel.Controls.Add(buttonRow);

        _statusPanel = new Panel
        {
            BackColor = StatusPanelBackgroundColor,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(18, 16, 18, 16)
        };
        _statusPanel.Paint += (_, e) => DrawPanelBorder(e.Graphics, _statusPanel.ClientRectangle, _statusAccentColor);
        _shellPanel.Controls.Add(_statusPanel);

        var statusLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0),
            RowCount = 4
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _statusPanel.Controls.Add(statusLayout);

        _summaryLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = CreateUiFont(18f, FontStyle.Bold),
            ForeColor = InkColor,
            Margin = new Padding(0, 0, 0, 8),
            Text = "Preparing guided setup"
        };
        statusLayout.Controls.Add(_summaryLabel, 0, 0);

        _detailLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = CreateUiFont(11.5f, FontStyle.Regular),
            ForeColor = MutedColor,
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(780, 0),
            Text = "The bootstrapper stages the latest public preview, refreshes the official Quest tooling cache from Meta and Google, and then opens Windows App Installer for the final install step."
        };
        statusLayout.Controls.Add(_detailLabel, 0, 1);

        var progressRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0),
            RowCount = 1
        };
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusLayout.Controls.Add(progressRow, 0, 2);

        _progressTrack = new Panel
        {
            BackColor = AccentSoftColor,
            Dock = DockStyle.Top,
            Height = 18,
            Margin = new Padding(0, 2, 12, 0),
            Padding = new Padding(1)
        };
        _progressTrack.Paint += (_, e) => DrawTrackBorder(e.Graphics, _progressTrack.ClientRectangle, _statusAccentColor);
        progressRow.Controls.Add(_progressTrack, 0, 0);

        _progressFill = new Panel
        {
            BackColor = AccentColor,
            Dock = DockStyle.Left,
            Width = 0
        };
        _progressTrack.Controls.Add(_progressFill);

        _progressValueLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Right,
            Font = CreateUiFont(11f, FontStyle.Bold),
            ForeColor = AccentColor,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 0),
            Text = "5%"
        };
        progressRow.Controls.Add(_progressValueLabel, 1, 0);
        _progressTrack.Resize += (_, _) => UpdateProgressBarFill();

        _footerLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = CreateUiFont(10f, FontStyle.Regular),
            ForeColor = MutedColor,
            Margin = new Padding(0),
            MaximumSize = new Size(780, 0),
            Text = "After this window opens Windows App Installer, confirm the update there. This helper also refreshes the managed LocalAppData cache for the official Meta hzdb and Android platform-tools downloads when those sources are reachable."
        };
        statusLayout.Controls.Add(_footerLabel, 0, 3);

        var headerPanel = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 16)
        };
        _shellPanel.Controls.Add(headerPanel);

        var headerLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            Padding = new Padding(0),
            RowCount = 3
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        headerPanel.Controls.Add(headerLayout);

        _logoBox = new PictureBox
        {
            Anchor = AnchorStyles.Left,
            Height = 54,
            Image = LoadEmbeddedLogo(),
            Margin = new Padding(0, 0, 0, 12),
            SizeMode = PictureBoxSizeMode.Zoom
        };

        _introLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = CreateUiFont(11f, FontStyle.Regular),
            ForeColor = MutedColor,
            Margin = new Padding(0),
            MaximumSize = new Size(800, 0),
            Text = "This installer stages the latest public preview, refreshes the official Quest tooling cache, and then hands off to Windows App Installer for the final install or update.",
            TextAlign = ContentAlignment.TopLeft
        };

        _titleLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = CreateUiFont(28f, FontStyle.Bold),
            ForeColor = InkColor,
            Margin = new Padding(0, 8, 0, 8),
            MaximumSize = new Size(800, 0),
            Text = "Install Or Update Viscereality Companion",
            TextAlign = ContentAlignment.MiddleLeft
        };
        headerLayout.Controls.Add(_logoBox, 0, 0);
        headerLayout.Controls.Add(_titleLabel, 0, 1);
        headerLayout.Controls.Add(_introLabel, 0, 2);
        UpdateProgressBarFill();
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
        if (disposing && Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

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
            ApplyStatusVisuals(StatusPanelSuccessBackgroundColor, SuccessColor, InkColor);
            _summaryLabel.Text = "Windows App Installer opened";
            _detailLabel.Text = "Finish the install or update in the Windows App Installer window. If that window did not appear, you can reopen it from here.";
            _footerLabel.Text = string.IsNullOrWhiteSpace(result.ToolingWarning)
                ? "The package metadata and managed official Quest tooling cache are already staged. This helper does not perform the final install itself."
                : $"{result.ToolingWarning} You can rerun the helper later or use `viscereality tooling install-official` after the app is installed.";
            _retryButton.Visible = true;
            _closeButton.Visible = true;
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception exception)
        {
            ApplyStatusVisuals(StatusPanelFailureBackgroundColor, FailureColor, FailureColor);
            _summaryLabel.Text = "Guided setup could not finish";
            _detailLabel.Text = exception.Message;
            _footerLabel.Text = "If the latest preview release is not reachable yet, open the GitHub releases page and download the certificate and MSIX manually.";
            _progressPercent = 0;
            UpdateProgressBarFill();
            _openReleaseButton.Visible = true;
            _closeButton.Visible = true;
        }
    }

    private void ApplyProgressUpdate(InstallerProgressUpdate update)
    {
        ApplyStatusVisuals(StatusPanelBackgroundColor, AccentColor, InkColor);
        _summaryLabel.Text = update.Status;
        _detailLabel.Text = update.Detail;
        _progressPercent = Math.Clamp(update.PercentComplete, 0, 100);
        UpdateProgressBarFill();
    }

    private void ApplyStatusVisuals(Color panelBackColor, Color accentColor, Color summaryColor)
    {
        _statusPanel.BackColor = panelBackColor;
        _statusAccentColor = accentColor;
        _summaryLabel.ForeColor = summaryColor;
        _progressFill.BackColor = accentColor;
        _progressValueLabel.ForeColor = accentColor;
        _statusPanel.Invalidate();
        _progressTrack.Invalidate();
    }

    private void UpdateProgressBarFill()
    {
        if (_progressFill is null || _progressValueLabel is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, _progressTrack.ClientSize.Width - _progressTrack.Padding.Horizontal);
        var fillWidth = (int)Math.Round(availableWidth * (_progressPercent / 100d));
        _progressFill.Width = Math.Clamp(fillWidth, 0, availableWidth);
        _progressValueLabel.Text = $"{_progressPercent}%";
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

    private static Button BuildButton(string text, bool isPrimary, Color accentColor)
    {
        var button = new Button
        {
            AutoSize = true,
            BackColor = isPrimary ? accentColor : ButtonBackgroundColor,
            FlatStyle = FlatStyle.Flat,
            Font = CreateUiFont(10f, FontStyle.Bold),
            ForeColor = isPrimary ? AppBackgroundColor : InkColor,
            Height = 38,
            Margin = new Padding(10, 0, 0, 0),
            Padding = new Padding(14, 0, 14, 0),
            Text = text
        };

        button.FlatAppearance.BorderColor = accentColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseDownBackColor = isPrimary ? ControlPaint.Dark(accentColor, 0.2f) : ButtonHoverColor;
        button.FlatAppearance.MouseOverBackColor = isPrimary ? ControlPaint.Light(accentColor, 0.08f) : ButtonHoverColor;
        return button;
    }

    private static Font CreateUiFont(float size, FontStyle style)
    {
        try
        {
            return new Font("Bahnschrift Condensed", size, style, GraphicsUnit.Point);
        }
        catch (ArgumentException)
        {
            var fallbackFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
            return new Font(fallbackFont.FontFamily, size, style, GraphicsUnit.Point);
        }
    }

    private static Image? LoadEmbeddedLogo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("ViscerealityCompanion.PreviewInstaller.Assets.viscereality-wordmark.png");
        return stream is null ? null : Image.FromStream(stream);
    }

    private static void DrawPanelBorder(Graphics graphics, Rectangle bounds, Color borderColor)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var borderRectangle = Rectangle.Inflate(bounds, -1, -1);
        using var path = CreateRoundedRectanglePath(borderRectangle, 8);
        using var pen = new Pen(borderColor, 1.2f);
        graphics.DrawPath(pen, path);

        using var accentBrush = new SolidBrush(borderColor);
        graphics.FillRectangle(accentBrush, borderRectangle.Left + 1, borderRectangle.Top + 1, Math.Max(0, borderRectangle.Width - 2), 2);
    }

    private static void DrawTrackBorder(Graphics graphics, Rectangle bounds, Color accentColor)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var borderRectangle = Rectangle.Inflate(bounds, -1, -1);
        using var path = CreateRoundedRectanglePath(borderRectangle, 5);
        using var pen = new Pen(accentColor, 1f);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void BeginWindowDrag(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        _ = SendMessage(Handle, WindowMessageNonClientLeftButtonDown, HitTestCaption, 0);
    }

    private const int WindowMessageNonClientLeftButtonDown = 0xA1;
    private const int HitTestCaption = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, int message, int wParam, int lParam);
}
