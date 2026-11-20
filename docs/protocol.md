# Moltaro.OnecOutpost — protocol v1

The wire protocol between an agent and a server. The [concept](concept.md) fixes its
principles (§3 "Pairing", §4, §6, D1, D4, D5, D15); this document fixes the bytes.

**Status.** Draft of Epic 1, sprint S2, written after the sprint's study. It is public
contract: the owner approves it before the contract step of S2. No code reads or writes it
yet.

## 1. Overview

- **Transport.** HTTPS, `POST` only, JSON bodies. The agent makes every request; the server
  only answers. Addresses and TLS are in §11.
- **Paths.** `{prefix}/{version}/{request}`:
  - `{prefix}` is the one the application gave `MapOutpostAgents`;
  - `{version}` is `v1`;
  - `{request}` is `pair`, `poll` or `outcome`.
- **Signatures.** Every request is signed by the agent's key, and every answer that the
  agent acts on is signed by the server's key (§3). The one exception is a bare HTTP 413
  to an `outcome` (§9, concept §6).
- **Order.** An agent has at most one poll open. `pair` comes before any poll; `outcome`
  requests come whenever an outcome is ready.

## 2. Encodings

- **No content coding.** The agent sends no `Accept-Encoding` and no `Content-Encoding`. The
  server never compresses an answer, and refuses a request with `Content-Encoding`. What is
  signed and hashed is the bytes of a body as it is.
- **JSON of the protocol.** These rules bind the codec of this library. The outcome object
  that the executor writes keeps its own escapes, and is measured as it is written (§8).
  - RFC 8259 text in UTF-8, without a byte order mark and without white space between
    tokens.
  - A writer escapes `"` as `\"`, `\` as `\\`, and the control characters U+0001 to U+001F:
    as `\b`, `\t`, `\n`, `\f` and `\r` where those exist, and otherwise as `\u00XX` with two
    uppercase hexadecimal digits. It writes every other character as it is.
  - A reader accepts every escape of RFC 8259.
  - No string of the protocol holds U+0000 or a lone surrogate, and a reader refuses one.
  - A message has exactly the members this document lists for it, each once. A reader
    refuses:
    - a missing, unknown or duplicated member, the names compared after unescaping;
    - a member that is `null` where this document does not allow it;
    - a value of the wrong type.

    Duplicated names are refused at every depth, inside parameters and results too.
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
- **Binary values** are base64url without padding (RFC 4648 §5). A reader refuses any other
  form, non-zero padding bits included.
- **Public keys** are the 91-byte DER `SubjectPublicKeyInfo` of an ECDSA key on the named
  curve P-256 with an uncompressed point, as `ECDsa.ExportSubjectPublicKeyInfo` writes it,
  in base64url. Any other key, or one with bytes beyond its structure, is refused.
- **Fingerprints.** The fingerprint of a key is the 64 lowercase hexadecimal digits of
  SHA-256 over its 91 bytes. The key is pinned, not a certificate.
- **Signatures** are ECDSA P-256 with SHA-256 over the signing input of §3, as the 64 bytes
  `r ‖ s` (IEEE P1363): 86 base64url characters. Because ECDSA signatures are malleable, a
  signature is never used as an identity or as a key of deduplication.
- **Agent versions** are 1 to 64 characters, each one of `A–Z a–z 0–9 . - +`.

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
- `{requestHash}` is SHA-256 over the exact bytes of the request's body followed by the 64
  bytes of the request's signature. An answer therefore fits only the one request it was
  made for, signature included: a request whose signature was changed on the path gets an
  answer that its agent cannot verify.

**Verify, then read.** A receiver verifies the exact bytes it received, and only then
parses them. A sender signs once and sends exactly the bytes it signed. There are two
exceptions, both in pairing:
- the server reads a `pair` request before it verifies it, because the key to verify it
  with is in the body. The body is at most 16 KiB;
- the agent reads the answer to `pair` before it verifies it, because the server's key is
  in the answer. It checks that key against the token's fingerprint first.

**The pairing proof** is an HMAC-SHA256, keyed by the 16 bytes of the token's secret, over
`outpost/1 pair-proof {agent} {keyHash}`:
- `{keyHash}` is SHA-256 over the 91 bytes of the agent's key, as 64 lowercase hexadecimal
  digits;
- the proof is 32 bytes;
- the server compares it in constant time (`CryptographicOperations.FixedTimeEquals`).

## 4. Headers

Each header occurs once.

| Header | In | Value |
| --- | --- | --- |
| `Content-Type` | Every request and answer with a body | Exactly `application/json` |
| `Outpost-Agent` | Every request | The agent's identity; in `pair`, the identity that the token names |
| `Outpost-Signature` | Every request, and every signed answer | The signature of §3 |

The agent sends no credentials to the server and ignores `WWW-Authenticate`.

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
{"agentVersion":"0.1.0","key":"MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE…","proof":"u3Fq…"}
```

