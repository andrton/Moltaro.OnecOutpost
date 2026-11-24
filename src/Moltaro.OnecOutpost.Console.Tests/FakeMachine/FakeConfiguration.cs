using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The metadata of a base on a test's machine as the script of metadata, MetadataScripts.Walk, reads it in a worker's
// session, each Read answered by the member read of its parameters as the script would answer it (the S10 brief, "The
// script" and "The report"). These answers are the form that the script gives and the console reads:
// - census: {"platform", "configuration": {"name", "version", "compatibilityMode"}, "categories": [{"category",
//   "count"}]}, the fifteen categories in the report's order, each with the Count() of its collection; each of the
//   four values a string, or {"error": "{1C's text}"} in its place for one that 1C cannot give, read in a Try of its
//   own (the S10 brief, "After the lab run of deeb370", 1);
// - page, with category, from, count and storage: {"objects": [...], "storageError"}, the objects of the category from
//   the index from, count of them at most, each as the report holds it but for its category: name, fullName, synonym,
//   comment, properties, fields, values, storage and error, storage its tables when storage is true and null otherwise;
//   storageError null, or the text of a raise of GetDBStorageStructureInfo, which leaves every storage null and every
//   error as it was; a property of an object that 1C cannot give, read in a Try of its own, is left out, and the
//   object's error names it, {property}: {text}, several joined by "; ", the rest of the object read;
// - common: the array of the storage tables of no object of the fifteen categories, such as a sequence's.
// An answer is the executor's outcome. The test adds the objects and their tables, and says which Read fails and how:
// a census that fails or overruns, or one of its values that 1C cannot give; a property of an object that 1C cannot
// give; a page that holds an object too large, one that overruns, one that fails in 1C or
// one that loses the connection, after which the worker ends; a page with storage that holds an object whose storage
// raises; a common that fails. A Read that overruns gives nothing until the worker is killed. Each Read is first given
// to Reading, with its description: census, page {category} {from} {count}, storage at its end when it asks for the
// storage, or common, the words that the line run of the fake worker gives it; then its answer is kept in Answers, in
// the order of the Reads. Not thread-safe: metadata makes one Read at a time.
internal sealed class FakeConfiguration
{
    public const string Platform = "8.3.17.1851";

    // The fifteen categories that the script reads, in the report's order.
    public static readonly IReadOnlyList<string> Categories =
    [
        "Catalogs",
        "Documents",
        "Enums",
        "Constants",
        "InformationRegisters",
        "AccumulationRegisters",
        "AccountingRegisters",
        "CalculationRegisters",
        "ChartsOfCharacteristicTypes",
        "ChartsOfAccounts",
        "ChartsOfCalculationTypes",
        "BusinessProcesses",
        "Tasks",
        "ExchangePlans",
        "CommonAttributes",
    ];

    private readonly Dictionary<string, List<JsonObject>> _objects =
        Categories.ToDictionary(category => category, _ => new List<JsonObject>(), StringComparer.Ordinal);

    // The storage tables of each object, by its full name.
    private readonly Dictionary<string, JsonArray> _storage = new(StringComparer.Ordinal);

    private readonly JsonArray _commonStorage = [];

    private readonly HashSet<string> _tooLarge = new(StringComparer.Ordinal);

    private readonly HashSet<string> _overruns = new(StringComparer.Ordinal);

    private readonly HashSet<string> _losesConnection = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _pageErrors = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _storageErrors = new(StringComparer.Ordinal);

    // The text of each value of the census that 1C cannot give, by its member: platform, name, version or
    // compatibilityMode.
    private readonly Dictionary<string, string> _censusErrors = new(StringComparer.Ordinal);

    // The properties of each object that 1C cannot give, by its full name, with their texts, in their order.
    private readonly Dictionary<string, List<(string Property, string Text)>> _propertyErrors = new(StringComparer.Ordinal);

    private readonly List<(string Read, byte[]? Outcome)> _answers = [];

    private int _tables;

    private string? _censusError;

    private bool _censusOverruns;

    private string? _commonError;

    public string Name { get; init; } = "OutpostTest";

    public string Version { get; init; } = "1.0.0.1";

    public string CompatibilityMode { get; init; } = "Version8_2_13";

    // Called with the description of each Read before it is answered, such as by a test that acts in the middle of a
    // walk.
    public Action<string>? Reading { get; set; }

    // Each Read answered so far, by its description, with the executor's outcome, null for one that overran.
    public IReadOnlyList<(string Read, byte[]? Outcome)> Answers => _answers;

