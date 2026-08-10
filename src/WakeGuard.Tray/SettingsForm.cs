using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WakeGuard.Tray;

internal sealed class SettingsForm : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcRound = 2;

    private readonly Func<TraySettings, bool> _applySettings;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Label _generalHeading;
    private readonly CheckBox _startWithWindowsCheckBox;
    private readonly Label _startWithWindowsDescription;
    private readonly Label _languageLabel;
    private readonly Label _languageDescription;
    private readonly ComboBox _languageComboBox;
    private readonly Label _aboutHeading;
    private readonly Label _versionLabel;
    private readonly Label _aboutDescription;
    private readonly Label _aboutCopyright;
    private readonly Button _closeButton;
    private readonly Panel _generalCard;
    private readonly Panel _aboutCard;
    private readonly Icon _windowIcon;
    private TraySettings _settings;
    private bool _updatingControls;

    internal SettingsForm(TraySettings settings, Func<TraySettings, bool> applySettings)
    {
        _settings = settings;
        _applySettings = applySettings;
        _windowIcon = TrayIconFactory.Create(TrayIconFactory.IconState.Inactive);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 550);
        DoubleBuffered = true;
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Icon = _windowIcon;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;

        _titleLabel = CreateLabel(18F, FontStyle.Bold);
        _subtitleLabel = CreateLabel(9F);
        _generalHeading = CreateLabel(10F, FontStyle.Bold);
        _startWithWindowsCheckBox = new CheckBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 10F),
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _startWithWindowsDescription = CreateLabel(8.5F);
        _startWithWindowsDescription.AutoEllipsis = false;
        _languageLabel = CreateLabel(10F, FontStyle.Regular);
        _languageDescription = CreateLabel(8.5F);
        _languageDescription.AutoEllipsis = false;
        _languageComboBox = new ComboBox
        {
            Anchor = AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.System,
            Width = 150,
        };
        _aboutHeading = CreateLabel(10F, FontStyle.Bold);
        _versionLabel = CreateLabel(10F, FontStyle.Bold);
        _aboutDescription = CreateLabel(9F);
        _aboutDescription.AutoEllipsis = false;
        _aboutCopyright = CreateLabel(8.5F);
        _closeButton = new Button
        {
            Anchor = AnchorStyles.Right,
            AutoSize = false,
            Height = 36,
            Width = 100,
        };
        _closeButton.Click += (_, _) => Hide();

        _generalCard = CreateCard(CreateGeneralContent());
        _aboutCard = CreateCard(CreateAboutContent());
        Controls.Add(CreateLayout());

        _startWithWindowsCheckBox.CheckedChanged += StartWithWindowsCheckedChanged;
        _languageComboBox.SelectedIndexChanged += LanguageSelectedIndexChanged;
        ApplyLocalization();
        ApplySettings(settings);
        ApplyTheme();
        ScaleForSystemDpi();
    }

    internal void ShowSettings(TraySettings settings)
    {
        SuspendLayout();
        try
        {
            ApplySettings(settings);
            ApplyLocalization();
            ApplyTheme();
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }

        WindowState = FormWindowState.Normal;
        if (!Visible)
        {
            // Create the native window, apply its DWM theme, and paint a complete
            // first frame while transparent so the default dialog never flashes.
            Opacity = 0;
            Show();
            PerformLayout();
            Refresh();
            Opacity = 1;
        }

        Activate();
        BringToFront();
    }

    internal void ApplyLocalization()
    {
        var text = UiText.Current;
        Text = text.SettingsTitle;
        _titleLabel.Text = text.SettingsTitle;
        _subtitleLabel.Text = text.SettingsSubtitle;
        _generalHeading.Text = text.GeneralHeading;
        _startWithWindowsCheckBox.Text = text.StartWithWindows;
        _startWithWindowsDescription.Text = text.StartWithWindowsDescription;
        _languageLabel.Text = text.LanguageLabel;
        _languageDescription.Text = text.LanguageDescription;
        _languageComboBox.AccessibleName = text.LanguageLabel;
        _aboutHeading.Text = text.AboutHeading;
        _versionLabel.Text = string.Format(
            CultureInfo.CurrentCulture,
            text.VersionFormat,
            GetDisplayVersion());
        _aboutDescription.Text = text.AboutDescription;
        _aboutCopyright.Text = text.AboutCopyright;
        _closeButton.Text = text.Close;

        _updatingControls = true;
        try
        {
            _languageComboBox.Items.Clear();
            _languageComboBox.Items.Add(text.ChineseLanguage);
            _languageComboBox.Items.Add(text.EnglishLanguage);
            _languageComboBox.SelectedIndex = (int)_settings.Language;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private TableLayoutPanel CreateLayout()
    {
        var header = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2,
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        header.Controls.Add(_titleLabel, 0, 0);
        header.Controls.Add(_subtitleLabel, 0, 1);

        var footer = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        footer.Controls.Add(_closeButton, 1, 0);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(28, 24, 28, 22),
            RowCount = 7,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_generalHeading, 0, 1);
        layout.Controls.Add(_generalCard, 0, 2);
        layout.Controls.Add(_aboutHeading, 0, 3);
        layout.Controls.Add(_aboutCard, 0, 4);
        layout.Controls.Add(footer, 0, 6);
        return layout;
    }

    private TableLayoutPanel CreateGeneralContent()
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 5,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(_startWithWindowsCheckBox, 0, 0);
        content.SetColumnSpan(_startWithWindowsCheckBox, 2);
        content.Controls.Add(_startWithWindowsDescription, 0, 1);
        content.SetColumnSpan(_startWithWindowsDescription, 2);
        content.Controls.Add(_languageLabel, 0, 3);
        content.Controls.Add(_languageComboBox, 1, 3);
        content.Controls.Add(_languageDescription, 0, 4);
        content.SetColumnSpan(_languageDescription, 2);
        return content;
    }

    private TableLayoutPanel CreateAboutContent()
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3,
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        content.Controls.Add(_versionLabel, 0, 0);
        content.Controls.Add(_aboutDescription, 0, 1);
        content.Controls.Add(_aboutCopyright, 0, 2);
        return content;
    }

    private static Panel CreateCard(Control content)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(18, 14, 18, 12),
        };
        panel.Controls.Add(content);
        return panel;
    }

    private Label CreateLabel(float size, FontStyle style = FontStyle.Regular) => new()
    {
        AutoEllipsis = true,
        BackColor = Color.Transparent,
        Dock = DockStyle.Fill,
        Font = new Font(Font.FontFamily, size, style, GraphicsUnit.Point),
        Margin = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private void ApplySettings(TraySettings settings)
    {
        _settings = settings;
        _updatingControls = true;
        try
        {
            _startWithWindowsCheckBox.Checked = settings.StartWithWindows;
            _languageComboBox.SelectedIndex = (int)settings.Language;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void StartWithWindowsCheckedChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingControls)
        {
            return;
        }

        TryApply(_settings with { StartWithWindows = _startWithWindowsCheckBox.Checked });
    }

    private void LanguageSelectedIndexChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingControls || _languageComboBox.SelectedIndex < 0)
        {
            return;
        }

        TryApply(_settings with { Language = (UiLanguage)_languageComboBox.SelectedIndex });
    }

    private void TryApply(TraySettings settings)
    {
        if (_applySettings(settings))
        {
            _settings = settings;
            ApplyLocalization();
            return;
        }

        ApplySettings(_settings);
    }

    private void ApplyTheme()
    {
        var isDark = IsDarkAppTheme();
        var background = isDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        var surface = isDark ? Color.FromArgb(45, 45, 45) : Color.White;
        var text = isDark ? Color.FromArgb(247, 247, 247) : Color.FromArgb(26, 26, 26);
        var secondary = isDark ? Color.FromArgb(190, 190, 190) : Color.FromArgb(96, 96, 96);

        BackColor = background;
        ForeColor = text;
        _generalCard.BackColor = surface;
        _aboutCard.BackColor = surface;
        _titleLabel.ForeColor = text;
        _generalHeading.ForeColor = text;
        _aboutHeading.ForeColor = text;
        _startWithWindowsCheckBox.ForeColor = text;
        _languageLabel.ForeColor = text;
        _versionLabel.ForeColor = text;
        foreach (var label in new[]
                 {
                     _subtitleLabel,
                     _startWithWindowsDescription,
                     _languageDescription,
                     _aboutDescription,
                     _aboutCopyright,
                 })
        {
            label.ForeColor = secondary;
        }

        if (IsHandleCreated)
        {
            var darkMode = isDark ? 1 : 0;
            _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
            var corners = DwmwcRound;
            _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref corners, sizeof(int));
        }
    }

    private static bool IsDarkAppTheme()
    {
        using var personalize = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            writable: false);
        return personalize?.GetValue("AppsUseLightTheme") is int useLight && useLight == 0;
    }

    private void ScaleForSystemDpi()
    {
        var scale = GetDpiForSystem() / 96F;
        if (scale <= 1F)
        {
            return;
        }

        AutoScaleMode = AutoScaleMode.None;
        Scale(new SizeF(scale, scale));
    }

    private static string GetDisplayVersion()
    {
        var version = FileVersionInfo.GetVersionInfo(Application.ExecutablePath).ProductVersion
            ?? Application.ProductVersion;
        var metadataIndex = version.IndexOf('+');
        return metadataIndex >= 0 ? version[..metadataIndex] : version;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        ApplyTheme();
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
            _windowIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
