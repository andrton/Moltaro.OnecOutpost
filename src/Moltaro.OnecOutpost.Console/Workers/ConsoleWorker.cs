using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Console.Workers;

// A worker that the console started, for check or metadata: its process over the worker's lines, and its standard
// error read to its end, which holds no password (the COM worker withholds a text that holds one).
internal sealed record ConsoleWorker(IWorkerProcess Process, Task<string> Diagnostics);
