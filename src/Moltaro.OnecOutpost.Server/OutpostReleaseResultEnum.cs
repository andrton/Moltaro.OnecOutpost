namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// What <see cref="IOutpostServer.ReleaseBaseAsync"/> did with a base key.
/// </summary>
public enum OutpostReleaseResultEnum
{
    /// <summary>
    /// No agent holds the key; nothing changed. The default value.
    /// </summary>
    NotHeld = 0,

    /// <summary>
    /// The key is free: the next agent to declare it gets it.
    /// </summary>
    Released,

    /// <summary>
    /// The agent that holds the key still declares it, and nothing changed.
    /// </summary>
    StillDeclared,
}
