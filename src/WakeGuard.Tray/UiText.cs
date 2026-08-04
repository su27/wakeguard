using System.Globalization;

namespace WakeGuard.Tray;

internal enum UiLanguage
{
    Chinese,
    English,
}

internal sealed record UiText
{
    private static UiText _current = Create(DetectDefaultLanguage());

    internal static UiText Current => _current;

    internal required UiLanguage Language { get; init; }
    internal required string AlreadyRunning { get; init; }
    internal required string MenuSettings { get; init; }
    internal required string MenuExit { get; init; }
    internal required string StatusInactive { get; init; }
    internal required string StatusDisconnected { get; init; }
    internal required string StatusKeepAwake { get; init; }
    internal required string StatusDisplayOn { get; init; }
    internal required string StatusOtherUser { get; init; }
    internal required string PopupConnecting { get; init; }
    internal required string ModeHeading { get; init; }
    internal required string ModeInactive { get; init; }
    internal required string ModeKeepAwake { get; init; }
    internal required string ModeDisplayOn { get; init; }
    internal required string ModeInactiveDescription { get; init; }
    internal required string ModeKeepAwakeDescription { get; init; }
    internal required string ModeDisplayOnDescription { get; init; }
    internal required string DurationHeading { get; init; }
    internal required string DurationUnlimited { get; init; }
    internal required string Duration30Minutes { get; init; }
    internal required string Duration1Hour { get; init; }
    internal required string Duration2Hours { get; init; }
    internal required string Duration4Hours { get; init; }
    internal required string ActionHeading { get; init; }
    internal required string ActionHelpAccessible { get; init; }
    internal required string ActionHelpText { get; init; }
    internal required string LockComputer { get; init; }
    internal required string StartScreenSaver { get; init; }
    internal required string LockComputerDescription { get; init; }
    internal required string StartScreenSaverDescription { get; init; }
    internal required string ProgressStopping { get; init; }
    internal required string ProgressStarting { get; init; }
    internal required string ProgressSwitching { get; init; }
    internal required string ProgressUpdating { get; init; }
    internal required string ProgressUpdatingDuration { get; init; }
    internal required string EndingSoonFormat { get; init; }
    internal required string RemainingFormat { get; init; }
    internal required string HourMinuteFormat { get; init; }
    internal required string HourFormat { get; init; }
    internal required string MinuteFormat { get; init; }
    internal required string LockFailed { get; init; }
    internal required string ScreenSaverFailed { get; init; }
    internal required string OperationFailed { get; init; }
    internal required string ReleaseFailedQuestion { get; init; }
    internal required string ServiceFailure { get; init; }
    internal required string ServiceTimeout { get; init; }
    internal required string LockNativeFailure { get; init; }
    internal required string ScreenSaverMissing { get; init; }
    internal required string SettingsTitle { get; init; }
    internal required string SettingsSubtitle { get; init; }
    internal required string GeneralHeading { get; init; }
    internal required string StartWithWindows { get; init; }
    internal required string StartWithWindowsDescription { get; init; }
    internal required string LanguageLabel { get; init; }
    internal required string LanguageDescription { get; init; }
    internal required string ChineseLanguage { get; init; }
    internal required string EnglishLanguage { get; init; }
    internal required string AboutHeading { get; init; }
    internal required string VersionFormat { get; init; }
    internal required string AboutDescription { get; init; }
    internal required string AboutCopyright { get; init; }
    internal required string Close { get; init; }
    internal required string StartupUpdateFailed { get; init; }
    internal required string SettingsSaveFailed { get; init; }
    internal required string TrayMenuFailed { get; init; }

    internal static void Use(UiLanguage language) => _current = Create(language);

