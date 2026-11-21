using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// The codes of <see cref="OutpostOutcome.Code"/>: why a command failed, was refused or ended as unknown.
/// </summary>
/// <remarks>
/// <para>
/// Each code belongs to the kinds its constant names, and the factories of <see cref="OutpostOutcome"/> refuse a code
/// for any other kind. Every <see cref="OutpostOutcomeKindEnum.Refused"/>, like
/// <see cref="OutpostOutcomeKindEnum.Expired"/>, means that nothing ran. A command that ended as Refused or Expired
/// frees its caller key; a refusal with <see cref="CallerKeyConflict"/> created no command and leaves the key with the
/// command that holds it.
/// </para>
/// <para>
/// There is no code for a command whose signature does not verify: the agent cannot trust anything in such a command,
/// its identity included, so it only records it in its audit log and reports nothing. The server then ends the command
/// as its take deadline and the agent's polls say.
/// </para>
/// </remarks>
public static class OutpostCodes
{
    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>: 1C raised an error, or the script of a Read or a Write began a
    /// transaction of its own and left it open, which the executor rolled back. A Write was rolled back.
    /// </summary>
    /// <remarks>
    /// <see cref="OutpostOutcome.ErrorText"/> holds the text of the error that 1C raised, or, for a transaction the
    /// script left open, a sentence of the executor's own, since 1C raised nothing then.
    /// </remarks>
    public const string ScriptError = OutpostOutcomeCodes.ScriptError;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> or <see cref="OutpostOutcomeKindEnum.Unknown"/>: the result holds a
    /// value that cannot be written as JSON, such as a reference, or nests deeper than 64 levels; or the executor
    /// handed the agent an outcome that the agent cannot accept.
    /// </summary>
    /// <remarks>
    /// Failed when the executor found it before the commit, and a Write was rolled back; also for a Read whose
    /// outcome the agent could not accept. Unknown for a Write whose outcome the agent could not accept, since the
    /// Write may have committed.
    /// </remarks>
    public const string ResultType = OutpostOutcomeCodes.ResultType;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> or <see cref="OutpostOutcomeKindEnum.Unknown"/>: the outcome is
    /// larger than <see cref="OutpostServerOptions.OutcomeSizeLimit"/>.
    /// </summary>
    /// <remarks>
    /// Failed when the executor found it before the commit, and a Write was rolled back; also for a Read whose outcome
    /// the server, or something on the path, refused for its size. Unknown for a Write whose outcome was refused that
    /// way, since the Write may have committed.
    /// </remarks>
    public const string ResultTooLarge = OutpostOutcomeCodes.ResultTooLarge;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> for a Read, <see cref="OutpostOutcomeKindEnum.Unknown"/> for a
    /// Write: the script exceeded the run limit that the local policy gives its mode, and ended with its worker.
    /// </summary>
    public const string RunLimit = OutpostOutcomeCodes.RunLimit;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>: a Read whose agent or worker stopped while it ran, whose agent
    /// stayed away longer than its run limit, or whose agent was revoked.
    /// </summary>
    public const string Lost = OutpostOutcomeCodes.Lost;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the caller key names another command of the base, with another
    /// mode, script or parameters.
    /// </summary>
    public const string CallerKeyConflict = OutpostOutcomeCodes.CallerKeyConflict;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: no agent holds the base key.
    /// </summary>
    public const string UnknownBase = OutpostOutcomeCodes.UnknownBase;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the agent that holds the base does not open the mode.
    /// </summary>
    public const string ModeClosed = OutpostOutcomeCodes.ModeClosed;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the local policy of the agent does not serve the base or the
    /// mode.
    /// </summary>
    public const string NotServed = OutpostOutcomeCodes.NotServed;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the command names another agent.
    /// </summary>
    public const string NotAddressed = OutpostOutcomeCodes.NotAddressed;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the agent does not support the protocol version of the command.
    /// </summary>
    public const string ProtocolVersion = OutpostOutcomeCodes.ProtocolVersion;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the command carries the server's signature and the protocol
    /// version of the agent, but breaks the rules of a command of the protocol, so the agent did not run it.
    /// </summary>
    public const string Malformed = OutpostOutcomeCodes.Malformed;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: by the agent's clock, the take deadline of the command passed
    /// longer than the skew ago.
    /// </summary>
    public const string Expired = OutpostOutcomeCodes.Expired;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: by the agent's clock, the command was issued further ahead than
    /// the skew.
    /// </summary>
    public const string ClockSkew = OutpostOutcomeCodes.ClockSkew;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/> or <see cref="OutpostOutcomeKindEnum.Unknown"/>: the agent was
    /// revoked.
    /// </summary>
    /// <remarks>
    /// Refused when no poll had taken the command; Unknown for a Write that a poll had taken. A Read that a poll had
    /// taken fails with <see cref="Lost"/>.
    /// </remarks>
    public const string Revoked = OutpostOutcomeCodes.Revoked;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/>: a Write whose agent stayed away longer than its run limit.
    /// </summary>
    public const string Disconnected = OutpostOutcomeCodes.Disconnected;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/>: a Write whose agent or worker stopped while it ran.
    /// </summary>
    public const string AgentStopped = OutpostOutcomeCodes.AgentStopped;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/>: the script ended the transaction of its Write itself, with
    /// <c>CommitTransaction</c> or <c>RollbackTransaction</c>, or the commit of the Write raised, so the executor
    /// cannot tell whether it was written.
    /// </summary>
    public const string TransactionEnded = OutpostOutcomeCodes.TransactionEnded;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>, for a Read and a Write: the COM worker of the agent has no session
    /// in the base, or could not open the executor, so the script did not start.
    /// </summary>
    /// <remarks>
    /// Examples are a wrong password, a cluster that does not answer, no free licence, and a COM connector that is not
    /// registered. <see cref="OutpostOutcome.ErrorText"/> is <see langword="null"/>: the text of 1C goes to the audit
    /// log of the agent only, since it may name an account or a path.
    /// </remarks>
    public const string BaseUnreachable = OutpostOutcomeCodes.BaseUnreachable;

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> for a Read, <see cref="OutpostOutcomeKindEnum.Unknown"/> for a
    /// Write: the session of the COM worker in the base ended while the script ran, for example because the cluster
    /// restarted.
    /// </summary>
    public const string ConnectionLost = OutpostOutcomeCodes.ConnectionLost;
}
