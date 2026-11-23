using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The redirected standard input of a test's console, holding the text the test gives, read however the command reads
// it. Its first read is first the line "read the input" of the operation list of the machine's data folder, so that a
// test states what the command did before it read the password, and that it read nothing.
internal sealed class FakeInput : TextReader
{
    private readonly MemoryDataFolder _notes;

    private readonly StringReader _text;

    private bool _read;

    public FakeInput(MemoryDataFolder notes, string text)
    {
        _notes = notes;
        _text = new StringReader(text);
    }

    public override int Peek()
    {
        Note();

        return _text.Peek();
    }

    public override int Read()
    {
        Note();

        return _text.Read();
    }

    public override string? ReadLine()
    {
        Note();

        return _text.ReadLine();
    }

    public override string ReadToEnd()
    {
        Note();

        return _text.ReadToEnd();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _text.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Note()
    {
        if (_read)
        {
            return;
        }

        _read = true;
        _notes.Note("read the input");
    }
}
