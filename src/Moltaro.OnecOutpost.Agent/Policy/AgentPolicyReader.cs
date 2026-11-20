using System.Globalization;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Agent.Policy;

// Reads the text of policy.json by the rules of its format and applies the defaults of absent members. Any fault
// makes the whole file invalid: an unknown member, a member given twice, a wrong type, a value outside its rule, or a
// required member left out. JSON comments and trailing commas are refused. A member given the value null has a wrong
// type, not an absent one. Names of members are compared exactly. The first fault in the order of the file is reported,
// with the path of its member.
internal static class AgentPolicyReader
{
    private const int DefaultAuditRetentionDays = 90;

    private const int DefaultWorkers = 1;

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

        foreach ((string name, JsonElement value, string path) in Members(document.RootElement, ""))
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
                    retryDelayMax = ReadTimeSpan(value, path, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1));
                    break;

                case "bases":
                    bases = ReadBases(value, path);
                    break;

                default:
                    throw UnknownMember(path);
            }
        }

        if (bases is null)
        {
            throw MissingMember("bases");
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
            throw new AgentPolicyException("", $"The file is not valid JSON: {exception.Message}");
        }
    }

    private static List<AgentBasePolicy> ReadBases(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            throw new AgentPolicyException(path, "An array of at least one base is required.");
        }

        List<AgentBasePolicy> bases = [];
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (JsonElement item in value.EnumerateArray())
        {
            string itemPath = $"{path}[{bases.Count}]";
            AgentBasePolicy basePolicy = ReadBase(item, itemPath);

            if (!keys.Add(basePolicy.Key))
            {
                throw new AgentPolicyException($"{itemPath}.key", "An earlier base has the same key.");
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

        foreach ((string name, JsonElement member, string memberPath) in Members(value, path))
        {
            switch (name)
            {
                case "key":
                    key = ReadBaseKey(member, memberPath);
                    break;

                case "server":
                    server = ReadText(member, memberPath);
                    break;

                case "database":
                    database = ReadText(member, memberPath);
                    break;

                case "file":
                    file = ReadText(member, memberPath);
                    break;

                case "read":
                    read = ReadMode(member, memberPath);
                    break;

                case "write":
                    write = ReadMode(member, memberPath);
                    break;

                default:
                    throw UnknownMember(memberPath);
            }
        }

        if (key is null)
        {
            throw MissingMember($"{path}.key");
        }

        // A base is either on a cluster, with server and database, or in a folder, with file.
        if (file is not null && (server is not null || database is not null))
        {
            throw new AgentPolicyException($"{path}.file", "A base has either server and database, or file, not both.");
        }

        if (file is null && server is null)
        {
            throw new AgentPolicyException($"{path}.server", "A base has either server and database, or file.");
        }

        if (file is null && database is null)
        {
            throw new AgentPolicyException($"{path}.database", "A base has either server and database, or file.");
        }

        if (read is null)
        {
            throw MissingMember($"{path}.read");
        }

        return new AgentBasePolicy(key, server, database, file, read, write);
    }

    private static AgentModePolicy ReadMode(JsonElement value, string path)
    {
        string? account = null;
        int workers = DefaultWorkers;
        TimeSpan runLimit = DefaultRunLimit;

        foreach ((string name, JsonElement member, string memberPath) in Members(value, path))
        {
            switch (name)
            {
                case "account":
                    account = ReadText(member, memberPath);
                    break;

                case "workers":
                    workers = ReadWholeNumber(member, memberPath, 1, 8);
                    break;

                case "runLimit":
                    runLimit = ReadTimeSpan(member, memberPath, TimeSpan.FromSeconds(1), TimeSpan.FromDays(1));
                    break;

                default:
                    throw UnknownMember(memberPath);
            }
        }

        if (account is null)
        {
            throw MissingMember($"{path}.account");
        }

        return new AgentModePolicy(account, workers, runLimit);
    }

    // The members of an object, each with its path. A value that is not an object, or a member given twice, makes the
    // file invalid: taking one of two values would be a guess.
    private static IEnumerable<(string Name, JsonElement Value, string Path)> Members(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new AgentPolicyException(path, "A JSON object is required.");
        }

        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (JsonProperty member in value.EnumerateObject())
        {
            string memberPath = path.Length == 0 ? member.Name : $"{path}.{member.Name}";

            if (!names.Add(member.Name))
            {
                throw new AgentPolicyException(memberPath, "The member is given twice.");
            }

            yield return (member.Name, member.Value, memberPath);
        }
    }

    private static int ReadWholeNumber(JsonElement value, string path, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int number)
            || number < minimum
            || number > maximum)
        {
            throw new AgentPolicyException(path, $"A whole number from {minimum} to {maximum} is required.");
        }

        return number;
    }

    // A string that is empty or only white space names nothing, so it is refused like a wrong type.
    private static string ReadText(JsonElement value, string path)
    {
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AgentPolicyException(path, "A string that is not empty is required.");
        }

        return text;
    }

    private static string ReadBaseKey(JsonElement value, string path)
    {
        string? key = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        if (key is null || !OutpostNames.IsBaseKey(key))
        {
            throw new AgentPolicyException(
                path,
                "A base key is required: 1 to 64 characters, each one of a-z 0-9 - _ ., the first a letter or a digit.");
        }

        return key;
    }

    private static Uri ReadProxy(JsonElement value, string path)
    {
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? proxy) || proxy.Scheme != Uri.UriSchemeHttp)
        {
            throw new AgentPolicyException(path, "An absolute http URL is required.");
        }

        return proxy;
    }

    private static TimeSpan ReadTimeSpan(JsonElement value, string path, TimeSpan minimum, TimeSpan maximum)
    {
        string? text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        if (!TimeSpan.TryParseExact(text, TimeSpanForms, CultureInfo.InvariantCulture, out TimeSpan span)
            || span < minimum
            || span > maximum)
        {
            throw new AgentPolicyException(
                path,
                $"A time span of the form [d.]hh:mm:ss from {minimum:c} to {maximum:c} is required.");
        }

        return span;
    }

    private static AgentPolicyException UnknownMember(string path)
    {
        return new AgentPolicyException(path, "The member is not part of the policy.");
    }

    private static AgentPolicyException MissingMember(string path)
    {
        return new AgentPolicyException(path, "The member is required.");
    }
}
