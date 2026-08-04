namespace WakeGuard.Contracts;

public static class ProtocolConstants
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 16 * 1024;
    public const string PipeName = "WakeGuard.Service.v1";

    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan LeaseTimeout = TimeSpan.FromSeconds(75);
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ServiceIdleTimeout = TimeSpan.FromSeconds(30);
}
