using System.Buffers;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.OneC.Tests.Lab;

namespace Moltaro.OnecOutpost.OneC.Tests.Guides;

// The scripts of guide G2, read from its Markdown (the S9b brief, "Guide G2"; the plan, "The user guides": the source of
// a script is the code block of the guide, and no copy of it is kept elsewhere). A script is a fenced block whose info
// string is bsl, then its mode, read or write, and, for a script that does not succeed, the kind and the code it gives,
// such as "bsl read failed script-error". Its parameters are the next block whose info string is "json parameters"
// before the next script, a JSON object; a script without one has none, {}. A block of "json result" is shown to the
// reader and not compared, since the data come from the base, and no other block is read. A bsl block in any other
// form, and a block of parameters that is not an object or follows no script of its own, are errors of the guide, each
// with its line, which a test reports.
internal sealed class ScriptGuide
{
    private const string Fence = "```";

    private ScriptGuide(IReadOnlyList<GuideScript> scripts, IReadOnlyList<string> errors)
    {
        Scripts = scripts;
        Errors = errors;
    }

    public IReadOnlyList<GuideScript> Scripts { get; }

    public IReadOnlyList<string> Errors { get; }

    public static ScriptGuide Read()
    {
        return Parse(File.ReadAllLines(Repository.ScriptGuidePath));
    }

    private static ScriptGuide Parse(string[] lines)
    {
        List<GuideScript> scripts = [];
        List<string> errors = [];
        string heading = "the start of the guide";

        // Whether the last script has its block of parameters, or there is no script yet.
        bool parametersGiven = true;
        int index = 0;

        while (index < lines.Length)
        {
            string trimmed = lines[index].TrimStart();

            if (!trimmed.StartsWith(Fence, StringComparison.Ordinal))
            {
                if (trimmed.StartsWith('#'))
                {
                    heading = trimmed.TrimStart('#').Trim();
                }

                index++;
                continue;
            }

            int line = index + 1;
            int indent = lines[index].Length - trimmed.Length;
            string[] info = trimmed[Fence.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            StringBuilder text = new();
            index++;

            while (index < lines.Length && !lines[index].TrimStart().StartsWith(Fence, StringComparison.Ordinal))
            {
                text.Append(Unindented(lines[index], indent)).Append('\n');
                index++;
            }

            if (index == lines.Length)
            {
                errors.Add($"Line {line}: the block has no closing fence.");
                break;
            }

            index++;

            if (info is ["bsl", ..])
            {
                GuideScript? script = info switch
                {
                    ["bsl", "read" or "write"] => new GuideScript(line, heading, info[1], "succeeded", null, text.ToString(), "{}"),
                    ["bsl", "read" or "write", "failed" or "unknown", _] =>
                        new GuideScript(line, heading, info[1], info[2], info[3], text.ToString(), "{}"),
                    _ => null,
                };

                if (script is null)
                {
                    errors.Add(
                        $"Line {line}: a bsl block names no mode, or not as bsl read, bsl write, or either followed by a kind and a code, such as bsl read failed script-error.");
                }
                else
                {
                    scripts.Add(script);
                    parametersGiven = false;
                }
            }
            else if (info is ["json", "parameters"])
            {
                string? parameters = Compact(text.ToString());

                if (parametersGiven)
                {
                    errors.Add($"Line {line}: a block of parameters follows no script of its own.");
                }
                else if (parameters is null)
                {
                    errors.Add($"Line {line}: the parameters are not a JSON object.");
                }
                else
                {
                    scripts[^1] = scripts[^1] with { Parameters = parameters };
                }

                parametersGiven = true;
            }
        }

        return new ScriptGuide(scripts, errors);
    }

    private static string Unindented(string line, int indent)
    {
        int spaces = line.Length - line.TrimStart(' ').Length;

        return line[Math.Min(spaces, indent)..];
    }

    // The JSON object written on one line, as a command line carries its parameters, or null when the text is not one.
    private static string? Compact(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            ArrayBufferWriter<byte> compact = new();

            using (Utf8JsonWriter writer = new(compact))
            {
                document.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(compact.WrittenSpan);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
