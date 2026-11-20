namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// Where an agent stands in its life: the state of an <see cref="OutpostAgent"/>.
/// </summary>
public enum OutpostAgentStateEnum
{
    /// <summary>
    /// Created with a pairing token, and not paired yet. The default value.
    /// </summary>
    /// <remarks>
    /// A pending agent whose token expired unused is removed by the next purge.
    /// </remarks>
    Pending = 0,

    /// <summary>
    /// Paired: the server knows the agent's public key, and the agent signs its requests with it.
    /// </summary>
    Paired,

    /// <summary>
    /// Revoked: its requests are refused and its polls take nothing. It works again only after a new pairing.
    /// </summary>
    Revoked,
}
