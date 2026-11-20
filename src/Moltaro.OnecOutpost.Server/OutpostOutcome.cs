using System.Text.Json;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// How a command ended: what <see cref="IOutpostServer.ReadAsync"/> and <see cref="IOutpostServer.WriteAsync"/>
/// return.
/// </summary>
/// <remarks>
/// <para>
/// An outcome is immutable. The kinds are those of <see cref="OutpostOutcomeKindEnum"/>; each kind holds only the
/// members its factory takes, and the others are <see langword="null"/>, empty or <see langword="false"/>.
/// </para>
/// <para>
/// The factories exist so that an application can fake <see cref="IOutpostServer"/> in its own tests. They check that
/// a code is one of <see cref="OutpostCodes"/> and belongs to the kind, and they copy the messages, so that a later
/// change of the list given has no effect.
/// </para>
/// </remarks>
public sealed class OutpostOutcome
{
    private OutpostOutcome(
        OutpostOutcomeKindEnum kind,
        Guid? commandId,
        JsonElement? result,
        string? code,
        string? errorText,
        IReadOnlyList<string> messages,
        bool messagesTrimmed)
    {
        Kind = kind;
        CommandId = commandId;
        Result = result;
        Code = code;
        ErrorText = errorText;
        Messages = messages;
        MessagesTrimmed = messagesTrimmed;
    }

    /// <summary>
    /// How the command ended.
    /// </summary>
    public OutpostOutcomeKindEnum Kind { get; }

    /// <summary>
    /// The identity of the command, under which the audit log of its agent records it. <see langword="null"/> only for
    /// <see cref="OutpostOutcomeKindEnum.Unavailable"/>, and for a <see cref="OutpostOutcomeKindEnum.Refused"/> that no
    /// command was created for.
    /// </summary>
    public Guid? CommandId { get; }

    /// <summary>
    /// The result that the script set, as JSON, for <see cref="OutpostOutcomeKindEnum.Succeeded"/>;
    /// <see langword="null"/> for every other kind.
    /// </summary>
    /// <remarks>
    /// A script that set no result gives an element of the kind <see cref="JsonValueKind.Null"/>, never
    /// <see langword="null"/>. The outcome owns the element: it stays valid for as long as the outcome does, and
    /// needs no document to be disposed.
    /// </remarks>
    public JsonElement? Result { get; }

    /// <summary>
    /// One of <see cref="OutpostCodes"/> for <see cref="OutpostOutcomeKindEnum.Failed"/>,
    /// <see cref="OutpostOutcomeKindEnum.Refused"/> and <see cref="OutpostOutcomeKindEnum.Unknown"/>;
    /// <see langword="null"/> for every other kind.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// The text of the error that 1C raised, for <see cref="OutpostOutcomeKindEnum.Failed"/> with
    /// <see cref="OutpostCodes.ScriptError"/>; <see langword="null"/> for every other kind.
    /// </summary>
    /// <remarks>
    /// The executor may have trimmed it to fit the size limit of the outcome; <see cref="MessagesTrimmed"/> then says
    /// so.
    /// </remarks>
    public string? ErrorText { get; }

    /// <summary>
    /// The user messages of 1C, for <see cref="OutpostOutcomeKindEnum.Succeeded"/> and
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>; empty for every other kind. Never <see langword="null"/>.
    /// </summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>
    /// Whether the executor trimmed the messages or the error text to fit the size limit of the outcome;
    /// <see langword="false"/> for every kind but <see cref="OutpostOutcomeKindEnum.Succeeded"/> and
    /// <see cref="OutpostOutcomeKindEnum.Failed"/>.
    /// </summary>
    public bool MessagesTrimmed { get; }

    /// <summary>
    /// Creates an outcome of the kind <see cref="OutpostOutcomeKindEnum.Succeeded"/>.
    /// </summary>
    /// <param name="commandId">The identity of the command.</param>
    /// <param name="result">
    /// The result as JSON; an element of the kind <see cref="JsonValueKind.Null"/> for a script that set none. The
    /// outcome keeps a clone of it, so the document of the element given may be disposed.
    /// </param>
    /// <param name="messages">The user messages of 1C, copied.</param>
    /// <param name="messagesTrimmed">Whether the executor trimmed the messages.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="result"/> is the default <see cref="JsonElement"/>, which holds no value.
    /// </exception>
    public static OutpostOutcome Succeeded(
        Guid commandId,
        JsonElement result,
        IReadOnlyList<string> messages,
        bool messagesTrimmed)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // The default element belongs to no document, and a result is never a missing value.
        if (result.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException(
                "The result holds no value; a script that set none gives an element of the kind Null.",
                nameof(result));
        }

