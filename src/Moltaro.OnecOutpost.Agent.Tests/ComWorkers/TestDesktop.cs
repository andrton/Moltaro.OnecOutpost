using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The window stations and desktops of the process of the tests, which stands for the agent: the desktop it runs on,
// as "{station}\{desktop}", the form in which the test program's script desktop names its own; and the access lists of
// its own non-interactive station and of a desktop on it, in SDDL, read back as Windows keeps them. The process's own
// station and the desktop of a thread are handles that Windows gives and that are not to be closed; the others are
// closed here. Reading a desktop of the non-interactive station swaps that station in and back, as WorkerDesktop.Make
// does, which changes the station of the whole process for that moment: the test classes that read the process's
// station or make a desktop are in one collection, WindowStationCollection, so that none runs beside another.
[SupportedOSPlatform("windows")]
internal static partial class TestDesktop
{
    // UOI_NAME of GetUserObjectInformationW.
    private const int NameIndex = 2;

    // READ_CONTROL, SE_WINDOW_OBJECT of SE_OBJECT_TYPE, DACL_SECURITY_INFORMATION and SDDL_REVISION_1.
    private const uint ReadControl = 0x00020000;

    private const int WindowObject = 7;

    private const uint DaclInformation = 0x00000004;

    private const uint SddlRevision = 1;

    public static string OfTheTests()
    {
        return $@"{NameOf(GetProcessWindowStation())}\{NameOf(GetThreadDesktop(GetCurrentThreadId()))}";
    }

    // The DACL of the non-interactive window station of the logon session of the tests, the one that
    // CreateWindowStationW(null, …) gives, as WorkerDesktop.Make takes it.
    public static string StationDacl()
    {
        nint station = OpenOwnStation();

        try
        {
            return DaclOf(station);
        }
        finally
        {
            CloseWindowStation(station);
        }
    }

    // The DACL of the desktop that lpDesktop names as "{station}\{desktop}", on that non-interactive station.
    public static string DesktopDacl(string name)
    {
        string desktopName = name[(name.IndexOf('\\', StringComparison.Ordinal) + 1)..];
        nint station = OpenOwnStation();
        nint desktop;

        try
        {
            nint previous = GetProcessWindowStation();

            if (!SetProcessWindowStation(station))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                desktop = OpenDesktopW(desktopName, 0, inherit: false, ReadControl);
            }
            finally
            {
                SetProcessWindowStation(previous);
            }

            if (desktop == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            CloseWindowStation(station);
        }

        try
        {
            return DaclOf(desktop);
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    private static nint OpenOwnStation()
    {
        nint station = CreateWindowStationW(null, 0, ReadControl, 0);

        if (station == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return station;
    }

    private static string DaclOf(nint userObject)
    {
        uint error = GetSecurityInfo(userObject, WindowObject, DaclInformation, 0, 0, 0, 0, out nint descriptor);

        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }

        try
        {
            if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(descriptor, SddlRevision, DaclInformation, out nint text, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                return Marshal.PtrToStringUni(text)!;
            }
            finally
            {
                LocalFree(text);
            }
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static unsafe string NameOf(nint userObject)
    {
        char[] name = new char[256];
        uint needed;

        fixed (char* start = name)
        {
            if (userObject == 0 || !GetUserObjectInformationW(userObject, NameIndex, start, (uint)(name.Length * sizeof(char)), out needed))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        // The length counts bytes, the closing null included.
        return new string(name, 0, ((int)needed / sizeof(char)) - 1);
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetProcessWindowStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWindowStation(nint station);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetThreadDesktop(uint thread);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(nint userObject, int index, char* information, uint length, out uint needed);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowStationW(string? name, uint flags, uint access, nint attributes);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenDesktopW(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseWindowStation(nint station);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(nint desktop);

    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityInfo(
        nint handle,
        int objectType,
        uint information,
        nint owner,
        nint group,
        nint dacl,
        nint sacl,
        out nint descriptor);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
        nint descriptor,
        uint revision,
        uint information,
        out nint text,
        out uint length);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
