using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using WakeGuard.Contracts;

namespace WakeGuard.Windows;

public static class SecurePipeFactory
{
    public static NamedPipeServerStream CreateServer(string pipeName = ProtocolConstants.PipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddRule(security, WellKnownSidType.LocalSystemSid, PipeAccessRights.FullControl);
        AddRule(security, WellKnownSidType.LocalServiceSid, PipeAccessRights.FullControl);
        AddRule(security, WellKnownSidType.CreatorOwnerSid, PipeAccessRights.FullControl);
        AddRule(security, WellKnownSidType.InteractiveSid, PipeAccessRights.ReadWrite);

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: ProtocolConstants.MaximumMessageBytes + sizeof(int),
            outBufferSize: ProtocolConstants.MaximumMessageBytes + sizeof(int),
            security);
    }

    private static void AddRule(
        PipeSecurity security,
        WellKnownSidType sidType,
        PipeAccessRights rights)
    {
        var sid = new SecurityIdentifier(sidType, domainSid: null);
        security.AddAccessRule(new PipeAccessRule(sid, rights, AccessControlType.Allow));
    }
}
