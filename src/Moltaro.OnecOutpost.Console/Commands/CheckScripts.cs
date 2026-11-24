using System.Text.Json;

namespace Moltaro.OnecOutpost.Console.Commands;

// The scripts that check runs in a base as Reads, in the English variant of the 1C language and within what the
// compatibility mode 8.2.13 allows, so no StrSplit and no StrFind; each takes no parameters. And the reasons of their
// lines, read from their results. Each reader throws KeyNotFoundException or InvalidOperationException for a result of
// another form, which the caller reports.
internal static class CheckScripts
{
    // The user of the session, compared with the account of the policy.
    public const string User = """Result = New Structure("user, fullName", UserName(), UserFullName());""";

    // Each event subscription that an external connection can raise whose handler's common module is missing or
    // lacks ExternalConnection. Handler puts the module before the procedure, with an optional metadata type prefix.
    // 1C can return the event name in English or Russian.
    public const string Subscriptions = """
        Result = New Array;

        For Each Subscription In Metadata.EventSubscriptions Do
            EventName = String(Subscription.Event);

            If EventName = "FormGetProcessing" Or EventName = "ОбработкаПолученияФормы" Then
                Continue;
            EndIf;

            Handler = String(Subscription.Handler);
            FirstDot = Find(Handler, ".");
            Rest = Mid(Handler, FirstDot + 1);
            NextDot = Find(Rest, ".");

            If NextDot > 0 Then
                ModuleName = Left(Rest, NextDot - 1);
            Else
                ModuleName = Left(Handler, Max(FirstDot - 1, 0));
            EndIf;

            Module = Metadata.CommonModules.Find(ModuleName);
            Found = Module <> Undefined;

            If Not Found Or Not Module.ExternalConnection Then
                Result.Add(New Structure("subscription, event, module, found",
                    String(Subscription.Name), EventName, String(ModuleName), Found));
            EndIf;
        EndDo;
        """;

    // The rights to change data that the session's user holds, by AccessRight without a third argument, each call in a
    // Try, a right that a kind refuses not held: the global rights over the configuration, then for each category and
    // each right the full names of the objects that hold it, the categories with none left out.
    public const string Rights = """
        Held = New Array;
        Categories = New Array;

        GlobalRights = New Array;
        GlobalRights.Add("Administration");
        GlobalRights.Add("DataAdministration");
        GlobalRights.Add("UpdateDataBaseConfiguration");

        For Each Right In GlobalRights Do
            Try
                If AccessRight(Right, Metadata) Then
                    Held.Add(Right);
                EndIf;
            Except
            EndTry;
        EndDo;

        Changes = "Insert,Update,Delete";
        Kinds = New Array;
        Kinds.Add(New Structure("category, collection, rights", "catalogs", Metadata.Catalogs, Changes));
        Kinds.Add(New Structure("category, collection, rights", "charts of characteristic types", Metadata.ChartsOfCharacteristicTypes, Changes));
        Kinds.Add(New Structure("category, collection, rights", "charts of accounts", Metadata.ChartsOfAccounts, Changes));
        Kinds.Add(New Structure("category, collection, rights", "charts of calculation types", Metadata.ChartsOfCalculationTypes, Changes));
        Kinds.Add(New Structure("category, collection, rights", "exchange plans", Metadata.ExchangePlans, Changes));
        Kinds.Add(New Structure("category, collection, rights", "business processes", Metadata.BusinessProcesses, Changes + ",Start"));
        Kinds.Add(New Structure("category, collection, rights", "tasks", Metadata.Tasks, Changes + ",Execute"));
        Kinds.Add(New Structure("category, collection, rights", "documents", Metadata.Documents, Changes + ",Posting,UndoPosting"));
        Kinds.Add(New Structure("category, collection, rights", "constants", Metadata.Constants, "Update"));
        Kinds.Add(New Structure("category, collection, rights", "information registers", Metadata.InformationRegisters, "Update"));
        Kinds.Add(New Structure("category, collection, rights", "accumulation registers", Metadata.AccumulationRegisters, "Update"));
        Kinds.Add(New Structure("category, collection, rights", "accounting registers", Metadata.AccountingRegisters, "Update"));
        Kinds.Add(New Structure("category, collection, rights", "calculation registers", Metadata.CalculationRegisters, "Update"));
        Kinds.Add(New Structure("category, collection, rights", "sequences", Metadata.Sequences, "Update"));

        For Each Kind In Kinds Do
            Rest = Kind.rights + ",";

            While Rest <> "" Do
                Comma = Find(Rest, ",");
                Right = Left(Rest, Comma - 1);
                Rest = Mid(Rest, Comma + 1);
                Holders = New Array;

                For Each Item In Kind.collection Do
                    Try
                        If AccessRight(Right, Item) Then
                            Holders.Add(Item.FullName());
                        EndIf;
                    Except
                    EndTry;
                EndDo;

                If Holders.Count() > 0 Then
                    Categories.Add(New Structure("category, right, count, objects", Kind.category, Right, Holders.Count(), Holders));
                EndIf;
            EndDo;
        EndDo;

        Result = New Structure("global, categories", Held, Categories);
        """;

