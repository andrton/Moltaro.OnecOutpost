using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The window station of the process of the tests and the desktop of the calling thread, as "{station}\{desktop}", the
// form in which the test program's script desktop names its own. The handles that Windows gives for them are not to be
// closed.
[SupportedOSPlatform("windows")]
internal static partial class TestDesktop
{
    // UOI_NAME of GetUserObjectInformationW.
    private const int NameIndex = 2;

    public static string OfTheTests()
    {
        return $@"{NameOf(GetProcessWindowStation())}\{NameOf(GetThreadDesktop(GetCurrentThreadId()))}";
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
    private static partial nint GetThreadDesktop(uint thread);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserObjectInformationW(nint userObject, int index, char* information, uint length, out uint needed);
}
