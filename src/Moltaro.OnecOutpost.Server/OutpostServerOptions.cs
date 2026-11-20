using System.Security.Cryptography;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// The options of the server: its store, its signing key, the prefix of its signals, and the timeouts and limits
/// that the agents and the callers observe.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OutpostServiceCollectionExtensions.AddOutpostServer"/> runs its delegate on a new object, validates it
/// and keeps a copy: a later change of the object has no effect on the server. A number or a
/// <see cref="TimeSpan"/> outside the range its property gives yields <see cref="ArgumentOutOfRangeException"/>. Any
/// other wrong value, such as a required property left <see langword="null"/>, gives <see cref="ArgumentException"/>.
/// In both, <see cref="ArgumentException.ParamName"/> is the name of the property. Every range includes its ends.
/// </para>
/// <para>
/// A command carries its take deadline, the skew and the outcome size limit inside its signature, so that the agent
/// judges it by the values of the node that created it.
/// </para>
/// </remarks>
public sealed class OutpostServerOptions
{
    /// <summary>
    /// The PostgreSQL connection of the store. Required: not <see langword="null"/>, empty or white space.
    /// </summary>
    /// <remarks>
    /// The store is an EF Core context of the library on this connection, in <see cref="Schema"/>.
    /// <see cref="OutpostMigrations"/> creates and updates its tables.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The schema of the store; <c>outpost</c> by default.
    /// </summary>
    /// <remarks>
    /// 1 to 63 characters, each one of <c>a–z 0–9 _</c>, the first a letter.
    /// </remarks>
    public string Schema { get; set; } = "outpost";

    /// <summary>
    /// The server's signing key: the PKCS#8 PEM, not encrypted, of an ECDSA P-256 private key. Required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It signs every command and every answer to an agent, and its public key is what an agent pins at pairing. The
    /// application keeps it in its own secret storage; the store never holds it. One line of .NET creates one:
    /// <c>ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportPkcs8PrivateKeyPem()</c>.
    /// </para>
    /// <para>
    /// Every node uses the same key. A new key takes a new pairing of every agent. The validation reads only the first
    /// PEM block; its error never contains the key.
    /// </para>
    /// </remarks>
    public string? SigningKeyPem { get; set; }

    /// <summary>
    /// The prefix of every key of Moltaro.Signals that the server sets or subscribes to; <c>outpost</c> by default.
    /// </summary>
    /// <remarks>
    /// A name of Moltaro.Signals of at most 64 characters: each one of <c>A–Z a–z 0–9 . _ : -</c>, where <c>.</c>
    /// separates segments and no segment is empty. The server follows it with the identity of an agent or of a
    /// command. Applications that share a Signals host use prefixes of their own.
    /// </remarks>
    public string SignalKeyPrefix { get; set; } = "outpost";