        return new OutpostOutcome(
            OutpostOutcomeKindEnum.Succeeded,
            commandId,
            result.Clone(),
            null,
            null,
            [.. messages],
            messagesTrimmed);
    }

    /// <summary>
    /// Creates an outcome of the kind <see cref="OutpostOutcomeKindEnum.Failed"/>.
    /// </summary>
    /// <param name="commandId">The identity of the command.</param>
    /// <param name="code">
    /// One of <see cref="OutpostCodes.ScriptError"/>, <see cref="OutpostCodes.ResultType"/>,
    /// <see cref="OutpostCodes.ResultTooLarge"/>, <see cref="OutpostCodes.RunLimit"/> and
    /// <see cref="OutpostCodes.Lost"/>.
    /// </param>
    /// <param name="errorText">The text of the error that 1C raised, for <see cref="OutpostCodes.ScriptError"/>.</param>
    /// <param name="messages">The user messages of 1C, copied.</param>
    /// <param name="messagesTrimmed">Whether the executor trimmed the messages or the error text.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="code"/> or <paramref name="messages"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is not a code of <see cref="OutpostCodes"/> that a Failed outcome carries.
    /// </exception>
    public static OutpostOutcome Failed(
        Guid commandId,
        string code,
        string? errorText,
        IReadOnlyList<string> messages,
        bool messagesTrimmed)
    {
        CheckCode(code, OutpostOutcomeKindEnum.Failed);
        ArgumentNullException.ThrowIfNull(messages);

        return new OutpostOutcome(
            OutpostOutcomeKindEnum.Failed,
            commandId,
            null,
            code,
            errorText,
            [.. messages],
            messagesTrimmed);
    }

    /// <summary>
    /// Creates an outcome of the kind <see cref="OutpostOutcomeKindEnum.Refused"/>.
    /// </summary>
    /// <param name="commandId">
    /// The identity of the command, or <see langword="null"/> when the server refused the call before it created one.
    /// </param>
    /// <param name="code">
    /// One of <see cref="OutpostCodes.CallerKeyConflict"/>, <see cref="OutpostCodes.UnknownBase"/>,
    /// <see cref="OutpostCodes.ModeClosed"/>, <see cref="OutpostCodes.NotServed"/>,
    /// <see cref="OutpostCodes.NotAddressed"/>, <see cref="OutpostCodes.ProtocolVersion"/>,
    /// <see cref="OutpostCodes.Expired"/>, <see cref="OutpostCodes.ClockSkew"/> and <see cref="OutpostCodes.Revoked"/>.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is not a code of <see cref="OutpostCodes"/> that a Refused outcome carries.
    /// </exception>
    public static OutpostOutcome Refused(Guid? commandId, string code)
    {
        CheckCode(code, OutpostOutcomeKindEnum.Refused);

        return new OutpostOutcome(OutpostOutcomeKindEnum.Refused, commandId, null, code, null, [], false);
    }

    /// <summary>
    /// Creates an outcome of the kind <see cref="OutpostOutcomeKindEnum.Unavailable"/>, which has no command.
    /// </summary>
    /// <returns>The outcome.</returns>
    public static OutpostOutcome Unavailable()
    {
        return new OutpostOutcome(OutpostOutcomeKindEnum.Unavailable, null, null, null, null, [], false);
    }

    /// <summary>
    /// Creates an outcome of the kind <see cref="OutpostOutcomeKindEnum.Expired"/>.
    /// </summary>
    /// <param name="commandId">The identity of the command.</param>
    /// <returns>The outcome.</returns>
    public static OutpostOutcome Expired(Guid commandId)
    {
        return new OutpostOutcome(OutpostOutcomeKindEnum.Expired, commandId, null, null, null, [], false);
    }

    /// <summary>
    /// Creates an outcome of the kind <see cref="OutpostOutcomeKindEnum.Unknown"/>.
    /// </summary>
    /// <param name="commandId">The identity of the command.</param>
    /// <param name="code">
    /// One of <see cref="OutpostCodes.ResultTooLarge"/>, <see cref="OutpostCodes.RunLimit"/>,
    /// <see cref="OutpostCodes.Revoked"/>, <see cref="OutpostCodes.Disconnected"/>,
    /// <see cref="OutpostCodes.AgentStopped"/> and <see cref="OutpostCodes.TransactionEnded"/>.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is not a code of <see cref="OutpostCodes"/> that an Unknown outcome carries.
    /// </exception>
    public static OutpostOutcome Unknown(Guid commandId, string code)
    {
        CheckCode(code, OutpostOutcomeKindEnum.Unknown);

        return new OutpostOutcome(OutpostOutcomeKindEnum.Unknown, commandId, null, code, null, [], false);
    }

    private static void CheckCode(string code, OutpostOutcomeKindEnum kind)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (!OutpostCodes.Fits(code, kind))
        {
            throw new ArgumentException($"'{code}' is not a code of OutpostCodes that a {kind} outcome carries.", nameof(code));
        }
    }
}