**What the server checks.** It reads the body, then checks that:
- the token names a pending agent, is unused, and has not expired by the clock of the node;
- the proof is right for the token's secret, the identity and `key`;
- the request's signature verifies with `key`.

If any check fails, the answer is the refusal `token`, whichever check it was. If they all
pass, one conditional change of the store registers `key` and deletes the secret. The
change holds only if the token is still unused at that moment. Of two pairings racing on
one token, one wins and the other gets `token`.

**Answer `200`**, signed with the server's key:

```json
{"time":"2026-11-20T10:15:00.123Z","agent":"3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b","key":"MFkw…","serverKey":"MFkw…","pollTimeoutMs":25000,"leaseGraceMs":10000}
```

**What the agent checks**, any mismatch ending the pairing:
- the fingerprint of `serverKey` equals the token's;
- the answer's signature verifies with `serverKey`;
- `agent` and `key` are what it sent.

Then it pins `serverKey`. The console writes `pollTimeoutMs` and `leaseGraceMs` into
`agent.json`, so that the first poll is bounded.

**Anything other than such an answer ends the pairing.** The console reports the status,
and the code if the body has one. A refusal of `pair` is not signed and proves nothing.
- A pairing whose answer was lost leaves the agent paired on the server but not on its
  machine. The application revokes it and creates a new token.

### 6.2 `poll`

```json
{"number":639307665001230000,"time":"2026-11-20T10:15:00.123Z","agentVersion":"0.1.0","bases":[{"key":"accounting","read":{"free":1,"runLimitMs":600000},"write":{"free":0,"runLimitMs":600000}}],"received":["9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6"]}
```

- **`number`** is the poll number, from 1 to 2⁶².
  - The agent takes it from its clock (UTC ticks), always above its previous one, and above
    the `newest` that an answer to an older poll named (concept rule 10).
  - Numbers taken from a clock stay far below 2⁶².
- **`time`** is the agent's time (concept rule 7).
- **`bases`** declares what the local policy serves:
  - at most 256 bases, possibly none, with keys by the rule of names and unique;
  - `read` is required. `write` is present only when the policy opens writing;
  - `free` is the number of free workers of the mode, from 0 to 8;
  - `runLimitMs` is from 1 000 to 86 400 000.
- **`received`** lists, without repetition, every command the agent received whose outcome
  the server has not confirmed (rule 11), refused ones included.
  - It holds at most 10 000 entries, and an agent never leaves one out.
  - The sum of `free` over all modes is at most the room left in the list, so that no
    answer can overfill it.

**The server's checks of a poll:**
- **An older poll.** A poll whose number is not greater than the newest that the server
  recorded for its agent is older, an exact replay included. It changes nothing in the
  store (rule 10).
- **Any other poll** records the agent's lease, bases and list. It takes at most `free`
  commands per mode, oldest first (rule 8). A command handed out again under rule 11 counts
  against `free` like any other.

**Answer `200`**, signed:

```json
{"time":"2026-11-20T10:15:01.456Z","newest":null,"pollTimeoutMs":25000,"leaseGraceMs":10000,"bases":{"held":["accounting"],"claimed":[]},"commands":[{"bytes":"eyJwcm90b2NvbCI6MSwi…","signature":"q3Vx…"}]}
```

- **The ordinary answer.** The server holds the poll until a command is due for one of its
  free modes, or until `pollTimeoutMs`. Then it answers with the commands, or with none, and
  `newest` is `null`.
