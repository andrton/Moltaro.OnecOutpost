namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// How a command ended: the kind of an <see cref="OutpostOutcome"/>.
/// </summary>
/// <remarks>
/// The default value is <see cref="Unknown"/>, since it claims nothing. Only <see cref="Succeeded"/> claims that the
/// script ran to its end; only <see cref="Refused"/>, <see cref="Unavailable"/> and <see cref="Expired"/> claim that
/// nothing ran.
/// </remarks>
public enum OutpostOutcomeKindEnum
{
    /// <summary>
    /// A Write whose effect in 1C cannot be known, or whose outcome could not be delivered. It may have committed or
    /// not; it is never reported as a success or a failure. <see cref="OutpostOutcome.Code"/> says why.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The script ran to its end, and a Write committed. The outcome carries the result and the user messages of 1C.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The command failed, with a code of <see cref="OutpostCodes"/>, the error text of 1C when 1C raised it, and the
    /// user messages. A Write fails only when 1C rolled back its transaction, so nothing was written.
    /// </summary>
    Failed,

    /// <summary>
    /// A check refused the command, with a code of <see cref="OutpostCodes"/> that says which. Nothing ran, and the
    /// caller key is free again.
    /// </summary>
    Refused,

    /// <summary>
    /// The agent of the base is not connected. No command was created.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The command did not reach the agent in time. Nothing ran, and the caller key is free again.
    /// </summary>
    Expired,
}