    // The storage tables of no object of the fifteen categories.
    public JsonArray CommonStorage => _commonStorage;

    // A configuration of six objects in five categories, which the walk reads in five pages, with a table of storage
    // each and two of common storage: the hierarchical catalog Products, whose synonym is Russian and whose comment
    // holds characters that JSON may escape, and Units; the document GoodsReceipt, with a reference, a number and a
    // string; the enum Statuses with its two values; the constant Currency; and the accumulation register Stock with
    // its dimension and its resource. The other ten categories are empty.
    public static FakeConfiguration Standard()
    {
        FakeConfiguration configuration = new();

        JsonObject products = configuration.Add("Catalogs", "Catalog.Products", "Товары");
        products["comment"] = "Goods & services <sold> 'here'";
        products["properties"] = new JsonObject
        {
            ["hierarchical"] = true,
            ["hierarchyType"] = "HierarchyFoldersAndItems",
            ["codeLength"] = 9,
            ["descriptionLength"] = 25,
            ["owners"] = new JsonArray(),
        };

        configuration.Add("Catalogs", "Catalog.Units", "Units");

        JsonObject receipt = configuration.Add("Documents", "Document.GoodsReceipt", "Goods receipt");
        receipt["properties"] = new JsonObject
        {
            ["numberLength"] = 9,
            ["numberType"] = "String",
            ["numberPeriodicity"] = "Year",
            ["posting"] = "Allow",
            ["registerRecords"] = new JsonArray("AccumulationRegister.Stock"),
        };
        receipt["fields"] = new JsonArray(
            Field("Attributes", "Product", "Catalog.Products"),
            Number("Attributes", "Quantity", 15, 3),
            Text("Attributes", "Comment", 200));

        JsonObject statuses = configuration.Add("Enums", "Enum.Statuses", "Statuses");
        statuses["fields"] = new JsonArray();
        statuses["values"] = new JsonArray(
            new JsonObject { ["name"] = "Open", ["synonym"] = "Open" },
            new JsonObject { ["name"] = "Closed", ["synonym"] = "Closed" });

        JsonObject currency = configuration.Add("Constants", "Constant.Currency", "Currency");
        currency["fields"] = new JsonArray(Text("Value", "Value", 3));

        JsonObject stock = configuration.Add("AccumulationRegisters", "AccumulationRegister.Stock", "Stock");
        stock["properties"] = new JsonObject { ["registerType"] = "Balance" };
        stock["fields"] = new JsonArray(
            Field("Dimensions", "Product", "Catalog.Products"),
            Number("Resources", "Quantity", 15, 3));

        configuration.AddCommonStorage("_Seq30", "Sequence.Goods", "Sequence.Goods", "Sequence");
        configuration.AddCommonStorage("_ConfigSave", "ConfigSave", "", "ConfigSave");

        return configuration;
    }

    // A field of the collection with the types, none of them a string, a number or a date.
    public static JsonObject Field(string collection, string name, params string[] types)
    {
        return new JsonObject
        {
            ["collection"] = collection,
            ["tabularSection"] = null,
            ["name"] = name,
            ["synonym"] = name,
            ["indexing"] = "DontIndex",
            ["types"] = new JsonArray([.. types.Select(type => JsonValue.Create(type))]),
            ["string"] = null,
            ["number"] = null,
            ["date"] = null,
        };
    }

    // A field of the collection that holds a string of the length.
    public static JsonObject Text(string collection, string name, int length)
    {
        JsonObject field = Field(collection, name, "String");
        field["string"] = new JsonObject { ["length"] = length, ["allowedLength"] = "Variable" };

        return field;
    }

    // A field of the collection that holds a number of the digits.
    public static JsonObject Number(string collection, string name, int digits, int fractionDigits)
    {
        JsonObject field = Field(collection, name, "Number");
        field["number"] = new JsonObject { ["digits"] = digits, ["fractionDigits"] = fractionDigits, ["allowedSign"] = "Any" };

        return field;
    }

