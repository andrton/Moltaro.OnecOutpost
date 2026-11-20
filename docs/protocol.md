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
- **Signatures.** Every request is signed by the agent's key. Every answer that the agent
  acts on is signed by the server's key, with one exception: a bare HTTP 413 to an
  `outcome` (§9, concept §6). Other unsigned answers exist (§9), but the agent acts on none
  of them, so they can only make something fail.
- **Order.** An agent has at most one poll open. `pair` comes before any poll; `outcome`
  requests come whenever an outcome is ready.

## 2. Encodings

- **No content coding.** The agent sends `Accept-Encoding: identity` and no
  `Content-Encoding`. The server never compresses an answer, and does not check
  `Accept-Encoding`. What is signed and hashed is the bytes of a body as it is.
- **JSON.** RFC 8259 text in UTF-8, without a byte order mark.
  - **Readers** accept every escape and insignificant white space, as RFC 8259 allows.
  - **The writers of this library** write no white space between tokens. They escape `"` as
    `\"`, `\` as `\\`, and the control characters U+0001 to U+001F: as `\b`, `\t`, `\n`,
    `\f` and `\r` where those exist, and otherwise as `\u00XX` with two uppercase
    hexadecimal digits. Every other character is written as it is. The executor in 1C is
    not such a writer: its outcome object keeps its own form and is measured as it is
    written (§8).
  - **No string of the protocol holds U+0000 or a lone surrogate.** A reader refuses one.
  - **A message has exactly the members this document lists for it, each once.** Every
    member is present, a `null` one included. A reader refuses:
    - a missing, unknown or duplicated member, the names compared after unescaping;
    - `null` where this document does not allow it;
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
  - a result that nests deeper fails its command with `result-type` (concept rule 5).
- **Identities** of agents and commands are GUIDs, written as 36 lowercase characters in the
  form `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`, in JSON and in headers alike.
- **Times** are UTC, written as exactly `yyyy-MM-ddTHH:mm:ss.fffZ`. A writer truncates
  anything finer than a millisecond.
- **Durations** are whole milliseconds, as JSON integers, in members whose names end with
  `Ms`. A writer truncates anything finer than a millisecond.
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
- **Ranges.** A member that mirrors a server option or a member of `policy.json` has the
  range of that option or member. The range lives in one pair of constants of
  `OutpostProtocol` in the protocol assembly, which the options, `AgentPolicyReader` and the
  codec all check. This document names the pair with each such member. The ranges are
  part of v1: a server that widens one would send what an agent of v1 refuses as
  `malformed` or counts as lost, so widening one needs a new version of the protocol.

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

A hop that keeps a request's body and changes its signature gets a genuine signed refusal
`signature`, bound to that body. The agent then records `signature` as the reason of a lost
answer, although its key is fine. That is the limit of this binding, and nothing more is
built against it.

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

| Header | In | Value |
| --- | --- | --- |
| `Content-Length` | Every request | The length of the body. The agent always sends it, but a hop may drop it, for example on HTTP/2, so the server does not require it (§9) |
| `Content-Type` | Every request and answer with a body | Exactly `application/json` |
| `Expect` | Every `outcome` request | `100-continue`, so that a refusal for the length comes before the agent sends the body (§9). A hop may drop it or answer it itself, as nginx and Envoy do, so the server neither requires nor checks it |
| `Outpost-Agent` | Every request | The agent's identity; in `pair`, the identity that the token names |
| `Outpost-Signature` | Every request, and every signed answer | The signature of §3 |

The server requires `Content-Type`, `Outpost-Agent` and `Outpost-Signature` in every
request, each once and in the form above. It requires no other header, and of the others
it refuses only a `Content-Encoding` (§2), so that an ingress needs nothing special
(concept D1). The agent sends no credentials to the server
and ignores `WWW-Authenticate`.

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

**What the server checks.** It reads the body by §2, then checks that:
- the token names a pending agent, is unused, and has not expired by the clock of the node;
- the proof is right for the token's secret, the identity and `key`;
- the request's signature verifies with `key`.

Any failure, a body that breaks §2 included, is answered with a bare `403` without a body,
the same for every failure. If every check passes, one conditional change of the store
registers `key` and deletes the secret. The change holds only if the token is still unused
at that moment, so of two pairings racing on one token, one wins and the other gets the
`403`.

**Answer `200`**, signed with the server's key:

```json
{"time":"2026-11-20T10:15:00.123Z","agent":"3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b","key":"MFkw…","serverKey":"MFkw…","pollTimeoutMs":25000,"leaseGraceMs":10000}
```

**What the agent checks**, any mismatch ending the pairing:
- the fingerprint of `serverKey` equals the token's;
- the answer's signature verifies with `serverKey`;
- `agent` and `key` are what it sent.

Then it pins `serverKey`. The console writes `pollTimeoutMs` and `leaseGraceMs` into
`agent.json`. They bound the first poll, and every poll then carries the values the agent
last learnt (§6.2).

**The agent's wait** for this answer is the `requestTimeout` of its local policy.

**Anything other than a verified `200` ends the pairing.** The console reports the HTTP
status together with what it saw, in its own words, for example:
- "the server's key does not match the fingerprint in the token", which points at a wrong
  server or at something that opens TLS on the path;
- "the answer's signature does not verify";
- "an unsigned answer in the form the server uses for a protocol version it does not
  speak" (§9);
- "a bare 403: the server answers so to any failure of pairing, and so can anything on
  the path".

It never shows text taken from a body it has not verified. A pairing whose answer was lost,
or replaced on the path by a bare `403`, may leave the agent paired on the server but not
on its machine; the application revokes it and creates a new token.

### 6.2 `poll`

```json
{"number":639307665001230000,"time":"2026-11-20T10:15:00.123Z","agentVersion":"0.1.0","pollTimeoutMs":25000,"bases":[{"key":"accounting","read":{"free":1,"runLimitMs":600000},"write":{"free":0,"runLimitMs":600000}},{"key":"warehouse","read":{"free":1,"runLimitMs":600000},"write":null}],"received":["9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6"]}
```

- **`number`** is the poll number, from 1 to 2⁶².
  - The agent takes it from its clock (UTC ticks), always above its previous one, and
    above the `newest` that an answer to an older poll named (concept rule 10). The
    reservation below bounds it from above while the agent runs, and from below after a
    restart.
  - **The reservation.** The agent sends only numbers below the bound it has reserved in
    its audit log:
    - before it would send a number at or above the bound, it records a new reservation,
      the entry `reservation` with its own identity and the bound `below`, that number plus
      one day of ticks (864 000 000 000). The entry is on the disk before the poll is sent
      (rule 2);
    - when it starts, it takes the highest `below` of the reservations of its identity in
      its audit log, whatever the files and times they are in. Its first number is at or
      above that bound, so the first poll records a new reservation. An agent with no
      reservation of its identity, as after pairing, records one before its first poll.

    Reservations only grow, so the highest is also the last. Every number sent after a
    restart is therefore above every number sent before it, even when the agent's clock was
    moved ahead and back meanwhile, once or many times (§13). A reservation is recorded at
    every start and about once a day while the clock runs as it should. Reservations of
    another identity, from an earlier pairing, do not count.
  - **Numbers run out only in theory.** Each start uses up at most a day of ticks, so about
    five million starts fit below 2⁶². A clock moved to the far future uses up much of the
    rest, and a server that holds the signing key could take all of it, through `newest`.
    When the next number would pass 2⁶², the agent records its reservation as for any
    number at or above its bound, and stops. At every later start that reservation leaves
    no number, so the agent records `started` with an error and stops, until it is paired
    anew, which gives it a new identity.
- **`time`** is the agent's time (concept rule 7).
- **`pollTimeoutMs`** is the poll timeout the agent last learnt, from `agent.json` or from the
  last answer, in the range of the option `PollTimeout` (`MinPollTimeout` to
  `MaxPollTimeout`). The server holds the poll for no longer than the smaller of this and
  its own option: that is the poll's **hold**. So raising the option never makes an agent's
  polls exceed its wait.
- **`bases`** declares what the local policy serves:
  - at most 256 bases, possibly none, with keys by the rule of names and unique;
  - `read` is an object. `write` is an object when the policy opens writing, and `null`
    otherwise;
  - `free` is the number of free workers of the mode, from 0 to `MaxWorkers` (8);
  - `runLimitMs` is in the range of `runLimit` of `policy.json` (`MinRunLimit` to
    `MaxRunLimit`).
- **`received`** lists, without repetition, every command the agent received whose outcome
  the server has not confirmed (rule 11), refused ones included.
  - It holds at most 10 000 entries, and an agent never leaves one out.
  - The sum of `free` over all modes is at most the room left in the list, so that no
    answer can overfill it.

**The server's checks of a poll:**
- **An older poll.** A poll whose number is not greater than the newest that the server
  recorded for its agent is older, an exact replay included. It changes nothing in the
  store (rule 10).
- **Any other poll** becomes the newest. It records the agent's bases and list, and takes at
  most `free` commands per mode, oldest first (rule 8). A command handed out again under rule
  11 counts against `free` like any other.
- **The lease.** The agent is connected while the lease of its newest poll runs (rule 6).
  That lease ends at the earlier of two moments, both judged by the node's clock:
  - the moment the poll was recorded, plus its hold, plus the lease grace;
  - its signed `time`, plus `ClockSkew`, plus its hold, plus the lease grace.

  The lease grace is the option `LeaseGrace` when the poll is recorded. What follows:
  - **Polls held back on the path.** The server cannot tell a poll held back from an agent
    whose clock runs ahead. Together, the polls of an agent that has gone keep it connected
    for at most `ClockSkew`, plus the lead the agent's clock had over the node's when it
    sent them,
    beyond the lease that its last poll would have had. They count together, not each,
    because only the newest poll grants a lease.
  - **An agent whose clock is behind** the node's by more than `ClockSkew` runs no command
    anyway (rule 1). Behind by more than `ClockSkew` plus the lease grace, its lease ends
    before its next poll comes, so it shows as connected only for part of each poll. Behind
    by more than `ClockSkew` plus the hold plus the lease grace, it never shows as
    connected, and calls for it get Unavailable.
  - **A live agent reading a large answer.** An agent that reads an answer for longer than
    the lease grace shows as not connected until its next poll, and calls in that gap get
    Unavailable.
- **A held poll that a newer poll has replaced** takes nothing (rule 10). It is answered as
  an older poll when it wakes or when its hold ends.

**Answer `200`**, signed:

```json
{"time":"2026-11-20T10:15:01.456Z","newest":null,"pollTimeoutMs":25000,"leaseGraceMs":10000,"bases":{"held":["accounting"],"claimed":[]},"commands":[{"bytes":"eyJwcm90b2NvbCI6MSwi…","signature":"q3Vx…"}]}
```

- **The ordinary answer.** The server holds the poll until a command is due for one of its
  free modes, or until its hold ends. Then it answers with the commands, or with none, and
  `newest` is `null`.
- **The answer to an older poll** comes at once, with no commands. `newest` is the newest
  number recorded for the agent, from 1 to 2⁶². The agent waits a tenth of its
  `retryDelayMax` before its next poll, so that a duplicated or retried request cannot make
  it poll in a busy loop.
- **`bases`** gives, as of the moment of the answer, the keys the agent holds and the keys
  it declared that another agent holds (rule 14). Every key that the newest poll declared is
  in exactly one of the two.
- **`commands`**: each holds the exact bytes of a command (§7) and the server's signature
  over them. An answer holds no more commands than the sum of `free` in its poll, and none
  when `newest` is not `null`; an answer that breaks either is lost. The agent checks every
  command by §7 before it records any (rule 2), and counts, by base and mode, the commands
  that passed and will run. When they are more than the `free` of a base and mode, the
  answer is lost and the agent records and runs none of its commands. A command that fails
  §7 is refused as §7 says and counts for nothing. So a command never waits on the agent
  for a worker, and `received` can never overfill.
- **`pollTimeoutMs`** and **`leaseGraceMs`** are the server's options `PollTimeout` and
  `LeaseGrace`, in their ranges (`MinPollTimeout` to `MaxPollTimeout`, `MinLeaseGrace` to
  `MaxLeaseGrace`). The agent keeps them for its next poll. The pairing answer carries them
  in the same ranges (§6.1).
- **The agent's wait.** The agent waits for the answer to begin for no longer than the poll
  timeout it sent plus the last lease grace it learnt. Then it reads the body within the
  `requestTimeout` of its local policy.

### 6.3 `outcome`

```json
{"command":"9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6","outcome":{"kind":"succeeded","code":null,"result":{"rows":3},"errorText":null,"messages":[],"messagesTrimmed":false}}
```

- `outcome` is the outcome object of §8, as §8 says the agent forwards it.
- The request carries `Expect: 100-continue` (§4).
- The agent's wait for the answer is the `requestTimeout` of its local policy.

**Answer `200`**, signed:

```json
{"time":"2026-11-20T10:15:02.789Z","confirmed":"9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6"}
```

`confirmed` is the request's `command`, and an answer with any other is lost.

**What the server checks**, after the signature:
1. **The body by §2**, all of it, the `outcome` member included: its JSON, its names and
   its strings. A body that breaks §2 is refused with `form`, whatever its size.
2. **The size.** An `outcome` member larger than the outcome limit of its command is
   refused with `result-too-large`. For a command the server does not know, the limit is
   `OutcomeSizeLimit`.
3. **The members of the outcome object by §8**, for the mode of its command. For a command
   the server does not know, either mode will do.

The server then confirms the outcome (rule 11):
- the first outcome of a command, from the agent it was handed to, is kept;
- a later one, one from another agent, and one for a command the server does not know are
  confirmed and change nothing;
- the one exception is a command whose outcome the server set itself, Unknown or Failed
  `lost`: the outcome of the agent it was handed to replaces it, as rule 11 says.

An outcome that the agent sends after a refusal for its size follows the same rules. It is
kept when the server kept nothing yet, it replaces an outcome that the server set itself,
and it changes nothing when the server already kept an outcome of the agent.

## 7. The command

The exact bytes of a command are one JSON object, written as §2 says. It is shown indented
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
| `issued`, `deadline` | Times. `deadline` is the take deadline, after `issued` by a duration in the range of `TakeTimeout` (`MinTakeTimeout` to `MaxTakeTimeout`) |
| `skewMs` | The server's `ClockSkew` when the command was made, in that option's range (`MinClockSkew` to `MaxClockSkew`). The server judges rule 7 for this command by this value, not by its current option |
| `outcomeLimit` | The server's `OutcomeSizeLimit` when the command was made, in that option's range (`MinOutcomeSizeLimit` to `MaxOutcomeSizeLimit`) |

**What the agent does with a command:**
1. It verifies the signature over the bytes with the pinned key.
2. It reads `protocol` and `id`. When the audit log already holds that `id`, the agent
   does nothing more with these bytes: the command runs once (rule 3), and its recorded
   outcome, or the refusal recorded for it, stays as it is.
   - A command of another version is refused with `protocol-version` under that `id`, and
     the rest of its bytes are not read.
   - Bytes that do not verify are only recorded in the audit log, with the code
     `signature`: nothing in them can be trusted. Verified bytes that do not yield a
     `protocol` and an `id` are only recorded, with the code `form`: they name no command
     that a refusal could answer. Nothing is reported for either (concept rule 1).
3. It reads the rest. A verified command of this version that breaks this section does not
   run, and is refused with `malformed`.
4. It applies the other checks of concept rule 1 in this order, and the first that fails
   gives the code:
   1. `not-addressed`, when `agent` is not this agent;
   2. `not-served`, when the local policy does not serve `base`, or serves it without
      `mode`;
   3. `clock-skew`, when by the agent's clock `issued` is later than `skewMs` ahead;
   4. `expired`, when by the agent's clock `deadline` passed longer than `skewMs` ago.

**Size.** The size of a command, which `CommandSizeLimit` bounds when the caller makes it, is
the length of two pieces of its bytes:
- the text of `script` between its quotes;
- the text of `parameters`, which is the 2 bytes `{}` when there are none.

Both are counted as UTF-8 with every escape counted as the writer of §2 writes it. The codec
computes this one number for both the server's check and its writer. An escaped control
character counts 6 bytes, so the number is a 64-bit integer. One command of the
largest `CommandSizeLimit`, 16 MiB, therefore fits a poll answer (§10).

## 8. The outcome object

```json
{"kind":"failed","code":"script-error","result":null,"errorText":"Failed to call the context method (Write)","messages":["The field \"Counterparty\" is not filled in"],"messagesTrimmed":false}
```

| Member | Rule |
| --- | --- |
| `kind` | `succeeded`, `failed`, `refused` or `unknown`. Only the server sets Expired and Unavailable |
| `code` | `null` for `succeeded`; otherwise a code the table below allows for the kind and the mode |
| `result` | For `succeeded`, any JSON value, `null` included, of at most 64 levels. `null` for every other kind |
| `errorText` | The text of the error 1C raised, for `failed` with `script-error`; `null` otherwise |
| `messages` | The user messages of 1C, an array of strings, for `succeeded` and `failed`; empty for `refused` and `unknown` |
| `messagesTrimmed` | `true` when the executor trimmed the messages or the error text to fit the limit; `false` for `refused` and `unknown` |

**The outcomes an agent may send, by mode:**

| Kind | Read | Write |
| --- | --- | --- |
| `succeeded` | Yes | Yes |
| `failed` | `script-error`, `result-type`, `result-too-large`, `run-limit`, `lost` | `script-error`, `result-type`, `result-too-large`: only after 1C rolled back |
| `refused` | `not-served`, `not-addressed`, `protocol-version`, `malformed`, `expired`, `clock-skew` | The same |
| `unknown` | None | `run-limit`, `agent-stopped`, `transaction-ended`, `result-too-large`, `result-type` |

Every other combination is the server's, and an outcome that carries one is refused with
`form`. A new code of an agent needs a new version of the protocol, or a server that knows
it first.

**The executor** may hand the agent only `succeeded`, `failed` with `script-error`,
`result-type` or `result-too-large`, or, for a Write, `unknown` with `transaction-ended`.
- **The executor checks its result before the commit of a Write.** A result that holds
  U+0000 or a lone surrogate, or that nests beyond 64 levels, fails with `result-type`.
- **The executor cleans, then trims, its messages and its error text** before it writes
  and measures the object. It replaces each U+0000 and each lone surrogate in them with
  U+FFFD, and then trims them to fit the limit, never inside a surrogate pair. So the text
  it measures is the text it hands over, valid Unicode without U+0000, and its UTF-8 bytes
  are the same wherever they are encoded.
- **The agent checks the executor's object by §2 and this section, and changes nothing.**
  Anything not allowed, or anything it cannot read, becomes Failed `result-type` for a Read
  and Unknown `result-type` for a Write.

**The size of an outcome** is the length in bytes of the UTF-8 text of this object, exactly
as it travels, every escape counted as it is written:
- the executor writes the object and measures its UTF-8 bytes, before the commit of a
  Write (rule 5);
- the object crosses COM as UTF-16 text, and the COM worker encodes it as UTF-8. Since the
  text is valid Unicode, these are exactly the bytes the executor measured;
- the agent forwards those bytes as they are. It writes the outcomes of its own, such as
  `refused` or `unknown`, by §2;
- the server measures the raw text of the `outcome` member it received.

## 9. Answers, refusals and lost answers

**A signed refusal** is an answer of the library with a status and a signed body:

```json
{"time":"2026-11-20T10:15:00.123Z","code":"form"}
```

| Status | Code | When |
| --- | --- | --- |
| 400 | `form` | A `poll` or `outcome` body breaks §2 or the rules of its message |
| 401 | `signature` | The request's signature does not verify |
| 413 | `result-too-large` | An `outcome` beyond its limit (§6.3) |

**Unsigned answers**, sent before the library reads the body, or for `pair`:

| Status | Body | When |
| --- | --- | --- |
| 400 | `{"code":"protocol-version"}` | The path names a version the server does not speak. Every later version keeps this form |
| 400 | None | A header breaks §4, or the request has a `Content-Encoding` |
| 401 | None | `Outpost-Agent` names no known agent, or one that is still pending and so has no key |
| 403 | None | The agent is revoked (rule 13). Anyone who knows an agent's identity can learn this |
| 403 | None | Any failure of `pair` (§6.1) |
| 413 | None | A body beyond the limits of §10. Kestrel and every ingress send one too, with headers and a body of their own, such as an HTML page |

"Bare" means without an `Outpost-Signature` that verifies, whatever the body. A path
`{prefix}/v1/{other}` with any other request name is not part of the protocol, and gets
whatever the application answers, usually a 404.

**The order of the server's checks:**
1. the version of the path;
2. the headers of §4;
3. the length against the limit of §10: the declared length before the body is read, and,
   when no length was declared, the length read so far while it is read;
4. for `pair`, the checks of §6.1. For `poll` and `outcome`:
   1. whether the agent is known and not pending;
   2. whether it is revoked;
   3. then it reads the body and checks the signature, the form and the checks of §6.

The first check that fails decides the answer. No body of a `poll` or an `outcome` is read
before its agent is known to have a key.

**The rate of requests.** The identity of an agent is not a secret. Anyone who knows one
can make a node read up to the body limit of `outcome` for every request, and the library
bounds neither how many such requests run at once nor how often they come. An application
that needs such a bound adds rate limiting to the group that `MapOutpostAgents` returns.

**A refusal for the length.** On HTTP/1.1, a server that refuses a request and closes the
connection while the client is still sending its body may cause a reset that wipes out the
answer (RFC 9112 §9.6). So an `outcome` carries `Expect: 100-continue`, and Kestrel and
common ingresses refuse a declared length beyond their limit before the agent sends the
body. The agent waits for `100 Continue` or a final answer for one second, a constant of
the agent and the default of .NET, and then sends the body anyway. A hop that ignores
`Expect`, a refusal that comes later than that second, or a hop that drops
`Content-Length` can still lose that 413. The agent then sees a lost answer and sends the
outcome again, and the outcome stays unsent
until the path is fixed, as below for an outcome the server cannot read. Such an outcome
shows only as an `ended` without its `confirmed` in the audit log: the state of the
connection follows the polls, and the console's check, with its small outcome, passes.

**What the agent does with an answer.** An answer is **verified** when:
- its `Content-Type` is exactly `application/json`;
- its body is within the read limit of §10;
- its `Outpost-Signature` verifies (§3);
- its body keeps §2 and the rules of its message. For a signed refusal that means a code of
  the table of signed refusals, under the status that the table gives it; for the answer
  to an `outcome`, a `confirmed` that is the request's `command` (§6.3).

The table decides first: only an answer that it does not name is lost.

| Request | Answer | What the agent does |
| --- | --- | --- |
| `poll` | A verified `200` | Acts on it (§6.2) |
| `outcome` | A verified `200` | Records `confirmed`, and removes the outcome from the outbox |
| `outcome` of kind `succeeded` or `failed` | A verified `result-too-large`, or a bare 413 whatever its headers and body | Replaces the outcome once, as concept rule 11 says, records a second `ended`, and sends the replacement. For the replacement, any answer but a verified `200` counts as lost |
| `pair` | A verified `200`, checked as §6.1 says | Pins the server's key |
| `pair` | Anything else | Ends the pairing (§6.1) |

**Every other answer to a `poll` or an `outcome` is lost**, for example:
- no answer, or no complete body, within the agent's wait;
- an error of the connection;
- a redirect, since the agent follows none;
- an answer that is not verified;
- a verified refusal, other than the row of `result-too-large` above.

The agent acts on nothing in a lost answer, and tries again with its growing delay: a poll
as a new poll, an outcome as the same bytes.

**What the audit log records.** The agent records `connection` when the state of its
polls changes. Only the answers to polls count, so that an outcome held up on its path
cannot make the state flap while the polls go through. The state is unknown when the agent
starts:
- `connected` is recorded with a poll answer that is not lost, when the state is not
  `connected`;
- `lost` is recorded with a lost poll answer, when the state is not `lost`.

A new reason while the state stays lost is not a change. The `reason` of `lost` is the first
of these that applies:
1. the code of a verified refusal;
2. `protocol-version` for a `400` whose body is `{"code":"protocol-version"}`;
3. `status-` and the HTTP status of any other answer whose status came, such as
   `status-403`, or `status-200` for a `200` that is not verified;
4. `unreachable` when no status came.

Recording a reason acts on nothing.

An outcome that the server cannot read never comes from an agent that checks it as §8 says.
If one does, a `form` refusal makes the agent send it again, and nothing ends that command
until the agent is fixed.

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
  its own command's limit. That outcome then ends as rule 11 says, when the refusal
  reaches the agent (§9).
- Each limit is refused one byte beyond it and accepted at it.

## 11. Addresses, TLS and the proxy

- **`http://`** is accepted only when the host of the address is written as `localhost`, as
  an address in `127.0.0.0/8`, or as `::1`. It is judged on the text of the address, never
  by resolving a name, and such an address is never reached through the proxy.
