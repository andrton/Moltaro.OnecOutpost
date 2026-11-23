using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The desktop of the workers of one base under its worker account (the S9c brief, "The desktop of a worker"): a
// desktop of the agent's own on the agent's own non-interactive window station, which admits the logon of one token
// and nobody else but SYSTEM and Administrators. Invariant: a worker account is never granted WinSta0 or its Default
// desktop. Its name holds the token's logon session, so that a desktop made for an earlier logon is never given again.
// It owns the handles of the station and the desktop and closes them when disposed. Every call to Windows goes through
// LibraryImport. Limitation: the station's ACE for the token's logon SID stays until the agent's logon session ends.
[SupportedOSPlatform("windows")]
internal sealed partial class WorkerDesktop : IDisposable
{
    // The prefix of the name that Windows gives the non-interactive window station of a logon session,
    // Service-0x{high}-{low}$.
    private const string NonInteractivePrefix = "Service-0x";

    // WINSTA_READATTRIBUTES, WINSTA_CREATEDESKTOP, READ_CONTROL and WRITE_DAC: to read the station's name, to make a
    // desktop on it, and to read and extend its list.
    private const uint StationAccess = 0x0002 | 0x0008 | 0x00020000 | 0x00040000;

    // What the station's ACE gives the token's logon SID: WINSTA_ENUMDESKTOPS, WINSTA_READATTRIBUTES, WINSTA_ENUMERATE
    // and WINSTA_ACCESSGLOBALATOMS, enough for a process to connect, without the clipboard, desktops of its own or
    // logging off.
    private const uint StationRightsOfTheLogon = 0x123;

    // The agent's handle only keeps the desktop alive, but CreateDesktopW fails with ERROR_NOT_ENOUGH_MEMORY for
    // DESKTOP_READOBJECTS alone, as measured, so it asks the rights that the workers get, 0xC7.
    private const uint DesktopAccess = 0xC7;

    // SE_WINDOW_OBJECT of SE_OBJECT_TYPE, and DACL_SECURITY_INFORMATION.
    private const int WindowObject = 7;

    private const uint DaclInformation = 0x00000004;

    // GRANT_ACCESS of ACCESS_MODE, NO_INHERITANCE, TRUSTEE_IS_SID and SDDL_REVISION_1.
    private const int GrantAccess = 1;

    private const uint NoInheritance = 0;

    private const int TrusteeIsSid = 0;

    private const uint SddlRevision = 1;

    // UOI_NAME of GetUserObjectInformationW.
    private const int NameIndex = 2;

    // SE_GROUP_LOGON_ID: the group that names one logon session.
    private const uint LogonIdGroup = 0xC0000000;

    // ERROR_ACCESS_DENIED and ERROR_NO_SUCH_LOGON_SESSION, for the two refusals that are not a call's own.
    private const int AccessDenied = 5;

    private const int NoSuchLogonSession = 1312;

    private readonly StationHandle _station;

    private readonly DesktopHandle _desktop;

    private WorkerDesktop(string name, StationHandle station, DesktopHandle desktop)
    {
        Name = name;
        _station = station;
        _desktop = desktop;
    }

    // The station and the desktop as lpDesktop names them: {station}\OnecOutpost-{base key}-{logon}, {logon} the
    // identifier of the token's logon session, its AuthenticationId, in lowercase hexadecimal.
    public string Name { get; }

    // CreateWindowStationW(null, …) gives the agent's own non-interactive station, refused unless its name starts with
    // Service-0x, so never WinSta0; it gains one ACE for the token's logon SID, 0x123 (enumerate desktops and the
    // station, read attributes, global atoms); CreateDesktopW makes OnecOutpost-{base key}-{logon} on it, the station
    // swapped in and back at once, with the DACL D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0xC7;;;{logon SID}). A failure, such as
    // no WRITE_DAC on the station, is a Win32Exception. It runs before any worker starts and never beside another Make,
    // since the swap changes the station of the whole process.
    public static WorkerDesktop Make(string baseKey, SafeAccessTokenHandle token)
    {
        SecurityIdentifier logon = LogonSidOf(token);
        ulong session = ComWorkerLauncher.LogonOf(token);
        StationHandle station = CreateWindowStationW(null, 0, StationAccess, 0);

        if (station.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();

            station.Dispose();

            throw new Win32Exception(error);
        }

        try
        {
            string stationName = NameOf(station);

            if (!stationName.StartsWith(NonInteractivePrefix, StringComparison.Ordinal))
            {
                throw new Win32Exception(
                    AccessDenied,
                    $"the window station {stationName} is not a non-interactive one of the agent's own");
            }

            Admit(station, logon);

            string desktopName = $"OnecOutpost-{baseKey}-{session:x}";
            DesktopHandle desktop = CreateDesktopOn(station, desktopName, logon);

            return new WorkerDesktop($@"{stationName}\{desktopName}", station, desktop);
        }
        catch
        {
            station.Dispose();
            throw;
        }
    }

