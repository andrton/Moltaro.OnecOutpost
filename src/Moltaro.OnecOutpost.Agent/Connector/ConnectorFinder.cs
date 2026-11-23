using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Moltaro.OnecOutpost.Agent.Connector;

// The one finder of the COM connector, from the classes it is given, never HKEY_CLASSES_ROOT, which merges in the
// classes of the user who runs it, unseen by the service: in each view, 64 bits only on a 64-bit Windows, the default
// value of {progId}\CLSID gives the class identifier and that of CLSID\{class}\InprocServer32 the path, expanded, which
// is Registered, with the FileVersion of its file, only when that file exists. Unregistered holds each other
// {platformFolder}\*\bin\comcntr.dll, of the bitness that the Machine of its PE header gives, by ordinal path. The
// classes of each view are the caller's, which keeps them open as long as the finder. Find writes nothing and keeps
// nothing between two calls, so several slots may call it at once. Limitation: a platform installed outside the folders
// given is found only by its registration.
[SupportedOSPlatform("windows")]
internal sealed class ConnectorFinder : IConnectorFinder
{
    // The ProgID of the COM connector that the platform registers.
    public const string ComConnector = "V83.COMConnector";

    // The folder under the program files folders where the platform installs each of its versions.
    private const string PlatformFolder = "1cv8";

    // The file of the connector in the bin folder of each version of the platform.
    private const string ConnectorFileName = "comcntr.dll";

    // Where a PE file keeps the offset of its PE header, the signature "PE\0\0" that begins that header, and the values of
    // the Machine that follows it for 64 and for 32 bits.
    private const int HeaderOffsetPosition = 0x3C;

    private const uint PeSignature = 0x00004550;

    private const ushort Machine64 = 0x8664;

    private const ushort Machine32 = 0x14C;

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
    // both program files folders, once each, since a 32-bit Windows names one folder for both. Its keys stay open for
    // the life of the program.
    public static ConnectorFinder Machine()
    {
        string[] platformFolders =
        [
            .. new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), PlatformFolder),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), PlatformFolder),
            }.Distinct(StringComparer.OrdinalIgnoreCase),
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
        List<(ConnectorBitnessEnum Bitness, ConnectorFile File)> copies = Copies();
        List<ConnectorFinding> findings = [];

        if (Environment.Is64BitOperatingSystem)
        {
            findings.Add(FindingOf(ConnectorBitnessEnum.X64, Registered(_classes64), copies));
        }

        findings.Add(FindingOf(ConnectorBitnessEnum.X86, Registered(_classes32), copies));

        return findings;
    }

    // HKLM's SOFTWARE\Classes in the view, which every Windows has.
    private static RegistryKey ClassesOf(RegistryView view)
    {
        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

        return machine.OpenSubKey(@"SOFTWARE\Classes")!;
    }

    // The copies of the bitness other than the one registered, in the order of their paths.
    private static ConnectorFinding FindingOf(
        ConnectorBitnessEnum bitness,
        ConnectorFile? registered,
        List<(ConnectorBitnessEnum Bitness, ConnectorFile File)> copies)
    {
        List<ConnectorFile> unregistered =
        [
            .. copies
                .Where(copy => copy.Bitness == bitness && !IsRegistered(copy.File, registered))
                .Select(copy => copy.File),
        ];

        return new ConnectorFinding(bitness, registered, unregistered);
    }

    // Windows names a file ignoring case, and a registration may name it by a path that is not full.
    private static bool IsRegistered(ConnectorFile copy, ConnectorFile? registered)
    {
        return registered is not null
            && string.Equals(Path.GetFullPath(copy.Path), Path.GetFullPath(registered.Path), StringComparison.OrdinalIgnoreCase);
    }

    // The connector registered in the classes of one view: a class identifier as the default value of
    // {progId}\CLSID, and CLSID\{that}\InprocServer32 with a default value, expanded when the registry keeps it so, that
    // names a file that exists. A key or a file that cannot be read is not registered.
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

            if (server?.GetValue("") is not string path || path.Length == 0 || !File.Exists(path))
            {
                return null;
            }

            return new ConnectorFile(path, VersionOf(path));
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // Each {platformFolder}\*\bin\comcntr.dll whose PE header names 64 or 32 bits, by ordinal path. A folder or a file
    // that cannot be read is left out.
    private List<(ConnectorBitnessEnum Bitness, ConnectorFile File)> Copies()
    {
        List<(ConnectorBitnessEnum Bitness, ConnectorFile File)> copies = [];

        foreach (string platformFolder in _platformFolders)
        {
            string[] versions;

            try
            {
                versions = Directory.GetDirectories(platformFolder);
            }
            catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (string version in versions)
            {
                string path = Path.Combine(version, "bin", ConnectorFileName);

                try
                {
                    if (File.Exists(path) && BitnessOf(path) is ConnectorBitnessEnum bitness)
                    {
                        copies.Add((bitness, new ConnectorFile(path, VersionOf(path))));
                    }
                }
                catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
                {
                    // Left out, as Find says.
                }
            }
        }

        return [.. copies.OrderBy(copy => copy.File.Path, StringComparer.Ordinal)];
    }

    // The bitness that the Machine of the file's PE header names, null for any other Machine or a file that is not a PE
    // file; a file shorter than its header is an EndOfStreamException, an IOException.
    private static ConnectorBitnessEnum? BitnessOf(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using BinaryReader reader = new(stream);

        stream.Position = HeaderOffsetPosition;
        int headerOffset = reader.ReadInt32();

        if (headerOffset < 0)
        {
            return null;
        }

        stream.Position = headerOffset;

        if (reader.ReadUInt32() != PeSignature)
        {
            return null;
        }

        return reader.ReadUInt16() switch
        {
            Machine64 => ConnectorBitnessEnum.X64,
            Machine32 => ConnectorBitnessEnum.X86,
            _ => null,
        };
    }

    // The FileVersion of the file, such as 8.3.17.1851, empty when it has none.
    private static string VersionOf(string path)
    {
        return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "";
    }
}
