namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// A request of an agent that a LoopbackServer finished handling: its path, whether its answer had started when the
// endpoint returned, and its Outpost-Signature as it came, which names one request of a test agent, or null. An
// endpoint that writes nothing leaves AnswerStarted false.
internal sealed record EndedRequest(string Path, bool AnswerStarted, string? Signature);
