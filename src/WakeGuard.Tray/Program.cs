namespace WakeGuard.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\WakeGuard.Tray",
            createdNew: out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "WakeGuard 已经在当前 Windows 会话中运行。",
                "WakeGuard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
