# Moltaro.OnecOutpost — concept

This document is the authority for what the library guarantees: its semantics, its
protocol principles, its decisions and its non-goals. It describes the design;
[README.md](../README.md) says what of it is built.

**Status.** The owner agrees to this concept by merging it into `main`. The open items of
section 12 are checked on a lab machine and may still reopen decisions D3, D12 and D14.

## 1. Where it comes from

Many companies keep their accounting and their warehouse in 1C:Enterprise 8.3, often in
one of its configurations sold under another name, such as BAS. The 1C server sits deep in
the back office. The only thing that reaches it is the 1C client, inside the company's
network, and nothing outside can connect to it.

Other systems still need it. They want reports over its data, simple changes, and more and
more often routine data entry: a document filled in and posted by a program instead of a
person.

1C offers several ways in, and none of them fits this case as it is:

- **OData and HTTP or web services** need the database published on a web server that the
  caller can reach, which is exactly what the network does not allow.
  - OData writes objects and posts documents, and runs their handlers when it does. It has
    no arbitrary query, though, and cannot call the common modules of the configuration.
  - HTTP services need a change of the configuration or an extension.
- **Reading the DBMS directly** bypasses the rights of 1C. Its table names differ from one
  database to the next and change with updates, and writing that way is out of the
  question.
- **Driving the 1C client** through its forms is slow and breaks whenever a form changes.

One way works everywhere: the **external connection** through the COM connector that is
installed with 1C. From any Windows machine of the network it opens a session in the
database with a 1C account. It needs no change of the configuration and no web server. It
offers the whole query language and everything the configuration does outside its forms:
posting, register movements, event subscriptions, its common modules. What it does not run
is the logic of forms, such as filling a field when a user picks a value. A program fills
those fields itself.

What is missing is a safe way to use that connection from outside the network. The
observation behind this project is that a caller does not need an API for 1C. It needs a
**tunnel**: a script in the 1C language is carried to a machine next to 1C, run whole
inside 1C, and its result is carried back. Everything else, from a report to a posted
document, is a script.

## 2. What it is

| Part | Where it runs | What it does |
| --- | --- | --- |
| **Agent** | A Windows service on a machine of the back-office network | Dials out to the server, takes commands, checks them, runs them in COM workers and returns their outcomes |
| **COM worker** | A process the agent starts, per base and mode | Holds the external connection to one database under one 1C account and runs scripts in it through the executor |
| **Executor** | An external data processor (`.epf`) that the COM worker opens in 1C | Runs the text of a script inside 1C, with its parameters, and hands back its result and messages |
| **Server** | A .NET library inside an ASP.NET Core application, on as many nodes as it runs | Pairs agents, signs commands, hands them to the agents, keeps their outcomes, answers the application's Read and Write calls |
| **Console** | The agent machine | Installs and pairs the agent, writes its secrets, checks the connections to 1C and to the server, writes a local metadata report of a base |
| **Shell** | Any machine | Runs a server of its own and sends Read and Write by hand, to try the tunnel without an application |

A command travels like this:

1. The application calls Read or Write on a server node. The node signs the command,
   writes it to the store and sets a signal for the agent that serves the base.
2. A poll of that agent is waiting on some node. The signal wakes it, it takes the command
   in the store and returns it as the answer to the poll.
3. The agent checks the command, records it in its audit log and gives it to a COM worker
   of the base. The worker runs the script in 1C.
4. The agent sends the outcome to any node. That node writes it to the store, confirms it
   to the agent and sets a signal for the command.
5. The node where the application's call is waiting wakes, reads the outcome and returns
   it.

## 3. Semantics

### Terms

- **Agent**: one installed agent, known to the server by the identity and the public key
  it registered at pairing.
- **Base**: one 1C database that an agent serves, named by a key that its local policy
  declares. A key belongs to one agent on a server (rule 14).
- **Command**: one Read or one Write addressed to a base.
- **Script**: the 1C language text a command runs.
- **Caller key**: the key a caller gives a command to make it run once; required for a
  Write, optional for a Read.
