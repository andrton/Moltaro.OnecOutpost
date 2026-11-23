using System.Diagnostics;
using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Connector;

namespace Moltaro.OnecOutpost.Console.Connector;

// The regsvr32 of Windows: %SystemRoot%\System32\regsvr32.exe for 64 bits and %SystemRoot%\SysWOW64\regsvr32.exe for
// 32 bits, the console being a 64-bit program; on a 32-bit Windows, whose System32 holds the regsvr32 of 32 bits, that
// one. It runs with the console's own token, so it registers in HKLM when the console is elevated.
[SupportedOSPlatform("windows")]
internal sealed class RegSvr32Registration : IConnectorRegistration
{
    public string CommandOf(ConnectorBitnessEnum bitness, string path)
    {
        return $"{ProgramOf(bitness)} \"{path}\"";
    }

    // regsvr32 /s ends when DllRegisterServer returns, so its run has no deadline; a cancel leaves the wait, not the run,
    // since a registration cut in the middle would be worse than one that ends.
    public async Task<int> RegisterAsync(ConnectorBitnessEnum bitness, string path, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new(ProgramOf(bitness))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("/s");
        start.ArgumentList.Add(path);

        using Process regsvr32 = Process.Start(start)!;

        await regsvr32.WaitForExitAsync(cancellationToken);

        return regsvr32.ExitCode;
    }

    private static string ProgramOf(ConnectorBitnessEnum bitness)
    {
        string folder = bitness == ConnectorBitnessEnum.X86 && Environment.Is64BitOperatingSystem ? "SysWOW64" : "System32";

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), folder, "regsvr32.exe");
    }
}
