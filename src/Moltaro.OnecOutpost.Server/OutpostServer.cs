using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server.Signing;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server;

// The IOutpostServer that AddOutpostServer registers. Each member checks its arguments, in the order of the rules of
// names, and then its token: a token that is cancelled already gives a cancelled task before the store is opened. The
// members of pairing, the list of agents, revocation and release give what the store gives.
//
// A Read or a Write finds the command that its caller key names, or creates a signed one for the agent that holds its
// base, and then waits for its outcome (concept rules 4, 6, 7, 11, 12). Nodes share nothing in memory (principle 6): a
// call learns of an outcome through the signals, on the key of its command, and then reads the store, and the poll of
// the agent learns of a new command on the key of the agent. Each set comes after the commit it announces. A set that
// the Signals host did not confirm is ignored: every waiter reads the store again by a deadline of its own.
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

    // The calls that wait for the commands that the revocation ended learn of it once it is committed (rule 13).
    public async Task<bool> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Revocation revocation = await _store.RevokeAgentAsync(agentId, cancellationToken);

        foreach (Guid commandId in revocation.Ended)
        {
            await _signals.SetAsync(_options.SignalKey(commandId), cancellationToken);
        }

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
        JsonDocument parameters = CommandChecks.Check(request.Script, request.Parameters, _options.CommandSizeLimit);

        return CallAsync(
            OutpostModeEnum.Read,
            request.BaseKey,
            request.CallerKey,
            request.Script,
            parameters,
            cancellationToken);
    }

    public Task<OutpostOutcome> WriteAsync(OutpostWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OutpostNames.CheckBaseKey(request.BaseKey, nameof(request.BaseKey));
        OutpostNames.CheckCallerKey(request.CallerKey, nameof(request.CallerKey));
        OutpostNames.CheckScript(request.Script, nameof(request.Script));
        JsonDocument parameters = CommandChecks.Check(request.Script, request.Parameters, _options.CommandSizeLimit);

        return CallAsync(
            OutpostModeEnum.Write,
            request.BaseKey,
            request.CallerKey,
            request.Script,
            parameters,
            cancellationToken);
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

    // A Read or a Write after the checks of its arguments, in the order of IOutpostServer: the caller key, the base,
    // the agent, and then the wait for the outcome. A refusal of the server creates no command and names none. The
    // parameters, as the call wrote them, are kept only until the command holds them: the wait can be long.
    private async Task<OutpostOutcome> CallAsync(
        OutpostModeEnum mode,
        string baseKey,
        string? callerKey,
        string script,
        JsonDocument parameters,
        CancellationToken cancellationToken)
    {
        Guid commandId;

        using (parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string parametersText = parameters.RootElement.GetRawText();
            CommandMatch? command = null;

            // Rule 4: a key that names a command makes the call follow it, before any check of its base or agent.
            if (callerKey is not null)
            {
                command = await _store.FindCallerKeyAsync(
                    baseKey,
                    callerKey,
                    mode,
                    script,
                    parametersText,
                    cancellationToken);
            }

            if (command is null)
            {
                BaseHolder? holder = await _store.FindBaseAsync(baseKey, cancellationToken);

                if (holder is null)
                {
                    return OutpostOutcome.Refused(null, OutpostCodes.UnknownBase);
                }

                // The modes are those of the newest poll of the holder: every key it declares opens Read, and Write
                // where it opens it (§6.2), so a key it no longer declares opens neither.
                bool open = mode == OutpostModeEnum.Write ? holder.WriteOpen : holder.Declared;

                if (!open)
                {
                    return OutpostOutcome.Refused(null, OutpostCodes.ModeClosed);
                }

                // Rule 6: no command for an agent that is not connected. The store decides it again when it creates
                // the command, under the lock of the agent row.
                if (holder.Connected)
                {
                    command = await CreateCommandAsync(
                        holder.AgentId,
                        mode,
                        baseKey,
                        callerKey,
                        script,
                        parameters.RootElement,
                        cancellationToken);
                }

                // Rule 4: a call whose key names a command never gets Unavailable, so a command that another call
                // created with the key meanwhile is followed.
                if (command is null && callerKey is not null)
                {
                    command = await _store.FindCallerKeyAsync(
                        baseKey,
                        callerKey,
                        mode,
                        script,
                        parametersText,
                        cancellationToken);
                }

                if (command is null)
                {
                    return OutpostOutcome.Unavailable();
                }
            }

            if (!command.Same)
            {
                return OutpostOutcome.Refused(null, OutpostCodes.CallerKeyConflict);
            }

            commandId = command.Id;
        }

        return await WaitForOutcomeAsync(commandId, cancellationToken);
    }

    // Writes the command of §7, signs its bytes once and stores them as they are, with the deadline and the skew that
    // were signed; or finds the command that holds its base and caller key. Null when the agent is no longer connected
    // or was revoked (rules 6, 13). Times and durations are truncated to the millisecond, as §2 writes them, so that
    // the store judges the command by the values the agent reads.
    private async Task<CommandMatch?> CreateCommandAsync(
        Guid agentId,
        OutpostModeEnum mode,
        string baseKey,
        string? callerKey,
        string script,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        DateTimeOffset issued = ToMilliseconds(_options.TimeProvider.GetUtcNow());
        DateTimeOffset deadline = ToMilliseconds(issued + _options.TakeTimeout);
        TimeSpan skew = _options.ClockSkew - TimeSpan.FromTicks(_options.ClockSkew.Ticks % TimeSpan.TicksPerMillisecond);

        OutpostCommandMessage message = new(
            OutpostProtocol.Version,
            Guid.NewGuid(),
            agentId,
            baseKey,
            mode,
            callerKey,
            script,
            parameters,
            issued,
            deadline,
            skew,
            _options.OutcomeSizeLimit);

        byte[] bytes = OutpostCodec.Write(message);

        CommandRow command = new()
        {
            Id = message.Id,
            AgentId = agentId,
            BaseKey = baseKey,
            Mode = mode,
            CallerKey = callerKey,
            Bytes = bytes,
            Signature = _signingKey.SignCommand(bytes),
            Deadline = deadline,
            Skew = skew,
            OutcomeLimit = message.OutcomeLimit,
            Phase = CommandPhaseEnum.Waiting,
        };

        CommandMatch? match = await _store.CreateCommandAsync(command, cancellationToken);

        // The held poll of the agent takes the new command once it is woken.
        if (match is { Created: true })
        {
            await _signals.SetAsync(_options.SignalKey(agentId), cancellationToken);
        }

        return match;
    }

    // Step 4 of a call: subscribe to the key of the command, then pass, wait, pass, until a pass reads its outcome
    // (rule 12). A pass makes the change that time made due and reads the outcome, or when time can next end the
    // command. Each wait ends when it is woken, when the command is due, or PollTimeout after it starts, so that a set
    // that was lost costs at most that; the deadline is on the clock of the options, which the Signals host does not
    // share. After any wait the caller's token is checked first: a cancelled call leaves its command as it is.
    private async Task<OutpostOutcome> WaitForOutcomeAsync(Guid commandId, CancellationToken cancellationToken)
    {
        // A group of its own, so that a set of the key wakes every call that waits for the command. A set that came
        // before the subscription was open on the host is covered by the pass that follows its Joined.
        using ISignalSubscription wakes = _signals.Subscribe(
            _options.SignalKey(commandId),
            Guid.NewGuid().ToString("N"));

        while (true)
        {
            CommandPass pass = await _store.PassCommandAsync(commandId, cancellationToken);

            if (pass.Outcome is not null)
            {
                return pass.Outcome;
            }

            // A Due that has passed gives a delay of zero, and the next pass makes the change it made due.
            TimeSpan untilDue = pass.Due - _options.TimeProvider.GetUtcNow();
            TimeSpan delay = untilDue < _options.PollTimeout ? untilDue : _options.PollTimeout;

            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            using CancellationTokenSource waitEnds = new(delay, _options.TimeProvider);
            using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                waitEnds.Token);

            try
            {
                await wakes.WaitAsync(null, wait.Token);
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested)
            {
                // The wait is over, by its time or by the caller, whom the check below answers.
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static DateTimeOffset ToMilliseconds(DateTimeOffset time)
    {
        return time.AddTicks(-(time.Ticks % TimeSpan.TicksPerMillisecond));
    }
}
