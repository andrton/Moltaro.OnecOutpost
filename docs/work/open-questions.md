# Open questions

Questions that came up while a sprint was carried out and that no brief, decision or rule
answers. Each is written down when it is found, so that it is not settled by whoever
happens to write the code, and not forgotten.

A question that blocks a sprint does not wait here: the coordinator stops and asks the
owner, as the [development process](process.md#when-to-stop-and-ask-the-owner) says. This
register is for what does not block: the sprint goes on, and the entry says what was done
in the meantime.

## How it is used

- **Who writes.** The coordinator, when an agent or a reviewer reports a gap, or when it
  finds one itself. An agent reports; it does not decide.
- **What an entry says.** Where the question was found, the question itself, what the
  code or the tests do until it is answered, and who has to answer it.
- **When it is looked at.** When a sprint closes, the coordinator names the entries the
  sprint added in its report to the owner. When the coordinator prepares a sprint, it
  reads the open entries that name that sprint and does not start it before each of them
  is answered or moved to a later sprint by whoever decides it. Before the contracts of a
  stage are written, the planner goes through the open entries and takes up those the
  stage touches. When an epic closes, every entry is gone through with the owner: none
  stays open without a decision to carry it into the next epic.
- **How it ends.** An answer comes from whoever the entry names as deciding. A rule of
  the public contract or a guarantee of the concept is the owner's; what follows from a
  rule that already stands the planner answers and reports to the owner. It is entered
  here with its date and with who gave it, and the rule it gives goes where it belongs:
  the concept, `docs/protocol.md`, the epic document, a brief, `AGENTS.md`. The entry then
  moves to **Answered** and says where the rule now lives.

An entry is numbered `Q1`, `Q2`, … and keeps its number for good:

| | |
| --- | --- |
| **Found** | When, and in which sprint or review |
| **Question** | The gap, stated so that it can be answered |
| **Until answered** | What the code and the tests do meanwhile |
| **Decides** | The owner or the planner |

## Open

### Q7. A user and a password in the URL of the proxy

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, review round 7 |
| **Question** | `proxy` takes any absolute `http` URL, so a URL with a user and a password in it, such as `http://user:password@proxy.local:3128`, is accepted, and the policy then holds a secret in plain text. The table of `policy.json` says that a proxy which asks for authentication gets the Windows credentials of the agent's account. Is a URL with a user or a password refused? |
| **Until answered** | `AgentPolicyReader` accepts it. No message of the reader repeats the value of `proxy`, but the internal `AgentPolicy` record would print it in full if anything wrote the record out; nothing does yet. To be answered before S6, which connects through the proxy |
| **Decides** | The owner: `policy.json` is public contract |
| **Proposed** | 20 November 2026, the planner, in S2's list of changes of the public contract that come with `docs/protocol.md`: a URL with a user or a password in `proxy` is refused. The owner's approval of that list answers this entry |

### Q8. Which code a verified command gets for a fault the brief does not place

| | |
| --- | --- |
| **Found** | 21 November 2026, Epic 1 S2, the tests and the implementation steps |
| **Question** | §7 of `docs/protocol.md` gives `form` without an identity to verified bytes that yield no `protocol` and no `id`, and `malformed` with the identity to a verified command of this version that breaks §7. It does not place every fault. Which code does each of these get: an `id` in another form than the 36 lowercase characters; a `protocol` or an `id` given twice; bytes that are not JSON, or nest beyond the 72 levels of a message; a fault of §2 in a command whose `protocol` and `id` can be read, such as a duplicated member, U+0000 or parameters deeper than 64 levels; and `protocol` 0? |
| **Taken** | 21 November 2026, the coordinator, as the recommended reading: an `id` in another form, a `protocol` or an `id` given twice, and bytes that are not JSON within 72 levels yield no identity, so they get `form` without one. A fault of §2 in a command whose `protocol` 1 and `id` were read gets `malformed` with its identity. `protocol` 0 is another version and gets `protocol-version`. The codec does so; its tests show an `id` in another form, bytes that are not JSON, the faults of §2 and `protocol` 0 |
| **Decides** | The owner: the codes of a refusal are public contract. A cheap change of the codec if overturned; nothing is sent or stored yet |

### Q9. Rules of §6 and §8 that the readers apply more strictly than the text says

| | |
| --- | --- |
| **Found** | 21 November 2026, Epic 1 S2, the implementation step |
| **Question** | Three rules are read strictly where the text of `docs/protocol.md` allows another reading. Does each hold? (a) For `failed` with `script-error`, `errorText` is a string and never `null` (§8 says "the text of the error 1C raised", and `null` otherwise). (b) The keys in `bases.held` and `bases.claimed` of a poll answer follow the rule of base keys (§6.2). (c) A signed refusal is checked only for its code against its status, not against the kind of the request (§9) |
| **Taken** | 21 November 2026, the coordinator, as the recommended reading: (a) and (b) yes, so a reader refuses such a message with `form`; (c) as the brief's contract of `ReadRefusal` states it. The codec does so |
| **Decides** | The planner, for (b) and (c), which follow from rules that stand; the owner for (a), a rule of the outcome object. To be answered before S6, whose agent writes and reads these messages |

### Q10. The behaviors of the COM worker that its Acceptance does not list one by one

| | |
| --- | --- |
| **Found** | 21 November 2026, Epic 1 S9, review round 1 of the internal contract |
| **Question** | The automated Acceptance of S9 names some lines of the worker and not others: that the executor's text is written byte for byte, that `{"ready":true}` is written, that `safeMode` reaches the executor of a Write, that every command gets a fresh executor that is released, that `parameters`, `callerKey` and `outcomeLimit` pass through, and that the next line is read only after the outcome is written. Does each need a test of its own? |
| **Taken** | 21 November 2026, the coordinator, as the planner recommends: the Acceptance's line on every line of the internal contract covers them, so the test author writes them as tests of those lines |
| **Decides** | The planner |

### Q11. The cost of measuring UTF-8 in the executor

| | |
| --- | --- |
| **Found** | 21 November 2026, Epic 1 S9, review round 1 of the internal contract |
| **Question** | The compatibility mode 8.2.13 reportedly has no binary data functions, so the executor counts the UTF-8 bytes of its outcome character by character. For an outcome near its limit, 16 MiB by default and up to 64 MiB, that loop runs inside the transaction of a Write and holds its locks. Is the default of the outcome limit still right? |
| **Taken** | 21 November 2026, the coordinator, as the planner recommends: a `CharCode` loop is accepted for S9. Its cost inside the transaction of a Write is measured on the lab machine before the default of the outcome limit is trusted, with a result near the limit and with many thousands of user messages, since the executor builds its texts by joining strings (S9 code review, round 1) |
| **Decides** | The planner, with the figure of the lab; the owner if the default of the limit, a public option, has to change |

### Q12. A commit that raises, and a transaction the script leaves open

| | |
| --- | --- |
| **Found** | 21 November 2026, Epic 1 S9, the implementation step |
| **Question** | Rule 5 names one way a Write's outcome cannot be known: the script ended the executor's transaction itself. Two more cases have no code. The executor's `CommitTransaction` may raise, so it cannot tell whether the Write was written. The script may also begin a nested transaction and leave it open; the executor then rolls everything back, and nothing was written. Which outcomes do they get? |
| **Taken** | 21 November 2026, the coordinator, as the planner recommends, with no new code: a commit that raises is Unknown `transaction-ended`, whose meaning widens to "the script ended the transaction of its Write itself, or the commit raised"; a transaction left open is Failed `script-error`, with a fixed English sentence of the executor as its `errorText`, since 1C raised nothing. After round 1 of the code review the same holds for a Read: the executor rolls back every transaction a script left open, so none outlives its command in the session of the worker. The executor does so, and lab tests state it. On the lab, 21 November 2026, a nested rollback on 8.3.17 left the transaction active and its commit did not raise, so the one way of provoking a commit that raises that was tried does not work there: the planner decided that the executor keeps Unknown `transaction-ended` for it, which principle 5 requires, and that the case has no lab test. Round 3 of the code review then found, and the lab confirmed, that a Write whose script begins and rolls back a nested transaction was reported Succeeded although 1C had silently rolled back all it wrote. The owner decided to fix it: before the commit of a Write the executor runs the query `SELECT 1`, and when it raises, rolls back and gives Failed `script-error` with its own sentence. Two lab tests state it |
| **Decides** | The owner: the meaning of a code and of `errorText` is public contract. Listed with the drafts of S9 |

### Q13. A schema whose name begins with `pg_`

| | |
| --- | --- |
| **Found** | 21 November 2026, Epic 1 S3, the code review, round 1 |
| **Question** | The rule of schemas of S1 accepts a name that begins with `pg_`, which PostgreSQL reserves for its own schemas. Now that `MigrateAsync` works, such a schema passes the check of the call and then fails in its task with the error 42939 of PostgreSQL. Should the rule of schemas refuse the prefix, so that the call throws `ArgumentException` itself? |
| **Until answered** | The rule stays as it is, and `MigrateAsync` reports the error of PostgreSQL through its task. `AddOutpostServer` accepts such a schema. It blocks no sprint: the planner carried it to the closing of the epic (21 November 2026), since either answer is a small change of one check |
| **Decides** | The owner: the rule of names is public contract |

## Answered

### Q1. Values of `policy.json` that its table does not mention

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, the contract step and review round 1 |
| **Question** | The table of `policy.json` does not say how a member given twice, a member set to `null`, or an empty or white-space string for `server`, `database`, `file` or `account` is treated. Is each of them an invalid file? |
| **Until answered** | `AgentPolicyReader` refuses all of them, so the whole file is invalid and the error names the member's path. No test states it. To be answered before S6, which reads the policy when the agent starts |
| **Decides** | The owner: `policy.json` is public contract |
| **Answer** | 20 November 2026, the owner, in their words: it does not work at all, and it writes the error well, so that it is clear where and what. Each of these makes the whole file invalid, and the agent serves nothing, as the reader of S1 does. The error says the path of the member, what is wrong with it, and what is expected there, for example `bases[0].server: the value is empty; expected the 1C cluster address such as "srv-1c:1541", or remove "server" and "database" and give "file"` |
| **Now lives in** | [`policy.json`](epic-1-first-release.md#policyjson) in the epic document, the error texts of `AgentPolicyReader` and their tests |

### Q2. What "characters" counts in the rules of names

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, the tests step |
| **Question** | The rule of agent names allows 1 to 100 "characters". Are these UTF-16 code units, Unicode scalar values or text elements? The base key, the caller key and the schema are ASCII, so only the agent name is affected |
| **Until answered** | `OutpostNames` counts UTF-16 code units, as `string.Length` does. No test depends on the difference. To be answered before S4, which stores agent names |
| **Decides** | The owner: the rules of names are public contract |
| **Answer** | 20 November 2026, the owner, in their words: if it is a display name of some kind, then arbitrary characters. The agent's identity is its `Guid`, so the name is a display label of arbitrary characters, and the rule stays as S1 has it: 1 to 100 characters counted as UTF-16 code units (`string.Length`), no control character, no white space at the start or the end |
| **Now lives in** | The rule of names in the [epic document](epic-1-first-release.md), and the XML documentation of `agentName` and of `OutpostAgent.Name` |

### Q3. One version of EF Core and its relational package

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, the contract step |
| **Question** | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 brings `Microsoft.EntityFrameworkCore.Relational` at its lowest allowed version, 10.0.4, while the server references `Microsoft.EntityFrameworkCore` 10.0.15. Should the server reference the relational package as well, so that both are at one version? The plan names only the two packages |
| **Until answered** | Nothing uses EF Core yet, and the versions stay as they are. To be answered before S3's contract step |
| **Decides** | The planner |
| **Answer** | 20 November 2026, the planner: yes. The server also references `Microsoft.EntityFrameworkCore.Relational`, at the same version as `Microsoft.EntityFrameworkCore`, so that every EF Core assembly the package brings is of one version |
| **Now lives in** | The S1 brief of the [epic document](epic-1-first-release.md), its list of references, and `src/Directory.Packages.props` |

### Q4. The exact text that `SigningKeyPem` accepts

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, review round 1 |
| **Question** | The check of `SigningKeyPem` read the first PEM block and ignored any text after it, so a text with a second block passed. Which text exactly does the option accept, so that the sprint that imports the key reads it the same way and never refuses a key that passed the check? |
| **Proposed** | 20 November 2026, the planner: the option accepts exactly one PEM block, the unencrypted PKCS#8 `PRIVATE KEY` of an ECDSA P-256 key, with nothing but white space before or after it. Any other text, a second block included, is refused with `ArgumentException` whose `ParamName` is `SigningKeyPem`, so that the import of S2 reads exactly what the check accepted |
| **Until answered** | The code, its test, the XML documentation of the option and its row in the options table followed the proposal, and S1 waited for the owner |
| **Decides** | The owner: the proposal changes the public contract (review round 2 of S1) |
| **Answer** | 20 November 2026, the owner, in their words: refuse it, of course, if it is not just the key but some rubbish, and if the key is not alone there, refuse it. The owner approves the proposal as it stands: anything but the one block with white space around it, a second block included, is refused with `ArgumentException` whose `ParamName` is `SigningKeyPem`, and a clear message |
| **Now lives in** | The row of `SigningKeyPem` in the options table of the [epic document](epic-1-first-release.md), the XML documentation of the option, its check and its tests |

### Q5. How deep the parameters of a command may nest

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, the fixes of review round 1 |
| **Question** | The parameters are written and read as JSON, and System.Text.Json refuses a nesting deeper than its default of 64 levels, so a caller can observe that limit. By `AGENTS.md` principle 10 an observable limit is an option with a default. Is it a limit that `docs/protocol.md` fixes for every message, an option of the server, or something else? |
| **Proposed** | 20 November 2026, the planner: a fixed limit of the protocol, not an option, like the lengths of names, at 64 levels; `AGENTS.md` principle 10 says that such a limit is not an option. After review round 3, the planner added how the nesting is counted inside a message, as "The protocol" in the [epic document](epic-1-first-release.md) now gives it |
| **Until answered** | `ReadAsync` and `WriteAsync` refused parameters nested deeper than 64 levels, the outer object being level 1, or holding a value that System.Text.Json cannot write, with `ArgumentException` whose `ParamName` is `Parameters`, and the tests held 64 and 65. The rule of names and "The protocol" in the [epic document](epic-1-first-release.md), principle 10 and the XML documentation of `Parameters` followed the proposal, marked in the epic document as waiting for the owner while S1 waited for the answer |
| **Decides** | The owner: the proposal changes the public contract (review round 2 of S1) |
| **Answer** | 20 November 2026, the owner, in their words: yes, a hard rule. The owner approves the proposal: the parameters of a command and the result of an outcome nest at most 64 levels, counted from their own outer value, a fixed limit of the protocol and not an option, the same on both sides; `docs/protocol.md` states it in S2; the narrowed sentence of `AGENTS.md` principle 10 stands |
| **Now lives in** | `AGENTS.md` principle 10; the rule of names and "The protocol" in the [epic document](epic-1-first-release.md); the XML documentation of `Parameters`; `docs/protocol.md` when S2 writes it |

### Q6. Strings that UTF-8 cannot carry

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, review rounds 6 and 8 |
| **Question** | A string of .NET can hold a lone surrogate, a code unit from U+D800 to U+DFFF without its pair, which no UTF-8 can carry. It meets the library in two places. First, the agent name, a script and the strings of the parameters accept one; strict UTF-8, as the store and the protocol will use, would then fail inside the task of the call, where the rules for every member want an argument error thrown by the call itself, as for U+0000. Second, in `policy.json` an escaped lone surrogate, in a string or in a member name, makes `AgentPolicyReader` throw `InvalidOperationException` instead of an error that says where and what. Is a lone surrogate refused as an argument by the rules of names, of scripts and of parameters? And is it a fault that makes `policy.json` invalid, reported at its path? |
| **Until answered** | The checks of S1 accept a lone surrogate in the agent name, a script and the parameters; nothing stores or sends a string yet. `AgentPolicyReader` throws `InvalidOperationException` for an escaped lone surrogate; the agent program does not read its policy yet. Both parts are answered at once, before S2, whose codec first turns these strings into UTF-8 bytes and signs them |
| **Decides** | The owner: the answer changes the rules of names, of scripts and of parameters and the format of `policy.json`, which are public contract |
| **Answer** | 20 November 2026, the owner, in their words: yes. A lone surrogate is refused at once, with a clear argument error, in the agent name, the script and the parameters of `ReadAsync` and `WriteAsync`; and it makes `policy.json` invalid, reported at its path as Q1 requires |
| **Now lives in** | The rule of names and [`policy.json`](epic-1-first-release.md#policyjson) in the epic document; the checks of `OutpostNames`, of the parameters and of `AgentPolicyReader`, and their tests, carried into S2 |
