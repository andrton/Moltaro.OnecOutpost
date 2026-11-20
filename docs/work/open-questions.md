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

### Q1. Values of `policy.json` that its table does not mention

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, the contract step and review round 1 |
| **Question** | The table of `policy.json` does not say how a member given twice, a member set to `null`, or an empty or white-space string for `server`, `database`, `file` or `account` is treated. Is each of them an invalid file? |
| **Until answered** | `AgentPolicyReader` refuses all of them, so the whole file is invalid and the error names the member's path. No test states it. To be answered before S6, which reads the policy when the agent starts |
| **Decides** | The owner: `policy.json` is public contract |

### Q2. What "characters" counts in the rules of names

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, the tests step |
| **Question** | The rule of agent names allows 1 to 100 "characters". Are these UTF-16 code units, Unicode scalar values or text elements? The base key, the caller key and the schema are ASCII, so only the agent name is affected |
| **Until answered** | `OutpostNames` counts UTF-16 code units, as `string.Length` does. No test depends on the difference. To be answered before S4, which stores agent names |
| **Decides** | The owner: the rules of names are public contract |

### Q3. One version of EF Core and its relational package

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, the contract step |
| **Question** | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 brings `Microsoft.EntityFrameworkCore.Relational` at its lowest allowed version, 10.0.4, while the server references `Microsoft.EntityFrameworkCore` 10.0.15. Should the server reference the relational package as well, so that both are at one version? The plan names only the two packages |
| **Until answered** | Nothing uses EF Core yet, and the versions stay as they are. To be answered before S3's contract step |
| **Decides** | The planner |

### Q4. The exact text that `SigningKeyPem` accepts

| | |
| --- | --- |
| **Found** | 20 November 2026, Epic 1 S1, review round 1 |
| **Question** | The check of `SigningKeyPem` reads the first PEM block and ignores any text after it, so a text with a second block passes. Which text exactly does the option accept, so that the sprint that imports the key reads it the same way and never refuses a key that passed the check? |
| **Until answered** | The first block must be the unencrypted PKCS#8 PEM of an ECDSA P-256 private key; the rest of the text is ignored, and no test states it. To be answered before S2's contract step, which signs with the key |
| **Decides** | The planner |

## Answered

None.
