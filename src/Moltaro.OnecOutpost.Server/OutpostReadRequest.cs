using System.Text.Json.Nodes;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// A Read for <see cref="IOutpostServer.ReadAsync"/>: a script that runs in a base under its read account, outside a
/// transaction.
/// </summary>
/// <remarks>
/// <para>
/// A Read is read-only only through the rights of the read account, which the administrator of the agent machine
/// chooses. The script runs whole, in one call, in the 1C safe mode, with a fresh executor object: it cannot declare
/// procedures or functions and cannot use <c>Return</c>. It reads <c>Parameters</c> and <c>CallerKey</c>, and sets
/// <c>Result</c>, which is written as JSON. <c>Result</c> may hold <c>String</c>, <c>Number</c>, <c>Boolean</c>,
/// <c>Date</c>, <c>Undefined</c>, <c>Null</c>, <c>Array</c>, <c>Structure</c>, and <c>Map</c> with string keys; a date
/// is written as <c>YYYY-MM-DDThh:mm:ss</c> without an offset, and <c>Undefined</c> and <c>Null</c> as <c>null</c>. Any
/// other value fails the command with <see cref="OutpostCodes.ResultType"/>.
/// </para>
/// <para>
/// The 1C session of a worker lives on between commands, so what a script changes in the session can be seen by the
/// next command of that worker. A transaction that the script of a Read leaves open is not left to it: the executor
/// rolls it back, and the Read fails with <see cref="OutpostCodes.ScriptError"/>.
/// </para>
/// </remarks>
public sealed class OutpostReadRequest
{
    /// <summary>
    /// The key of the base, as the local policy of its agent declares it. Required.
    /// </summary>
    /// <remarks>
    /// 1 to 64 characters, each one of <c>a–z 0–9 - _ .</c>, the first a letter or a digit; compared exactly.
    /// </remarks>
    public required string BaseKey { get; init; }

    /// <summary>
    /// The script, in the 1C language. Required.
    /// </summary>
    /// <remarks>
    /// At least one character that is not white space, no U+0000, and no lone surrogate, a code unit from U+D800 to
    /// U+DFFF without its pair, which UTF-8 cannot carry. Together with <see cref="Parameters"/> it is the command, whose
    /// size <see cref="OutpostServerOptions.CommandSizeLimit"/> bounds.
    /// </remarks>
    public required string Script { get; init; }

    /// <summary>
    /// The parameters, which the script reads as <c>Parameters</c>: a <c>Map</c> with string keys.
    /// <see langword="null"/> means none, an empty <c>Map</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An object becomes a <c>Map</c> and an array an <c>Array</c>. A string, number or boolean becomes the matching 1C
    /// value, and <c>null</c> becomes <c>Undefined</c>. A string is never turned into a date by guessing.
    /// </para>
    /// <para>
    /// No string in them, the name of a member included, holds U+0000, which PostgreSQL cannot store, or a lone
    /// surrogate, which UTF-8 cannot carry. A string inside another .NET value wrapped in a <see cref="JsonValue"/>, such
    /// as a <see cref="char"/> or an array, is written as System.Text.Json writes it, with U+FFFD in place of a lone
    /// surrogate. No object in them has a name twice, the names compared after unescaping. They nest at most 64 levels,
    /// counted from their own outer value, the outer object being level 1: a fixed limit of the protocol. They are
    /// written as JSON when the call is made: a later change of the object has no effect on the command.
    /// </para>
    /// </remarks>
    public JsonObject? Parameters { get; init; }

    /// <summary>
    /// The caller key, or <see langword="null"/> for none. It allows at most one run in the base while the command
    /// holds the key. The script reads it as <c>CallerKey</c>, which is <c>Undefined</c> without one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1 to 200 characters, each from U+0021 to U+007E: printable ASCII without the space; compared exactly.
    /// </para>
    /// <para>
    /// The server accepts one command per base and caller key. A repeat with the same mode, script and parameters waits
    /// for the outcome of the first command and returns it while the store retains it. The script is compared exactly
    /// and the parameters as JSON values, so the order of members and the notation of numbers do not matter.
    /// A repeat with anything else is <see cref="OutpostOutcomeKindEnum.Refused"/> with
    /// <see cref="OutpostCodes.CallerKeyConflict"/>. When the first command ended as
    /// <see cref="OutpostOutcomeKindEnum.Expired"/> or <see cref="OutpostOutcomeKindEnum.Refused"/>, the key is free
    /// again, and a repeat creates a new command. The store keeps outcomes for
    /// <see cref="OutpostServerOptions.OutcomeRetention"/>. The purge frees any key still held by the command.
    /// The store compares the parameters as PostgreSQL's <c>jsonb</c>, so a number in them that its <c>numeric</c>
    /// cannot hold, such as <c>1e-20000</c>, makes a call with a caller key fail with the exception of the store.
    /// </para>
    /// </remarks>
    public string? CallerKey { get; init; }
}
