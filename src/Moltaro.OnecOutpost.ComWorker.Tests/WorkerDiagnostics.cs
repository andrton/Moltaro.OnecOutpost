using System.Text;

namespace Moltaro.OnecOutpost.ComWorker.Tests;

// The standard error of a worker as a person reads it, the text the worker wrote there, which also notes how many lines
// its standard output had ended when the first text came, so that a test states that the diagnostics of a failure come
// before the line that reports it: the agent's console reads them once the worker's line has told it to look.
internal sealed class WorkerDiagnostics : TextWriter
{
    private readonly StringBuilder _text = new();

    private readonly WorkerOutput _output;

    public WorkerDiagnostics(WorkerOutput output)
    {
        _output = output;
    }

    public override Encoding Encoding => Encoding.UTF8;

    // The lines of the standard output when the first text came, or null when none came.
    public int? OutputLinesBefore { get; private set; }

    public override void Write(char value)
    {
        OutputLinesBefore ??= _output.LineCount;
        _text.Append(value);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        OutputLinesBefore ??= _output.LineCount;
        _text.Append(value);
    }

    public override string ToString()
    {
        return _text.ToString();
    }
}
