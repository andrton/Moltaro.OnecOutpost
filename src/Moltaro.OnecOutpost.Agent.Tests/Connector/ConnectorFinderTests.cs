using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Connector;

// The one finder of the COM connector, which the launcher and the console use (the S10 brief, "From the architect's
// review 1" and "ConnectorFinder"), with Requires=Windows: over the classes of the current user in both views, which
// TestConnector registers, as the finder of the machine reads HKLM's, and a temporary folder of the platform. Find gives
// a finding for 64 bits, then one for 32 bits. In each view {progId}\CLSID and CLSID\{class}\InprocServer32 give the
// path, expanded, which is Registered, with the FileVersion of its file, only when its file exists. Unregistered holds
// each other {platformFolder}\*\bin\comcntr.dll, of the bitness that the Machine of its PE header gives, 0x8664 for 64
// bits and 0x14C for 32, with its FileVersion, by ordinal path; a folder that cannot be read is left out, and Find throws
// nothing. The folder of the platform holds copies of the test worker's program, whose header is that of 64 bits on the
// 64-bit Windows of the tests, and of its assembly, whose header is that of 32 bits as for any assembly built for any
// CPU, each named comcntr.dll.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class ConnectorFinderTests : IDisposable
{
    // The variable of the environment that an expandable registration names, this test's own.
    private readonly string _variable = $"OUTPOST_CONNECTOR_TEST_{Guid.NewGuid():N}";

    private readonly TemporaryFolder _folder = new();

    private readonly string _platform;

    private readonly TestConnector _connector = TestConnector.Register();

    public ConnectorFinderTests()
    {
        _platform = _folder.PathOf("1cv8");
        Environment.SetEnvironmentVariable(_variable, _folder.Path);
    }

    // A copy of a program of 64 bits, and of one of 32 bits.
    private static string Program64 => Path.Combine(AppContext.BaseDirectory, "Moltaro.OnecOutpost.TestWorker.exe");

    private static string Program32 => Path.Combine(AppContext.BaseDirectory, "Moltaro.OnecOutpost.TestWorker.dll");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_variable, null);
        _connector.Dispose();
        _folder.Dispose();
    }

    // The reading that the launcher had before S10a, which the finder keeps: a registration in a view picks that view's
    // bitness, a path that the registry expands expanded.
    [Fact]
    public void Find_gives_a_finding_for_64_bits_then_one_for_32_bits_each_with_the_path_registered_in_its_view_expanded()
    {
        string file = Copy(Program64, @"8.3.27.1859\bin");
        _connector.RegisterIn(RegistryView.Registry64, $@"%{_variable}%\1cv8\8.3.27.1859\bin\comcntr.dll", RegistryValueKind.ExpandString);

        IReadOnlyList<ConnectorFinding> findings = _connector.FinderOver([]).Find();

        Assert.Equal([ConnectorBitnessEnum.X64, ConnectorBitnessEnum.X86], findings.Select(finding => finding.Bitness));
        Assert.Equal(file, findings[0].Registered?.Path, ignoreCase: true);
        Assert.Null(findings[1].Registered);
    }

    [Fact]
    public void A_registration_carries_the_FileVersion_of_its_file()
    {
        string file = Copy(Program32, @"8.3.25.1000\bin");
        _connector.RegisterIn(RegistryView.Registry32, file, RegistryValueKind.String);

        ConnectorFile registered = Assert.IsType<ConnectorFile>(Finding(_connector.FinderOver([]), ConnectorBitnessEnum.X86).Registered);

        Assert.Equal(file, registered.Path, ignoreCase: true);
        Assert.Equal(FileVersionOf(Program32), registered.Version);
    }

    // The agent's behavior changes here (the S10 brief, a draft for the owner): a registration whose file is gone no
    // longer picks that bitness.
    [Fact]
    public void A_registration_whose_file_is_gone_is_not_Registered()
    {
        string gone = Copy(Program64, @"8.3.27.1859\bin");
        File.Delete(gone);
        _connector.RegisterIn(RegistryView.Registry64, gone, RegistryValueKind.String);

        Assert.Null(Finding(_connector.FinderOver([_platform]), ConnectorBitnessEnum.X64).Registered);
    }

    // A copy outside a bin folder is not a copy of the platform, and a folder of the platform that does not exist, as
    // Program Files (x86) on a machine without a 32-bit platform, gives nothing.
    [Fact]
    public void Unregistered_holds_each_other_copy_in_a_bin_folder_of_the_platform_by_the_bitness_of_its_header_and_by_ordinal_path()
    {
        string registered = Copy(Program64, @"8.3.27.1859\bin");
        string older64 = Copy(Program64, @"8.3.23.2000\bin");
        string newer64 = Copy(Program64, @"8.3.24.1691\bin");
        string only32 = Copy(Program32, @"8.3.25.1000\bin");
        Copy(Program64, "8.3.26.1");
        _connector.RegisterIn(RegistryView.Registry64, registered, RegistryValueKind.String);
        ConnectorFinder finder = _connector.FinderOver([_platform, _folder.PathOf("absent")]);

        ConnectorFinding x64 = Finding(finder, ConnectorBitnessEnum.X64);
        ConnectorFinding x86 = Finding(finder, ConnectorBitnessEnum.X86);

        Assert.Equal(registered, x64.Registered?.Path, ignoreCase: true);
        Assert.Equal(
            [(older64, FileVersionOf(Program64)), (newer64, FileVersionOf(Program64))],
            x64.Unregistered.Select(file => (file.Path, file.Version)));
        Assert.Null(x86.Registered);
        Assert.Equal([(only32, FileVersionOf(Program32))], x86.Unregistered.Select(file => (file.Path, file.Version)));
    }

    private static ConnectorFinding Finding(ConnectorFinder finder, ConnectorBitnessEnum bitness)
    {
        return Assert.Single(finder.Find(), finding => finding.Bitness == bitness);
    }

    private static string FileVersionOf(string file)
    {
        return FileVersionInfo.GetVersionInfo(file).FileVersion ?? "";
    }

    // Copies the program as comcntr.dll into the folder under the platform's, which it creates, and gives its full path.
    private string Copy(string program, string folder)
    {
        string target = Path.Combine(_platform, folder);
        Directory.CreateDirectory(target);

        string file = Path.Combine(target, "comcntr.dll");
        File.Copy(program, file);

        return file;
    }
}
