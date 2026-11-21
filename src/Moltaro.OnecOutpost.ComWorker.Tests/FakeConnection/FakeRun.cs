namespace Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

// The arguments of one call of IOneCExecutor.Run, as the fake executor received them.
internal sealed record FakeRun(string Mode, string Script, string ParametersJson, string? CallerKey, long OutcomeLimit);
