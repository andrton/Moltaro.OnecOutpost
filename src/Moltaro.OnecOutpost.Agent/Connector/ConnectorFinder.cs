using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Moltaro.OnecOutpost.Agent.Connector;

// The one finder of the COM connector, from the classes it is given, never HKEY_CLASSES_ROOT, which merges in the
// classes of the user who runs it, unseen by the service: in each view, 64 bits only on a 64-bit Windows, the default
// value of {progId}\CLSID gives the class identifier and that of CLSID\{class}\InprocServer32 the path. The classes of
// each view and the folders of the platform are the caller's, which keeps them open as long as the finder.
//
// Until S10a's implementation (the S10 brief, "The steps"), Find reads Registered as the launcher read the registry
// before: a registration counts without its file being checked, its version is empty, and Unregistered is empty.
[SupportedOSPlatform("windows")]
internal sealed class ConnectorFinder : IConnectorFinder
{
    // The ProgID of the COM connector that the platform registers.
    public const string ComConnector = "V83.COMConnector";

    // The folder under the program files folders where the platform installs each of its versions.
    private const string PlatformFolder = "1cv8";

    private readonly RegistryKey _classes64;

    private readonly RegistryKey _classes32;

    private readonly string _progId;

    private readonly IReadOnlyList<string> _platformFolders;

    // The classes of the 64-bit and the 32-bit view, such as HKLM's SOFTWARE\Classes opened in each, the ProgID of the
    // connector, and the folders that hold the platform's versions, each version's comcntr.dll in {version}\bin.
    public ConnectorFinder(RegistryKey classes64, RegistryKey classes32, string progId, IReadOnlyList<string> platformFolders)
    {
        _classes64 = classes64;
        _classes32 = classes32;
        _progId = progId;
        _platformFolders = platformFolders;
    }

    // The finder of the machine, as the service sees it: HKLM's SOFTWARE\Classes in both views, and the 1cv8 folders of
    // both program files folders. Its keys stay open for the life of the program.
    public static ConnectorFinder Machine()
    {
        string[] platformFolders =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), PlatformFolder),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), PlatformFolder),
        ];

        return new ConnectorFinder(
            ClassesOf(RegistryView.Registry64),
            ClassesOf(RegistryView.Registry32),
            ComConnector,
            platformFolders);
    }

    // A 32-bit Windows shows its one view as the view of 64 bits too, so 64 bits are asked of a 64-bit Windows only.
    public IReadOnlyList<ConnectorFinding> Find()
    {
        List<ConnectorFinding> findings = [];

        if (Environment.Is64BitOperatingSystem)
        {
            findings.Add(new ConnectorFinding(ConnectorBitnessEnum.X64, Registered(_classes64), []));
        }

        findings.Add(new ConnectorFinding(ConnectorBitnessEnum.X86, Registered(_classes32), []));

        return findings;
    }

    // HKLM's SOFTWARE\Classes in the view, which every Windows has.
    private static RegistryKey ClassesOf(RegistryView view)
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

        return machine.OpenSubKey(@"SOFTWARE\Classes")!;
    }

    // The connector registered in the classes of one view: a class identifier as the default value of
    // {progId}\CLSID, and CLSID\{that}\InprocServer32 with a default value that is not empty. A key that cannot be read
    // is not registered.
    private ConnectorFile? Registered(RegistryKey classes)
    {
        try
        {
            using RegistryKey? progId = classes.OpenSubKey($@"{_progId}\CLSID");

            if (progId?.GetValue("") is not string classId || !Guid.TryParse(classId, out _))
            {
                return null;
            }

            using RegistryKey? server = classes.OpenSubKey($@"CLSID\{classId}\InprocServer32");

            if (server?.GetValue("") is not string path || path.Length == 0)
            {
                return null;
            }

            return new ConnectorFile(path, "");
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
