namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// A pairing token: the one string that the administrator of an agent machine gives the console to pair a new agent,
/// with the identity that agent will have and the time the token expires.
/// </summary>
/// <remarks>
/// <see cref="IOutpostServer.CreatePairingTokenAsync"/> creates it. The token is a credential until it is used or
/// expires: whoever holds it in time can pair an agent of their own. <see cref="ToString"/> leaves it out, so that an
/// application that logs the object does not log a credential.
/// </remarks>
public sealed class OutpostPairingToken
{
    /// <summary>
    /// Creates a token from its parts, so that an application can fake <see cref="IOutpostServer"/> in its own tests.
    /// </summary>
    /// <param name="agentId">The identity the agent will have once it is paired. Not <see cref="Guid.Empty"/>.</param>
    /// <param name="token">The string to give the console. Not <see langword="null"/>, empty or white space.</param>
    /// <param name="expiresAt">When the token stops working.</param>
    /// <remarks>
    /// It checks only what the parameters say; it does not check that <paramref name="token"/> has the form of the
    /// protocol, and the object it creates pairs nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="agentId"/> is <see cref="Guid.Empty"/>, or <paramref name="token"/> is empty or white space.
    /// </exception>
    public OutpostPairingToken(Guid agentId, string token, DateTimeOffset expiresAt)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("The identity of the agent cannot be empty.", nameof(agentId));
        }

        // The message of this check never contains the value, which is a credential.
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        AgentId = agentId;
        Token = token;
        ExpiresAt = expiresAt;
    }

    /// <summary>
    /// The identity the agent will have once it is paired: the <see cref="OutpostAgent.Id"/> of its pending agent.
    /// </summary>
    public Guid AgentId { get; }

    /// <summary>
    /// The one string that the administrator gives the console. It holds the identity of the token, its secret and the
    /// fingerprint of the server's signing key; its exact form is part of the protocol.
    /// </summary>
    /// <remarks>
    /// It works once, until <see cref="ExpiresAt"/>. Keep it out of logs and hand it to the administrator over a
    /// channel you trust: the fingerprint inside it is what lets the agent refuse a server key that is not yours.
    /// </remarks>
    public string Token { get; }

    /// <summary>
    /// When the token stops working, by the clock of the node that created it; a node that redeems it judges by its own
    /// clock.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// Gives the identity of the agent and the expiry, never the token.
    /// </summary>
    /// <returns>A text such as <c>Pairing token for agent 5b1c…, expires at 2026-11-20T12:15:00.0000000+00:00</c>.</returns>
    public override string ToString()
    {
        return $"Pairing token for agent {AgentId}, expires at {ExpiresAt:O}";
    }
}