- **Local policy**: the file on the agent machine that says which bases the agent serves,
  how it connects to each, which 1C account reads, which writes, whether writing is
  allowed, and the workers and run limits of each mode.
- **Audit log**: the local record of every command the agent received, and the agent's
  journal.
- **Outcome**: how a command ended. It is one of:
  - **Succeeded**, with the result and the user messages of 1C;
  - **Failed**, with an error code, the error text of 1C when 1C raised it, and the user
    messages;
  - **Refused**, with a code that says which check refused it;
  - **Unavailable**: the agent of the base is not connected, and no command was created;
  - **Expired**: the command did not reach the agent in time (rule 7);
  - **Unknown**: a Write whose effect in 1C cannot be known, or whose outcome could not be
    delivered (rule 11).

### The two commands

- **Read** runs the script under the read account of the base, outside a transaction. It
  is read-only only through the rights of that account. The administrator gives the read
  account no right to change data, and the console's check lists every such right it has.
- **Write** runs the script under the write account of the base, inside one 1C
  transaction. Either everything the script did is committed, or nothing, with the one
  exception that rule 5 names.

The script's contract is the same for both:

- **`Parameters`** holds the parameters, read from a JSON object into a `Map` with string
  keys; with no parameters it is an empty `Map`. Inside it:
  - an object becomes a `Map` and an array an `Array`;
  - a string, number or boolean becomes the matching 1C value, and `null` becomes
    `Undefined`;
  - a string is never turned into a date by guessing.
- **`CallerKey`** holds the caller key, or `Undefined`. A Write script can use it to make
  itself idempotent, for example by marking what it creates, so that a later script can
  find out what an Unknown Write did.
- **`Result`** is set by the script and written as JSON. It may hold `String`, `Number`,
  `Boolean`, `Date`, `Undefined`, `Null`, `Array`, `Structure`, and `Map` with string keys.
  - A date is written as `YYYY-MM-DDThh:mm:ss`, without an offset, since 1C dates carry
    none.
  - `Undefined` and `Null` are written as `null`.
  - Any other value, such as a reference, fails the command with the code `result-type`.
    The script converts such values itself.
- **Each command gets a fresh executor object**, which runs the script with `Execute`. So
  a script cannot declare procedures or functions and cannot use `Return`. The 1C session
  of the worker lives on between commands, so what a script changes in the session can be
  seen by the next command of that worker.
- **The script runs in the 1C safe mode.** Safe mode forbids files, the network, COM,
  external components and starting programs, and privileged mode has no effect in it. The
  script can still do everything the account's rights allow with the data.
  - In an external connection, the script and the configuration code it calls run in the
    COM worker on the agent machine, as far as is known; open item O6 checks this for
    client-server bases.
  - Safe mode is what keeps a script away from the files of that machine, the agent's key
    and the local policy included.
  - Open item O1 checks that typical posting gives the same result in safe mode.

### Rules

These are the guarantees. Each numbered rule has a deterministic test. The parts that only
1C can show, such as the transaction of the executor in rule 5, are also accepted by hand,
as section 11 says.

1. **Only a signed command runs.** The agent runs a command only when all of these hold:
   - its signature verifies, over its exact bytes, with the server key the agent pinned at
     pairing;
   - it names this agent, and a base and a mode that the local policy opens;
   - its protocol version is one the agent supports;
   - by the agent's clock it was issued no later than the allowed skew ahead, and its take
     deadline passed no longer than the allowed skew ago. The skew is an option of the
     server and travels in the signed command; the agent applies that value and has no
     setting of its own.

   Anything else is refused with a code, among them `expired` and `clock-skew`, and
   recorded in the audit log. A command whose signature does not verify is only recorded:
   nothing in it can be trusted, its identity included, so the agent reports nothing for
   it, and the server ends it by rules 7 and 11.
2. **The audit log comes first.** The agent records each command in the audit log:
   - when it receives it: time, identity, base, mode, caller key, script and parameters;
   - when it ends: its outcome and duration;
   - when the server confirms the outcome.

   A command that the agent cannot record does not run. The audit log is also the agent's
   journal for rules 3 and 11, and it keeps its entries for at least a day.