    // Adds an object to the end of the category, as a page gives it but for its storage, with the attribute Description
    // as its one field and a table of its own, and gives it, for the test to change before the walk.
    public JsonObject Add(string category, string fullName, string synonym)
    {
        JsonObject item = new()
        {
            ["name"] = fullName[(fullName.IndexOf('.', StringComparison.Ordinal) + 1)..],
            ["fullName"] = fullName,
            ["synonym"] = synonym,
            ["comment"] = "",
            ["properties"] = new JsonObject(),
            ["fields"] = new JsonArray(Text("StandardAttributes", "Description", 25)),
            ["values"] = new JsonArray(),
            ["error"] = null,
        };

        _objects[category].Add(item);

        _tables++;
        string table = $"_Table{_tables.ToString(CultureInfo.InvariantCulture)}";
        _storage[fullName] = new JsonArray(new JsonObject
        {
            ["storageName"] = table,
            ["name"] = fullName,
            ["metadata"] = fullName,
            ["purpose"] = "Main",
            ["fields"] = new JsonArray(new JsonObject { ["storageName"] = "_IDRRef", ["name"] = "Ref", ["metadata"] = "" }),
            ["indexes"] = new JsonArray(new JsonObject
            {
                ["storageName"] = $"{table}_ByRef",
                ["fields"] = new JsonArray(new JsonObject { ["storageName"] = "_IDRRef", ["name"] = "Ref" }),
            }),
        });

        return item;
    }

    // Adds a table of storage of no object of the fifteen categories.
    public void AddCommonStorage(string storageName, string name, string metadata, string purpose)
    {
        _commonStorage.Add(new JsonObject
        {
            ["storageName"] = storageName,
            ["name"] = name,
            ["metadata"] = metadata,
            ["purpose"] = purpose,
            ["fields"] = new JsonArray(),
            ["indexes"] = new JsonArray(),
        });
    }

    // The objects of the category, in the order of its collection.
    public IReadOnlyList<JsonObject> ObjectsOf(string category)
    {
        return _objects[category];
    }

    // The object as a page gives it, a copy of its own: with its tables when storage is true, null otherwise.
    public JsonObject Paged(string fullName, bool storage)
    {
        JsonObject item = (JsonObject)_objects.Values.SelectMany(objects => objects).Single(item => FullNameOf(item) == fullName).DeepClone();
        item["storage"] = storage ? _storage[fullName].DeepClone() : null;

        if (_propertyErrors.TryGetValue(fullName, out List<(string Property, string Text)>? failed))
        {
            JsonObject properties = (JsonObject)item["properties"]!;

            foreach ((string property, _) in failed)
            {
                properties.Remove(property);
            }

            item["error"] = string.Join("; ", failed.Select(error => $"{error.Property}: {error.Text}"));
        }

        return item;
    }

    // The census fails in 1C with the text.
    public void CensusFails(string errorText)
    {
        _censusError = errorText;
    }

    // The value of the census, platform, name, version or compatibilityMode, cannot be given by 1C, which raises the
    // text, such as XMLString for a value without an XML form.
    public void CensusValueFails(string member, string text)
    {
        _censusErrors[member] = text;
    }

    // The property of the object, a member of its properties such as hierarchyType, cannot be given by 1C, which raises
    // the text.
    public void PropertyFails(string fullName, string property, string text)
    {
        if (!_propertyErrors.TryGetValue(fullName, out List<(string Property, string Text)>? failed))
        {
            failed = [];
            _propertyErrors[fullName] = failed;
        }

        failed.Add((property, text));
    }

    // The census runs past any run limit.
    public void CensusOverruns()
    {
        _censusOverruns = true;
    }

    // A page that holds the object gives result-too-large.
    public void TooLarge(string fullName)
    {
        _tooLarge.Add(fullName);
    }

    // A page that holds the object runs past any run limit.
    public void Overruns(string fullName)
    {
        _overruns.Add(fullName);
    }

    // A page that holds the object fails in 1C with the text.
    public void FailsPage(string fullName, string errorText)
    {
        _pageErrors[fullName] = errorText;
    }

    // A page that holds the object loses the connection to the base, which ends the worker.
    public void LosesConnection(string fullName)
    {
        _losesConnection.Add(fullName);
    }

    // A page with storage that holds the object gives the raise of GetDBStorageStructureInfo as its storageError.
    public void StorageFails(string fullName, string storageError)
    {
        _storageErrors[fullName] = storageError;
    }

    // The common storage fails in 1C with the text.
    public void CommonFails(string errorText)
    {
        _commonError = errorText;
    }

    // The words by which the line run of a fake worker and Reading name a Read of the walk, by its parameters.
    public static string Describe(string parametersJson)
    {
        using JsonDocument parameters = JsonDocument.Parse(parametersJson);
        JsonElement root = parameters.RootElement;

        if (!root.TryGetProperty("read", out JsonElement read) || read.ValueKind != JsonValueKind.String)
        {
            return $"unknown {parametersJson}";
        }

        if (read.GetString() != "page")
        {
            return read.GetString()!;
        }

        string page = $"page {TextOf(root, "category")} {TextOf(root, "from")} {TextOf(root, "count")}";

        return root.TryGetProperty("storage", out JsonElement storage) && storage.ValueKind == JsonValueKind.True ? $"{page} storage" : page;
    }

