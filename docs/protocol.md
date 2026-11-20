# Moltaro.OnecOutpost — protocol v1

The wire protocol between an agent and a server. The [concept](concept.md) fixes its
principles (§3 "Pairing", §4, §6, D1, D4, D5, D15); this document fixes the bytes.

**Status.** Draft of Epic 1, sprint S2, written after the sprint's study. It is public
contract: the owner approves it before the contract step of S2. No code reads or writes it
yet.

## 1. Overview

- **Transport.** HTTPS, `POST` only, JSON bodies. The agent makes every request; the server
  only answers. An agent accepts an `http://` address of the server only when its host is a
  loopback address, and requires `https://` everywhere else.
- **Paths.** `{prefix}/{version}/{request}`:
  - `{prefix}` is the one the application gave `MapOutpostAgents`;
  - `{version}` is `v1`;
  - `{request}` is `pair`, `poll` or `outcome`.

  A path with another version is answered with the refusal `protocol-version` (§9), never
  with a routing 404.
- **Signatures.** Every request is signed by the agent's key, and every answer by the
  server's key (§3). The one answer that the agent acts on without a signature is a bare
  HTTP 413 to an `outcome` request (concept §6): an ingress or the web server sends it before
  the library sees the request.
- **One poll at a time.** An agent has at most one poll open. `pair` comes once in its life,
  before any poll. `outcome` requests come whenever an outcome is ready.

## 2. Encodings