- **The answer to an older poll** comes at once, with no commands. `newest` is the newest
  number recorded for the agent, from 1 to 2⁶².
- **`bases`** gives, as of the moment of the answer, the keys the agent holds and the keys
  it declared that another agent holds (rule 14). Every key that the newest poll declared is
  in exactly one of the two.
- **`commands`**: each holds the exact bytes of a command (§7) and the server's signature
  over them, at most `free` per mode.
- **`pollTimeoutMs`** and **`leaseGraceMs`** are 1 000 to 300 000 each, the ranges of the
  server's options.
- **The agent's wait** for an answer is `pollTimeoutMs` plus `leaseGraceMs`, from
  `agent.json` or from the last answer.

### 6.3 `outcome`

```json
{"command":"9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6","outcome":{"kind":"succeeded","code":null,"result":{"rows":3},"errorText":null,"messages":[],"messagesTrimmed":false}}
```

- `outcome` is the outcome object of §8, exactly as the executor or the agent wrote it. The
  agent places its bytes into the body unchanged.
- The agent's wait for an answer is the `requestTimeout` of its local policy.

**Answer `200`**, signed:

```json
{"time":"2026-11-20T10:15:02.789Z","confirmed":"9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6"}
```

`confirmed` is the request's `command`.

**What the server checks**, after the signature:
1. **The body by §2.**
2. **The size.** An `outcome` member larger than the outcome limit of its command is
   refused with `result-too-large`. For a command the server does not know, the limit is
   `OutcomeSizeLimit`.
3. **The `outcome` member by §8**, for the mode of its command. For a command the server
   does not know, any mode that allows the outcome will do.

The server then confirms the outcome (rule 11):
- the first outcome of a command, from the agent it was handed to, is kept;
- a later one, one from another agent, and one for a command the server does not know are
  confirmed and change nothing;
- the replacements of rule 11 are the one exception.

## 7. The command

The exact bytes of a command are one JSON object, written compactly. It is shown indented
here:

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
| `protocol` | 1, this version. Every later version keeps `protocol` and `id` in this form |
| `id`, `agent` | Identities |
| `base` | A base key by the rule of names |
| `mode` | `read` or `write` |
| `callerKey` | A caller key by the rule of names; required for `write`, `null` for a Read without one |
| `script` | By the rule of scripts |
| `parameters` | An object, `{}` when there are none, of at most 64 levels |
| `issued`, `deadline` | Times. `deadline` is the take deadline, after `issued` by 1 second to 1 hour |
| `skewMs` | The server's `ClockSkew` when the command was made, from 1 000 to 3 600 000. The server judges rule 7 for this command by this value, not by its current option |
| `outcomeLimit` | The server's `OutcomeSizeLimit` when the command was made, from 65 536 to 67 108 864 |

**What the agent does with a command:**
1. It verifies the signature over the bytes with the pinned key.
2. It reads `protocol` and `id`.
   - A command of another version is refused with `protocol-version` under that `id`, and
     the rest of its bytes are not read.
   - Bytes that do not verify, or that do not yield a `protocol` and an `id`, are only
     recorded in the audit log, with the code `signature` or `form`. Nothing in them can be
     trusted, so nothing is reported for them (concept rule 1).
3. It reads the rest. A verified command of this version that breaks this section does not
   run, and is refused with `malformed`.
4. It applies the checks of concept rule 1.

**Size.** The size of a command, which `CommandSizeLimit` bounds when the caller makes it, is
the length of two pieces of its bytes:
- the text of `script` between its quotes;
- the text of `parameters`.

Both are counted as UTF-8 with every escape of §2 counted as written. One command of the
largest `CommandSizeLimit`, 16 MiB, therefore fits a poll answer (§10).

## 8. The outcome object

```json
{"kind":"failed","code":"script-error","result":null,"errorText":"Failed to call the context method (Write)","messages":["The field \"Counterparty\" is not filled in"],"messagesTrimmed":false}
```

