using System.Text.Json.Nodes;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// A Write for <see cref="IOutpostServer.WriteAsync"/>: a script that runs in a base under its write account, inside
/// one 1C transaction, at most once per caller key.
/// </summary>
/// <remarks>
/// <para>
/// Either everything the script did is committed, or nothing. The executor checks the type of the result and the size
/// of the whole outcome before it commits, so a Write that fails either check is rolled back.
/// <see cref="OutpostOutcomeKindEnum.Failed"/> therefore means that nothing was written. When the agent cannot tell
/// whether the commit happened, the outcome is <see cref="OutpostOutcomeKindEnum.Unknown"/>, never a success or a
/// failure. A script that ends the transaction itself, with <c>CommitTransaction</c> or <c>RollbackTransaction</c>,
/// voids this, and the executor reports <see cref="OutpostOutcomeKindEnum.Unknown"/> with
/// <see cref="OutpostCodes.TransactionEnded"/> when it finds no active transaction after it.
/// </para>
/// <para>
/// The script runs as <see cref="OutpostReadRequest"/> describes: whole, in the 1C safe mode, with a fresh executor
/// object, reading <c>Parameters</c> and <c>CallerKey</c> and setting <c>Result</c>.
/// </para>
/// </remarks>
public sealed class OutpostWriteRequest
{
    /// <summary>
    /// The key of the base, as the local policy of its agent declares it. Required.
    /// </summary>
    /// <remarks>
    /// 1 to 64 characters, each one of <c>a–z 0–9 - _ .</c>, the first a letter or a digit; compared exactly.
    /// </remarks>
    public required string BaseKey { get; init; }

    /// <summary>
    /// The caller key, which makes the Write run at most once per base. Required: a Write without one cannot be
    /// written, and <see langword="null"/> gives <see cref="ArgumentNullException"/>. The script reads it as
    /// <c>CallerKey</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1 to 200 characters, each from U+0021 to U+007E: printable ASCII without the space; compared exactly.
    /// </para>
    /// <para>
    /// The server accepts one command per base and caller key. A repeat with the same mode, script and parameters waits
    /// for the outcome of the first command and returns it, and never runs the script again; the script is compared
    /// exactly, the parameters as JSON values, so the order of members and the notation of numbers do not matter. A
    /// repeat with anything else is <see cref="OutpostOutcomeKindEnum.Refused"/> with
    /// <see cref="OutpostCodes.CallerKeyConflict"/>. When the first command ended as
    /// <see cref="OutpostOutcomeKindEnum.Expired"/> or <see cref="OutpostOutcomeKindEnum.Refused"/>, nothing ran, so
    /// the key is free again and a repeat creates a new command. An outcome, and with it its key, is kept for
    /// <see cref="OutpostServerOptions.OutcomeRetention"/>; after that a repeat runs the script again.
    /// </para>
    /// <para>
    /// A script can use the key to make itself idempotent, for example by marking what it creates, so that a later
    /// script can find out what an <see cref="OutpostOutcomeKindEnum.Unknown"/> Write did.
    /// </para>
    /// </remarks>
    public required string CallerKey { get; init; }

    /// <summary>
    /// The script, in the 1C language. Required.
    /// </summary>
    /// <remarks>
    /// At least one character that is not white space, and no U+0000. Together with <see cref="Parameters"/> it is the
    /// command, whose size <see cref="OutpostServerOptions.CommandSizeLimit"/> bounds.
    /// </remarks>
    public required string Script { get; init; }

    /// <summary>
    /// The parameters, which the script reads as <c>Parameters</c>: a <c>Map</c> with string keys.
    /// <see langword="null"/> means none, an empty <c>Map</c>.
    /// </summary>
    /// <remarks>
    /// They are read into 1C values as <see cref="OutpostReadRequest.Parameters"/> describes. No string in them, the
    /// name of a member included, holds U+0000, which PostgreSQL cannot store. They nest at most 64 levels, the outer
    /// object being level 1: the limit of every JSON message of the protocol. They are written as JSON when the call is
    /// made: a later change of the object has no effect on the command.
    /// </remarks>
    public JsonObject? Parameters { get; init; }
}
