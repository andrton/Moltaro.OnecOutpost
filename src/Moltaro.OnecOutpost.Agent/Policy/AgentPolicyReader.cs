using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Agent.Policy;

// Reads the text of policy.json by the rules of its format and applies the defaults of absent members. Any fault
// makes the whole file invalid: an unknown member, a member given twice, a wrong type, a value outside its rule, a
// required member left out, or an escaped lone surrogate in a string or a name (Q6). JSON comments and trailing commas
// are refused. A member given the value null has a wrong type, not an absent one. Names of members are compared
// exactly. The first fault in the order of the file is reported, with the path of its member and a reason that says
// what is wrong there and what is expected, in the form "what is wrong; expected what" (Q1). A reason never repeats a
// string value of the file, which may hold a secret such as a password in the URL of the proxy (principle 9): it names
// what the value is, or which rule the string breaks.
internal static class AgentPolicyReader
{
    private const int DefaultAuditRetentionDays = 90;

    private const int DefaultWorkers = 1;

    // What each object of the file may hold, for the errors: the names it allows, and what it is expected to be.
    private const string PolicyNames = "\"auditRetentionDays\", \"proxy\", \"retryDelayMax\", \"bases\"";

    private const string PolicyObject =
        "a JSON object with \"bases\", and optionally \"auditRetentionDays\", \"proxy\" and \"retryDelayMax\"";

    private const string BaseNames = "\"key\", \"server\", \"database\", \"file\", \"read\", \"write\"";

    private const string BaseObject =
        "an object with \"key\", \"read\", either \"server\" and \"database\" or \"file\", and optionally \"write\"";

    private const string ModeNames = "\"account\", \"workers\", \"runLimit\"";

    private const string ModeObject = "an object with \"account\", and optionally \"workers\" and \"runLimit\"";

    // What the values of a base are expected to be, for the errors.
    private const string BaseKeyRule =
        "a base key of 1 to 64 characters, each one of a-z 0-9 - _ ., the first a letter or a digit, such as \"accounting\"";

    private const string ServerRule =
        "the 1C cluster address such as \"srv-1c:1541\", or remove \"server\" and \"database\" and give \"file\"";

    private const string DatabaseRule =
        "the name of the database on the 1C cluster, such as \"accounting\", or remove \"server\" and \"database\" and give \"file\"";

    private const string FileRule =
        "the folder of the file database, such as \"D:\\\\1C\\\\Warehouse\", or remove \"file\" and give \"server\" and \"database\"";

    private static readonly TimeSpan DefaultRetryDelayMax = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DefaultRunLimit = TimeSpan.FromMinutes(10);

    // The constant form [d.]hh:mm:ss of a TimeSpan, without a sign and without a fraction of a second.
    private static readonly string[] TimeSpanForms = [@"hh\:mm\:ss", @"d\.hh\:mm\:ss"];

    public static AgentPolicy Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = Parse(json);

        int auditRetentionDays = DefaultAuditRetentionDays;
        Uri? proxy = null;
        TimeSpan retryDelayMax = DefaultRetryDelayMax;
        IReadOnlyList<AgentBasePolicy>? bases = null;

        foreach ((string name, JsonElement value, string path) in Members(document.RootElement, "", PolicyObject))
        {
            switch (name)
            {
                case "auditRetentionDays":
                    auditRetentionDays = ReadWholeNumber(value, path, 1, 3650);
                    break;

                case "proxy":
                    proxy = ReadProxy(value, path);
                    break;

                case "retryDelayMax":
                    retryDelayMax = ReadTimeSpan(
                        value,
                        path,
                        TimeSpan.FromSeconds(1),
                        TimeSpan.FromHours(1),
                        DefaultRetryDelayMax);
                    break;

                case "bases":
                    bases = ReadBases(value, path);
                    break;

                default:
                    throw UnknownMember(path, PolicyNames);
            }
        }

        if (bases is null)
        {
            throw MissingMember("bases", "an array of at least one base");
        }

