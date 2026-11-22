namespace Moltaro.OnecOutpost.Server.Store;

// The command that a base and a caller key name (rule 4): Created when the call created it, and Same when its mode, its
// script and its parameters, compared as JSON values, are those of the call.
internal sealed record CommandMatch(Guid Id, bool Created, bool Same);