3. **A command runs once on its agent.** A command whose identity is in the audit log
   does not run again. The agent sends its recorded outcome, or nothing while it still runs.
   Suppose a command was received and has no recorded outcome, because the agent or its
   worker stopped. A Write then ends as Unknown, and a Read as Failed with the code `lost`.
4. **A caller key makes a command run once.** The server accepts one command per base and
   caller key. A call is checked against its caller key before anything else, so a call
   whose key names an existing command follows this rule and never returns Unavailable.
   - A repeat with the same mode, script and parameters waits for the outcome of the first
     and returns it. The script is compared exactly; the parameters are compared as JSON
     values, so the order of keys and the notation of numbers do not matter.
   - A repeat with anything else is refused with the code `caller-key-conflict`.
   - When the first command ended as Expired or Refused, nothing ran, so the key is free
     again and a repeat creates a new command.
   - An outcome is kept for its retention, 7 days by default. After that the key is free.
5. **Failed means nothing was written.** A Write fails only when 1C rolled back its
   transaction.
   - The executor checks the type of the result and the size of the whole outcome before
     it commits. The size covers the result, the user messages and the error text,
     counted in UTF-8 as they travel, with characters outside ASCII not escaped. Before
     the check, the executor trims the messages and the text to fit and marks that it did.
     An outcome that still fails either check rolls the Write back and fails it with
     `result-type` or `result-too-large`. The size limit, 16 MiB by default, travels in the
     signed command, so the agent and the server judge by the same number. A Read is
     checked the same way.
   - When the agent cannot tell whether the commit happened, the outcome is Unknown, never
     a success or a failure.
   - The server never turns the outcome of a Write into Failed after the agent reported it.
   - A script that ends the executor's transaction itself, with `CommitTransaction` or
     `RollbackTransaction`, voids this rule for its Write. The executor reports Unknown
     when it finds no active transaction after such a script.
6. **An agent that is not connected is an error at once.** An agent counts as connected
   while the lease of its newest poll in the store has not run out. The lease is the poll
   timeout plus a grace, 10 seconds by default.
   - A call to a base whose agent is not connected returns Unavailable at once and creates
     no command, unless its caller key names an existing command (rule 4).
   - An agent that is gone is reported as connected no longer than its lease, plus the
     difference between the clocks of the nodes, which are expected to be synchronized.
   - The server keeps nothing for later delivery.
7. **A command waits for a poll only until its take deadline.** A command waits in the
   store until a poll of its agent takes it, for example while the workers of its base are
   busy, but at most until its take deadline, 30 seconds by default. Then it ends as
   Expired. Taking and expiring are one conditional change of the command in the store,
   judged by the clock of the node that makes the change; the first to commit wins,
   whatever the clocks say.
   - A command that no poll ever took expires at its take deadline.
   - A command that was ever handed out may still reach the agent, from a lost or held-back
     poll answer, and the agent accepts it until its own clock passes the take deadline
     plus the skew (rule 1). So the agent's clock decides when such a command expires.
     Every poll carries the agent's time, inside its signature. The command expires when
     a poll of its agent, whose time is past the take deadline plus the skew, does not
     list it. Until then it stays taken, and rule 11 applies.
   - An agent whose clock jumps back voids this rule.
8. **Workers bound what runs at once.** A base has workers for Read and, if writing is
   allowed, for Write, one of each by default. Each worker holds a session under the
   account of its mode.
   - A poll asks only for the modes of the bases that have a free worker.
   - Commands are handed out oldest first, by the store's sequence. Commands with more than
     one worker may run at the same time, so a caller that needs an order waits for one
     outcome before its next call.
9. **A script has a run limit.** The local policy gives each mode of a base a run limit.
   A script that exceeds it ends with its worker. A Read then fails with the code
   `run-limit`, and a Write is Unknown. The agent reports its run limits in its polls.
