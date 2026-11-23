# Writing scripts

For the developer of an application that calls `ReadAsync` and `WriteAsync` of the Moltaro.OnecOutpost server: how
to write the 1C script that a command runs, what it receives, what it may return, and what each outcome tells you.

**Status:** in this version the agent starts its COM workers, so a command from `ReadAsync` or `WriteAsync` runs in
the base, a Write of a base whose Writes run without safe mode included, as
[What a command is](#what-a-command-is) says. Every script in this guide is run from this file by a test on the lab
machine, through the COM worker, against the repository's test base on 1C:Enterprise 8.3.17.1851 in the
compatibility mode 8.2.13. These tests passed on the lab on 23 November 2026. The lab tests of Writes without safe
mode under a worker account wait for the lab run.

## Before you start

You need:

- an application that embeds the server and calls `ReadAsync` with an `OutpostReadRequest` and `WriteAsync` with an
  `OutpostWriteRequest`;
- an agent, installed next to the 1C base and paired with your server, whose local policy serves the base under the
  key you give as `BaseKey`, and for a Write also opens writing;
- the names of the objects of the base's configuration, as its metadata has them, and its compatibility mode, which
  decides which functions of the 1C language a script may use.

The examples use the English variant of the 1C language and the repository's own test configuration: the
hierarchical catalog `Products`, the document `GoodsReceipt` with the attributes `Product`, `Quantity` and `Comment`,
and the accumulation register `Stock`, to which a receipt posts.

## What a command is

A command is one Read or one Write. It carries a script in the 1C language, the parameters of the script as a JSON
object, and for a Write a caller key. The agent runs the script whole, in one call, inside 1C, and returns its outcome.
The library knows nothing of your configuration: it does not model catalogs or documents, and it offers no operation
but running your script.

- **A Read** runs under the read account of the base, outside a transaction. It is read-only only through the rights
  of that account: the administrator gives it no right to change data.
- **A Write** runs under the write account of the base, inside one 1C transaction. Either everything the script did is
  committed, or nothing is.
- **Both run in the 1C safe mode**, which forbids files, the network, COM, external components and starting programs.
  The script can still do everything the account's rights allow with the data. Only the administrator of the agent
  machine can turn safe mode off, and only for the Writes of a base.

**Writes without safe mode.** When the local policy of a base turns safe mode off for its Writes
(`"safeMode": false`), it also names a local Windows account of that base alone (`"workerAccount"`), and every COM
worker of the base runs under it, those of its Reads included. A Write of that base is not limited by safe mode: it
can do on the agent machine what that account can do in Windows, and nothing more. That account can read neither the
agent's secrets nor its audit log. The Reads of the base still run in safe mode. Two limitations remain:

- such a script can start a process outside the job object of the agent, which then does not end with the agent,
  through a service that the account may use, such as Task Scheduler or WMI, so the rights the administrator gives the account are the boundary;
- the workers of one base run under one account, so such a script can read the commands of the other workers of its
  base, their 1C passwords included, and forge what they report.

## The contract of a script

A script sees three variables, and nothing else of the agent:

| Name | What it holds |
| --- | --- |
| `Parameters` | The parameters of the command, read from their JSON object into a `Map` with string keys; an empty `Map` when the command has none |
| `CallerKey` | The caller key of the command, a string, or `Undefined` for a Read without one |
| `Result` | What the script sets as its result, written back as JSON; `Undefined` when the script sets nothing |

### From JSON to 1C

| JSON | 1C |
| --- | --- |
| an object | a `Map` with string keys |
| an array | an `Array` |
| a string | a `String`, also one that looks like a date |
| a number | a `Number` |
| `true`, `false` | a `Boolean` |
| `null` | `Undefined` |

A string is never turned into a date by guessing. A script that needs a date converts the string itself, as the
second example shows.

```bsl read
Customer = Parameters["customer"];
Lines = Parameters["lines"];
Total = 0;

For Each Item In Lines Do
    Total = Total + Item["quantity"];
EndDo;

Result = New Structure("customer, lines, quantity", Customer["name"], Lines.Count(), Total);
```

```json parameters
{
  "customer": { "name": "Rossi", "vip": true },
  "lines": [
    { "sku": "A-1", "quantity": 2 },
    { "sku": "B-7", "quantity": 1.5 }
  ]
}
```

```json result
{"customer":"Rossi","lines":2,"quantity":3.5}
```

### From 1C to JSON

`Result` may hold `String`, `Number`, `Boolean`, `Date`, `Undefined`, `Null`, `Array`, `Structure`, and `Map` with
string keys, nested up to 64 levels. A `Structure` and a `Map` become JSON objects, with their keys as the script
wrote them.

- **A date** is written as `YYYY-MM-DDThh:mm:ss`, without an offset, since a 1C date carries none.
- **`Undefined` and `Null`** are written as `null`.
- **Any other value**, such as a reference, a value table or a fixed array, fails the command with `result-type`: the
  script converts it first.

```bsl read
Received = XMLValue(Type("Date"), Parameters["received"]);

Result = New Structure("received, dayAfter, now", Received, Received + 86400, CurrentDate());
```

```json parameters
{ "received": "2026-11-21T09:30:00" }
```

```json result
{"received":"2026-11-21T09:30:00","dayAfter":"2026-11-22T09:30:00","now":"2026-11-22T10:15:07"}
```

A parameter that is `null`, one that is missing, and `Null` all come back as `null`. `Parameters.Get` gives
`Undefined` for a key the parameters do not have.

```bsl read
Result = New Structure("note, missing, nothing", Parameters["note"], Parameters.Get("missing"), Null);
```

```json parameters
{ "note": null }
```

```json result
{"note":null,"missing":null,"nothing":null}
```

### The caller key

A Write always has a caller key, which your application chooses: 1 to 200 printable ASCII characters without a space.
The script sees it as `CallerKey`, and can use it to mark what it creates, so that a later script finds out what the
Write did, as [Creating and posting a document once per caller key](#creating-and-posting-a-document-once-per-caller-key)
shows.

```bsl write
Result = CallerKey;
```

### Each command gets a fresh script context

The agent runs your script with `Execute` in a fresh executor for each command, so:

- a script cannot declare a procedure or a function, and cannot use `Return`;
- the 1C session of a worker lives on between commands, so what a script changes in the session, such as the
  session's parameters, can be seen by the next command of that worker. Do not rely on it either way.

## What a script may not do

**Declare a procedure or a function.** 1C refuses the whole script.

```bsl read failed script-error
Procedure Greet()
EndProcedure

Result = 1;
```

**Return a reference.** A reference, such as an item of a catalog, cannot be written as JSON. Return its unique
identifier, its code or its description instead, and turn an identifier back into a reference with `GetRef`, as
[One item by its unique identifier](#one-item-by-its-unique-identifier) shows.

```bsl read failed result-type
Result = Catalogs.Products.EmptyRef();
```

```bsl read
Product = Catalogs.Products.EmptyRef();

Result = String(Product.UUID());
```

```json result
"00000000-0000-0000-0000-000000000000"
```

**End the transaction of its Write.** The executor holds the transaction; a script that calls `CommitTransaction` or
`RollbackTransaction` on it makes the outcome Unknown with `transaction-ended`, since nobody can tell any more what
was written.

```bsl write unknown transaction-ended
CommitTransaction();
```

**Leave a transaction of its own open.** The executor rolls it back, in a Write together with everything else the
script did, and the command fails with `script-error` and a sentence of the executor as its error text. A script may
begin and commit a transaction of its own inside a Write, but a nested rollback rolls the whole Write back in the
compatibility mode 8.2.13, and the executor then fails it with `script-error` too.

```bsl read failed script-error
BeginTransaction();
```

**Reach files, the network, COM or programs.** Safe mode refuses them, whatever the account may do in Windows.

```bsl read failed script-error
Reader = New TextReader("C:\Windows\win.ini");
Result = Reader.ReadLine();
```

**Use a function that the compatibility mode of the base lacks.** The language a script may use is that of the
compatibility mode of the base, not that of the platform. `StrSplit` exists from the mode 8.3.6 on, so the test base,
in the mode 8.2.13, refuses it.

```bsl read failed script-error
Result = StrSplit("a,b,c", ",");
```

## Read and Write

A Read gets the rights of the read account. The administrator gives that account no right to change data, so a Read
that tries is refused by 1C and fails with `script-error`:

```bsl read failed script-error
Product = Catalogs.Products.CreateItem();
Product.Description = "Not allowed";
Product.Write();
```

A Write runs in one transaction. When it fails, 1C has rolled everything back: nothing it did is kept. This one
writes a product and then raises, so the product is not in the base afterwards.

```bsl write failed script-error
Product = Catalogs.Products.CreateItem();
Product.Description = Parameters["description"];
Product.Write();

Raise "The order has no lines, so nothing is written.";
```

```json parameters
{ "description": "Never kept" }
```

### What the outcome tells you

`ReadAsync` and `WriteAsync` return an `OutpostOutcome`. Its `Kind` says how the command ended:

| Kind | What it means | What to do |
| --- | --- | --- |
| Succeeded | The script ran; for a Write, its transaction was committed. `Result` holds the result, `Messages` the user messages | Use the result |
| Failed | The script did not succeed. For a Write, nothing was written: 1C rolled it back, or the script never started. `Code` says why, `ErrorText` holds the text of 1C for `script-error` | See the codes below; a Write may be sent again with a new caller key |
| Refused | A check refused the command before it ran: nothing ran | Fix what `Code` names |
| Unavailable | The agent of the base is not connected; no command was created | Try again later |
| Expired | No poll of the agent took the command before its take deadline, 30 seconds by default, for example while the workers of the base were busy, or while none of them could open a session in 1C: nothing ran | Send it again; when it keeps expiring, ask the administrator of the agent machine |
| Unknown | Only a Write: nobody can tell whether it was committed | Find out, as below, before you write again |

**After an Unknown Write**, do not send the same work with a new caller key: it could be written twice. Repeating the
call with the same caller key, mode, script and parameters never runs the script again: it returns the outcome of the
first command, which stays Unknown unless the agent, back after it was away, reported the real one. To learn what
happened, read the base for the mark that the Write leaves, as
[Finding out what an Unknown Write did](#finding-out-what-an-unknown-write-did) shows. A caller key is kept with its
outcome for the retention of the server, 7 days by default; after that a repeat runs anew.

## Sizes and limits

- **The outcome** of a command, its result with its user messages and its error text as JSON, is at most 16 MiB by
  default, a limit of the server (`OutcomeSizeLimit`). When it would be larger, the executor first trims the error
  text and then the messages, and sets `MessagesTrimmed`. A result that still does not fit fails the command with
  `result-too-large`, and a Write is rolled back. The library does not send a result in parts: read a large list in
  pages, as [A page of the items of a catalog](#a-page-of-the-items-of-a-catalog) shows.
- **The command**, its script and its parameters, is at most 1 MiB by default (`CommandSizeLimit`). A larger one is
  a mistake of the call, which throws `ArgumentException`.
- **The run limit** of each mode of a base is set by the administrator in the local policy, 10 minutes by default. A
  script that runs longer is ended with its worker: a Read fails with `run-limit`, and a Write is Unknown with
  `run-limit`, since its commit may have come just before the end. The agent starts a new worker in its place after
  a short delay, a tenth of the policy's `retryDelayMax`, 3 seconds by default; until then the mode has one worker
  fewer.
- **The workers** of a mode, one by default, run one command each at a time. A long Read holds the Read worker of its
  base, and the next Read waits for a free worker until its take deadline.
- **Parameters and results** nest at most 64 levels.

## User messages and the error text

What the script reports with `Message` arrives in `Messages`, in its order, and only with its own command.

```bsl read
Query = New Query("SELECT COUNT(*) AS Total FROM Catalog.Products AS Products");
Selection = Query.Execute().Select();
Selection.Next();

Message("Counted the products of the catalog.");
Result = Selection.Total;
```

```json result
12
```

When 1C raises an error, the command fails with `script-error` and `ErrorText` holds the text of 1C. Its wording
follows the language of the Windows account and the platform that run the agent, so the same error can arrive in
another language from another machine: never let your application depend on the wording.

```bsl read failed script-error
Quantity = 0;
Result = 10 / Quantity;
```

## Recipes

Each recipe is a whole script with its parameters and a result it may give on the test base; your data give other
results. The names of objects are those of the test configuration: put those of your configuration in their place.

### The catalogs, documents and registers of a base

```bsl read
CatalogNames = New Array;
For Each Item In Metadata.Catalogs Do
    CatalogNames.Add(Item.Name);
EndDo;

DocumentNames = New Array;
For Each Item In Metadata.Documents Do
    DocumentNames.Add(Item.Name);
EndDo;

RegisterNames = New Array;
For Each Item In Metadata.InformationRegisters Do
    RegisterNames.Add("InformationRegister." + Item.Name);
EndDo;
For Each Item In Metadata.AccumulationRegisters Do
    RegisterNames.Add("AccumulationRegister." + Item.Name);
EndDo;
For Each Item In Metadata.AccountingRegisters Do
    RegisterNames.Add("AccountingRegister." + Item.Name);
EndDo;

Result = New Structure("catalogs, documents, registers", CatalogNames, DocumentNames, RegisterNames);
```

```json result
{"catalogs":["Products"],"documents":["GoodsReceipt"],"registers":["AccumulationRegister.Stock"]}
```

### The attributes and tabular sections of one object, with their types

The object is named by its full name, such as `Catalog.Products` or `Document.GoodsReceipt`. The attributes of a
tabular section are named `Section.Attribute`. The standard attributes, such as `Code`, `Description`, `Date` and
`Number`, are in `StandardAttributes`, which this recipe leaves out.

```bsl read
MetadataObject = Metadata.FindByFullName(Parameters["object"]);

If MetadataObject = Undefined Then
    Raise "The configuration has no object " + Parameters["object"] + ".";
EndIf;

Fields = New Array;

For Each Attribute In MetadataObject.Attributes Do
    Fields.Add(New Structure("Name, Type", Attribute.Name, Attribute.Type));
EndDo;

For Each Section In MetadataObject.TabularSections Do
    For Each Attribute In Section.Attributes Do
        Fields.Add(New Structure("Name, Type", Section.Name + "." + Attribute.Name, Attribute.Type));
    EndDo;
EndDo;

Described = New Array;

For Each Field In Fields Do
    TypeNames = New Array;

    For Each FieldType In Field.Type.Types() Do
        If FieldType = Type("String") Then
            TypeNames.Add("String(" + XMLString(Field.Type.StringQualifiers.Length) + ")");
        ElsIf FieldType = Type("Number") Then
            TypeNames.Add("Number(" + XMLString(Field.Type.NumberQualifiers.Digits) + "," + XMLString(Field.Type.NumberQualifiers.FractionDigits) + ")");
        ElsIf FieldType = Type("Date") Then
            TypeNames.Add("Date");
        ElsIf FieldType = Type("Boolean") Then
            TypeNames.Add("Boolean");
        Else
            TypeObject = Metadata.FindByType(FieldType);

            If TypeObject = Undefined Then
                TypeNames.Add(String(FieldType));
            Else
                TypeNames.Add(TypeObject.FullName());
            EndIf;
        EndIf;
    EndDo;

    Described.Add(New Structure("name, types", Field.Name, TypeNames));
EndDo;

Result = New Structure("object, attributes", MetadataObject.FullName(), Described);
```

```json parameters
{ "object": "Document.GoodsReceipt" }
```

```json result
{"object":"Document.GoodsReceipt","attributes":[{"name":"Product","types":["Catalog.Products"]},{"name":"Quantity","types":["Number(15,3)"]},{"name":"Comment","types":["String(200)"]}]}
```

### A page of the items of a catalog

The items, without the folders, ordered by their code, `size` of them after the code `after`. The first page starts
after `""`; each next page starts after the `last` code of the page before, and a page shorter than `size` is the last.
`size` is a whole number, since it becomes part of the text of the query.

```bsl read
Query = New Query;
Query.Text = "SELECT TOP " + XMLString(Parameters["size"]) + "
    |    Products.Code AS Code,
    |    Products.Description AS Description
    |FROM
    |    Catalog.Products AS Products
    |WHERE
    |    NOT Products.IsFolder
    |    AND Products.Code > &After
    |ORDER BY
    |    Code";
Query.SetParameter("After", Parameters["after"]);
Selection = Query.Execute().Select();

Page = New Array;
Last = Undefined;

While Selection.Next() Do
    Page.Add(New Structure("code, description", Selection.Code, Selection.Description));
    Last = Selection.Code;
EndDo;

Result = New Structure("items, last", Page, Last);
```

```json parameters
{ "after": "", "size": 50 }
```

```json result
{"items":[{"code":"000000001","description":"Hex bolt M8"},{"code":"000000002","description":"Hex nut M8"}],"last":"000000002"}
```

### A search by description, and the children of a folder

A search for the items and folders whose description holds a text. Whether `LIKE` tells capitals from small letters
follows the DBMS of the base. In the text, `%`, `_` and `[` have a meaning of their own in `LIKE`.

```bsl read
Query = New Query;
Query.Text = "SELECT TOP 20
    |    Products.Ref AS Ref,
    |    Products.Description AS Description,
    |    Products.IsFolder AS IsFolder
    |FROM
    |    Catalog.Products AS Products
    |WHERE
    |    Products.Description LIKE &Pattern
    |ORDER BY
    |    Description";
Query.SetParameter("Pattern", "%" + Parameters["text"] + "%");
Selection = Query.Execute().Select();

Found = New Array;

While Selection.Next() Do
    Found.Add(New Structure("id, description, folder", String(Selection.Ref.UUID()), Selection.Description, Selection.IsFolder));
EndDo;

Result = Found;
```

```json parameters
{ "text": "bolt" }
```

```json result
[{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","description":"Hex bolt M8","folder":false}]
```

The items and folders right under a folder, found by its description, folders first; an empty list when there is no
such folder. `Products.Parent IN HIERARCHY(&Folder)` in place of `Products.Parent = &Folder` gives those of every
level below it.

```bsl read
Folder = Catalogs.Products.FindByDescription(Parameters["folder"], True);
Children = New Array;

If Not Folder.IsEmpty() Then
    Query = New Query;
    Query.Text = "SELECT
        |    Products.Ref AS Ref,
        |    Products.Description AS Description,
        |    Products.IsFolder AS IsFolder
        |FROM
        |    Catalog.Products AS Products
        |WHERE
        |    Products.Parent = &Folder
        |ORDER BY
        |    IsFolder DESC,
        |    Description";
    Query.SetParameter("Folder", Folder);
    Selection = Query.Execute().Select();

    While Selection.Next() Do
        Children.Add(New Structure("id, description, folder", String(Selection.Ref.UUID()), Selection.Description, Selection.IsFolder));
    EndDo;
EndIf;

Result = Children;
```

```json parameters
{ "folder": "Fasteners" }
```

```json result
[{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","description":"Hex bolt M8","folder":false}]
```

### One item by its unique identifier

A reference cannot travel, but its unique identifier can: a script returns `String(Ref.UUID())`, and a later script
turns it back into the reference with `GetRef(New UUID(...))`. A reference to an item that does not exist, or no
longer does, finds no row, and the script gives `null`.

```bsl read
Product = Catalogs.Products.GetRef(New UUID(Parameters["id"]));

Query = New Query;
Query.Text = "SELECT
    |    Products.Code AS Code,
    |    Products.Description AS Description,
    |    Products.Parent.Description AS Folder
    |FROM
    |    Catalog.Products AS Products
    |WHERE
    |    Products.Ref = &Product";
Query.SetParameter("Product", Product);
Selection = Query.Execute().Select();

If Selection.Next() Then
    Result = New Structure("id, code, description, folder", Parameters["id"], Selection.Code, Selection.Description, Selection.Folder);
Else
    Result = Undefined;
EndIf;
```

```json parameters
{ "id": "6f9619ff-8b86-d011-b42d-00cf4fc964ff" }
```

```json result
{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","code":"000000001","description":"Hex bolt M8","folder":"Fasteners"}
```

### A query with parameters

The posted receipts of a period with at least a quantity. The parameters of the query are set with `SetParameter`,
never pasted into its text, and the dates are converted from their strings.

```bsl read
Query = New Query;
Query.Text = "SELECT
    |    Receipts.Number AS ReceiptNumber,
    |    Receipts.Date AS ReceiptDate,
    |    Receipts.Product.Description AS Product,
    |    Receipts.Quantity AS Quantity
    |FROM
    |    Document.GoodsReceipt AS Receipts
    |WHERE
    |    Receipts.Posted
    |    AND Receipts.Date BETWEEN &PeriodStart AND &PeriodEnd
    |    AND Receipts.Quantity >= &Least
    |ORDER BY
    |    ReceiptDate";
Query.SetParameter("PeriodStart", XMLValue(Type("Date"), Parameters["from"]));
Query.SetParameter("PeriodEnd", XMLValue(Type("Date"), Parameters["to"]));
Query.SetParameter("Least", Parameters["least"]);
Selection = Query.Execute().Select();

Rows = New Array;

While Selection.Next() Do
    Rows.Add(New Structure("number, date, product, quantity", Selection.ReceiptNumber, Selection.ReceiptDate, Selection.Product, Selection.Quantity));
EndDo;

Result = Rows;
```

```json parameters
{ "from": "2026-11-01T00:00:00", "to": "2026-11-30T23:59:59", "least": 1 }
```

```json result
[{"number":"000000001","date":"2026-11-21T09:30:00","product":"Hex bolt M8","quantity":250}]
```

### Creating and posting a document once per caller key

A Write that creates and posts a goods receipt, and keeps its caller key in the receipt's `Comment`. A repeat of the
call with the same key returns the first outcome without running the script; the mark matters when the script runs
again with that key all the same, as after the server removed the key's outcome at the end of its retention, and for
the Read of the next recipe. The script then finds the receipt it made and posts nothing new. The product is found by
its description, and created when there is none.

```bsl write
Query = New Query;
Query.Text = "SELECT TOP 1
    |    Receipts.Ref AS Ref
    |FROM
    |    Document.GoodsReceipt AS Receipts
    |WHERE
    |    Receipts.Comment = &CallerKey";
Query.SetParameter("CallerKey", CallerKey);
Selection = Query.Execute().Select();

If Selection.Next() Then
    Receipt = Selection.Ref;
    Created = False;
Else
    Product = Catalogs.Products.FindByDescription(Parameters["product"], True);

    If Product.IsEmpty() Then
        NewProduct = Catalogs.Products.CreateItem();
        NewProduct.Description = Parameters["product"];
        NewProduct.Write();
        Product = NewProduct.Ref;
    EndIf;

    NewReceipt = Documents.GoodsReceipt.CreateDocument();
    NewReceipt.Date = CurrentDate();
    NewReceipt.Product = Product;
    NewReceipt.Quantity = Parameters["quantity"];
    NewReceipt.Comment = CallerKey;
    NewReceipt.Write(DocumentWriteMode.Posting);
    Receipt = NewReceipt.Ref;
    Created = True;
EndIf;

Result = New Structure("id, number, created", String(Receipt.UUID()), Receipt.Number, Created);
```

```json parameters
{ "product": "Hex bolt M8", "quantity": 250 }
```

```json result
{"id":"3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b","number":"000000002","created":true}
```

The attribute that keeps the key has to hold 200 characters, the longest caller key, and should be indexed, since
every run looks it up.

### Finding out what an Unknown Write did

A Read that looks for the mark of the Write above, with the caller key of the Unknown Write as its parameter. A
receipt means the Write was committed; `null` means nothing of it was written, and the work may be written again with
a new caller key.

```bsl read
Query = New Query;
Query.Text = "SELECT
    |    Receipts.Ref AS Ref,
    |    Receipts.Number AS ReceiptNumber,
    |    Receipts.Posted AS Posted
    |FROM
    |    Document.GoodsReceipt AS Receipts
    |WHERE
    |    Receipts.Comment = &CallerKey";
Query.SetParameter("CallerKey", Parameters["callerKey"]);
Selection = Query.Execute().Select();

If Selection.Next() Then
    Result = New Structure("id, number, posted", String(Selection.Ref.UUID()), Selection.ReceiptNumber, Selection.Posted);
Else
    Result = Undefined;
EndIf;
```

```json parameters
{ "callerKey": "receipt-2026-11-22-0042" }
```

```json result
null
```

## When a command does not succeed

Each code below is a constant of `OutpostCodes`, which `OutpostOutcome.Code` holds.

| Code | Kind | Cause | Remedy |
| --- | --- | --- | --- |
| `script-error` | Failed | 1C raised an error in the script, and `ErrorText` holds its text; or the script left a transaction of its own open, or rolled back a nested one, and `ErrorText` holds a sentence of the executor | Fix the script or its data. A Write wrote nothing |
| `result-type` | Failed, Unknown | The result holds a value that cannot be JSON, such as a reference, or nests deeper than 64 levels; Unknown when the agent could not accept the outcome of a Write | Convert the value in the script |
| `result-too-large` | Failed, Unknown | The outcome is larger than its limit | Return less, or read in pages |
| `run-limit` | Failed (Read), Unknown (Write) | The script ran longer than the run limit of its mode | Make the script shorter, or ask the administrator for a longer limit |
| `transaction-ended` | Unknown | The script ended the transaction of its Write itself, or the commit raised | Do not call `CommitTransaction` or `RollbackTransaction` on it; find out what was written |
| `base-unreachable` | Failed | No worker could run the command: the worker had its session in the base but could not open the executor in it, or no worker of the mode was ready any more when the command reached the agent; the script never started. A worker that cannot open its session at all, after a wrong password, with a cluster that does not answer or without a free licence, takes no command, so the command ends as Expired instead | Send it again; when it keeps failing, ask the administrator of the agent machine |
| `connection-lost` | Failed (Read), Unknown (Write) | The session in 1C ended while the script ran, as when the cluster restarted | Send a Read again; find out what a Write did |
| `lost` | Failed | A Read whose agent or worker stopped while it ran, whose agent stayed away, or whose agent was revoked | Send it again |
| `agent-stopped` | Unknown | A Write whose agent or worker stopped while it ran | Find out what it did |
| `disconnected` | Unknown | A Write whose agent stayed away longer than its run limit | Find out what it did |
| `not-served` | Refused | The local policy of the agent does not serve the base or the mode | Ask the administrator |
| `mode-closed` | Refused | The agent that holds the base does not open the mode, such as Write for a base that is only read | Ask the administrator |
| `unknown-base` | Refused | No agent holds the base key | Check the key, and that its agent is paired and polls |
| `caller-key-conflict` | Refused | The caller key names another command of the base, with another mode, script or parameters | Use a new key for new work |
| `expired`, `clock-skew` | Refused | By the agent's clock the command came too late, or from too far ahead | Send it again; check the clocks of the machines |
| `revoked` | Refused, Unknown | The agent was revoked | Pair the agent again |
| `not-addressed`, `protocol-version`, `malformed` | Refused | The command did not fit the agent: another agent, another version of the protocol, a broken command | Update the agent or the server |

## What the library does not do

- It does not model the objects of your configuration and offers no operation on them: everything is a script.
- It keeps no list of allowed scripts. The local policy decides bases, accounts and modes; an application that wants
  to approve scripts does so before it calls.
- It does not deliver a command to an agent that is not connected: the call returns Unavailable at once.
- It does not send a result larger than the outcome limit in parts.