    // The objects of a kind that a failed line names by their full names.
    private const int NamedObjects = 3;

    // The line Connection: passed only for a session of the account of the policy, ignoring case, in a base with users,
    // since 1C opens a base without users to anybody with every right.
    public static (bool Passed, string Reason) Session(JsonElement result, string account)
    {
        string user = TextOf(result, "user");
        string fullName = TextOf(result, "fullName");

        if (user.Length == 0)
        {
            return (false, "the base has no users, so every connection has every right");
        }

        if (!string.Equals(user, account, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"the session's user is {user}, not {account} of the policy");
        }

        return (true, $"opened as {user} ({fullName})");
    }

    // The line Subscriptions: each subscription named with its event and its module, and a module that is no common
    // module said so.
    public static (bool Passed, string Reason) SubscriptionsOf(JsonElement result)
    {
        List<string> named = [];

        foreach (JsonElement subscription in result.EnumerateArray())
        {
            string name = TextOf(subscription, "subscription");
            string eventName = TextOf(subscription, "event");
            string module = TextOf(subscription, "module");
            bool found = subscription.GetProperty("found").GetBoolean();

            named.Add(found ? $"{name} ({eventName}) in {module}" : $"{name} ({eventName}) in {module}, which is not a common module");
        }

        if (named.Count == 0)
        {
            return (true, "every event subscription that an external connection can raise has its common module available to it");
        }

        return (false, $"a Write that reaches these fails: {string.Join(", ", named)}; set \"External connection\" of each module");
    }

    // The line ReadRights: the global rights, then the rights of each kind and the objects that hold any of them.
    public static (bool Passed, string Reason) RightsOf(JsonElement result, string account)
    {
        List<string> rights = [];
        List<string> held = [.. result.GetProperty("global").EnumerateArray().Select(TextOf)];

        if (held.Count > 0)
        {
            rights.Add(string.Join(", ", held));
        }

        foreach (IGrouping<string, JsonElement> kind in result.GetProperty("categories").EnumerateArray()
            .GroupBy(category => TextOf(category, "category"), StringComparer.Ordinal))
        {
            string kindRights = string.Join(", ", kind.Select(category => TextOf(category, "right")));
            HashSet<string> objects = new(StringComparer.Ordinal);
            List<string> names = [];

            foreach (JsonElement item in kind.SelectMany(category => category.GetProperty("objects").EnumerateArray()))
            {
                string name = TextOf(item);

                if (objects.Add(name) && names.Count < NamedObjects)
                {
                    names.Add(name);
                }
            }

            string more = objects.Count > names.Count ? $" and {objects.Count - names.Count} more" : "";

            rights.Add($"{kindRights} on {objects.Count} {kind.Key}: {string.Join(", ", names)}{more}");
        }

        if (rights.Count == 0)
        {
            return (true, $"{account} holds no right to change data");
        }

        return (false, $"{account} holds rights to change data: {string.Join("; ", rights)}");
    }

    private static string TextOf(JsonElement value, string name)
    {
        return TextOf(value.GetProperty(name));
    }

    // A string, never null: a value of another kind, null included, is an InvalidOperationException.
    private static string TextOf(JsonElement value)
    {
        return value.GetString() ?? throw new InvalidOperationException("The result holds null where check reads a string.");
    }
}
