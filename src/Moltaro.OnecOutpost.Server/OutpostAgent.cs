namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// An agent as the store knows it: one entry of <see cref="IOutpostServer.GetAgentsAsync"/>.
/// </summary>
/// <param name="Id">
/// The identity of the agent; for a pending agent, the identity it will have once paired, which is
/// <see cref="OutpostPairingToken.AgentId"/>.
/// </param>
/// <param name="Name">
/// The name given to <see cref="IOutpostServer.CreatePairingTokenAsync"/>: a display label of arbitrary characters, which
/// need not be unique.
/// </param>
/// <param name="State">Pending, paired or revoked.</param>
/// <param name="CreatedAt">When the pending agent was created, with its pairing token.</param>
/// <param name="PairedAt">When the agent paired; <see langword="null"/> for one that never paired.</param>
/// <param name="LastPollAt">When the server recorded the newest poll of the agent; <see langword="null"/> before its first.</param>
/// <param name="Connected">
/// Whether the lease of its newest poll had not run out at the moment of the call, by the clock of the node that
/// answered. The lease is <see cref="OutpostServerOptions.PollTimeout"/> plus
/// <see cref="OutpostServerOptions.LeaseGrace"/>. A call to a base of an agent that is not connected gives
/// <see cref="OutpostOutcomeKindEnum.Unavailable"/>.
/// </param>
/// <param name="AgentVersion">The version of the agent program, from its newest poll; <see langword="null"/> before its first.</param>
/// <param name="Bases">What the agent declared in its newest poll; empty before its first.</param>
public sealed record OutpostAgent(
    Guid Id,
    string Name,
    OutpostAgentStateEnum State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PairedAt,
    DateTimeOffset? LastPollAt,
    bool Connected,
    string? AgentVersion,
    IReadOnlyList<OutpostBase> Bases);
