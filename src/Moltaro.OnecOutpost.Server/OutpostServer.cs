using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server.Signing;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server;

// The IOutpostServer that AddOutpostServer registers. Each member checks its arguments, in the order of the rules of
// names, and then its token: a token that is cancelled already gives a cancelled task before the store is opened. The
// members of pairing, the list of agents, revocation and release give what the store gives. Read and Write refuse:
// the commands that make them work come with S5, which uses the signals.
internal sealed class OutpostServer : IOutpostServer
{
    // The secret of a pairing token: 16 random bytes, 128 bits (§5 of docs/protocol.md).
    private const int PairingSecretLength = 16;

    // The validated copy that AddOutpostServer keeps; nothing changes it.
    private readonly OutpostServerOptions _options;

    private readonly ISignals _signals;

    private readonly OutpostStore _store;

    private readonly ServerSigningKey _signingKey;

    internal OutpostServer(
        OutpostServerOptions options,
        ISignals signals,
        OutpostStore store,
        ServerSigningKey signingKey)
    {
        _options = options;
        _signals = signals;
        _store = store;
        _signingKey = signingKey;
    }

    public Task<OutpostPairingToken> CreatePairingTokenAsync(string agentName, CancellationToken cancellationToken)
    {
        OutpostNames.CheckAgentName(agentName, nameof(agentName));

        return CreatePairingTokenCoreAsync(agentName, cancellationToken);
    }

    // Without arguments to check, the whole member runs in its task, so a cancelled token gives a cancelled task.
    public async Task<IReadOnlyList<OutpostAgent>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _store.ListAgentsAsync(cancellationToken);
    }

    public async Task<bool> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Revocation revocation = await _store.RevokeAgentAsync(agentId, cancellationToken);

        return revocation.Revoked;
    }

    public Task<OutpostReleaseResultEnum> ReleaseBaseAsync(string baseKey, CancellationToken cancellationToken)
    {
        OutpostNames.CheckBaseKey(baseKey, nameof(baseKey));

        return ReleaseBaseCoreAsync(baseKey, cancellationToken);
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

    // The core of a member with arguments runs after their checks, which the call itself throws. An exception inside an
    // async method ends its task, so a cancelled token gives a cancelled task here.
    private async Task<OutpostPairingToken> CreatePairingTokenCoreAsync(
        string agentName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Guid agentId = Guid.NewGuid();
        byte[] secret = RandomNumberGenerator.GetBytes(PairingSecretLength);

        // Truncated to whole microseconds, the step of a timestamptz, so that ExpiresAt is the expiry the store keeps:
        // a later one would promise a moment at which the token no longer works.
        DateTimeOffset expiresAt = _options.TimeProvider.GetUtcNow() + _options.PairingTokenLifetime;
        expiresAt = expiresAt.AddTicks(-(expiresAt.Ticks % TimeSpan.TicksPerMicrosecond));

        await _store.CreatePairingTokenAsync(agentId, agentName, secret, expiresAt, cancellationToken);

        string token = new OutpostPairingTokenText(agentId, secret, _signingKey.Fingerprint).Write();

        return new OutpostPairingToken(agentId, token, expiresAt);
    }

    private async Task<OutpostReleaseResultEnum> ReleaseBaseCoreAsync(
        string baseKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _store.ReleaseBaseAsync(baseKey, cancellationToken);
    }
}