        return new AgentPolicy(auditRetentionDays, proxy, retryDelayMax, bases);
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            // The default options refuse comments and trailing commas.
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            // The parser's own text is written for developers, so only its place of the fault is used.
            throw new AgentPolicyException(
                "",
                $"the file is not valid JSON at {Place(json, exception)}; expected plain JSON, without comments or trailing commas");
        }
    }

    // The place of a fault as a person counts it: the line from 1, and the column from 1 in characters. The reader sets
    // both for every fault it reports; it counts lines from 0 at each '\n', so its line is always one of the lines of
    // the text, and the column from 0 in UTF-8 bytes of that line.
    private static string Place(string json, JsonException exception)
    {
        long lineIndex = exception.LineNumber!.Value;
        long bytesBefore = exception.BytePositionInLine!.Value;

        string line = json.Split('\n')[lineIndex];

        int column = 1;

        foreach (Rune character in line.EnumerateRunes())
        {
            if (bytesBefore <= 0)
            {
                break;
            }

            bytesBefore -= character.Utf8SequenceLength;
            column++;
        }

        return $"line {lineIndex + 1}, column {column}";
    }

    private static List<AgentBasePolicy> ReadBases(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw WrongValue(path, value, "an array of at least one base");
        }

        if (value.GetArrayLength() == 0)
        {
            throw new AgentPolicyException(path, "the array is empty; expected at least one base");
        }

        List<AgentBasePolicy> bases = [];
        Dictionary<string, int> indexOfKey = new(StringComparer.Ordinal);

        foreach (JsonElement item in value.EnumerateArray())
        {
            string itemPath = $"{path}[{bases.Count}]";
            AgentBasePolicy basePolicy = ReadBase(item, itemPath);

            if (!indexOfKey.TryAdd(basePolicy.Key, bases.Count))
            {
                throw new AgentPolicyException(
                    $"{itemPath}.key",
                    $"the key is already the key of {path}[{indexOfKey[basePolicy.Key]}]; expected a key that no other base has");
            }

            bases.Add(basePolicy);
        }

        return bases;
    }

    private static AgentBasePolicy ReadBase(JsonElement value, string path)
    {
        string? key = null;
        string? server = null;
        string? database = null;
        string? file = null;
        AgentModePolicy? read = null;
        AgentModePolicy? write = null;

        foreach ((string name, JsonElement member, string memberPath) in Members(value, path, BaseObject))
        {
            switch (name)
            {
                case "key":
                    key = ReadBaseKey(member, memberPath);
                    break;

                case "server":
                    server = ReadText(member, memberPath, ServerRule);
                    break;

                case "database":
                    database = ReadText(member, memberPath, DatabaseRule);
                    break;

                case "file":
                    file = ReadText(member, memberPath, FileRule);
                    break;

                case "read":
                    read = ReadMode(member, memberPath, ModeObject, "outpost-read");
                    break;

                case "write":
                    write = ReadMode(
                        member,
                        memberPath,
                        $"{ModeObject}, or no \"write\" when the base may not be written",
                        "outpost-write");
                    break;

                default:
                    throw UnknownMember(memberPath, BaseNames);
            }
        }

        if (key is null)
        {
            throw MissingMember($"{path}.key", BaseKeyRule);
        }

        // A base is either on a cluster, with server and database, or in a folder, with file.
        if (file is not null && (server is not null || database is not null))
        {
            throw new AgentPolicyException(
                $"{path}.file",
                "the base has \"file\" and also \"server\" or \"database\"; expected either \"server\" and \"database\" for a base on a 1C cluster, or \"file\" for a file database, not both");
        }

        if (file is null && server is null)
        {
            throw MissingMember(
                $"{path}.server",
                "the 1C cluster address such as \"srv-1c:1541\", with \"database\", or \"file\" instead for a file database");
        }

        if (file is null && database is null)
        {
            throw MissingMember(
                $"{path}.database",
                "the name of the database on the 1C cluster, such as \"accounting\", or remove \"server\" and give \"file\"");
        }

        if (read is null)
        {
            throw MissingMember($"{path}.read", $"{ModeObject}, such as {{ \"account\": \"outpost-read\" }}");
        }

        return new AgentBasePolicy(key, server, database, file, read, write);
    }

    private static AgentModePolicy ReadMode(JsonElement value, string path, string expectedObject, string exampleAccount)
    {
        string accountRule = $"the 1C account of the mode, such as \"{exampleAccount}\"";
        string? account = null;
        int workers = DefaultWorkers;
        TimeSpan runLimit = DefaultRunLimit;

        foreach ((string name, JsonElement member, string memberPath) in Members(value, path, expectedObject))
        {
            switch (name)
            {
                case "account":
                    account = ReadText(member, memberPath, accountRule);
                    break;

                case "workers":
                    workers = ReadWholeNumber(member, memberPath, 1, OutpostProtocol.MaxWorkers);
                    break;

                case "runLimit":
                    runLimit = ReadTimeSpan(
                        member,
                        memberPath,
                        OutpostProtocol.MinRunLimit,
                        OutpostProtocol.MaxRunLimit,
                        DefaultRunLimit);
                    break;

                default:
                    throw UnknownMember(memberPath, ModeNames);
            }
        }

        if (account is null)
        {
            throw MissingMember($"{path}.account", accountRule);
        }

        return new AgentModePolicy(account, workers, runLimit);
    }

    // The members of an object, each with its path. A value that is not an object, or a member given twice, makes the
    // file invalid: taking one of two values would be a guess.
    private static IEnumerable<(string Name, JsonElement Value, string Path)> Members(
        JsonElement value,
        string path,
        string expectedObject)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw WrongValue(path, value, expectedObject);
        }

        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (JsonProperty member in value.EnumerateObject())
        {
            string name = Name(member, path);
            string memberPath = path.Length == 0 ? name : $"{path}.{name}";

            if (!names.Add(name))
            {
                throw new AgentPolicyException(memberPath, "the member is given twice in one object; expected it once");
            }

            yield return (name, member.Value, memberPath);
        }
    }

    // The name of a member. A name with an escaped lone surrogate cannot be read as a string (Name throws
    // InvalidOperationException), so its path shows the name as the file writes it, escapes included: a name is not a
    // value, and the path of an unknown member shows its name as well.
    private static string Name(JsonProperty member, string path)
    {
        try
        {
            return member.Name;
        }
        catch (InvalidOperationException)
        {
            string written = Encoding.UTF8.GetString(JsonMarshal.GetRawUtf8PropertyName(member));

            throw LoneSurrogate(path.Length == 0 ? written : $"{path}.{written}", "the name");
        }
    }

    // The string of a value. Only an escaped lone surrogate makes GetString throw InvalidOperationException: the text
    // came in as a .NET string, so its UTF-8 is valid, and a raw lone surrogate in it, which no file decoded from its
    // bytes holds, makes JsonDocument.Parse throw ArgumentException before any value is read.
    private static string Text(JsonElement value, string path)
    {
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw LoneSurrogate(path, path.Length == 0 ? "the value of the file" : "the value");
        }
    }

    private static int ReadWholeNumber(JsonElement value, string path, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number)
            || number < minimum
            || number > maximum)
        {
            throw WrongValue(path, value, $"a whole number from {minimum} to {maximum}");
        }

        return number;
    }

    // A string that is empty or only white space names nothing, so it is refused like a wrong type.
    private static string ReadText(JsonElement value, string path, string expected)
    {
        string? text = value.ValueKind == JsonValueKind.String ? Text(value, path) : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw WrongValue(path, value, expected);
        }

        return text;
    }

    // A string that breaks the rule of base keys is named by that rule, never repeated.
    private static string ReadBaseKey(JsonElement value, string path)
    {
        string key = ReadText(value, path, BaseKeyRule);

        if (!OutpostNames.IsBaseKey(key))
        {
            throw new AgentPolicyException(path, $"the value is not a base key; expected {BaseKeyRule}");
        }

        return key;
    }

    // A string that is not an absolute http URL is named by that rule, never repeated.
    private static Uri ReadProxy(JsonElement value, string path)
    {
        const string Expected =
            "an absolute http URL such as \"http://proxy.local:3128\", or no \"proxy\" to connect directly";

        string text = ReadText(value, path, Expected);

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? proxy) || proxy.Scheme != Uri.UriSchemeHttp)
        {
            throw new AgentPolicyException(path, $"the value is not an absolute http URL; expected {Expected}");
        }

        return proxy;
    }

    // A string of the wrong form, or of a time span outside the range, is named by the rule it breaks, never repeated.
    private static TimeSpan ReadTimeSpan(
        JsonElement value,
        string path,
        TimeSpan minimum,
        TimeSpan maximum,
        TimeSpan example)
    {
        string expected =
            $"a time span of the form [d.]hh:mm:ss from {minimum:c} to {maximum:c}, such as \"{example:c}\"";

        string text = ReadText(value, path, expected);

        if (!TimeSpan.TryParseExact(text, TimeSpanForms, CultureInfo.InvariantCulture, out TimeSpan span))
        {
            throw new AgentPolicyException(
                path,
                $"the value is not a time span of the form [d.]hh:mm:ss; expected {expected}");
        }

        if (span < minimum || span > maximum)
        {
            throw new AgentPolicyException(path, $"the time span is outside its range; expected {expected}");
        }

        return span;
    }

    private static AgentPolicyException WrongValue(string path, JsonElement value, string expected)
    {
        string subject = path.Length == 0 ? "the value of the file" : "the value";

        return new AgentPolicyException(path, $"{subject} is {Found(value, path)}; expected {expected}");
    }

    private static AgentPolicyException UnknownMember(string path, string allowedNames)
    {
        return new AgentPolicyException(
            path,
            $"the member is unknown; expected one of {allowedNames} (names are case-sensitive)");
    }

    private static AgentPolicyException MissingMember(string path, string expected)
    {
        return new AgentPolicyException(path, $"the member is missing; expected {expected}");
    }

    // No UTF-8 can carry a lone surrogate, so it breaks the rule of every string and name of the file (Q6). The reason
    // names the escape by its range, never the string that holds it.
    private static AgentPolicyException LoneSurrogate(string path, string subject)
    {
        return new AgentPolicyException(
            path,
            $"{subject} holds a lone surrogate, an escape from \\uD800 to \\uDFFF without its pair, which UTF-8 cannot carry; expected surrogates only in pairs, an escape from \\uD800 to \\uDBFF followed at once by one from \\uDC00 to \\uDFFF");
    }

    // What the file holds where a value breaks its rule. A string is named only by its kind, never repeated; a number,
    // true, false and null hold no secret and are shown as written; an object or an array is named by its kind.
    private static string Found(JsonElement value, string path)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => "an object",
            JsonValueKind.Array => "an array",
            JsonValueKind.String when Text(value, path).Length == 0 => "empty",
            JsonValueKind.String when string.IsNullOrWhiteSpace(Text(value, path)) => "only white space",
            JsonValueKind.String => "a string",
            JsonValueKind.Number => $"the number {value.GetRawText()}",

            // true, false or null.
            _ => value.GetRawText(),
        };
    }
}