10. **Only the newest poll takes commands.** The agent has one poll open at a time, and it
    numbers its polls with numbers that increase across restarts. It starts from its clock,
    and when the server answers that a poll is older, it continues above the newest number
    that answer names, so that a clock set back stalls nothing.
    - A poll takes commands only while it is the newest poll of its agent in the store. A
      poll whose number is not greater than the newest one recorded counts as older, an
      exact replay included. An older poll ends without commands and changes nothing
      else: not the lease, not the bases, not the list of received commands.
    - The agent records every command it received in its audit log before it opens its
      next poll.
11. **A taken command is never lost and never doubled.** Every poll lists the commands the
    agent received whose outcome the server has not confirmed.
    - A command taken by a poll and missing from the list of a newer poll had not reached
      the agent by then. It is handed out again until its take deadline, and then ends as
      Expired when rule 7 says.
    - The agent sends an outcome until the server confirms it. The server accepts the first
      outcome of a command, and only from the agent it handed the command to; a later one
      changes nothing.
    - When the server, or something on the path, refuses an outcome for its size, the agent
      replaces it with Unknown `result-too-large` for a Write and Failed
      `result-too-large` for a Read, and sends that instead.
    - A taken command whose agent stays disconnected for longer than the command's run
      limit ends as rule 3 says: Unknown for a Write, Failed with `lost` for a Read.
    - When the agent comes back, the outcome the server set is replaced in two cases. The
      agent may report the real outcome of the command. Or a poll may show, as rule 7
      says, that the command never reached the agent; it then becomes Expired. This is
      the one case in which an outcome changes. Nobody is told that it changed: a caller
      sees the new outcome by repeating the command with its caller key.
    - The server confirms an outcome for a command it no longer knows, for example one
      removed after its retention, and keeps nothing of it, so the agent stops sending it.
12. **The caller decides how long it waits.** Read and Write return when the outcome is
    final, or when the caller cancels the wait. A cancelled wait does not stop the command.
    A caller gets the outcome of a command later by repeating it with its caller key.
13. **Revoking an agent ends its work.** A revoked agent's requests are refused and its
    polls take nothing.
    - Its commands that no poll took end as Refused with `revoked`.
    - Its taken commands without an outcome end at once as rule 3 says.
14. **A base key belongs to the first agent that declares it.** Another agent that
    declares the same key is refused for that base with `base-claimed`. This holds until
    the first agent is revoked or the application releases the key.
    - A release is refused while the agent that holds the key still declares it.
    - The next agent to declare a released key gets it.

### Pairing

1. The application creates a **pairing token** for a new agent. In one string it holds:
   - the identity of the token;
   - a random secret of 128 bits;
   - the SHA-256 fingerprint of the server's public signing key.

   The token can be used once and expires after 15 minutes by default, judged by the
   clock of the node that redeems it; a node whose clock is off makes it last that much
   longer or shorter. The server keeps the secret in its store until the token is used,
   and deletes it in the same change that redeems the token. A token that expires unused
   is removed by the next purge.
2. On the agent machine, the administrator runs the console with the server's address and
   the token.
3. The agent creates its own ECDSA P-256 key pair and keeps the private key as section 5
   says. It sends the identity of the token, its public key, and an HMAC-SHA256 of that key
   computed with the secret. The secret itself never travels, so nobody on the path can
   put another key in its place.
4. The server checks the HMAC and registers the key. It answers, signed with its own key,
   with the agent's identity, the agent's public key as it registered it, and its own
   public signing key. The agent checks the server's key against the fingerprint in the
   token, checks the signature and its own key in the answer, and pins the server's key.
   Any mismatch ends the pairing.

The server keeps the agent's public key; no secret of the agent is ever on the server.
Every later request of the agent is signed with its key. The signature covers the exact
bytes of the body, the kind of request and the agent's identity. A replayed request
achieves nothing:

- a poll carries its number, so a replayed poll is never the newest and takes nothing
  (rule 10);
- only the first outcome of a command counts (rule 11);
- a token works once.