- **JSON.** RFC 8259 text in UTF-8 without a byte order mark.
  - A writer escapes only `"`, `\` and the control characters U+0001 to U+001F, and writes
    every other character as it is.
  - A reader accepts every escape of RFC 8259.
  - A body has exactly the members this document lists for it. A missing, unknown or
    duplicated member, or a value of the wrong type, refuses the message with `form`.
  - The order of members carries no meaning.
- **Nesting.** Every message is read with a maximum depth of 72. Parameters and results
  are also checked on their own:
  - at most 64 levels each, counted from their own outer value, an object or an array at
    the top being level 1 (Q5);
  - the envelope that carries them does not count against them, so a command that
    `ReadAsync` or `WriteAsync` accepted is never refused for its nesting;
  - a result that nests deeper fails its command with `result-type` (concept rule 5), before
    the commit of a Write.
- **Identities** of agents and commands are GUIDs, written as 36 lowercase characters in the
  form `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`, in JSON and in headers alike.
- **Times** are UTC, written as exactly `yyyy-MM-ddTHH:mm:ss.fffZ`.
- **Durations** are whole milliseconds, as JSON integers, in members whose names end with
  `Ms`.
- **Binary values** are base64url without padding (RFC 4648 §5).
- **Public keys** are the DER `SubjectPublicKeyInfo` of an ECDSA key on the curve P-256
  (`ECDsa.ExportSubjectPublicKeyInfo`), as base64url. A key of another curve or algorithm
  is refused with `form`.
- **Fingerprints.** The fingerprint of a key is `sha256:` followed by the 64 lowercase
  hexadecimal digits of SHA-256 over its DER `SubjectPublicKeyInfo`. The key is pinned, not
  a certificate.
- **Signatures** are ECDSA P-256 with SHA-256 over the signing input of §3, as the 64 bytes
  `r ‖ s` (IEEE P1363), in base64url. Because ECDSA signatures are malleable, a signature
  is never used as an identity or as a key of deduplication.

## 3. What is signed

Every signing input is an ASCII string of fields separated by single spaces. Every field
has a fixed form, so the framing cannot be read two ways:
- the kinds are the words below;
- the identities are 36 characters;
- the hashes are 64 lowercase hexadecimal digits;
- the status is three digits.

| Signed | By | Signing input |
| --- | --- | --- |
| A request | The agent's key | `outpost/1 request {kind} {agent} {bodyHash}` |
| An answer | The server's key | `outpost/1 answer {kind} {status} {requestHash} {bodyHash}` |
| A command | The server's key | `outpost/1 command {bodyHash}` |

- `{kind}` is `pair`, `poll` or `outcome`.
- `{agent}` is the identity the request names in `Outpost-Agent`.
- `{bodyHash}` is SHA-256 over the exact bytes of the body, or of the command.
- `{status}` is the HTTP status of the answer.
- `{requestHash}` is SHA-256 over the exact bytes of the body of the request that the
  answer answers. An answer therefore fits only the one request it was made for, and a
  poll body carries the poll number and the agent's time.
- A request or an answer whose body is empty hashes the empty string.

The receiver verifies the exact bytes it received, and only then parses them. A sender
signs once and sends exactly the bytes it signed; it never serializes again after signing.

**The pairing proof** is an HMAC-SHA256, keyed by the 16 bytes of the token's secret, over
`outpost/1 pair-proof {agent} {keyHash}`:
- `{keyHash}` is SHA-256 over the DER `SubjectPublicKeyInfo` of the agent's key, as the
  request carries it;
- the server compares the proof in constant time (`CryptographicOperations.FixedTimeEquals`).

## 4. Headers

| Header | In | Value |
| --- | --- | --- |
| `Content-Type` | Every request and answer with a body | `application/json` |
| `Outpost-Agent` | Every request | The agent's identity; in `pair`, the identity the token names |
| `Outpost-Signature` | Every request and every answer of the library | The signature of §3 |

A request without `Outpost-Agent` or `Outpost-Signature` is refused with `signature`.

## 5. The pairing token

`outpost1.{agent}.{secret}.{fingerprint}`, for example (shortened):

```text
outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.q3Vx8Kd2mP0sT1uW4yZ6bA.4c1f…e9a0
```

- `outpost1` names this form.
- `{agent}` is the identity of the pending agent that the token is for.
- `{secret}` is 16 random bytes as base64url: 22 characters, 128 bits.
- `{fingerprint}` is the 64 hexadecimal digits of the server key's fingerprint, without
  `sha256:`.

The parts are separated by `.`. A token with another prefix, another number of parts, or a
part of another form is not a token, and the console refuses it before it sends anything.

## 6. The requests

### 6.1 `pair`

Signed with the agent's new key; `Outpost-Agent` is the identity in the token.

```json
{
  "agentVersion": "0.1.0",
  "key": "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…",
  "proof": "u3Fq…"
}
```

**What the server checks, in this order:**
1. The token names a pending agent; it is not used, and has not expired by the clock of the
   node.
2. The proof is right for the token's secret, the identity and `key`.
3. The request's signature verifies with `key`.

Then, in one change of the store, it registers `key` for the agent and deletes the token's
secret.

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

Refusals: `token` for a token that names no pending agent, is used or expired, or has a
wrong proof. The one code says nothing about which of these it was. `signature` for a
signature that does not verify with `key`.

### 6.2 `poll`

```json
{
  "number": 638680032001230000,
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

- `number` is the poll number: a 64-bit signed integer from 1 to 2⁶³ − 2. The agent takes
  it from its clock (UTC ticks), always above its previous one, and above the `newest`
  that an answer to an older poll named (concept rule 10).
- `time` is the agent's time (concept rule 7).
- `bases` declares what the local policy serves:
  - at most 256 bases, with keys by the rule of names and unique;
  - `read` is required, and `write` is present only when the policy opens writing;
  - `free` is the number of free workers of the mode, from 0 to 8;
  - `runLimitMs` is from 1 000 to 86 400 000.
- `received` lists, without repetition, the commands the agent received whose outcome the
  server has not confirmed (rule 11). At most 10 000 entries; an agent that would have more
  is beyond what this version carries.

**Answer `200`**, signed:

```json
{
  "time": "2026-11-20T10:15:01.456Z",
  "older": false,
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
  free modes, or until `pollTimeoutMs`. Then it answers with the commands, or with none.
  `older` is `false` and `newest` is `null`.
- **The answer to an older poll** comes at once:
  - `older` is `true`;
  - `newest` is the newest number the server recorded for the agent, below 2⁶³ − 1;
  - `commands` is empty;
  - the poll has changed nothing in the store (rule 10).

  An agent that receives a `newest` of 2⁶³ − 1 treats the answer as lost.
- **`bases`** gives what the agent holds and the keys it declared that another agent holds
  (rule 14), as of the moment of the answer.
- **`commands`**: each holds the exact bytes of a command (§7) and the server's signature
  over them. The server puts in as many as fit in the limit of a poll answer (§10), and
  always at least one when one is due.

### 6.3 `outcome`

```json
{
  "command": "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6",
  "durationMs": 1830,
  "outcome": { "kind": "succeeded", "code": null, "result": { "rows": 3 }, "errorText": null, "messages": [], "messagesTrimmed": false }
}
```

- `outcome` is the outcome object of §8, exactly as the executor or the agent wrote it. The
  agent places its bytes into the body unchanged.

**Answer `200`**, signed:

```json
{ "time": "2026-11-20T10:15:02.789Z", "confirmed": "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6" }
```

The server confirms every outcome it is sent (rule 11):
- the first outcome of a command, from the agent it was handed to, is kept;
- a later one, one from another agent, and one for a command the server does not know are
  confirmed and change nothing;
- the replacements of rule 11 are the one exception.

An outcome whose `outcome` member is larger than the outcome limit of its command is refused
with `result-too-large`. For a command the server does not know, `OutcomeSizeLimit` is the
limit. A bare 413 from anything on the path has the same effect on the agent (concept rule
11).

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

- `protocol` is this version: the agent refuses another with `protocol-version`
  (concept rule 1).
- `mode` is `read` or `write`.
- `callerKey` is `null` for a Read without one.
- `parameters` is always an object, `{}` when there are none.
- `deadline` is the take deadline.
- `skewMs` is the server's `ClockSkew`.
- `outcomeLimit` is the server's `OutcomeSizeLimit`.

**The agent:**
1. verifies the signature over the bytes with the pinned key;
2. only then parses them;
3. only then applies the checks of concept rule 1.

A command whose signature does not verify, or whose verified bytes do not parse, is only
recorded in the audit log: nothing in it can be trusted, its identity included, so nothing
is reported for it (concept rule 1).

**Size.** The size of a command, which `CommandSizeLimit` bounds when the caller makes it
(the public contract, "Sizes"), is the UTF-8 length of its script plus that of its
parameters written as JSON. Neither is escaped beyond §2. The bytes of the command are a
little longer, and §10 bounds them.

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
| `kind` | `succeeded`, `failed`, `refused` or `unknown`. The server alone sets Expired and Unavailable |
| `code` | `null` for `succeeded`; otherwise a code of `OutpostCodes` that fits the kind |
| `result` | Present only for `succeeded`: any JSON value, `null` included, of at most 64 levels. `null` otherwise |
| `errorText` | The text of the error 1C raised, for `failed` with `script-error`; `null` otherwise |
| `messages` | The user messages of 1C, an array of strings, possibly empty |
| `messagesTrimmed` | `true` when the executor trimmed the messages or the error text to fit the limit |

**The size of an outcome** is the length in bytes of the UTF-8 text of this object, exactly
as it travels, every escape counted as it is written. Every side measures the same bytes:
- the executor writes the object and measures it, before the commit of a Write (rule 5);
- the agent forwards those bytes unchanged, and writes outcomes of its own, such as
  `refused` or `unknown`, by the same rules;
- the server measures the raw text of the `outcome` member it received.

## 9. Refusals

A refusal is an answer of the library with a status, a body and a signature:

```json
{ "time": "2026-11-20T10:15:00.123Z", "code": "form" }
```

| Status | Code | When |
| --- | --- | --- |
| 400 | `protocol-version` | The path names a version other than `v1` |
| 400 | `form` | The body is not JSON, exceeds the nesting of §2, or has a member missing, unknown, duplicated, of the wrong type or outside its rule |
| 401 | `signature` | A header of §4 is missing, the agent is unknown, or the request's signature does not verify |
| 403 | `token` | `pair` only: as §6.1 says |
| 403 | `revoked` | The agent is revoked (rule 13) |
| 413 | `size` | A `pair` or `poll` body beyond its limit |
| 413 | `result-too-large` | An `outcome` beyond its limit (§6.3) |

What the agent does with a refusal:
- **`result-too-large`, or a bare 413 to an `outcome`:** it replaces the outcome as concept
  rule 11 says, and sends the replacement.
- **Any other refusal of a poll or an outcome:** it records `connection` with the reason
  `lost`, and tries again with its growing delay.
- **`revoked`:** it keeps trying at its longest delay, since a new pairing is the
  administrator's step.
- **A refusal of `pair`:** it ends the pairing, and the console reports the code.

## 10. Limits

| What | Limit |
| --- | --- |
| A `pair` body | 16 KiB |
| A `poll` body | 1 MiB |
| An `outcome` body | `OutcomeSizeLimit` plus 4 KiB; the endpoint sets this limit, so that Kestrel's default never applies |
| A poll answer | 64 MiB. The agent reads no more; the server fills an answer only up to it |
| Any other answer | 64 KiB |
| Nesting | 72 for a message; 64 for parameters and for a result, from their own outer value |
| Bases in a poll | 256 |
| Entries of `received` | 10 000 |
| A poll number | 1 to 2⁶³ − 2 |

One command of the largest `CommandSizeLimit`, 16 MiB, fits a poll answer even when escapes
double its script and base64url makes it a third longer. Each limit is refused one byte
beyond it and accepted at it.

## 11. The console's check of the server

The console never polls, since a poll of its own would become the newest and take the
service's place (rule 10).
- It sends an `outcome` for a command identity it has just made up, with the outcome
  `{"kind":"refused","code":"not-addressed","result":null,"errorText":null,"messages":[],"messagesTrimmed":false}`.
- The server confirms it and keeps nothing (§6.3).
- A signed confirmation shows that the server is reachable and accepts the agent's key. Its
  `time` gives the skew between the clocks.

## 12. What stops a replay

| Replayed | Stopped by |
| --- | --- |
| `pair` | The token works once |
| `poll` | A number not greater than the newest is older and takes nothing (rule 10) |
| `outcome` | Only the first outcome of a command counts (rule 11) |
| An answer | Its signature covers the hash of the one request it answers |
| A command | The audit log and the take deadline with the skew (rules 1, 3, 7) |
