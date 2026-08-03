namespace WakeGuard.Tray;

internal static class TrayIconFactory
{
    private const string AssetNamespace = "WakeGuard.Tray.Assets";

    internal enum IconState
    {
        Inactive,
        KeepAwake,
        DisplayOn,
        Disconnected,
    }

    internal static Icon Create(IconState state)
    {
        var assetName = state switch
        {
            IconState.KeepAwake => "TrayKeepAwake.ico",
            IconState.DisplayOn => "TrayDisplayOn.ico",
            _ => "TrayInactive.ico",
        };
        using var stream = typeof(TrayIconFactory).Assembly.GetManifestResourceStream(
            $"{AssetNamespace}.{assetName}")
            ?? throw new InvalidOperationException($"Embedded tray icon {assetName} was not found.");
        using var icon = new Icon(stream, SystemInformation.SmallIconSize);
        return (Icon)icon.Clone();
    }
}
