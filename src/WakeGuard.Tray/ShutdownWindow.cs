namespace WakeGuard.Tray;

internal sealed class ShutdownWindow : NativeWindow, IDisposable
{
    private const int WmClose = 0x0010;
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;

    private readonly Func<Task> _shutdown;
    private bool _shutdownRequested;

    internal ShutdownWindow(Func<Task> shutdown)
    {
        _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        CreateHandle(new CreateParams
        {
            Caption = "WakeGuard.Tray.Shutdown",
            X = -32_000,
            Y = -32_000,
            Width = 1,
            Height = 1,
        });
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case WmQueryEndSession:
                message.Result = new nint(1);
                return;
            case WmClose:
                RequestShutdown();
                return;
            case WmEndSession when message.WParam != nint.Zero:
                RequestShutdown();
                break;
        }

        base.WndProc(ref message);
    }

    private void RequestShutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _ = _shutdown();
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