    // The executor's outcome of the Read of the walk with the parameters, null for one that overruns.
    public byte[]? OutcomeOf(string parametersJson)
    {
        string read = Describe(parametersJson);

        Reading?.Invoke(read);

        byte[]? outcome = Answer(parametersJson);
        _answers.Add((read, outcome));

        return outcome;
    }

    private static string FullNameOf(JsonObject item)
    {
        return item["fullName"]!.GetValue<string>();
    }

    private static string TextOf(JsonElement parameters, string member)
    {
        return parameters.TryGetProperty(member, out JsonElement value) ? value.ToString() : "-";
    }

    private static byte[] Succeeded(JsonNode result)
    {
        using JsonDocument document = JsonDocument.Parse(result.ToJsonString());

        return OutpostCodec.Write(new OutpostOutcomeMessage(
            OutpostAgentOutcomeKindEnum.Succeeded,
            null,
            document.RootElement.Clone(),
            null,
            [],
            false));
    }

    private static byte[] Failed(string code, string? errorText)
    {
        return OutpostCodec.Write(new OutpostOutcomeMessage(OutpostAgentOutcomeKindEnum.Failed, code, null, errorText, [], false));
    }

    private byte[]? Answer(string parametersJson)
    {
        using JsonDocument parameters = JsonDocument.Parse(parametersJson);
        JsonElement root = parameters.RootElement;

        switch (root.TryGetProperty("read", out JsonElement read) ? read.GetString() : null)
        {
            case "census":
                return Census();
            case "page" when root.TryGetProperty("category", out JsonElement category)
                && root.TryGetProperty("from", out JsonElement from)
                && root.TryGetProperty("count", out JsonElement count)
                && root.TryGetProperty("storage", out JsonElement storage):
                return Page(category.GetString()!, from.GetInt32(), count.GetInt32(), storage.GetBoolean());
            case "common":
                return Common();
            default:
                return Failed(OutpostOutcomeCodes.ScriptError, $"The fake configuration reads nothing by {parametersJson}.");
        }
    }

    private byte[]? Census()
    {
        if (_censusOverruns)
        {
            return null;
        }

        if (_censusError is string error)
        {
            return Failed(OutpostOutcomeCodes.ScriptError, error);
        }

        return Succeeded(new JsonObject
        {
            ["platform"] = CensusValue("platform", Platform),
            ["configuration"] = new JsonObject
            {
                ["name"] = CensusValue("name", Name),
                ["version"] = CensusValue("version", Version),
                ["compatibilityMode"] = CensusValue("compatibilityMode", CompatibilityMode),
            },
            ["categories"] = new JsonArray(
                [.. Categories.Select(category => new JsonObject { ["category"] = category, ["count"] = _objects[category].Count })]),
        });
    }

    // A value of the census as the script writes it: the value, or {"error": text} for one that 1C cannot give.
    private JsonNode CensusValue(string member, string value)
    {
        return _censusErrors.TryGetValue(member, out string? text) ? new JsonObject { ["error"] = text } : JsonValue.Create(value);
    }

    // The objects from the index, count of them at most: the last index of the collection is the last read.
    private byte[]? Page(string category, int from, int count, bool storage)
    {
        List<JsonObject> objects = [.. _objects.GetValueOrDefault(category, []).Skip(from).Take(count)];
        List<string> names = [.. objects.Select(FullNameOf)];

        if (names.Any(_overruns.Contains))
        {
            return null;
        }

        if (names.Any(_losesConnection.Contains))
        {
            return Failed(OutpostOutcomeCodes.ConnectionLost, null);
        }

        if (names.Any(_tooLarge.Contains))
        {
            return Failed(OutpostOutcomeCodes.ResultTooLarge, null);
        }

        if (names.FirstOrDefault(_pageErrors.ContainsKey) is string failing)
        {
            return Failed(OutpostOutcomeCodes.ScriptError, _pageErrors[failing]);
        }

        string? storageError = storage ? names.Where(_storageErrors.ContainsKey).Select(name => _storageErrors[name]).FirstOrDefault() : null;

        return Succeeded(new JsonObject
        {
            ["objects"] = new JsonArray([.. names.Select(name => Paged(name, storage && storageError is null))]),
            ["storageError"] = storageError,
        });
    }

    private byte[] Common()
    {
        if (_commonError is string error)
        {
            return Failed(OutpostOutcomeCodes.ScriptError, error);
        }

        return Succeeded(_commonStorage.DeepClone());
    }
}