    // Closes the handles of the desktop and the station.
    public void Dispose()
    {
        _desktop.Dispose();
        _station.Dispose();
    }

    // The group of the token that names its logon session, which only the processes of that one logon hold.
    private static SecurityIdentifier LogonSidOf(SafeAccessTokenHandle token)
    {
        foreach ((SecurityIdentifier sid, uint attributes) in ComWorkerLauncher.Groups(token))
        {
            if ((attributes & LogonIdGroup) == LogonIdGroup)
            {
                return sid;
            }
        }

        throw new Win32Exception(NoSuchLogonSession, "the token holds no logon SID");
    }

    // The name that Windows gives the station.
    private static unsafe string NameOf(StationHandle station)
    {
        // Longer than any name of a window station, with its null.
        const int Capacity = 256;

        char* name = stackalloc char[Capacity];

        if (!GetUserObjectInformationW(station, NameIndex, name, Capacity * sizeof(char), out uint needed))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // The length counts bytes, the closing null included.
        return new string(name, 0, ((int)needed / sizeof(char)) - 1);
    }

    // Adds to the station's list one entry that gives the logon SID what a process needs to connect to the station.
    private static unsafe void Admit(StationHandle station, SecurityIdentifier logon)
    {
        uint error = GetSecurityInfo(station, WindowObject, DaclInformation, 0, 0, out nint dacl, 0, out nint descriptor);

        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }

        nint extended = 0;

        try
        {
            byte[] sid = new byte[logon.BinaryLength];
            logon.GetBinaryForm(sid, 0);

            fixed (byte* sidStart = sid)
            {
                ExplicitAccess entry = new()
                {
                    AccessPermissions = StationRightsOfTheLogon,
                    AccessMode = GrantAccess,
                    Inheritance = NoInheritance,
                    TrusteeForm = TrusteeIsSid,
                    TrusteeName = (nint)sidStart,
                };

                error = SetEntriesInAclW(1, in entry, dacl, out extended);
            }

            if (error == 0)
            {
                error = SetSecurityInfo(station, WindowObject, DaclInformation, 0, 0, extended, 0);
            }

            if (error != 0)
            {
                throw new Win32Exception((int)error);
            }
        }
        finally
        {
            // The DACL that GetSecurityInfo gave lies inside its descriptor; the extended one is a block of its own.
            LocalFree(extended);
            LocalFree(descriptor);
        }
    }

    // Makes the desktop on the station, which CreateDesktopW takes from the window station of the process: the station
    // is swapped in, and the process's own back at once.
    private static DesktopHandle CreateDesktopOn(StationHandle station, string name, SecurityIdentifier logon)
    {
        string sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0xC7;;;{logon.Value})";

        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SddlRevision, out nint descriptor, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            SecurityAttributes attributes = new()
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
            };

            // The process's own station, a handle that Windows keeps and that is never closed here.
            nint own = GetProcessWindowStation();

            if (own == 0 || !SetProcessWindowStation(station.DangerousGetHandle()))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            DesktopHandle desktop;
            int error;

            try
            {
                desktop = CreateDesktopW(name, 0, 0, 0, DesktopAccess, in attributes);
                error = Marshal.GetLastPInvokeError();
            }
            finally
            {
                // Back to a station that the process had, through a handle that stays valid, so it cannot fail.
                SetProcessWindowStation(own);
            }

            if (desktop.IsInvalid)
            {
                desktop.Dispose();

                throw new Win32Exception(error);
            }

            return desktop;
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial StationHandle CreateWindowStationW(string? name, uint flags, uint access, nint attributes);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial DesktopHandle CreateDesktopW(
        string name,
        nint device,
        nint deviceMode,
        uint flags,
        uint access,
        in SecurityAttributes attributes);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetProcessWindowStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWindowStation(nint station);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(
        StationHandle userObject,
        int index,
        char* information,
        uint length,
        out uint needed);

    // GetSecurityInfo, SetEntriesInAclW and SetSecurityInfo give their error as the result, never as the last error.
    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityInfo(
        StationHandle handle,
        int objectType,
        uint securityInformation,
        nint owner,
        nint group,
        out nint dacl,
        nint sacl,
        out nint securityDescriptor);

    [LibraryImport("advapi32.dll")]
    private static partial uint SetEntriesInAclW(uint count, in ExplicitAccess entries, nint oldAcl, out nint newAcl);

    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(
        StationHandle handle,
        int objectType,
        uint securityInformation,
        nint owner,
        nint group,
        nint dacl,
        nint sacl);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl,
        uint revision,
        out nint securityDescriptor,
        nint size);

    // Frees memory that the calls above allocated; null frees nothing.
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
