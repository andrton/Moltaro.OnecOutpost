using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The desktop of the workers of one base under its worker account (the S9c brief, "The desktop of a worker"): a
// desktop of the agent's own on the agent's own non-interactive window station, which admits the logon of one token
// and nobody else but SYSTEM and Administrators. Invariant: a worker account is never granted WinSta0 or its Default
// desktop. It owns the handles of the station and the desktop and closes them when disposed; the station's ACE for the
// token's logon SID stays until the agent's logon session ends.
[SupportedOSPlatform("windows")]
internal sealed partial class WorkerDesktop : IDisposable
{
    private WorkerDesktop(string name)
    {
        Name = name;
    }

    // The station and the desktop as lpDesktop names them: {station}\OnecOutpost-{base key}.
    public string Name { get; }

    // CreateWindowStationW(null, …) gives the agent's own non-interactive station, refused unless its name starts with
    // Service-0x, so never WinSta0; it gains one ACE for the token's logon SID, 0x123 (enumerate desktops and the
    // station, read attributes, global atoms); CreateDesktopW makes OnecOutpost-{base key} on it, the station swapped
    // in and back at once, with the DACL D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0xC7;;;{logon SID}). A failure, such as no
    // WRITE_DAC on the station, is a Win32Exception.
    public static WorkerDesktop Make(string baseKey, SafeAccessTokenHandle token)
    {
        throw new NotSupportedException();
    }

    // Closes the handles of the desktop and the station.
    public void Dispose()
    {
    }
}
