namespace Moltaro.OnecOutpost.Server.Tests.Loopback;

// A request of an agent that a LoopbackServer finished handling: its path, and whether its answer had started when the
// endpoint returned. An endpoint that writes nothing leaves it false.
internal sealed record EndedRequest(string Path, bool AnswerStarted);
