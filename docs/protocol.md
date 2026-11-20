# Moltaro.OnecOutpost — protocol v1

The wire protocol between an agent and a server. The [concept](concept.md) fixes its
principles (§3 "Pairing", §4, §6, D1, D4, D5, D15); this document fixes the bytes.

**Status.** Draft of Epic 1, sprint S2, written after the sprint's study. It is public
contract: the owner approves it before the contract step of S2. No code reads or writes it
yet.

## 1. Overview

- **Transport.** HTTPS, `POST` only, JSON bodies. The agent makes every request; the server
  only answers.
- **Paths.** `{prefix}/{version}/{request}`:
  - `{prefix}` is the one the application gave `MapOutpostAgents`;
  - `{version}` is `v1`;
  - `{request}` is `pair`, `poll` or `outcome`.
- **Signatures.** Every request is signed by the agent's key, and every answer of the
  library by the server's key (§3). Two answers are not signed (§9), and both can only make
  something fail, never succeed:
  - a bare HTTP 413;
  - the answer to an unsupported version.
- **Order.** An agent has at most one poll open. `pair` comes before any poll; `outcome`
  requests come whenever an outcome is ready.

## 2. Encodings

- **No content coding.** The agent sends no `Accept-Encoding` and no `Content-Encoding`.
  The bytes that are signed and hashed are the bytes of a body as it is, without content
  coding.