    internal static UiLanguage DetectDefaultLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals(
            "zh",
            StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Chinese
            : UiLanguage.English;

    private static UiText Create(UiLanguage language) => language == UiLanguage.English
        ? CreateEnglish()
        : CreateChinese();

    private static UiText CreateChinese() => new()
    {
        Language = UiLanguage.Chinese,
        AlreadyRunning = "WakeGuard 已经在当前 Windows 会话中运行。",
        MenuSettings = "设置…",
        MenuExit = "退出",
        StatusInactive = "未保持唤醒",
        StatusDisconnected = "后台服务未连接",
        StatusKeepAwake = "保持唤醒 · 屏幕由系统管理",
        StatusDisplayOn = "保持唤醒 · 屏幕常亮",
        StatusOtherUser = "本程序未请求 · 其他用户正在保持唤醒",
        PopupConnecting = "正在连接 WakeGuard 服务…",
        ModeHeading = "唤醒状态",
        ModeInactive = "不保持",
        ModeKeepAwake = "保持唤醒",
        ModeDisplayOn = "唤醒且常亮",
        ModeInactiveDescription = "停止 WakeGuard 的唤醒请求",
        ModeKeepAwakeDescription = "保持系统唤醒，屏幕关闭仍由 Windows 管理",
        ModeDisplayOnDescription = "同时保持系统唤醒和屏幕常亮",
        DurationHeading = "保持时间",
        DurationUnlimited = "不限",
        Duration30Minutes = "30 分钟",
        Duration1Hour = "1 小时",
        Duration2Hours = "2 小时",
        Duration4Hours = "4 小时",
        ActionHeading = "立即操作",
        ActionHelpAccessible = "查看立即操作说明",
        ActionHelpText = "不会改变上面的唤醒状态和保持时间",
        LockComputer = "锁定电脑",
        StartScreenSaver = "启动屏幕保护程序",
        LockComputerDescription = "只锁定电脑，不改变当前唤醒状态和保持时间",
        StartScreenSaverDescription = "只启动屏幕保护程序，不改变当前唤醒状态和保持时间",
        ProgressStopping = "正在关闭服务…",
        ProgressStarting = "正在启动服务…",
        ProgressSwitching = "正在切换模式…",
        ProgressUpdating = "正在更新服务…",
        ProgressUpdatingDuration = "正在更新保持时间…",
        EndingSoonFormat = "{0} · 即将结束",
        RemainingFormat = "{0} · 剩余 {1} · {2:HH:mm} 结束",
        HourMinuteFormat = "{0} 小时 {1} 分钟",
        HourFormat = "{0} 小时",
        MinuteFormat = "{0} 分钟",
        LockFailed = "WakeGuard 无法锁定电脑。",
        ScreenSaverFailed = "WakeGuard 无法启动屏幕保护程序。",
        OperationFailed = "WakeGuard 没有完成操作。",
        ReleaseFailedQuestion = "后台服务暂时无法确认释放。即使继续退出，租约也会在最多 75 秒后自动失效。\n\n仍要退出吗？",
        ServiceFailure = "后台服务返回失败",
        ServiceTimeout = "WakeGuard 后台服务没有在规定时间内响应。",
        LockNativeFailure = "Windows 无法锁定当前工作站",
        ScreenSaverMissing = "Windows 没有配置屏幕保护程序，也找不到内置黑屏屏保。",
        SettingsTitle = "WakeGuard 设置",
        SettingsSubtitle = "这些偏好设置仅应用于当前 Windows 用户",
        GeneralHeading = "常规",
        StartWithWindows = "登录 Windows 后自动启动 WakeGuard",
        StartWithWindowsDescription = "关闭后仍可从开始菜单手动启动",
        LanguageLabel = "语言",
        LanguageDescription = "更改会立即应用到托盘界面",
        ChineseLanguage = "中文",
        EnglishLanguage = "English",
        AboutHeading = "关于",
        VersionFormat = "版本 {0}",
        AboutDescription = "通过 Windows Power Request API 在锁屏或屏幕关闭后可靠地保持系统唤醒。",
        AboutCopyright = "WakeGuard contributors",
        Close = "关闭",
        StartupUpdateFailed = "无法更新开机启动设置。",
        SettingsSaveFailed = "无法保存 WakeGuard 设置。",
        TrayMenuFailed = "无法打开托盘菜单。",
    };

    private static UiText CreateEnglish() => new()
    {
        Language = UiLanguage.English,
        AlreadyRunning = "WakeGuard is already running in this Windows session.",
        MenuSettings = "Settings…",
        MenuExit = "Exit",
        StatusInactive = "System default",
        StatusDisconnected = "Background service unavailable",
        StatusKeepAwake = "Keep awake · Display managed by Windows",
        StatusDisplayOn = "Keep awake · Display on",
        StatusOtherUser = "No request from this app · Another user is keeping the system awake",
        PopupConnecting = "Connecting to the WakeGuard service…",
        ModeHeading = "Awake mode",
        ModeInactive = "System default",
        ModeKeepAwake = "Keep awake",
        ModeDisplayOn = "Keep screen on",
        ModeInactiveDescription = "Stop WakeGuard's wake request",
        ModeKeepAwakeDescription = "Keep the system awake while Windows manages the display",
        ModeDisplayOnDescription = "Keep both the system and display awake",
        DurationHeading = "Duration",
        DurationUnlimited = "Unlimited",
        Duration30Minutes = "30 min",
        Duration1Hour = "1 hour",
        Duration2Hours = "2 hours",
        Duration4Hours = "4 hours",
        ActionHeading = "Immediate actions",
        ActionHelpAccessible = "Show immediate-action help",
        ActionHelpText = "These actions do not change the awake mode or duration above",
        LockComputer = "Lock computer",
        StartScreenSaver = "Start screen saver",
        LockComputerDescription = "Lock only; keep the current awake mode and duration",
        StartScreenSaverDescription = "Start the screen saver only; keep the current awake mode and duration",
        ProgressStopping = "Stopping service…",
        ProgressStarting = "Starting service…",
        ProgressSwitching = "Switching mode…",
        ProgressUpdating = "Updating service…",
        ProgressUpdatingDuration = "Updating duration…",
        EndingSoonFormat = "{0} · Ending soon",
        RemainingFormat = "{0} · {1} remaining · Ends at {2:HH:mm}",
        HourMinuteFormat = "{0} hr {1} min",
        HourFormat = "{0} hr",
        MinuteFormat = "{0} min",
        LockFailed = "WakeGuard could not lock the computer.",
        ScreenSaverFailed = "WakeGuard could not start the screen saver.",
        OperationFailed = "WakeGuard could not complete the operation.",
        ReleaseFailedQuestion = "The background service could not confirm release. If you exit, the lease will still expire within 75 seconds.\n\nExit anyway?",
        ServiceFailure = "The background service returned an error",
        ServiceTimeout = "The WakeGuard background service did not respond in time.",
        LockNativeFailure = "Windows could not lock the current workstation",
        ScreenSaverMissing = "Windows has no configured screen saver and the built-in blank screen saver was not found.",
        SettingsTitle = "WakeGuard Settings",
        SettingsSubtitle = "These preferences apply only to the current Windows user",
        GeneralHeading = "General",
        StartWithWindows = "Start WakeGuard when I sign in to Windows",
        StartWithWindowsDescription = "You can still start it manually from the Start menu",
        LanguageLabel = "Language",
        LanguageDescription = "Changes apply to the tray interface immediately",
        ChineseLanguage = "中文",
        EnglishLanguage = "English",
        AboutHeading = "About",
        VersionFormat = "Version {0}",
        AboutDescription = "Reliably keeps Windows awake after locking or turning off the display using the Windows Power Request API.",
        AboutCopyright = "WakeGuard contributors",
        Close = "Close",
        StartupUpdateFailed = "Could not update the startup setting.",
        SettingsSaveFailed = "Could not save WakeGuard settings.",
        TrayMenuFailed = "Could not open the tray menu.",
    };
}
