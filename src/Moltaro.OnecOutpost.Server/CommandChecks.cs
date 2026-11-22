using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;

namespace Moltaro.OnecOutpost.Server;

// The rule of the parameters of a command and the rule of its size, which the checks of ReadAsync and WriteAsync run
// after the rules of the other names.
//
// The nesting and the size are those of the protocol, which the codec computes: the levels of docs/protocol.md §2,
// and the size of §7, the text of the script between its quotes plus the text of the parameters, `{}` when there are
// none, both as the command carries them, in UTF-8 with every escape counted as it is written.
internal static class CommandChecks
{
    internal static void Check(string script, JsonObject? parameters, int commandSizeLimit)
    {
        // A command without parameters carries an empty object.
        using JsonDocument written = Write(parameters ?? []);

        if (OutpostCodec.ValueDepth(written.RootElement) > OutpostProtocol.MaxValueDepth)
        {
            throw new ArgumentException(
                $"The parameters nest deeper than {OutpostProtocol.MaxValueDepth} levels, counted from their own outer value, the outer object being level 1.",
                nameof(OutpostReadRequest.Parameters));
        }

        // Before the walk of the caller's object: a JsonObject parsed from text that gives a name twice throws an
        // exception of its own when it is walked.
        CheckText(written.RootElement);

        if (parameters is not null)
        {
            CheckLoneSurrogates(parameters);
        }

        long size = OutpostCodec.CommandSize(script, written.RootElement);

        if (size > commandSizeLimit)
        {
            throw new ArgumentException(
                $"The command is {size} bytes, more than the limit of {commandSizeLimit} bytes: the text of its script and of its parameters as the command carries them, in UTF-8 with their escapes.",
                "request");
        }
    }

    // Written as JSON when the call is made, as the contract says. Parsing the text again gives every string unescaped,
    // whatever the writer escaped. Parameters that cannot be written as JSON are the caller's mistake, whatever the
    // reason: the writer refuses, among others, a number that is not finite, a value of a .NET type it cannot write
    // and a nesting deeper than its own limit, which is far above that of the protocol. Each with an exception of its
    // own, so every exception is caught: the try touches nothing but the caller's object and the text written from
    // it. The reader sets no limit of nesting of its own: the writer has bounded it, and the check of the protocol's
    // limit follows.
    private static JsonDocument Write(JsonObject parameters)
    {
        try
        {
            return JsonDocument.Parse(parameters.ToJsonString(), new JsonDocumentOptions { MaxDepth = int.MaxValue });
        }
        catch (Exception exception)
        {
            throw new ArgumentException(
                $"The parameters cannot be written as JSON: {exception.Message}",
                nameof(OutpostReadRequest.Parameters),
                exception);
        }
    }

    // System.Text.Json writes a lone surrogate of a .NET string as U+FFFD without a word, so the text read back never
    // holds one, and the names and strings of the caller's object are checked as the caller gave them (Q6). A string
    // inside a value of another .NET type that the caller wrapped in a JsonValue, such as a char or an array, is
    // written by System.Text.Json alone and travels with U+FFFD in place of a lone surrogate. Called once the object
    // was written and its nesting checked, so it nests at most the levels of the protocol; an escaped lone surrogate
    // in a JsonElement behind the object was refused by the writer already.
    private static void CheckLoneSurrogates(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                foreach ((string name, JsonNode? value) in members)
                {
                    CheckLoneSurrogate(name);
                    CheckLoneSurrogates(value);
                }

                break;

            case JsonArray items:
                foreach (JsonNode? item in items)
                {
                    CheckLoneSurrogates(item);
                }

                break;

            case JsonValue value when value.TryGetValue(out string? text):
                CheckLoneSurrogate(text);
                break;
        }
    }

    private static void CheckLoneSurrogate(string text)
    {
        if (OutpostNames.HasLoneSurrogate(text))
        {
            throw new ArgumentException(
                "No string in the parameters, names included, may hold a lone surrogate, a code unit from U+D800 to U+DFFF without its pair, which UTF-8 cannot carry.",
                nameof(OutpostReadRequest.Parameters));
        }
    }

    // The rules of names and strings of docs/protocol.md §2, which the agent applies to the command alike: no name twice
    // in one object, the names compared after unescaping, and no name or string with U+0000, which PostgreSQL cannot
    // store. Checked on the text read back, which holds every string that travels, those of wrapped .NET values
    // included. jsonb keeps the last value of a name given twice, so without this check a repeat could compare equal to
    // parameters other than those signed (rule 4).
    private static void CheckText(JsonElement parameters)
    {
        try
        {
            OutpostJsonReader.CheckText(parameters);
        }
        catch (OutpostProtocolException exception)
        {
            throw new ArgumentException(
                $"The parameters break the rules of the protocol for names and strings: {exception.Message}",
                nameof(OutpostReadRequest.Parameters),
                exception);
        }
    }
}
