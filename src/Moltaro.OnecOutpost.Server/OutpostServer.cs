using Moltaro.OnecOutpost.Protocol;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server;

// The IOutpostServer that AddOutpostServer registers. Each member checks its arguments, in the order of the rules of
// names, and then refuses: the store, the signals and the endpoints that make it work are not built yet.
internal sealed class OutpostServer : IOutpostServer
{
    // The validated copy that AddOutpostServer keeps; nothing changes it.
    private readonly OutpostServerOptions _options;

    private readonly ISignals _signals;

    internal OutpostServer(OutpostServerOptions options, ISignals signals)
    {
        _options = options;
        _signals = signals;
    }

    public Task<OutpostPairingToken> CreatePairingTokenAsync(string agentName, CancellationToken cancellationToken)
    {
        OutpostNames.CheckAgentName(agentName, nameof(agentName));

        throw new NotSupportedException();
    }

    public Task<IReadOnlyList<OutpostAgent>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<bool> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public Task<OutpostReleaseResultEnum> ReleaseBaseAsync(string baseKey, CancellationToken cancellationToken)
    {
        OutpostNames.CheckBaseKey(baseKey, nameof(baseKey));

        throw new NotSupportedException();
    }

    public Task<OutpostOutcome> ReadAsync(OutpostReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OutpostNames.CheckBaseKey(request.BaseKey, nameof(request.BaseKey));

        if (request.CallerKey is not null)
        {
            OutpostNames.CheckCallerKey(request.CallerKey, nameof(request.CallerKey));
        }

        OutpostNames.CheckScript(request.Script, nameof(request.Script));
        CommandChecks.Check(request.Script, request.Parameters, _options.CommandSizeLimit);

        throw new NotSupportedException();
    }

    public Task<OutpostOutcome> WriteAsync(OutpostWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OutpostNames.CheckBaseKey(request.BaseKey, nameof(request.BaseKey));
        OutpostNames.CheckCallerKey(request.CallerKey, nameof(request.CallerKey));
        OutpostNames.CheckScript(request.Script, nameof(request.Script));
        CommandChecks.Check(request.Script, request.Parameters, _options.CommandSizeLimit);

        throw new NotSupportedException();
    }
}
