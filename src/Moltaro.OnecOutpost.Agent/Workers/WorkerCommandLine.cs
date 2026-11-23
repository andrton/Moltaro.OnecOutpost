namespace Moltaro.OnecOutpost.Agent.Workers;

// One command line of a COM worker's input, in the form the COM worker reads: the command's script,
// Parameters.GetRawText() as ParametersJson, its caller key or null, and its OutcomeLimit.
internal sealed record WorkerCommandLine(string Script, string ParametersJson, string? CallerKey, long OutcomeLimit);
