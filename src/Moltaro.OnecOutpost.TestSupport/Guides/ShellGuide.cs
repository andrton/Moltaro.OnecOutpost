using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Guides;

// The startup and session commands of G3 as the reader pastes them, with the scripts and parameters still in their
// frames. Both the shell tests and the lab tests read the same blocks from the guide's Markdown.
internal sealed record ShellGuide(string Startup, IReadOnlyList<string> Commands)
{
    public static ShellGuide Read()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "server.md"));
        List<string> startup = [];
        List<string> commands = [];

        for (int index = 0; index < lines.Length; index++)
        {
            List<string>? blocks = lines[index] switch
            {
                "```text shell-start" => startup,
                "```text shell" => commands,
                _ => null,
            };

            if (blocks is null)
            {
                continue;
            }

            int end = Array.IndexOf(lines, "```", index + 1);
            Assert.True(end > index + 1, $"The shell block on line {index + 1} must have contents and a closing fence.");
            blocks.Add(string.Join('\n', lines[(index + 1)..end]));
            index = end;
        }

        return new ShellGuide(Assert.Single(startup), commands);
    }

    // The guide uses one complete frame per Read or Write, each with a parameter object.
    public static (string Mode, string BaseKey, string? CallerKey, string Script, string Parameters) ReadFrame(string command)
    {
        string[] lines = command.Split('\n');
        string[] header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(header is ["read", _] or ["write", _, _]);
        int parameters = Array.IndexOf(lines, ".parameters");
        Assert.True(parameters > 1);
        Assert.Equal(".", lines[^1]);

        return (
            header[0],
            header[1],
            header.Length == 3 ? header[2] : null,
            string.Join('\n', lines[1..parameters]),
            string.Join('\n', lines[(parameters + 1)..^1]));
    }
}
