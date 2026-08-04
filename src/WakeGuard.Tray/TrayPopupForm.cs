using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WakeGuard.Contracts;
using Timer = System.Windows.Forms.Timer;

namespace WakeGuard.Tray;

internal sealed class TrayPopupForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcRound = 2;
    private const int CsDropShadow = 0x00020000;
    private const int WsExToolWindow = 0x00000080;

    private readonly Actions _actions;
    private readonly PictureBox _appIcon;
    private readonly Label _statusLabel;
    private readonly Panel _headerDivider;
    private readonly Label _modeHeading;
    private readonly PopupButton _inactiveButton;
    private readonly PopupButton _keepAwakeButton;
    private readonly PopupButton _displayOnButton;
    private readonly Label _durationHeading;
    private readonly PopupButton _unlimitedButton;
    private readonly Dictionary<PopupButton, TimeSpan> _durationButtons = [];
    private readonly Label _actionHeading;
    private readonly HelpIconButton _actionHelpButton;
    private readonly PopupButton _lockButton;
    private readonly PopupButton _screenSaverButton;
    private readonly ToolTip _actionHelpTip;
    private readonly Timer _countdownTimer;
    private readonly Timer _pendingFeedbackTimer;
    private Bitmap? _appIconImage;
    private TrayIconFactory.IconState _appIconState;
    private State _state;
    private State _reportedState;
    private Palette _palette;
    private bool _wakeCommandPending;
    private bool _showWakeCommandProgress;
    private string _wakeCommandProgressText = string.Empty;

    internal readonly record struct Actions(
        Func<WakeMode, Task> SetModeAsync,
        Func<TimeSpan?, Task> SetDurationAsync,
        Func<Task> LockAsync,
        Func<Task> StartScreenSaverAsync);

    internal readonly record struct State(
        string StatusText,
        WakeMode Mode,
        DateTimeOffset? StopAtUtc,
        TimeSpan? SelectedDuration,
        bool ServiceConnected);

    internal TrayPopupForm(Actions actions)
    {
        _actions = actions;
        _palette = Palette.Create();

        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = _palette.Background;
        ClientSize = new Size(620, 490);
        DoubleBuffered = true;
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "WakeGuardPanel";
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "WakeGuard";
        TopMost = true;

        _appIcon = new PictureBox
        {
            AccessibleDescription = "WakeGuard",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 4, 3),
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false,
        };
        _statusLabel = CreateLabel(UiText.Current.PopupConnecting, 10F);
        _headerDivider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            Margin = new Padding(0, 8, 0, 9),
        };

        _modeHeading = CreateLabel(string.Empty, 9F, FontStyle.Bold);
        _inactiveButton = CreateButton(string.Empty);
        _keepAwakeButton = CreateButton(string.Empty);
        _displayOnButton = CreateButton(string.Empty);
        _inactiveButton.Click += async (_, _) => await SetModeAsync(WakeMode.Inactive);
        _keepAwakeButton.Click += async (_, _) => await SetModeAsync(WakeMode.KeepAwake);
        _displayOnButton.Click += async (_, _) => await SetModeAsync(WakeMode.KeepAwakeAndDisplayOn);

        _durationHeading = CreateLabel(string.Empty, 9F, FontStyle.Bold);
        _unlimitedButton = CreateButton(string.Empty);
        _unlimitedButton.Click += async (_, _) => await SetDurationAsync(null);
        AddDurationButton(string.Empty, TimeSpan.FromMinutes(30));
        AddDurationButton(string.Empty, TimeSpan.FromHours(1));
        AddDurationButton(string.Empty, TimeSpan.FromHours(2));
        AddDurationButton(string.Empty, TimeSpan.FromHours(4));

        _actionHeading = CreateLabel(string.Empty, 9F, FontStyle.Bold);
        _actionHeading.AutoEllipsis = false;
        _actionHeading.AutoSize = true;
        _actionHeading.Anchor = AnchorStyles.Left;
        _actionHeading.Dock = DockStyle.None;
        _actionHelpButton = new HelpIconButton
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 6.5F, FontStyle.Bold),
            Margin = Padding.Empty,
        };
        _actionHelpTip = new ToolTip
        {
            AutoPopDelay = 5_000,
            InitialDelay = 0,
            ReshowDelay = 0,
            ShowAlways = true,
        };
        _actionHelpButton.Click += (_, _) => _actionHelpTip.Show(
            UiText.Current.ActionHelpText,
            _actionHelpButton,
            0,
            _actionHelpButton.Height + 4,
            5_000);
        _lockButton = CreateButton(string.Empty);
        _screenSaverButton = CreateButton(string.Empty);
        _lockButton.Click += LockButtonClick;
        _screenSaverButton.Click += ScreenSaverButtonClick;

        var header = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(_appIcon, 0, 0);
        header.Controls.Add(_statusLabel, 1, 0);

        var modeButtons = CreateButtonRow(_inactiveButton, _keepAwakeButton, _displayOnButton);
        var durationButtons = CreateButtonRow([_unlimitedButton, .. _durationButtons.Keys]);
        var actionButtons = CreateButtonRow(_lockButton, _screenSaverButton);
        var actionHeading = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1,
        };
        actionHeading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        actionHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionHeading.Controls.Add(_actionHeading, 0, 0);
        actionHeading.Controls.Add(_actionHelpButton, 1, 0);
        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(20, 18, 20, 20),
            RowCount = 10,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_headerDivider, 0, 1);
        layout.Controls.Add(_modeHeading, 0, 2);
        layout.Controls.Add(modeButtons, 0, 3);
        layout.Controls.Add(_durationHeading, 0, 5);
        layout.Controls.Add(durationButtons, 0, 6);
        layout.Controls.Add(actionHeading, 0, 8);
        layout.Controls.Add(actionButtons, 0, 9);
        Controls.Add(layout);

        _countdownTimer = new Timer { Interval = 1_000 };
        _countdownTimer.Tick += (_, _) => UpdateHeaderText();
        _pendingFeedbackTimer = new Timer { Interval = 3_000 };
        _pendingFeedbackTimer.Tick += (_, _) =>
        {
            _pendingFeedbackTimer.Stop();
            if (!_wakeCommandPending)
            {
                return;
            }

            _showWakeCommandProgress = true;
            UpdateHeaderText();
        };
        Deactivate += (_, _) => Hide();
        VisibleChanged += (_, _) => _countdownTimer.Enabled = Visible;

        ApplyLocalization();
        ApplyTheme();
        ApplyState();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            parameters.ExStyle |= WsExToolWindow;
            return parameters;
        }
    }

    internal void ShowAtCursor(State state)
    {
        ApplyTheme();
        UpdateState(state);

        if (!Visible)
        {
            // Prepare a complete frame off-screen so child controls never appear in stages.
            Opacity = 0;
            Show();
            PositionNearCursor();
            PerformLayout();
            Refresh();
            Opacity = 1;
        }
        else
        {
            PositionNearCursor();
        }

        Activate();
        BringToFront();
    }

    internal void UpdateState(State state)
    {
        _reportedState = state;
        if (!_wakeCommandPending || !state.ServiceConnected)
        {
            _state = state;
        }

        ApplyState();
    }

    internal void ApplyLocalization()
    {
        var text = UiText.Current;
        _modeHeading.Text = text.ModeHeading;
        _inactiveButton.Text = text.ModeInactive;
        _keepAwakeButton.Text = text.ModeKeepAwake;
        _displayOnButton.Text = text.ModeDisplayOn;
        _inactiveButton.AccessibleDescription = text.ModeInactiveDescription;
        _keepAwakeButton.AccessibleDescription = text.ModeKeepAwakeDescription;
        _displayOnButton.AccessibleDescription = text.ModeDisplayOnDescription;
        _durationHeading.Text = text.DurationHeading;
        _unlimitedButton.Text = text.DurationUnlimited;
        foreach (var (button, duration) in _durationButtons)
        {
            button.Text = duration switch
            {
                var value when value == TimeSpan.FromMinutes(30) => text.Duration30Minutes,
                var value when value == TimeSpan.FromHours(1) => text.Duration1Hour,
                var value when value == TimeSpan.FromHours(2) => text.Duration2Hours,
                _ => text.Duration4Hours,
            };
        }

        _actionHeading.Text = text.ActionHeading;
        _actionHelpButton.AccessibleDescription = text.ActionHelpAccessible;
        _lockButton.Text = text.LockComputer;
        _screenSaverButton.Text = text.StartScreenSaver;
        _lockButton.AccessibleDescription = text.LockComputerDescription;
        _screenSaverButton.AccessibleDescription = text.StartScreenSaverDescription;
        ApplyState();
    }

    private Label CreateLabel(
        string text,
        float fontSize,
        FontStyle fontStyle = FontStyle.Regular) =>
        new()
        {
            AutoEllipsis = true,
            AutoSize = false,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, fontSize, fontStyle, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
        };

    private PopupButton CreateButton(string text)
    {
        var button = new PopupButton
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point),
            Text = text,
        };
        button.ApplyPalette(_palette);
        return button;
    }

    private void AddDurationButton(string text, TimeSpan duration)
    {
        var button = CreateButton(text);
        _durationButtons.Add(button, duration);
        button.Click += async (_, _) => await SetDurationAsync(duration);
    }

    private static TableLayoutPanel CreateButtonRow(params PopupButton[] buttons)
    {
        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = buttons.Length,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
            RowCount = 1,
        };
        for (var index = 0; index < buttons.Length; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / buttons.Length));
            buttons[index].Margin = new Padding(
                index == 0 ? 0 : 4,
                0,
                index == buttons.Length - 1 ? 0 : 4,
                0);
            layout.Controls.Add(buttons[index], index, 0);
        }

        return layout;
    }

    private async Task SetModeAsync(WakeMode mode)
    {
        var previousMode = _state.Mode;
        var preserveDuration = mode != WakeMode.Inactive && previousMode != WakeMode.Inactive;
        var optimisticState = _state with
        {
            Mode = mode,
            StopAtUtc = preserveDuration ? _state.StopAtUtc : null,
            SelectedDuration = preserveDuration ? _state.SelectedDuration : null,
        };
        var progressText = mode switch
        {
            WakeMode.Inactive => UiText.Current.ProgressStopping,
            _ when previousMode == WakeMode.Inactive => UiText.Current.ProgressStarting,
            _ when previousMode != mode => UiText.Current.ProgressSwitching,
            _ => UiText.Current.ProgressUpdating,
        };
        await RunWakeCommandAsync(
            optimisticState,
            progressText,
            () => _actions.SetModeAsync(mode));
    }

    private async Task SetDurationAsync(TimeSpan? duration)
    {
        var optimisticState = _state with
        {
            StopAtUtc = duration is { } value ? DateTimeOffset.UtcNow.Add(value) : null,
            SelectedDuration = duration,
        };
        await RunWakeCommandAsync(
            optimisticState,
            UiText.Current.ProgressUpdatingDuration,
            () => _actions.SetDurationAsync(duration));
    }

    private async Task RunWakeCommandAsync(
        State optimisticState,
        string progressText,
        Func<Task> command)
    {
        if (_wakeCommandPending)
        {
            return;
        }

        _wakeCommandPending = true;
        _showWakeCommandProgress = false;
        _wakeCommandProgressText = progressText;
        _state = optimisticState;
        _pendingFeedbackTimer.Start();
        ApplyState();
        try
        {
            await command();
        }
        finally
        {
            _pendingFeedbackTimer.Stop();
            _wakeCommandPending = false;
            _showWakeCommandProgress = false;
            _wakeCommandProgressText = string.Empty;
            _state = _reportedState;
            ApplyState();
        }
    }

    private async void LockButtonClick(object? sender, EventArgs eventArgs)
    {
        Hide();
        await _actions.LockAsync();
    }

    private async void ScreenSaverButtonClick(object? sender, EventArgs eventArgs)
    {
        Hide();
        await _actions.StartScreenSaverAsync();
    }

    private void ApplyState()
    {
        if (IsDisposed)
        {
            return;
        }

        UpdateStatusIcon();
        UpdateHeaderText();

        _inactiveButton.IsSelected = _state.Mode == WakeMode.Inactive;
        _keepAwakeButton.IsSelected = _state.Mode == WakeMode.KeepAwake;
        _displayOnButton.IsSelected = _state.Mode == WakeMode.KeepAwakeAndDisplayOn;

        // Keep the controls visually stable while a request is in flight. The command
        // guard already prevents a second request until the first one completes.
        var wakeControlsEnabled = _state.ServiceConnected;
        _inactiveButton.Enabled = wakeControlsEnabled;
        _keepAwakeButton.Enabled = wakeControlsEnabled;
        _displayOnButton.Enabled = wakeControlsEnabled;

        var durationEnabled = wakeControlsEnabled && _state.Mode != WakeMode.Inactive;
        _durationHeading.ForeColor = durationEnabled ? _palette.Text : _palette.DisabledText;
        _unlimitedButton.Enabled = durationEnabled;
        _unlimitedButton.IsSelected = _state.StopAtUtc is null;
        foreach (var (button, duration) in _durationButtons)
        {
            button.Enabled = durationEnabled;
            button.IsSelected = _state.StopAtUtc is not null && _state.SelectedDuration == duration;
        }

    }

    private void UpdateStatusIcon()
    {
        var iconState = _state.Mode switch
        {
            WakeMode.KeepAwake => TrayIconFactory.IconState.KeepAwake,
            WakeMode.KeepAwakeAndDisplayOn => TrayIconFactory.IconState.DisplayOn,
            _ => TrayIconFactory.IconState.Inactive,
        };
        if (_appIconImage is not null && _appIconState == iconState)
        {
            return;
        }

        using var icon = TrayIconFactory.Create(iconState);
        var newImage = icon.ToBitmap();
        var oldImage = _appIconImage;
        _appIconImage = newImage;
        _appIconState = iconState;
        _appIcon.Image = newImage;
        _appIcon.AccessibleDescription = iconState switch
        {
            TrayIconFactory.IconState.KeepAwake => UiText.Current.ModeKeepAwake,
            TrayIconFactory.IconState.DisplayOn => UiText.Current.StatusDisplayOn,
            _ => UiText.Current.StatusInactive,
        };
        oldImage?.Dispose();
    }

    private void UpdateHeaderText()
    {
        if (!_state.ServiceConnected)
        {
            _statusLabel.Text = _state.StatusText;
            return;
        }

        if (_showWakeCommandProgress)
        {
            _statusLabel.Text = _wakeCommandProgressText;
            return;
        }

        var statusText = _state.Mode switch
        {
            WakeMode.KeepAwake => UiText.Current.ModeKeepAwake,
            WakeMode.KeepAwakeAndDisplayOn => UiText.Current.ModeDisplayOn,
            _ => UiText.Current.StatusInactive,
        };
        if (_state.Mode == WakeMode.Inactive || _state.StopAtUtc is not { } stopAtUtc)
        {
            _statusLabel.Text = statusText;
            return;
        }

        var remaining = stopAtUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _statusLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                UiText.Current.EndingSoonFormat,
                statusText);
            return;
        }

        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        var remainingText = hours switch
        {
            > 0 when minutes > 0 => string.Format(
                CultureInfo.CurrentCulture,
                UiText.Current.HourMinuteFormat,
                hours,
                minutes),
            > 0 => string.Format(CultureInfo.CurrentCulture, UiText.Current.HourFormat, hours),
            _ => string.Format(CultureInfo.CurrentCulture, UiText.Current.MinuteFormat, minutes),
        };
        _statusLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            UiText.Current.RemainingFormat,
            statusText,
            remainingText,
            stopAtUtc.ToLocalTime());
    }

    private void PositionNearCursor()
    {
        const int screenMargin = 8;
        const int cursorGap = 12;

        var cursor = Cursor.Position;
        var workArea = Screen.FromPoint(cursor).WorkingArea;
        var minX = workArea.Left + screenMargin;
        var maxX = Math.Max(minX, workArea.Right - Width - screenMargin);
        var minY = workArea.Top + screenMargin;
        var maxY = Math.Max(minY, workArea.Bottom - Height - screenMargin);
        var x = Math.Clamp(cursor.X - Width + 24, minX, maxX);

        int y;
        if (cursor.Y < workArea.Top || cursor.Y >= workArea.Bottom)
        {
            // A tray click happens inside the taskbar, outside the usable work area.
            y = cursor.Y >= workArea.Bottom ? maxY : minY;
        }
        else
        {
            var aboveCursor = cursor.Y - Height - cursorGap;
            y = aboveCursor >= minY ? aboveCursor : cursor.Y + cursorGap;
            y = Math.Clamp(y, minY, maxY);
        }

        Location = new Point(x, y);
    }

    private void ApplyTheme()
    {
        _palette = Palette.Create();
        BackColor = _palette.Background;
        ForeColor = _palette.Text;
        _statusLabel.ForeColor = _palette.Text;
        _headerDivider.BackColor = _palette.Border;
        _modeHeading.ForeColor = _palette.Text;
        _durationHeading.ForeColor = _palette.Text;
        _actionHeading.ForeColor = _palette.Text;
        _actionHelpButton.ApplyPalette(_palette);
        foreach (var button in GetButtons())
        {
            button.ApplyPalette(_palette);
            if (button.Parent is not null)
            {
                button.Parent.BackColor = _palette.Background;
            }
        }

        if (IsHandleCreated)
        {
            ApplyDwmAttributes();
        }

        Invalidate(true);
    }

    private IEnumerable<PopupButton> GetButtons()
    {
        yield return _inactiveButton;
        yield return _keepAwakeButton;
        yield return _displayOnButton;
        yield return _unlimitedButton;
        foreach (var button in _durationButtons.Keys)
        {
            yield return button;
        }

        yield return _lockButton;
        yield return _screenSaverButton;
    }

    private void ApplyDwmAttributes()
    {
        var darkMode = _palette.IsDark ? 1 : 0;
        _ = DwmSetWindowAttribute(
            Handle,
            DwmwaUseImmersiveDarkMode,
            ref darkMode,
            Marshal.SizeOf<int>());

        var cornerPreference = DwmwcRound;
        _ = DwmSetWindowAttribute(
            Handle,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());

        var borderColor = ToColorRef(_palette.Border);
        _ = DwmSetWindowAttribute(
            Handle,
            DwmwaBorderColor,
            ref borderColor,
            Marshal.SizeOf<int>());
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        ApplyDwmAttributes();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRectangle.Create(
            new RectangleF(0.5F, 0.5F, ClientSize.Width - 1F, ClientSize.Height - 1F),
            12F);
        using var pen = new Pen(_palette.Border);
        eventArgs.Graphics.DrawPath(pen, path);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(eventArgs);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Dispose();
            _pendingFeedbackTimer.Dispose();
            _actionHelpTip.Dispose();
            _appIcon.Image = null;
            _appIconImage?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);

    private readonly record struct Palette(
        bool IsDark,
        Color Background,
        Color Surface,
        Color SurfaceHover,
        Color Border,
        Color Text,
        Color SecondaryText,
        Color DisabledText,
        Color Accent,
        Color AccentText)
    {
        internal static Palette Create()
        {
            if (SystemInformation.HighContrast)
            {
                return new Palette(
                    SystemColors.Window.GetBrightness() < 0.5F,
                    SystemColors.Window,
                    SystemColors.Control,
                    SystemColors.ControlLight,
                    SystemColors.WindowFrame,
                    SystemColors.WindowText,
                    SystemColors.GrayText,
                    SystemColors.GrayText,
                    SystemColors.Highlight,
                    SystemColors.HighlightText);
            }

            var isDark = IsDarkAppTheme();
            var accent = isDark
                ? Color.FromArgb(8, 105, 232)
                : Color.FromArgb(0, 98, 205);
            if (isDark)
            {
                return new Palette(
                    true,
                    Color.FromArgb(32, 32, 32),
                    Color.FromArgb(45, 45, 45),
                    Color.FromArgb(55, 55, 55),
                    Color.FromArgb(70, 70, 70),
                    Color.FromArgb(247, 247, 247),
                    Color.FromArgb(190, 190, 190),
                    Color.FromArgb(120, 120, 120),
                    accent,
                    Color.White);
            }

            return new Palette(
                false,
                Color.FromArgb(243, 243, 243),
                Color.FromArgb(253, 253, 253),
                Color.FromArgb(235, 235, 235),
                Color.FromArgb(210, 210, 210),
                Color.FromArgb(26, 26, 26),
                Color.FromArgb(96, 96, 96),
                Color.FromArgb(150, 150, 150),
                accent,
                GetContrastingText(accent));
        }

        private static bool IsDarkAppTheme()
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return personalize?.GetValue("AppsUseLightTheme") is int useLightTheme && useLightTheme == 0;
        }

        internal static Color Blend(Color foreground, Color background, float foregroundAmount)
        {
            var backgroundAmount = 1F - foregroundAmount;
            return Color.FromArgb(
                (int)((foreground.R * foregroundAmount) + (background.R * backgroundAmount)),
                (int)((foreground.G * foregroundAmount) + (background.G * backgroundAmount)),
                (int)((foreground.B * foregroundAmount) + (background.B * backgroundAmount)));
        }

        private static Color GetContrastingText(Color background)
        {
            var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
            return luminance > 150 ? Color.Black : Color.White;
        }
    }

    private sealed class HelpIconButton : Control
    {
        private Palette _palette;
        private bool _mouseOver;

        internal HelpIconButton()
        {
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Selectable |
                ControlStyles.StandardClick |
                ControlStyles.UserPaint,
                true);
            TabStop = true;
        }

        internal void ApplyPalette(Palette palette)
        {
            _palette = palette;
            BackColor = palette.Background;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(_palette.Background);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var diameter = Math.Min(20F, Math.Min(Width, Height) - 4F);
            var bounds = new RectangleF(
                (Width - diameter) / 2F,
                (Height - diameter) / 2F,
                diameter,
                diameter);
            var color = _mouseOver ? _palette.SecondaryText : _palette.DisabledText;
            using var outline = new Pen(color, 0.65F);
            eventArgs.Graphics.DrawEllipse(outline, bounds);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                "?",
                Font,
                Rectangle.Round(bounds),
                color,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(eventArgs.Graphics, Rectangle.Round(bounds));
            }
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            _mouseOver = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            _mouseOver = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            Focus();
            base.OnMouseDown(eventArgs);
        }

        protected override void OnKeyUp(KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode is Keys.Space or Keys.Enter)
            {
                eventArgs.Handled = true;
                OnClick(EventArgs.Empty);
            }

            base.OnKeyUp(eventArgs);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return (keyData & Keys.KeyCode) is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
        }
    }

    private sealed class PopupButton : Control
    {
        private Palette _palette;
        private bool _isSelected;
        private bool _mouseOver;
        private bool _mouseDown;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                Invalidate();
            }
        }

        internal PopupButton()
        {
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.StandardClick |
                ControlStyles.UserPaint,
                true);
            TabStop = true;
        }

        internal void ApplyPalette(Palette palette)
        {
            _palette = palette;
            BackColor = palette.Background;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(_palette.Background);
        }

        protected override void OnMouseEnter(EventArgs eventArgs)
        {
            _mouseOver = true;
            Invalidate();
            base.OnMouseEnter(eventArgs);
        }

        protected override void OnMouseLeave(EventArgs eventArgs)
        {
            _mouseOver = false;
            _mouseDown = false;
            Invalidate();
            base.OnMouseLeave(eventArgs);
        }

        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            Focus();
            _mouseDown = eventArgs.Button == MouseButtons.Left;
            Invalidate();
            base.OnMouseDown(eventArgs);
        }

        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            _mouseDown = false;
            Invalidate();
            base.OnMouseUp(eventArgs);
        }

        protected override void OnKeyDown(KeyEventArgs eventArgs)
        {
            if (eventArgs.KeyCode is Keys.Space or Keys.Enter)
            {
                _mouseDown = true;
                eventArgs.Handled = true;
                Invalidate();
            }

            base.OnKeyDown(eventArgs);
        }

        protected override void OnKeyUp(KeyEventArgs eventArgs)
        {
            if (_mouseDown && eventArgs.KeyCode is Keys.Space or Keys.Enter)
            {
                _mouseDown = false;
                eventArgs.Handled = true;
                Invalidate();
                OnClick(EventArgs.Empty);
            }

            base.OnKeyUp(eventArgs);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return (keyData & Keys.KeyCode) is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnEnabledChanged(EventArgs eventArgs)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(eventArgs);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            eventArgs.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            eventArgs.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var bodyBounds = new RectangleF(1.5F, 1F, Width - 3F, Height - 5F);
            DrawOuterShadow(eventArgs.Graphics, bodyBounds);
            using var bodyPath = RoundedRectangle.Create(bodyBounds, 9F);
            var (topColor, bottomColor) = GetFillColors();
            using var fill = new LinearGradientBrush(
                bodyBounds,
                topColor,
                bottomColor,
                LinearGradientMode.Vertical);
            eventArgs.Graphics.FillPath(fill, bodyPath);

            DrawInnerShadows(eventArgs.Graphics, bodyBounds, bodyPath);

            var textColor = !Enabled
                ? _palette.DisabledText
                : IsSelected
                    ? _palette.AccentText
                    : _palette.Text;
            TextRenderer.DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                Rectangle.Inflate(Rectangle.Round(bodyBounds), -4, -4),
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(
                    eventArgs.Graphics,
                    Rectangle.Inflate(Rectangle.Round(bodyBounds), -4, -4),
                    textColor,
                    bottomColor);
            }
        }

        private void DrawOuterShadow(Graphics graphics, RectangleF bodyBounds)
        {
            if (Enabled && IsSelected)
            {
                var glowBounds = RectangleF.Inflate(bodyBounds, 2.25F, 2.25F);
                glowBounds.Offset(0, 0.5F);
                using var glowPath = RoundedRectangle.Create(glowBounds, 11F);
                var glowAlpha = _palette.IsDark ? 34 : 24;
                using var glow = new SolidBrush(Color.FromArgb(glowAlpha, _palette.Accent));
                graphics.FillPath(glow, glowPath);
            }

            var softBounds = RectangleF.Inflate(bodyBounds, 1F, 1F);
            softBounds.Offset(0, 1F);
            using var softPath = RoundedRectangle.Create(softBounds, 10F);
            var softAlpha = Enabled ? (_palette.IsDark ? 20 : 14) : 9;
            using var softShadow = new SolidBrush(Color.FromArgb(softAlpha, Color.Black));
            graphics.FillPath(softShadow, softPath);

            var coreBounds = bodyBounds;
            coreBounds.Offset(0, 2.5F);
            using var corePath = RoundedRectangle.Create(coreBounds, 9F);
            var coreAlpha = Enabled ? (_palette.IsDark ? 38 : 24) : 14;
            using var coreShadow = new SolidBrush(Color.FromArgb(coreAlpha, Color.Black));
            graphics.FillPath(coreShadow, corePath);
        }

        private void DrawInnerShadows(
            Graphics graphics,
            RectangleF bodyBounds,
            GraphicsPath bodyPath)
        {
            var savedState = graphics.Save();
            graphics.SetClip(bodyPath);
            var darkAlpha = Enabled ? (_palette.IsDark ? 11 : 8) : 5;
            using (var softDarkShadow = new Pen(Color.FromArgb(darkAlpha, Color.Black), 4F))
            {
                graphics.DrawPath(softDarkShadow, bodyPath);
            }

            using (var nearDarkShadow = new Pen(Color.FromArgb(darkAlpha, Color.Black), 2F))
            {
                graphics.DrawPath(nearDarkShadow, bodyPath);
            }

            graphics.Restore(savedState);

            var innerBounds = RectangleF.Inflate(bodyBounds, -1F, -1F);
            using var innerPath = RoundedRectangle.Create(innerBounds, 8F);
            var lightAlpha = Enabled
                ? IsSelected
                    ? (_palette.IsDark ? 30 : 46)
                    : (_palette.IsDark ? 13 : 38)
                : 8;
            using var sharpLightShadow = new Pen(Color.FromArgb(lightAlpha, Color.White), 0.45F);
            graphics.DrawPath(sharpLightShadow, innerPath);
        }

        private (Color Top, Color Bottom) GetFillColors()
        {
            Color baseColor;
            if (!Enabled)
            {
                baseColor = Palette.Blend(_palette.Surface, _palette.Background, 0.68F);
            }
            else if (IsSelected)
            {
                baseColor = _palette.Accent;
            }
            else
            {
                baseColor = _mouseOver ? _palette.SurfaceHover : _palette.Surface;
            }

            if (_mouseDown && Enabled)
            {
                baseColor = Palette.Blend(baseColor, _palette.Background, 0.86F);
            }

            if (Enabled && IsSelected)
            {
                var energizedColor = _mouseOver
                    ? Palette.Blend(baseColor, Color.White, 0.93F)
                    : baseColor;
                return (
                    Palette.Blend(energizedColor, Color.FromArgb(82, 180, 255), 0.76F),
                    Palette.Blend(energizedColor, Color.Black, 0.87F));
            }

            var topAmount = _palette.IsDark ? 0.96F : 0.99F;
            var bottomAmount = _palette.IsDark ? 0.94F : 0.97F;
            return (
                Palette.Blend(baseColor, Color.White, topAmount),
                Palette.Blend(baseColor, Color.Black, bottomAmount));
        }

    }

    private static class RoundedRectangle
    {
        internal static GraphicsPath Create(RectangleF bounds, float radius)
        {
            var diameter = radius * 2F;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