The application can revoke an agent (rule 13); it works again only after a new pairing. A
new signing key of the server takes a new pairing of every agent. Rotating the key without
that is later work.

### What protects what

| Threat | What stops it |
| --- | --- |
| Something on the path injects or changes a command or an answer of the server, for example a proxy that opens TLS or a compromised hop behind the ingress | The server's signature over the exact bytes, by the key the agent pinned (rule 1, §6), except a bare HTTP 413, which can only make an outcome less known (§6) |
| The same hop answers the pairing with its own key, or puts its own key in place of the agent's | The fingerprint of the server key in the pairing token, and the HMAC of the agent's key with a secret that never travels |
| A captured command is sent again, also after the caller was told it expired | The take deadline, the audit log, and an expiry of a handed-out command that the agent's own signed time decides (rules 1, 3, 7) |
| Someone poses as an agent, replays its requests or forges an outcome | The agent's signature on every request, the poll numbers (rule 10), and the rule that only the first outcome counts, and only from the agent the command was handed to (rule 11) |
| Two agents claim the same base, by mistake or on purpose | The first claim binds the key (rule 14) |
| The application, or whoever controls it, is compromised | It can run scripts only on the bases and in the modes the local policy opens, under the 1C accounts the administrator chose, in safe mode, and every command is in the local audit log |
| A script tries to reach the operating system | Safe mode (open items O1, O6) |
| The server's database leaks | It holds no agent secret and no signing key, which stays in the application's secret storage. It does hold the secrets of pairing tokens not used yet, until they are used, or until the purge after their expiry: whoever reads one in time can pair an agent of their own. It also holds the scripts, parameters and results of commands until their retention |
| A file of the agent machine leaks | The agent's private key and the 1C passwords are encrypted with DPAPI for that machine, so a copy is useless elsewhere |
| The whole agent machine is cloned | Nothing: the clone carries the agent's identity and secrets. Two running copies of one agent void rules 3 and 11. A clone must not run the agent until it is paired anew with a token of its own |

Confidentiality rests on TLS. Whoever opens TLS on the path can read the scripts,
parameters and results that pass, but can neither change nor inject them.

The library does not decide which scripts are allowed. The local policy decides bases,
accounts and modes. An application that wants to approve scripts does so before it calls
Write.

## 4. Liveness

- The agent has one poll open at a time. A poll waits on the server for up to its
  timeout, 25 seconds by default, below the idle limits of common proxies. Then the agent
  opens the next one.
- Every poll carries:
  - the protocol version, the agent version, the poll number and the agent's time;
  - the bases, with their modes, free workers and run limits;
  - the received commands whose outcome is not confirmed.
- After an error, the agent retries with a growing delay, up to the longest delay of its
  local policy, 30 seconds by default.
- The agent judges deadlines by its own clock, with the skew the server puts in every
  command, 2 minutes by default (rule 1). Its polls carry its time (rule 7). The server's
  answers carry the server's time only so that the console can report the skew.
- A server node that stops loses nothing. A poll waiting on it ends, and the agent opens
  the next poll on any node. A command that the stopped node had taken and never delivered
  is handed out again (rule 11).
- When the host of the signals cannot be reached, waits end at their deadlines and polls
  at their timeouts. Every wait reads the store again when it rejoins the host, so nothing
  is lost, although commands may expire meanwhile.

## 5. Hosting

**The server** is added to an ASP.NET Core application with its options, and maps the
endpoints of the agents under a prefix the application chooses. The application provides:

- **`ISignals` of [Moltaro.Signals](https://github.com/andrton/Moltaro.Signals)**, from the
  nuget.org package. On one node it is an in-process `SignalHost`. On several nodes it is a
  host in one process and clients in the others. The library never creates its own, so an
  application that already runs Signals keeps one mechanism.
  - The library's keys start with a prefix the application chooses, `outpost` by default,
    followed by the identity of an agent or of a command.
  - It opens one subscription per waiting poll and per waiting call. The application sizes
    the subscription limit of its Signals host for its agents and its concurrent calls.
- **The PostgreSQL connection and the schema** of the library's store. The store is an EF
  Core context of the library. Its migrations ship as EF Core migrations, and as SQL
  scripts for an application that applies migrations itself. Every node removes ended
  commands after their retention, and pairing tokens when they expire; removing is
  idempotent.
- **The signing key**, from the application's own secret storage. The store never holds
  it.
- **The authorization of callers.** Who may call Read or Write is the application's
  decision. The library authenticates only agents.

Its nodes need no sticky sessions. The ingress in front of them must let a poll stay open
for its timeout and let an outcome of the size limit through.

**The agent** runs as a Windows service under its own account, not LocalSystem.
- Its machine needs the COM connector of the same version as the 1C cluster. As far as is
  known, one COM connector is registered per bitness on a machine. An agent machine then
  serves the bases of one platform version per bitness; open item O8 checks this.
- It starts its COM workers with the bitness of that connector and ends them with itself.
  A Write running at that moment is Unknown (rule 3).
- A worker that crashes or hangs is ended and started again, with a growing delay.

**The local policy** is a JSON file in the agent's data folder, kept by the administrator
of that machine. For each base it gives:

- the key;
- the connection: a server and a database name, or the path of a file database;
- the read account, and the write account if writing is allowed;
- the workers and the run limit of each mode.

**The data folder** holds the local policy, the secrets, the audit log and the outcomes
not yet confirmed. Nobody but administrators, SYSTEM and the agent's account has any right
to it. The agent's account may only read it, except where it writes the audit log and the
outcomes, and the agent refuses to start when the folder or a file it reads admits anybody
else.

**The secrets** are the agent's private key and the passwords of the 1C accounts. They
are encrypted with DPAPI for the machine and kept in the data folder. The console, run by
an administrator, writes them. The server can neither read nor change the policy or the
secrets.

**The audit log** is a set of local files, one JSON line per entry, one file per day. It
is kept for 90 days by default and never for less than a day.

**The executor** is built with the Designer of 1C:Enterprise 8.3.17.1851, the oldest
supported platform. It is committed as the built `.epf` and its XML sources.

**The console** has these commands:

- install and uninstall the service;
- pair it;
- write a secret;
- check:
  - the COM connector and its version;
  - the connection to each base under each account;
  - the rights to change data that the read account has;
  - the server and the clock skew, without polling, so that the console never takes the
    place of the service's poll;
- write a local metadata report of a base: its objects, their fields and how they are
  stored. The report is a local archive for a developer and does not go through the
  server.

**The shell** depends on the server library. It needs a PostgreSQL connection, runs an
in-process `SignalHost`, keeps its signing key in a local file, and creates pairing tokens
for the agents it talks to.

## 6. Protocol

- HTTPS and JSON. Every request is made by the agent and signed with its key (section 3,
  Pairing). Every path carries the protocol version: `v1`.
- The agent has three requests:
  - **pair**, with the identity of the token, the agent's public key and its HMAC:
    answered, signed by the server, with the agent's identity, the registered key and the
    server's public signing key;
  - **poll**, carrying what section 4 lists: answered with the commands for the agent, or
    with none when its timeout ends, and with the server's time;
  - **outcome**, carrying the identity of a command and its outcome: answered with the
    confirmation.
- A command travels as the exact UTF-8 bytes of its JSON with a separate ECDSA P-256
  signature over those bytes. The agent verifies the bytes before it parses them, so no
  re-serialization stands between the signature and what runs.
- Every answer of the server is signed in the same way and bound to the request it
  answers by the SHA-256 of that request's exact body. The agent verifies an answer
  before it acts on any of it, and treats an answer that does not verify as a lost one.
- There is one exception: a bare HTTP 413 to an `outcome` request, which an ingress or the
  web server sends before the library sees the request. The agent acts on it as rule 11
  says, because all it can cause is Unknown or Failed `result-too-large`. That never
  claims a success and never frees a caller key, so a forged 413 can only make an outcome
  less known.
- A command names:
  - the protocol version, its identity, the agent, the base and the mode;
  - the caller key, if it has one;
  - the script and the parameters;
  - when it was issued, its take deadline, the allowed skew and the size limit of its
    outcome.
- [`protocol.md`](protocol.md) gives the exact messages. It is written with the sprint
  that introduces them.

## 7. .NET surface

The plan of the first epic fixes the names. In outline:

- registration of the server with its options, and a method that maps the endpoints of
  the agents under a prefix;
- a server object with these operations:
  - create a pairing token;
  - list agents and their bases;
  - revoke an agent;
  - release a base key;
  - Read and Write;
- the outcome as a result type: its kind, the result as JSON, the error code, the error
  text and the user messages.

The agent, the COM worker, the console and the shell are programs, not libraries. Their
contract is their command line, the local policy and the audit log.

## 8. Decisions

| # | Decision | Why |
| --- | --- | --- |
| D1 | The agent dials out over HTTPS with long polling. There is no WebSocket and no gRPC | Polls pass every proxy and need nothing special from an ingress. A held socket would tie the agent to one node, which would still need the store and the signals to meet the caller |
| D2 | A tunnel with two commands, Read and Write, each a script in the 1C language. There is no model of 1C objects | A script reaches everything the configuration offers. A model would never be complete and would tie the library to configurations |
| D3 | 1C is reached through the external connection and the executor `.epf`. OData, HTTP services and the DBMS are not used | The external connection needs no change of the configuration and no web server, and keeps the rights and the logic of 1C |
| D4 | Every command and every answer of the server is signed over its exact bytes with the server key that the agent pinned at pairing, checked against the fingerprint in the pairing token. The one exception is a bare HTTP 413 to an outcome (§6) | Nothing between the agent and the server can turn into a command or change what the agent acts on, whatever the TLS path looks like, beyond making an outcome less known |
| D5 | An agent signs every request with its own key pair, created at pairing and bound to its token by an HMAC with a secret that never travels. The server keeps only the public key. Polls are numbered, and only the newest takes commands | No secret of the agent ever travels or sits on the server, nobody on the path can pair in its place, and a replayed request achieves nothing |
| D6 | The local policy decides bases, accounts and modes. The server cannot change it, and there is no list of allowed scripts | The administrator of the back office keeps the last word, and the tunnel stays a tunnel |
| D7 | The audit log is written before a command runs, and it is also the agent's journal | The administrator sees everything that was done, independently of the server, and there is one record, not two |
| D8 | A caller key makes a command run once per base. It is required for a Write and optional for a Read. A Write runs in one transaction, and its result is checked before the commit. An unknown outcome is said to be unknown | A repeated call never posts a document twice, a failure means nothing changed, and a long command can be found again after a restart |
| D9 | The store is PostgreSQL through the library's own EF Core context. Nodes share nothing in memory and are woken through `ISignals` of Moltaro.Signals, which the application provides | Any node serves any request, and an application with its own signals keeps one mechanism |
| D10 | An agent that is not connected gives Unavailable at once. A command waits for a poll for at most its take deadline, then expires. A command that was handed out expires only when the agent's own signed time shows it can no longer accept it. Nothing is delivered later | A caller knows at once, and a command reported as expired never runs |
| D11 | COM runs only in worker processes, per base and mode, with the connector's bitness, and they end with the agent | A failure of COM ends at most the commands of one base, and no orphan session outlives the agent |
| D12 | Scripts run in safe mode | A script can work with data but cannot reach the operating system of the machine it runs on |
| D13 | The agent's private key and the 1C passwords are encrypted with DPAPI for the machine, in the agent's data folder, to which only administrators, SYSTEM and the agent's account have rights. The signing key of the server stays in the application's secret storage. The only secret in the store is that of an unused pairing token, deleted when it is used or removed by the purge after it expires | No secret travels, none lives long in the store, and a copied file is useless |
| D14 | The oldest supported platform is 1C:Enterprise 8.3.17.1851, and the executor is built with its Designer. The COM connector has the version of the cluster | The oldest version in use by the first adopters, and a connector of another version does not connect |
| D15 | Versions stay 0.x until a first production adopter runs on the library. Every request carries the protocol version | Changes stay cheap until someone depends on them, and agents are updated later than servers |
| D16 | The library is generic and public, under Apache-2.0. Nothing of a particular customer or product goes into it | It can be read, trusted and used by anybody |
| D17 | A base key belongs to the first agent that declares it, until that agent is revoked or the application releases the key | The scripts of one database never reach another |

## 9. Non-goals and possible later work

Not goals:

- a model of 1C objects, a CRUD or OData-like API;
- lists of allowed scripts, or approval of scripts;
- delivery to an agent that is not connected;
- WebSocket, gRPC or any other transport;
- confidentiality beyond TLS;
- an agent on Linux, since the COM connector exists on Windows only;
- driving the forms of the 1C client;
- access to the DBMS of 1C;
- systems other than 1C.

Possible later work:

- rotating the signing key without pairing again;
- results larger than the limit, sent in parts;
- updating the agent from the server.

## 10. Existing tools

**Generic reverse tunnels**, such as SSH port forwarding, frp or hosted tunnel services,
carry a port, not a command. Using one would publish the external connection or a web
server to the outside, add a third party, and give neither a check of each command nor a
local record of it.

**Prior art for the studies.** Several established agents already dial out and take work
from a server. Each sprint on such a mechanism studies them first, as the
[development process](work/process.md#a-study-comes-before-a-design) requires, with their
licences checked at that time:

- the GitHub Actions runner, a long-polling agent that takes jobs from a service;
- the Azure Pipelines agent;
- Octopus Deploy's Halibut, a polling mode with pinned certificates.

## 11. Quality bar

- Every numbered rule of section 3 has a deterministic test, with time from a fake clock.
- The transport is tested over loopback HTTP, with an agent and a server in one test. The
  tests cover several nodes, a stopped node, a lost poll answer, an older poll, and a lost
  outcome.
- The store is tested against a real PostgreSQL.
- No automated test needs 1C. What only 1C can show is accepted by hand on a lab machine,
  on a copy of a database, with 1C:Enterprise 8.3.17.1851: the executor, the COM worker,
  the script contract and the open items below.
- Latency. On a loopback stand without 1C, a command reaches a waiting poll and its
  outcome reaches the waiting caller each within 100 milliseconds at the 95th percentile.
  No hop waits for a fixed interval.

## 12. Open items

These are checked on the lab machine before the sprint that depends on them is planned in
detail:

- **O1. Posting in safe mode.** Does a typical document post in safe mode, with the same
  register movements as when a person posts it? Common modules of typical configurations
  switch to privileged mode, which has no effect in safe mode, so the rights of the account
  apply where they did not before. If the movements differ, decision D12 goes back to the
  owner. Weakening it then requires COM workers under an account of their own, which
  cannot read the agent's secrets or its audit log; they become part of the sprint that
  weakens it.
- **O2. Opening the executor.** Can an external connection open the executor with
  `ExternalDataProcessors.Create`, in safe mode, under both accounts on 8.3.17.1851? The
  check covers:
  - which rights that needs, the right of external connection included;
  - whether the account's protection against dangerous actions interferes;
  - whether the file is read on the agent machine.
- **O3. Licences.** Does a session of an external connection take a client licence, and
  how many workers does a typical licence set allow?
- **O4. JSON.** Do `ReadJSON` into a `Map` and `WriteJSON` in safe mode behave as section 3
  says, `null` and `Undefined` included?
- **O5. Long Writes.** A Write holds the managed locks of its transaction until it ends. What
  run limits are reasonable for typical postings, for Read and for Write?
- **O6. Where scripts run.** For a client-server base, does the code of an external
  connection run in the COM worker or on the cluster? The answer decides what safe mode
  protects.
- **O7. User messages.** Can the executor collect and clear the user messages of one
  command in an external connection?
- **O8. One connector per bitness.** Is only one version of the COM connector registered
  per bitness on a machine?
