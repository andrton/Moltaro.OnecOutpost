using Moltaro.OnecOutpost.Agent.Connector;

namespace Moltaro.OnecOutpost.Console.Connector;

// The registration of a COM connector by the regsvr32 of its bitness: the boundary that RegSvr32Registration
// implements and tests replace.
internal interface IConnectorRegistration
{
    // The command that registers the file, as an administrator would type it, without /s.
    string CommandOf(ConnectorBitnessEnum bitness, string path);

    // Runs regsvr32 /s for the file and gives its exit code, waiting with the token and no deadline, since /s ends when
    // DllRegisterServer returns. A regsvr32 that cannot be started is a Win32Exception.
    Task<int> RegisterAsync(ConnectorBitnessEnum bitness, string path, CancellationToken cancellationToken);
}
