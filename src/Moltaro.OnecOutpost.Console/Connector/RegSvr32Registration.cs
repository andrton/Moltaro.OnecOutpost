using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Connector;

namespace Moltaro.OnecOutpost.Console.Connector;

// The regsvr32 of Windows: %SystemRoot%\System32\regsvr32.exe for 64 bits and %SystemRoot%\SysWOW64\regsvr32.exe for
// 32 bits, the console being a 64-bit program.
[SupportedOSPlatform("windows")]
internal sealed class RegSvr32Registration : IConnectorRegistration
{
    public string CommandOf(ConnectorBitnessEnum bitness, string path)
    {
        throw new NotSupportedException();
    }

    public Task<int> RegisterAsync(ConnectorBitnessEnum bitness, string path, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