- **Every other address is `https://`.** The agent validates the server's certificate as the
  system does, without a check of revocation, and never turns that validation off. The
  guarantees rest on the pinned server key in every command and answer, not on the
  certificate. Confidentiality rests on TLS (concept §3).
- **The proxy** is used as `policy.json` says. The agent offers it the computer's Windows
  credentials by Kerberos only, never by NTLM, so that a proxy that is not what it claims
  cannot relay them. A machine outside a domain therefore reaches only a proxy that asks
  for no credentials.

## 12. The console's check of the server

The console never polls, since a poll of its own would become the newest and take the
service's place (rule 10).
- It sends an `outcome` for a command identity it has just made up, with the outcome
  `{"kind":"refused","code":"not-addressed","result":null,"errorText":null,"messages":[],"messagesTrimmed":false}`.
- The server confirms it and keeps nothing (§6.3).
- A verified confirmation shows that the server is reachable and accepts the agent's key.
  Its `time` gives the skew between the clocks.
- Any other answer is reported as its status together with the console's own account of
  what it saw, as §6.1 does, such as "the answer's signature does not verify" or "an
  unsigned answer in the form the server uses for a protocol version it does not speak".
  The console never shows text taken from a body it has not verified.

## 13. What stops a replay, and what does not

| Replayed | Stopped by |
| --- | --- |
| `pair` | The token works once |
| `poll` | A number not greater than the newest is older and takes nothing (rule 10). The agent waits before it polls again after such an answer (§6.2). The reservation keeps every number sent after a restart above every number sent before it (§6.2) |
| `outcome` | Only the first outcome of a command counts (rule 11) |
| An answer | Its signature covers the agent and the hash of the one request it answers |
| A command | The audit log and the take deadline with the skew the command carries (rules 1, 3, 7) |

