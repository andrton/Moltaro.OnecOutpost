# The 1C test configuration

The tests that need 1C run on the lab machine against a file base built from this folder, as `AGENTS.md`,
"Tests And Samples", says. Nothing here runs in CI.

**Status:** neither the sources nor the script have run against a real 1C yet. The first run on the lab machine is
their first test.

## What is here

- `Configuration`: the XML sources of a minimal configuration, in the hierarchical format that the Designer of
  1C:Enterprise 8.3.17 writes (format version 2.10), as UTF-8 with a BOM. The configuration runs in the
  compatibility mode 8.2.13, the lowest in use, with the English script variant. It has:
  - the catalog `Products`;
  - the document `GoodsReceipt`, which posts its `Product` and `Quantity` to the accumulation register `Stock`;
  - the common module `Calculations`, available to an external connection, with the function `Add`;
  - the role `OutpostRead`, which reads every object and changes none, and the role `OutpostWrite`, which also
    writes and posts. Both have the right of external connection and nothing else on the configuration;
  - the role `Administrator`, with the Administration right and the rights to open the base in the Designer and
    the client. It exists only because 1C refuses to save the first user of a base without the Administration
    right, which neither tunnel role may have.
- `Build-TestBase.ps1`: builds the base.
- `lab.local.example.json`: the shape of `lab.local.json`, with placeholders.

## Building the base

On the lab machine, with 1C:Enterprise 8.3.17.1851 installed and its COM connector registered, run from the
repository root:

```
powershell -ExecutionPolicy Bypass -File src\OneC.TestConfiguration\Build-TestBase.ps1
```

The script runs in Windows PowerShell 5.1. It needs the COM connector of the bitness of that PowerShell: the
64-bit Windows PowerShell loads only a 64-bit connector, the 32-bit one only a 32-bit connector. It checks this
first and says how to register the connector when it is missing; it never registers it itself.

A run rebuilds the base from scratch:

1. It reads `lab.local.json`. When the file is missing, it creates it with the base in `.local\test-base` of the
   repository and generated passwords, and goes on.
2. It finds `1cv8.exe` of 8.3.17.1851 under `Program Files\1cv8` or `Program Files (x86)\1cv8`.
3. It deletes the base folder, but only when the folder is empty or holds `outpost-test-base.txt`, the marker it
   writes into every base it builds; any other folder, another 1C file base included, stops it. Then it creates a
   file base there and writes the marker into it.
4. It loads the configuration with the Designer and updates the database configuration. It prints the Designer's
   log after each run: the Designer can drop a property it does not accept, say so in the log and still succeed.
5. It creates the users `outpost-admin`, `outpost-read` and `outpost-write` through the COM connector, each with
   its role and the password from `lab.local.json`.

Besides what 1C itself keeps in the profile of the Windows account for every base it opens, such as its cache, the
script changes only `lab.local.json`, the base folder, and a log of the Designer in the temporary folder, which it
deletes at the end. It writes no registry entry, registers nothing, and does not add the base to the list of bases
of 1C.

## lab.local.json

It holds the full path of the test base and the user and password of each of its three accounts. Git ignores it
(`*.local.json`), and it never leaves the lab machine. The script puts the passwords on no command line, in no
log and on no screen. The tests read their bases and accounts from this file; the copies of typical bases are
added to it by the sprint whose tests use them.