    /// <summary>
    /// How long a poll of an agent waits for a command before it is answered without one: 1 second to 5 minutes,
    /// 25 seconds by default.
    /// </summary>
    /// <remarks>
    /// Keep it below the idle limit of every proxy and ingress between the agents and the server. Every poll answer
    /// carries it, so the agents follow a change.
    /// </remarks>
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// What the lease of an agent adds to <see cref="PollTimeout"/>: 1 second to 5 minutes, 10 seconds by default.
    /// </summary>
    /// <remarks>
    /// An agent counts as connected while the lease of its newest poll has not run out; a call to a base whose agent
    /// is not connected gives <see cref="OutpostOutcomeKindEnum.Unavailable"/> at once. A gone agent is reported as
    /// connected for at most its lease, plus the difference between the clocks of the nodes.
    /// </remarks>
    public TimeSpan LeaseGrace { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The take deadline of a command, counted from its creation: 1 second to 1 hour, 30 seconds by default.
    /// </summary>
    /// <remarks>
    /// A command waits for a poll of its agent, for example while the workers of its base are busy, until this
    /// deadline, and then ends as <see cref="OutpostOutcomeKindEnum.Expired"/>. A command that was handed out expires
    /// only when the agent's own signed time shows that it can no longer accept it.
    /// </remarks>
    public TimeSpan TakeTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The skew between the clocks of the server and of an agent that every command allows: 1 second to 1 hour,
    /// 2 minutes by default.
    /// </summary>
    /// <remarks>
    /// It travels in every signed command. An agent refuses a command issued further ahead of its clock than this,
    /// with <see cref="OutpostCodes.ClockSkew"/>, and one whose take deadline passed longer ago than this, with
    /// <see cref="OutpostCodes.Expired"/>. The agent has no setting of its own.
    /// </remarks>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The largest command, in bytes: 4 KiB to 16 MiB, 1 MiB by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The size of a command is the number of bytes of its script in UTF-8, plus the number of bytes of its
    /// parameters written as compact JSON in UTF-8; a command without parameters counts its script alone. Characters
    /// outside ASCII are counted as they are, never as escapes: <c>é</c> counts 2 bytes, and a character outside the
    /// Basic Multilingual Plane 4. In the JSON, only what JSON requires is escaped: <c>"</c> and <c>\</c> count 2
    /// bytes each, U+0008, U+0009, U+000A, U+000C and U+000D 2 bytes each, and the other characters below U+0020 6
    /// bytes each. Numbers count as they are written.
    /// </para>
    /// <para>
    /// A command up to the limit is accepted; a larger one is the caller's mistake and gives
    /// <see cref="ArgumentException"/> from <see cref="IOutpostServer.ReadAsync"/> or
    /// <see cref="IOutpostServer.WriteAsync"/>.
    /// </para>
    /// </remarks>
    public int CommandSizeLimit { get; set; } = 1024 * 1024;

    /// <summary>
    /// The largest outcome, in bytes: 64 KiB to 64 MiB, 16 MiB by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The size of an outcome is that of its result as the executor wrote it, plus its user messages and its error
    /// text, in UTF-8 with characters outside ASCII not escaped. The limit travels in every signed command, so that
    /// the executor, the agent and the server judge by the same number.
    /// </para>
    /// <para>
    /// The executor trims the messages and the error text to fit and marks that it did. An outcome that still does not
    /// fit fails with <see cref="OutpostCodes.ResultTooLarge"/>, and a Write is rolled back first. When the server, or
    /// something on the path, refuses an outcome for its size, a Write becomes
    /// <see cref="OutpostOutcomeKindEnum.Unknown"/> and a Read <see cref="OutpostOutcomeKindEnum.Failed"/>, both with
    /// <see cref="OutpostCodes.ResultTooLarge"/>. The body limit of the outcome request of the agents follows this
    /// value; the ingress in front of the nodes has to let such a body through.
    /// </para>
    /// </remarks>
    public int OutcomeSizeLimit { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// How long an ended command is kept with its outcome, and with it its caller key: 1 hour to 365 days, 7 days by
    /// default.
    /// </summary>
    /// <remarks>
    /// Within it, a repeat with the caller key returns the outcome of the first command. After it the key is free,
    /// and a repeat creates a new command.
    /// </remarks>
    public TimeSpan OutcomeRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a pairing token can be used after it is created: 1 minute to 1 day, 15 minutes by default.
    /// </summary>
    /// <remarks>
    /// Judged by the clock of the node that redeems the token: on a node whose clock is off, a token lasts that much
    /// longer or shorter.
    /// </remarks>
    public TimeSpan PairingTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often a node removes what outlived its retention: ended commands after
    /// <see cref="OutcomeRetention"/>, and expired pairing tokens with their pending agents. 1 minute to 1 day, 1 hour
    /// by default.
    /// </summary>
    /// <remarks>
    /// Every node removes; removing is idempotent, so the nodes need not agree on who does it.
    /// </remarks>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The clock of every time decision of the server; <see cref="System.TimeProvider.System"/> by default. Required.
    /// </summary>
    /// <remarks>
    /// Leases, take deadlines, the expiry of a command that no poll took, retention and the lifetime of tokens are all
    /// judged by it. Nodes are expected to keep their clocks synchronized. Tests use a fake one.
    /// </remarks>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    // A copy that a later change of this object does not reach. Every property is a value or an immutable string,
    // except TimeProvider, which the copy shares on purpose.
    internal OutpostServerOptions Copy()
    {
        return (OutpostServerOptions)MemberwiseClone();
    }

    // Throws for the first property, in the order of declaration, that breaks its rule. Run on the copy, so that the
    // values checked are the values kept.
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new ArgumentException("The PostgreSQL connection string of the store is required.", nameof(ConnectionString));
        }

        if (Schema is null)
        {
            throw new ArgumentException("The schema of the store is required.", nameof(Schema));
        }

        ServerNames.CheckSchema(Schema, nameof(Schema));

        // The message never contains the value: it is a secret.
        if (SigningKeyPem is null || !IsSigningKey(SigningKeyPem))
        {
            throw new ArgumentException(
                "The signing key is required, as the PKCS#8 PEM, not encrypted, of an ECDSA P-256 private key.",
                nameof(SigningKeyPem));
        }

        if (SignalKeyPrefix is null)
        {
            throw new ArgumentException("The prefix of the keys of the signals is required.", nameof(SignalKeyPrefix));
        }

        ServerNames.CheckSignalKeyPrefix(SignalKeyPrefix, nameof(SignalKeyPrefix));

        CheckRange(PollTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), nameof(PollTimeout));
        CheckRange(LeaseGrace, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), nameof(LeaseGrace));
        CheckRange(TakeTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), nameof(TakeTimeout));
        CheckRange(ClockSkew, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), nameof(ClockSkew));
        CheckRange(CommandSizeLimit, 4 * 1024, 16 * 1024 * 1024, nameof(CommandSizeLimit));
        CheckRange(OutcomeSizeLimit, 64 * 1024, 64 * 1024 * 1024, nameof(OutcomeSizeLimit));
        CheckRange(OutcomeRetention, TimeSpan.FromHours(1), TimeSpan.FromDays(365), nameof(OutcomeRetention));
        CheckRange(PairingTokenLifetime, TimeSpan.FromMinutes(1), TimeSpan.FromDays(1), nameof(PairingTokenLifetime));
        CheckRange(PurgeInterval, TimeSpan.FromMinutes(1), TimeSpan.FromDays(1), nameof(PurgeInterval));

        if (TimeProvider is null)
        {
            throw new ArgumentException("The time provider is required.", nameof(TimeProvider));
        }
    }

    private static void CheckRange<T>(T value, T minimum, T maximum, string paramName)
        where T : IComparable<T>
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum, paramName);
    }

    // True when the first PEM block is labelled PRIVATE KEY, which is PKCS#8 and not encrypted, and its key is an
    // ECDSA key on the named curve P-256.
    private static bool IsSigningKey(string pem)
    {
        if (!PemEncoding.TryFind(pem, out PemFields fields) || !pem.AsSpan(fields.Label).SequenceEqual("PRIVATE KEY"))
        {
            return false;
        }

        // TryFind has checked the base64, so the decoding cannot fail.
        byte[] pkcs8 = Convert.FromBase64String(pem[fields.Base64Data]);

        using ECDsa key = ECDsa.Create();

        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out int bytesRead);

            if (bytesRead != pkcs8.Length)
            {
                return false;
            }
        }
        catch (CryptographicException)
        {
            // Not an ECDSA key, or not a well-formed one.
            return false;
        }

        ECCurve curve = key.ExportParameters(includePrivateParameters: false).Curve;

        return curve.IsNamed && curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
    }
}
