namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// A base as an agent declared it in its newest poll: one of <see cref="OutpostAgent.Bases"/>.
/// </summary>
/// <param name="Key">The key of the base, as the local policy of the agent declares it.</param>
/// <param name="ReadOpen">Whether the agent opens Read on the base.</param>
/// <param name="WriteOpen">Whether the agent opens Write on the base: its local policy allows writing it.</param>
/// <param name="Held">
/// Whether this agent holds the key. <see langword="false"/> for a key that another agent holds: the server refused
/// that base for this agent, and commands for the key go to the agent that holds it.
/// </param>
public sealed record OutpostBase(string Key, bool ReadOpen, bool WriteOpen, bool Held);
