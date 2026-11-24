using System.Text.Json;
using System.Text.Json.Nodes;

namespace Moltaro.OnecOutpost.Shell;

internal sealed record CommandFrame(string Script, JsonObject? Parameters)
{
    public static async Task<CommandFrame> ReadAsync(SessionInput input, CancellationToken cancellationToken)
    {
        List<string> script = [];
        List<string>? parameters = null;

        while (await input.ReadLineAsync(cancellationToken) is string line)
        {
            if (line == ".")
            {
                return new CommandFrame(string.Join('\n', script), ParseParameters(parameters));
            }

            if (parameters is not null)
            {
                parameters.Add(line);
            }
            else if (line == ".parameters")
            {
                parameters = [];
            }
            else
            {
                script.Add(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);
            }
        }

        throw new ShellError(ShellExit.Usage, "End the script frame with a line containing only a dot.");
    }

    private static JsonObject? ParseParameters(List<string>? lines)
    {
        if (lines is null)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(string.Join('\n', lines)) is JsonObject parameters)
            {
                return parameters;
            }
        }
        catch (JsonException)
        {
            // The frame is already consumed, so the next line is another command.
        }

        throw new ShellError(ShellExit.Usage, "The .parameters section requires a JSON object.");
    }
}