- **JSON.** RFC 8259 text in UTF-8 without a byte order mark.
  - A writer escapes `"` as `\"`, `\` as `\\`, and the control characters U+0001 to U+001F:
    as `\b`, `\t`, `\n`, `\f` and `\r` where those exist, and otherwise as `\u00XX` with two
    uppercase hexadecimal digits. It writes every other character as it is.
  - No string of the protocol holds U+0000 or a lone surrogate. A reader refuses one with
    `form`.
  - A reader accepts every escape of RFC 8259.
  - A message has exactly the members this document lists for it. A missing, unknown or
    duplicated member, a member that is `null` where this document does not allow it, or a
    value of the wrong type refuses the message with `form`. Duplicated member names are
    refused at every depth, inside parameters and results too.
  - Integers have no fraction and no exponent.
  - The order of members carries no meaning.
- **Nesting.** Every message is read with a maximum depth of 72. Parameters and results
  are also checked on their own (Q5):
  - at most 64 levels each, counted from their own outer value. An object or an array at
    the top is level 1, and a scalar is level 0;
  - the envelope that carries them does not count against them, so a command that
    `ReadAsync` or `WriteAsync` accepted is never refused for its nesting;
  - a result that nests deeper fails its command with `result-type` (concept rule 5), before
    the commit of a Write.
- **Identities** of agents and commands are GUIDs, written as 36 lowercase characters in the
  form `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`, in JSON and in headers alike.
- **Times** are UTC, written as exactly `yyyy-MM-ddTHH:mm:ss.fffZ`. A writer truncates
  anything finer than a millisecond.
- **Durations** are whole milliseconds, as JSON integers, in members whose names end with
  `Ms`.
- **Binary values** are base64url without padding (RFC 4648 §5). A reader refuses a value
  that is not in that exact form, one with non-zero padding bits included.
- **Public keys** are the DER `SubjectPublicKeyInfo` of an ECDSA key on the named curve
  P-256 with an uncompressed point, 91 bytes, as
  `ECDsa.ExportSubjectPublicKeyInfo` writes it, in base64url. Any other key, or one with
  bytes beyond its structure, is refused with `form`.
- **Fingerprints.** The fingerprint of a key is the 64 lowercase hexadecimal digits of
  SHA-256 over its 91 bytes. The key is pinned, not a certificate.
- **Signatures** are ECDSA P-256 with SHA-256 over the signing input of §3, as the 64 bytes
  `r ‖ s` (IEEE P1363), in base64url. Because ECDSA signatures are malleable, a signature
  is never used as an identity or as a key of deduplication.

## 3. What is signed

Every signing input is an ASCII string of fields separated by single spaces. Every field
has a fixed form, so the framing cannot be read two ways:
- the kinds are the words below;
- identities are 36 characters;
- hashes are 64 lowercase hexadecimal digits;
- the status is three digits.

| Signed | By | Signing input |
| --- | --- | --- |
| A request | The agent's key | `outpost/1 request {kind} {agent} {bodyHash}` |
| An answer | The server's key | `outpost/1 answer {kind} {agent} {status} {requestHash} {bodyHash}` |
| A command | The server's key | `outpost/1 command {bodyHash}` |

- `{kind}` is `pair`, `poll` or `outcome`.
- `{agent}` is the identity of the request's `Outpost-Agent` header.
- `{bodyHash}` is SHA-256 over the exact bytes of the body, or of the command.
- `{status}` is the HTTP status of the answer.
- `{requestHash}` is SHA-256 over the exact bytes of the body of the request that the
  answer answers. An answer therefore fits only the one request it was made for, and a
  poll body carries the poll number and the agent's time.

**Verify, then read.** A receiver verifies the exact bytes it received, and only then
parses them. A sender signs once and sends exactly the bytes it signed; it never serializes
again after signing.
- The one exception is a `pair` request. The server must read its key before it can verify
  it, so it parses the body first; the body is at most 16 KiB.
- Every answer to `pair` carries the server's key, a refusal included, so that the agent
  can verify it once it has checked the key against the token (§6.1).

**The pairing proof** is an HMAC-SHA256, keyed by the 16 bytes of the token's secret, over
`outpost/1 pair-proof {agent} {keyHash}`:
- `{keyHash}` is SHA-256 over the 91 bytes of the agent's key;
- the proof is 32 bytes;
- the server compares it in constant time (`CryptographicOperations.FixedTimeEquals`).

## 4. Headers

| Header | In | Value |
| --- | --- | --- |
| `Content-Type` | Every request and answer with a body | `application/json` |
| `Outpost-Agent` | Every request | The agent's identity; in `pair`, the identity that the token names |
| `Outpost-Signature` | Every request, and every answer except those of §9's two exceptions | The signature of §3 |

## 5. The pairing token

`outpost1.{agent}.{secret}.{fingerprint}`, for example (shortened):

```text
outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.q3Vx8Kd2mP0sT1uW4yZ6bA.4c1f…e9a0
```

- `outpost1` names this form.
- `{agent}` is the identity of the pending agent that the token is for.
- `{secret}` is 16 random bytes as base64url: 22 characters, 128 bits.
- `{fingerprint}` is the server key's fingerprint, 64 lowercase hexadecimal digits.

The parts are separated by `.`. A token with another prefix, another number of parts, or a
part of another form is not a token, and the console refuses it before it sends anything.

## 6. The requests

### 6.1 `pair`

`Outpost-Agent` is the identity in the token. The request is signed with the agent's new
key.

```json
{
  "agentVersion": "0.1.0",
  "key": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
  "proof": "u3Fq…"
}
```

- `agentVersion` is 1 to 64 characters, each one of `A–Z a–z 0–9 . - +`.

**What the server checks**, in this order:
1. The token names a pending agent; it is not used, and has not expired by the clock of the
   node.
2. The proof is right for the token's secret, the identity and `key`.
3. The request's signature verifies with `key`.

Then, in one change of the store, it registers `key` for the agent and deletes the token's
secret.

**A repeated pair.** Suppose the agent is paired already, with this same `key`, and the
request is signed with it. The server then answers again as it did the first time, without
the secret, which is gone. So a lost answer to `pair` can be fetched again.

**Answer `200`**, signed with the server's key:

```json
{
  "time": "2026-11-20T10:15:00.123Z",
  "agent": "3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b",
  "key": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
  "serverKey": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
  "pollTimeoutMs": 25000,
  "leaseGraceMs": 10000
}
```

**What the agent checks**, any mismatch ending the pairing:
- the fingerprint of `serverKey` equals the token's;
- the answer's signature verifies with `serverKey`;
- `agent` and `key` are what it sent.

Then it pins `serverKey`. The console writes `pollTimeoutMs` and `leaseGraceMs` into
`agent.json`, so that the first poll is bounded.

**Refusals of `pair`** carry `serverKey` as well, and the agent verifies them in the same
way:
- `token`: the token names no pending agent, is used or expired, or the proof is wrong. The
  one code says nothing about which of these it was;
- `signature`: the signature does not verify with `key`.

### 6.2 `poll`

```json
{
  "number": 639307665001230000,
  "time": "2026-11-20T10:15:00.123Z",
  "agentVersion": "0.1.0",
  "bases": [
    {
      "key": "accounting",
      "read": { "free": 1, "runLimitMs": 600000 },
      "write": { "free": 0, "runLimitMs": 600000 }
    }
  ],
  "received": [ "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6" ]
}
```

- **`number`** is the poll number, from 1 to 2⁶².
  - The agent takes it from its clock (UTC ticks), always above its previous one, and above
    the `newest` that an answer to an older poll named (concept rule 10).
  - Numbers taken from a clock stay far below 2⁶².
- **`time`** is the agent's time (concept rule 7).
- **`bases`** declares what the local policy serves:
  - at most 256 bases, possibly none, with keys by the rule of names and unique;
  - `read` is required. `write` is present only when the policy opens writing, and is never
    `null`;
  - `free` is the number of free workers of the mode, from 0 to 8;
  - `runLimitMs` is from 1 000 to 86 400 000.
- **`received`** lists, without repetition, every command the agent received whose outcome
  the server has not confirmed (rule 11), refused ones included.
  - It holds at most 10 000 entries, and an agent never leaves one out.
  - While its list is full, the agent declares `free` as 0 for every mode, so that it takes
    no new command.

**The server's checks of a poll:**
- A poll is **older** when its number is not greater than the newest that the server
  recorded for its agent, an exact replay included.
- A poll is also older when its `time` is behind the clock of the node that receives it by
  more than the server's `ClockSkew`. A poll held back on the path can therefore never renew
  the lease of an agent that is gone (rule 6).
- An older poll changes nothing in the store (rule 10).
- A poll that is not older records the agent's lease, bases and list, and takes at most
  `free` commands per mode, oldest first (rule 8).

**Answer `200`**, signed:

```json
{
  "time": "2026-11-20T10:15:01.456Z",
  "newest": null,
  "pollTimeoutMs": 25000,
  "leaseGraceMs": 10000,
  "bases": { "held": [ "accounting" ], "claimed": [] },
  "commands": [
    { "bytes": "eyJwcm90b2NvbCI6MSwi…", "signature": "MEUCIQ…" }
  ]
}
```

- **The ordinary answer.** The server holds the poll until a command is due for one of its
  free modes, or until `pollTimeoutMs`. Then it answers with the commands, or with none, and
  `newest` is `null`.
- **The answer to an older poll** comes at once:
  - `newest` is the newest number the server recorded for the agent;
  - `commands` is empty;
  - the other members are as in any answer.
- **`bases`** gives, as of the moment of the answer, the keys the agent holds and the keys
  it declared that another agent holds (rule 14). Every key the poll declared is in exactly
  one of the two.
- **`commands`**: each holds the exact bytes of a command (§7) and the server's signature
  over them.
- **`pollTimeoutMs`** and **`leaseGraceMs`** are within the ranges of the server's options:
  1 000 to 300 000 each.
- **The agent's wait.** The agent waits for an answer no longer than `pollTimeoutMs` plus
  `leaseGraceMs`, from `agent.json` or from the last answer, and then counts the poll as
  lost (§9).

### 6.3 `outcome`

```json
{
  "command": "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6",
  "outcome": { "kind": "succeeded", "code": null, "result": { "rows": 3 }, "errorText": null, "messages": [], "messagesTrimmed": false }
}
```

- `outcome` is the outcome object of §8, exactly as the executor or the agent wrote it. The
  agent places its bytes into the body unchanged.

**Answer `200`**, signed:

```json
{ "time": "2026-11-20T10:15:02.789Z", "confirmed": "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6" }
```

**The server's checks of an outcome**, in this order:
1. **Its size.** An `outcome` member larger than the outcome limit of its command is
   refused with `result-too-large`. For a command the server does not know, the limit is
   `OutcomeSizeLimit`.
2. **Its form.** The member must follow §8.

The server then confirms the outcome (rule 11):
- the first outcome of a command, from the agent it was handed to, is kept;
- a later one, one from another agent, and one for a command the server does not know are
  confirmed and change nothing;
- the replacements of rule 11 are the one exception.

## 7. The command

The exact bytes of a command are one JSON object:

```json
{
  "protocol": 1,
  "id": "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6",
  "agent": "3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b",
  "base": "accounting",
  "mode": "write",
  "callerKey": "invoice-2026-11-20-17",
  "script": "Result = Parameters[\"date\"];",
  "parameters": { "date": "2026-11-20" },
  "issued": "2026-11-20T10:15:00.000Z",
  "deadline": "2026-11-20T10:15:30.000Z",
  "skewMs": 120000,
  "outcomeLimit": 16777216
}
```

| Member | Rule |
| --- | --- |
| `protocol` | This version, 1 |
| `id`, `agent` | Identities |
| `base` | A base key by the rule of names |
| `mode` | `read` or `write` |
| `callerKey` | A caller key by the rule of names; required for `write`, `null` for a Read without one |
| `script` | By the rule of scripts |
| `parameters` | An object, `{}` when there are none, of at most 64 levels |
| `issued`, `deadline` | Times. `deadline` is the take deadline, after `issued` by 1 second to 1 hour |
| `skewMs` | The server's `ClockSkew` when the command was made, from 1 000 to 3 600 000. The server judges rule 7 for this command by this value, not by its current option |
| `outcomeLimit` | The server's `OutcomeSizeLimit` when the command was made, from 65 536 to 67 108 864 |

**The agent:**
1. verifies the signature over the bytes with the pinned key;
2. reads `protocol` and `id` first. A command of another version is refused with
   `protocol-version` under that `id`, and the rest of its bytes are not read;
3. reads the rest;
4. applies the checks of concept rule 1.

A command whose signature does not verify, or whose verified bytes do not yield a
`protocol` and an `id`, is only recorded in the audit log, with the code `signature` or
`form`. Nothing in it can be trusted, so nothing is reported for it (concept rule 1).

**Size.** The size of a command, which `CommandSizeLimit` bounds when the caller makes it,
is the length of its `script` and its `parameters` as they are written in its bytes: their
UTF-8 text with every escape of §2 counted as written. One command of the largest
`CommandSizeLimit`, 16 MiB, therefore fits a poll answer (§10).

## 8. The outcome object

```json
{
  "kind": "failed",
  "code": "script-error",
  "result": null,
  "errorText": "Failed to call the context method (Write)",
  "messages": [ "The field \"Counterparty\" is not filled in" ],
  "messagesTrimmed": false
}
```

| Member | Rule |
| --- | --- |
| `kind` | `succeeded`, `failed`, `refused` or `unknown`. Only the server sets Expired and Unavailable |
| `code` | `null` for `succeeded`; otherwise a code that an agent may send for that kind, below |
| `result` | Present only for `succeeded`: any JSON value, `null` included, of at most 64 levels. `null` otherwise |
| `errorText` | The text of the error 1C raised, for `failed` with `script-error`; `null` otherwise |
| `messages` | The user messages of 1C, an array of strings, for `succeeded` and `failed`; empty for `refused` and `unknown` |
| `messagesTrimmed` | `true` when the executor trimmed the messages or the error text to fit the limit |

**The codes an agent may send:**

| Kind | Codes |
| --- | --- |
| `failed` | `script-error`, `result-type`, `result-too-large`, `run-limit`, `lost` |
| `refused` | `not-served`, `not-addressed`, `protocol-version`, `expired`, `clock-skew` |
| `unknown` | `run-limit`, `agent-stopped`, `transaction-ended`, `result-too-large`, `result-type` |

Every other code is the server's, and an outcome that carries one is refused with `form`.

**What the executor may hand the agent.** Only `succeeded`, `failed` with `script-error`,
`result-type` or `result-too-large`, or `unknown` with `transaction-ended`.
- **The agent checks it by this section before it sends it.** Anything else the executor
  hands it, or anything it cannot read, becomes Unknown `result-type` for a Write and
  Failed `result-type` for a Read.
- **Strings an outcome cannot carry.** No string of an outcome holds U+0000 or a lone
  surrogate (§2). A result that holds one fails with `result-type`, before the commit of a
  Write. In messages and in the error text, each such character is replaced with U+FFFD.

**The size of an outcome** is the length in bytes of the UTF-8 text of this object, exactly
as it travels, every escape counted as it is written. Every side measures the same bytes:
- the executor writes the object and measures it, before the commit of a Write (rule 5);
- the agent forwards those bytes unchanged, and writes the outcomes of its own, such as
  `refused` or `unknown`, by the same rules;
- the server measures the raw text of the `outcome` member it received.

## 9. Refusals, the two unsigned answers, and lost answers

A refusal is an answer of the library with a status, a signed body, and nothing else:

```json
{ "time": "2026-11-20T10:15:00.123Z", "code": "form" }
```

A refusal of `pair` also carries `serverKey` (§6.1).

| Status | Code | When |
| --- | --- | --- |
| 401 | `signature` | A header of §4 is missing or malformed, the agent is unknown, or the request's signature does not verify |
| 403 | `revoked` | The agent is revoked (rule 13) |
| 403 | `token` | `pair` only: as §6.1 says |
| 400 | `form` | The body breaks §2 or the rules of its message |
| 413 | `result-too-large` | An `outcome` beyond its limit (§6.3) |

**The order of the server's checks:**
1. the version of the path;
2. the size of the body;
3. the headers of §4;
4. whether the agent is known and not revoked;
5. for `pair`, the form of the body and then the checks of §6.1. For `poll` and `outcome`,
   the signature, then the form, then the checks of §6.

The first check that fails decides the answer.

**The two unsigned answers:**
- **A bare HTTP 413**: a 413 without an `Outpost-Signature` that verifies, whatever its
  body. Kestrel and every ingress send one for a body beyond their limit, and so does the
  library for a body beyond the limits of §10, before it has read the body.
- **The answer to an unsupported version**: to a path whose version the server does not
  speak, it answers `400` with the body `{"code":"protocol-version","versions":[1]}`, not
  signed. This answer keeps this form in every later version of the protocol. A request
  `{prefix}/v1/{other}` for any other request name is not part of the protocol, and gets
  whatever the application answers, usually a 404.

**A lost answer** is any of these:
- no answer within the agent's wait (§6.2);
- an error of the connection;
- a redirect: the agent follows none;
- an unsigned answer other than the two above;
- an answer whose signature does not verify;
- a verified answer that breaks §2 or the rules of its message.

The agent acts on nothing in a lost answer.

**What the agent does:**
- **A poll lost or refused:** it records `connection` with `state` `lost` and the code as
  `reason`, or `unreachable` for a lost answer. It tries again with its growing delay.
- **An outcome lost:** it sends it again after its growing delay.
- **An outcome refused with `result-too-large` or `form`, or answered with a bare 413:** it
  replaces it once, as concept rule 11 says, records a second `ended`, and sends the
  replacement. After that, any refusal of that command's outcome counts as lost.
- **`revoked`:** it keeps trying at its longest delay, since a new pairing is the
  administrator's step.
- **A refusal of `pair`, or a lost answer to it:** the pairing ends, and the console reports
  the code, or that no answer came. A lost answer can be fetched again by repeating the
  same request (§6.1).
- **An unsupported version:** the agent records `connection` with the reason
  `protocol-version`, and tries again at its longest delay.

## 10. Limits

| What | Limit |
| --- | --- |
| A `pair` body | 16 KiB |
| A `poll` body | 1 MiB |
| An `outcome` body | `OutcomeSizeLimit` plus 4 KiB. The endpoint sets this limit, so that Kestrel's default never applies |
| A poll answer | 64 MiB. The agent reads no more, and the server fills an answer only up to it, always with at least one command when one is due |
| Any other answer | 64 KiB |
| Nesting | 72 for a message; 64 for parameters and for a result, from their own outer value |
| Bases in a poll | 256 |
| Entries of `received` | 10 000 |
| A poll number | 1 to 2⁶² |

- A command of the largest `CommandSizeLimit`, 16 MiB counted as §7 says, is under
  22 MiB as base64url, so it always fits a poll answer.
- Lowering `OutcomeSizeLimit` while commands are taken can refuse an outcome that is within
  its own command's limit; that outcome then ends as rule 11 says.
- Each limit is refused one byte beyond it and accepted at it.

## 11. Addresses and TLS

- **`http://`** is accepted only when the host of the address is written as `localhost`, as
  an address in `127.0.0.0/8`, or as `::1`. It is judged on the text of the address, never
  by resolving a name, and such an address is never reached through the proxy.
- **Every other address is `https://`.** The agent validates the server's certificate as the
  system does, and never turns that validation off. Confidentiality rests on TLS (concept §3).

## 12. The console's check of the server

The console never polls, since a poll of its own would become the newest and take the
service's place (rule 10).
- It sends an `outcome` for a command identity it has just made up, with the outcome
  `{"kind":"refused","code":"not-addressed","result":null,"errorText":null,"messages":[],"messagesTrimmed":false}`.
- The server confirms it and keeps nothing (§6.3).
- A signed confirmation shows that the server is reachable and accepts the agent's key. Its
  `time` gives the skew between the clocks.

## 13. What stops a replay

| Replayed | Stopped by |
| --- | --- |
| `pair` | The token works once. A repeat by the same key gets only the answer it already had |
| `poll` | A number not greater than the newest is older and takes nothing, and so is a poll held back longer than the skew (rule 10) |
| `outcome` | Only the first outcome of a command counts (rule 11) |
| An answer | Its signature covers the agent and the hash of the one request it answers |
| A command | The audit log and the take deadline with the skew the command carries (rules 1, 3, 7) |
