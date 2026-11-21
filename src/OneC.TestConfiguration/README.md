# The 1C test configuration

The tests that need 1C run on the lab machine against a base built from this folder on the lab's 1C cluster, as
`AGENTS.md`, "Tests And Samples", says. Nothing here runs in CI. The base is a client-server one because the lab's
1C server gives a licence to nobody who opens a file base.

**Status:** the script built the base from these sources on the lab machine on 21 November 2026, on
1C:Enterprise 8.3.17.1851 with PostgreSQL.

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
- `Build-TestBase.ps1`: builds the base, and the executor that the tests open in it.
- `lab.local.example.json`: the shape of `lab.local.json`, with placeholders.

## Building the base

On the lab machine, with 1C:Enterprise 8.3.17.1851, its server with a cluster, PostgreSQL and the registered COM
connector, run from the repository root:

```
powershell -ExecutionPolicy Bypass -File src\OneC.TestConfiguration\Build-TestBase.ps1
```

The script runs in Windows PowerShell 5.1. It needs the COM connector of the bitness of that PowerShell: the
64-bit Windows PowerShell loads only a 64-bit connector, the 32-bit one only a 32-bit connector. It checks this
first and says how to register the connector when it is missing; it never registers it itself.

It uses the first cluster of the 1C server agent that `lab.local.json` names, and creates the base with PostgreSQL
as its DBMS. It signs in to that cluster with an empty administrator name, so the cluster must have no cluster
administrators.

A run rebuilds the base from scratch:

1. It reads `lab.local.json`. When the file is missing, it creates it for a cluster and a PostgreSQL on the same
   machine, the base `outpost_test` and generated passwords, and goes on.
2. It finds `1cv8.exe` of 8.3.17.1851 under `Program Files\1cv8` or `Program Files (x86)\1cv8`.
3. It asks for the password of the PostgreSQL user, without echoing it. The script keeps it nowhere; the 1C
   cluster stores it with the registration of the base, as it does for every base.
4. It drops the base of an earlier run with its database, and creates the base and its database again, through
   the administration of the cluster. Its one guard is the name of the base on the cluster, which has to start
   with `outpost_`; dropping a base also deletes the database that its registration names, whatever that name is,
   so an `outpost_` base on the cluster is taken to be the script's own. When PostgreSQL still has a database of
   the base's name after the drop, the script creates the base on that database as it is, not on a new one.
5. It loads the configuration with the Designer and updates the database configuration. It prints the Designer's
   log after each run: the Designer can drop a property it does not accept, say so in the log and still succeed.
6. It builds the executor `src/Executor/OutpostExecutor.epf` from its sources in `src/Executor` with the Designer's
   `/LoadExternalDataProcessorOrReportFromFiles`, in the base while it has no users, so that no password reaches
   the command line. It deletes the `.epf` of an earlier run first, and stops when the Designer writes none. The
   Administrator Guide of 8.3.23 documents the switch; that 8.3.17 has it is not confirmed yet.
7. It creates the users `outpost-admin`, `outpost-read` and `outpost-write` through the COM connector, each with
   its role and the password from `lab.local.json`.

Besides what 1C itself keeps in the profile of the Windows account for every base it opens, such as its cache, the
script changes only `lab.local.json`, the one base on the cluster with its database, the executor's `.epf`, and a
log of the Designer in the temporary folder, which it deletes at the end. It writes no registry entry, registers nothing, and does not
add the base to the list of bases of the 1C client.

## lab.local.json

It holds the address of the cluster and of its agent, the name of the test base, the PostgreSQL server and user,
and the user and password of each of the three accounts of the base. The password of PostgreSQL is not in it. An
optional `locale`, such as `ru_RU`, names the locale of the base when that of the Windows account is not the one
the PostgreSQL of the lab was installed with: 1C then refuses to create the base, since the database does not
support the sort order. A PostgreSQL on another port is written into `dbServer` as `localhost port=5433`. Git
ignores `lab.local.json` (`*.local.json`), and it never leaves the lab machine. The script puts the passwords on
no command line, in no log and on no screen. The tests read their bases and accounts from this file. The owner adds
the copies of typical bases to it by hand, under `typicalBases`: for each its cluster `server` and `database`, its
`configuration` and `version`, the accounts `read` and `write`, the `documentType` whose latest posted document a test
posts again, and `writeSafeMode`, the safe mode of its Writes. The script uses only `testBase` and leaves
`typicalBases` as it is.
