namespace Moltaro.OnecOutpost.TestSupport.Programs;

// How a run of a built program ended: its exit code and everything it wrote to its standard output and its standard
// error.
internal sealed record ProgramEnd(int ExitCode, string Output, string Error);