| Member | Rule |
| --- | --- |
| `kind` | `succeeded`, `failed`, `refused` or `unknown`. Only the server sets Expired and Unavailable |
| `code` | `null` for `succeeded`; otherwise a code the table below allows for the kind and the mode |
| `result` | Present only for `succeeded`: any JSON value, `null` included, of at most 64 levels. `null` otherwise |
| `errorText` | The text of the error 1C raised, for `failed` with `script-error`; `null` otherwise |
| `messages` | The user messages of 1C, an array of strings, for `succeeded` and `failed`; empty for `refused` and `unknown` |
| `messagesTrimmed` | `true` when the executor trimmed the messages or the error text to fit the limit |

**The outcomes an agent may send, by mode:**

| Kind | Read | Write |
| --- | --- | --- |
| `succeeded` | Yes | Yes |
| `failed` | `script-error`, `result-type`, `result-too-large`, `run-limit`, `lost` | `script-error`, `result-type`, `result-too-large`: only after 1C rolled back |
| `refused` | `not-served`, `not-addressed`, `protocol-version`, `malformed`, `expired`, `clock-skew` | The same |
| `unknown` | None | `run-limit`, `agent-stopped`, `transaction-ended`, `result-too-large`, `result-type` |

Every other combination is the server's, and an outcome that carries one is refused with
`form`.

**The executor** may hand the agent only `succeeded`, `failed` with `script-error`,
`result-type` or `result-too-large`, or, for a Write, `unknown` with `transaction-ended`.
- **The agent checks it by this section before it sends it.** Anything else, or anything it
  cannot read, becomes Unknown `result-type` for a Write and Failed `result-type` for a
  Read.
- **Strings an outcome cannot carry.** No string of an outcome holds U+0000 or a lone
  surrogate. A result that holds one fails with `result-type`, before the commit of a
  Write. In messages and in the error text, each such character is replaced with U+FFFD.

**The size of an outcome** is the length in bytes of the UTF-8 text of this object, exactly
as it travels, every escape counted as it is written. Every side measures the same bytes:
- the executor writes the object and measures it, before the commit of a Write (rule 5);
- the agent forwards those bytes unchanged, and writes outcomes of its own, such as
  `refused` or `unknown`, by the rules of §2;
- the server measures the raw text of the `outcome` member it received.

## 9. Answers, refusals and lost answers

**A refusal** is an answer of the library with a status, a signed body, and nothing else:

```json
{"time":"2026-11-20T10:15:00.123Z","code":"form"}
```

| Status | Code | When |
| --- | --- | --- |
| 400 | `form` | The body breaks §2 or the rules of its message |
| 401 | `signature` | The agent is unknown, or the request's signature does not verify |
| 403 | `revoked` | The agent is revoked (rule 13) |
| 403 | `token` | `pair` only, not signed (§6.1) |
| 413 | `result-too-large` | An `outcome` beyond its limit (§6.3) |

**Answers that are not signed:**
- **A bare 401** answers a request whose `Outpost-Agent` or `Outpost-Signature` is missing,
  malformed or repeated. There is no agent for the server to sign for.
- **A bare 413** answers a body beyond the limits of §10, before the library reads it.
  Kestrel and every ingress send one too. "Bare" means without an `Outpost-Signature`
  that verifies, whatever the body.
- **A path whose version the server does not speak** is answered with `400` and the body
  `{"code":"protocol-version"}`. This form is kept by every later version. The agent never
  chooses a version from such an answer.
- A path `{prefix}/v1/{other}` with any other request name is not part of the protocol. It
  gets whatever the application answers, usually a 404.

**The order of the server's checks:**
1. the version of the path;
2. the size of the body;
3. the headers of §4;
4. for `pair`, the checks of §6.1. For `poll` and `outcome`:
   1. whether the agent is known;
   2. whether it is revoked;
   3. the signature;
   4. the form;
   5. the checks of §6.

The first check that fails decides the answer.

**What the agent does with an answer:**

