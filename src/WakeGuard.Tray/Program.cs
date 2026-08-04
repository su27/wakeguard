namespace WakeGuard.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var settings = TraySettingsStore.Load();
        UiText.Use(settings.Language);
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\WakeGuard.Tray",
            createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                UiText.Current.AlreadyRunning,
                "WakeGuard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(settings));
    }
}
