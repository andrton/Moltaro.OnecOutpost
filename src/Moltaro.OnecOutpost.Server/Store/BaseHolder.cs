namespace Moltaro.OnecOutpost.Server.Store;

// The agent that holds a base key: whether its newest poll declares the key, which opens Read, and opens Write there,
// and whether it is connected (rule 6).
internal sealed record BaseHolder(Guid AgentId, bool Declared, bool WriteOpen, bool Connected);