| Request | Answer | What the agent does |
| --- | --- | --- |
| `poll` | A verified `200` | Acts on it (§6.2). An answer to an older poll makes it continue above `newest` |
| `outcome` | A verified `200` | Records `confirmed` and removes the outcome from the outbox |
| `outcome` | A verified `result-too-large`, or a bare 413 | Replaces the outcome once, as concept rule 11 says, records a second `ended`, and sends the replacement. A refusal of that replacement counts as lost |
| `pair` | A verified `200`, checked as §6.1 says | Pins the server's key |
| `pair` | Anything else | Ends the pairing (§6.1) |
| `poll`, `outcome` | Anything else | Counts it as lost |

**A lost answer** includes:
- no answer within the agent's wait;
- an error of the connection;
- a redirect, since the agent follows none;
- an answer whose signature does not verify;
- a verified answer that breaks §2 or the rules of its message;
- every refusal that the table does not name.

The agent acts on nothing in a lost answer. It records `connection` with `state` `lost`,
once per change of state, not once per attempt. The `reason` is the code of a verified
refusal, or else `unreachable`. Then it tries again with its growing delay: a poll as a new
poll, an outcome as the same bytes.

An outcome that the server cannot read never comes from an agent that checks it by §8
first. If it does, a `form` refusal makes the agent send it again, and nothing ends that
command until the agent is fixed.

## 10. Limits

| What | Limit |
| --- | --- |
| A `pair` body | 16 KiB |
| A `poll` body | 1 MiB |
| An `outcome` body | `OutcomeSizeLimit` plus 4 KiB. The endpoint sets this limit, so that Kestrel's default never applies |
| A poll answer | 64 MiB. The agent reads no more, and the server fills an answer only up to it, always with at least one command when one is due |
| Any other answer | 64 KiB, which the agent reads no more than |
| Nesting | 72 for a message; 64 for parameters and for a result, from their own outer value |
| Bases in a poll | 256 |
| Entries of `received` | 10 000 |
| A poll number | 1 to 2⁶² |

- A command of the largest `CommandSizeLimit`, 16 MiB counted as §7 says, is under
  22 MiB as base64url, so it always fits a poll answer.
- Lowering `OutcomeSizeLimit` while commands are taken can refuse an outcome that is within
  its own command's limit. That outcome then ends as rule 11 says.
- Each limit is refused one byte beyond it and accepted at it.

## 11. Addresses, TLS and the proxy

- **`http://`** is accepted only when the host of the address is written as `localhost`, as
  an address in `127.0.0.0/8`, or as `::1`. It is judged on the text of the address, never
  by resolving a name, and such an address is never reached through the proxy.
- **Every other address is `https://`.** The agent validates the server's certificate as the
  system does, and never turns that validation off. Confidentiality rests on TLS (concept
  §3).
- **The proxy** is used as `policy.json` says. The agent offers it the computer's Windows
  credentials by Kerberos only, never by NTLM, so that a proxy that is not what it claims
  cannot relay them.

## 12. The console's check of the server

The console never polls, since a poll of its own would become the newest and take the
service's place (rule 10).
- It sends an `outcome` for a command identity it has just made up, with the outcome
  `{"kind":"refused","code":"not-addressed","result":null,"errorText":null,"messages":[],"messagesTrimmed":false}`.
- The server confirms it and keeps nothing (§6.3).
- A verified confirmation shows that the server is reachable and accepts the agent's key.
  Its `time` gives the skew between the clocks.

## 13. What stops a replay, and what does not

| Replayed | Stopped by |
| --- | --- |
| `pair` | The token works once |
| `poll` | A number not greater than the newest is older and takes nothing (rule 10) |
| `outcome` | Only the first outcome of a command counts (rule 11) |
| An answer | Its signature covers the agent, and the hash of the one request it answers with that request's signature |
| A command | The audit log and the take deadline with the skew the command carries (rules 1, 3, 7) |

**What a hop on the path can still do.** Something that opens TLS between the agent and the
server can delay, drop or hold back messages. It cannot make a command run twice, run one
nobody signed, or make an outcome claim more than happened. What it can do:
- make outcomes late or Unknown;
- hold back the last poll of an agent that has gone and release it later. The server then
  takes that poll for a live one and counts the agent as connected for one more lease. A
  command created in that time ends as Unknown `disconnected`, not as Unavailable.

Dropping messages already gives such a hop the same power, so nothing more is built
against it.
