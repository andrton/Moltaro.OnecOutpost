namespace Moltaro.OnecOutpost.Server.Store;

// What a pass of a waiting call reads: the outcome of the command, or null while it has none, and the moment at which
// time can next end it.
internal sealed record CommandPass(OutpostOutcome? Outcome, DateTimeOffset Due);
