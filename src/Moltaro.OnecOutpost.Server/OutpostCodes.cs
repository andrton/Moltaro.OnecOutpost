namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// The codes of <see cref="OutpostOutcome.Code"/>: why a command failed, was refused or ended as unknown.
/// </summary>
/// <remarks>
/// <para>
/// Each code belongs to the kinds its constant names, and the factories of <see cref="OutpostOutcome"/> refuse a code
/// for any other kind. Every <see cref="OutpostOutcomeKindEnum.Refused"/>, like
/// <see cref="OutpostOutcomeKindEnum.Expired"/>, means that nothing ran, and frees the caller key.
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
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>: 1C raised an error, whose text is in
    /// <see cref="OutpostOutcome.ErrorText"/>. A Write was rolled back.
    /// </summary>
    public const string ScriptError = "script-error";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>: the result holds a value that cannot be written as JSON, such as a
    /// reference. A Write was rolled back.
    /// </summary>
    public const string ResultType = "result-type";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> or <see cref="OutpostOutcomeKindEnum.Unknown"/>: the outcome is
    /// larger than <see cref="OutpostServerOptions.OutcomeSizeLimit"/>.
    /// </summary>
    /// <remarks>
    /// Failed when the executor found it before the commit, and a Write was rolled back; also for a Read whose outcome
    /// the server, or something on the path, refused for its size. Unknown for a Write whose outcome was refused that
    /// way, since the Write may have committed.
    /// </remarks>
    public const string ResultTooLarge = "result-too-large";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/> for a Read, <see cref="OutpostOutcomeKindEnum.Unknown"/> for a
    /// Write: the script exceeded the run limit that the local policy gives its mode, and ended with its worker.
    /// </summary>
    public const string RunLimit = "run-limit";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>: a Read whose agent or worker stopped while it ran, whose agent
    /// stayed away longer than its run limit, or whose agent was revoked.
    /// </summary>
    public const string Lost = "lost";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the caller key names another command of the base, with another
    /// mode, script or parameters.
    /// </summary>
    public const string CallerKeyConflict = "caller-key-conflict";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: no agent holds the base key.
    /// </summary>
    public const string UnknownBase = "unknown-base";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the agent that holds the base does not open the mode.
    /// </summary>
    public const string ModeClosed = "mode-closed";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the local policy of the agent does not serve the base or the
    /// mode.
    /// </summary>
    public const string NotServed = "not-served";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the command names another agent.
    /// </summary>
    public const string NotAddressed = "not-addressed";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: the agent does not support the protocol version of the command.
    /// </summary>
    public const string ProtocolVersion = "protocol-version";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: by the agent's clock, the take deadline of the command passed
    /// longer than the skew ago.
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/>: by the agent's clock, the command was issued further ahead than
    /// the skew.
    /// </summary>
    public const string ClockSkew = "clock-skew";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Refused"/> or <see cref="OutpostOutcomeKindEnum.Unknown"/>: the agent was
    /// revoked.
    /// </summary>
    /// <remarks>
    /// Refused when no poll had taken the command; Unknown for a Write that a poll had taken. A Read that a poll had
    /// taken fails with <see cref="Lost"/>.
    /// </remarks>
    public const string Revoked = "revoked";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/>: a Write whose agent stayed away longer than its run limit.
    /// </summary>
    public const string Disconnected = "disconnected";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/>: a Write whose agent or worker stopped while it ran.
    /// </summary>
    public const string AgentStopped = "agent-stopped";

    /// <summary>
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/>: the script ended the transaction of its Write itself, with
    /// <c>CommitTransaction</c> or <c>RollbackTransaction</c>, so whether it wrote anything cannot be known.
    /// </summary>
    public const string TransactionEnded = "transaction-ended";

    // Whether a code is one of the codes above that an outcome of this kind carries, by the table of codes. Run-limit
    // belongs to both Failed and Unknown, since only the mode of the command, which an outcome does not hold, decides
    // between them.
    internal static bool Fits(string code, OutpostOutcomeKindEnum kind)
    {
        return kind switch
        {
            OutpostOutcomeKindEnum.Failed => code is ScriptError or ResultType or ResultTooLarge or RunLimit or Lost,
            OutpostOutcomeKindEnum.Refused => code
                is CallerKeyConflict
                or UnknownBase
                or ModeClosed
                or NotServed
                or NotAddressed
                or ProtocolVersion
                or Expired
                or ClockSkew
                or Revoked,
            OutpostOutcomeKindEnum.Unknown => code
                is ResultTooLarge
                or RunLimit
                or Revoked
                or Disconnected
                or AgentStopped
                or TransactionEnded,
            _ => false,
        };
    }
}