**What a hop on the path can still do.** Something that opens TLS between the agent and the
server can delay, drop or hold back messages. It cannot make a command run twice, run one
nobody signed, or make an outcome claim more than happened. What it can do:
- it can make outcomes late. A taken Write then ends as Unknown `disconnected`, a taken Read
  as Failed `lost`, and a command never taken as Expired;
- it can hold back the polls of an agent that has gone. Together they keep the agent
  connected for at most `ClockSkew`, plus the lead the agent's clock had over the node's
  when it sent them,
  beyond the lease of its last poll (§6.2). In that time `GetAgentsAsync` shows the agent
  as connected, and a command created for it waits and then ends as above, not as
  Unavailable;
- it can widen that lead itself, if it can also move the agent's clock ahead through a time
  service that is not authenticated. The lease of an agent whose polls arrive in time
  still ends by the node's clock, and an agent ahead by more than the take timeout plus
  `ClockSkew` refuses every command (rule 1).

  Such a hop could also keep a poll it captured while the clock was ahead: a high number
  and a late `time`. Without the reservation of §6.2, an agent restarted after its clock
  was set back would number its polls below that one. The hop could then replay it after
  the agent had run a Write: the poll would become the newest, its list and its time would
  make the Write Expired (rules 7, 11), the caller key would be free, and a repeat would
  run the Write a second time. The reservation closes this: every poll after the restart
  is above the captured one, which stays older and changes nothing. That holds however
  often the clock is moved and the agent restarted, because the agent starts at or above
  the highest bound it reserved, which no clock orders.

**What the reservation rests on.** It lives as long as the audit log of the agent's
identity.
- An audit log that was cleared, cut short, or restored from an older data folder or a
  snapshot of the machine brings back lower reservations, or none, and with them the
  replay above. Such an agent is paired anew, as a clone is (concept "What protects
  what").
- The agent alone writes its audit log, one entry at a time, and each entry is on the disk
  before the next is written. An entry counts as written only with its line end.
- When it starts, the agent may cut only the last line of a file, and only when that line
  lacks its line end or is not JSON: a write that never completed, after a loss of power.
  After a write that fails, for example on a full disk, it cuts the file back to the length
  it had before that write. The agent went on from neither.
- Any other line that it cannot read makes the agent record `started` with an error and
  stop, since the log is also its journal of rules 3, 10 and 11. The administrator moves
  `audit\` aside and pairs the agent anew. The reader of the log accepts everything its
  writer writes: no limit of depth or length below the deepest and the longest entry.

Dropping messages already gives such a hop the power over outcomes, so nothing more is built
against it.
