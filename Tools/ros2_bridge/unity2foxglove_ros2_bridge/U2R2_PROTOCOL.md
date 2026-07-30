# U2R2 protocol authority

The normative byte and state ledger is
[`test/fixtures/u2r2_protocol_vectors.json`](test/fixtures/u2r2_protocol_vectors.json).
Both the extracted Bridge package C# codec and this sidecar's C++ codec read
that fixture independently and reproduce every listed v2 frame byte for byte.

## Frame envelope

`U2R2` is a framed loopback TCP protocol. Each frame starts with 16 bytes:

| Offset | Width | Meaning |
| --- | ---: | --- |
| 0 | 4 | ASCII `U2R2` |
| 4 | 1 | envelope version `1` |
| 5 | 3 | zero reserved flags |
| 8 | 4 | little-endian JSON-header length |
| 12 | 4 | little-endian payload length |

The JSON header must be one strict RFC 8259 UTF-8 object. Only double-quoted
strings and keys are accepted; comments, trailing commas, nonfinite/undefined
values, invalid escapes, and unpaired escaped surrogates are rejected. JSON
whitespace is limited to ASCII space, tab, carriage return, and line feed.
The header cannot start with a UTF-8 BOM. Its complete value domain is
objects, arrays, strings, Booleans, null, and unsigned 64-bit integers.
Integer tokens are exactly `0` or `[1-9][0-9]*`: negative signs (including
`-0`), fractions, exponents, and values above `18446744073709551615` are
invalid. The maximum JSON container depth is 64. Encoding and decoding apply
the same value-domain, depth, and UTF-8 checks. Duplicate properties,
replacement decoding, trailing JSON roots, truncated data, impossible
lengths, and trailing frame bytes are terminal `invalid_frame` errors.

Canonical writers sort object keys by the ordinal bytes of their unescaped
UTF-8 encoding and preserve array order. Output is ASCII-only: every
non-ASCII code point is escaped with lowercase `\uXXXX`, and non-BMP code
points use lowercase UTF-16 surrogate pairs such as `\ud83d\ude00`. Key
sorting happens before escaping.

## Dialects and identity

The sidecar classifies a connection from its first operation:

- v1 `health_ping` is a one-shot probe and does not take the data lease;
- v1 `prepare_publisher` or `publish` takes the sole data lease;
- v2 begins with `hello` and takes the sole data lease after successful
  capability negotiation.

One socket carries exactly one dialect. A v2 client cannot downgrade in
place; a legacy retry opens a new socket. Frozen v1 control requests retain
their string IDs.

v2 requests and responses use exactly correlated, nonzero unsigned 64-bit
`requestId` values. Data uses nonzero unsigned 64-bit `messageId` values.
Counters fault with `counter_exhausted` before wrap. `hello_ack` supplies a
fresh, sidecar-owned nonempty `sessionId` and a nonzero process-local
`connectionGeneration`; clients echo both on later session operations and
never synthesize them.

Header-field applicability is fail-closed. `requestId` occurs only on
requests and responses. `messageId` occurs on `publish`, successful
`publish_result`, and inbound `message`; `contractId` occurs on subscription
requests, successful subscription responses, and inbound `message`. Error
responses omit both IDs. `logTimeNs` is publish-only. `receiveTimeNs` and
`representation` are inbound-message-only. `encoding` is limited to
publisher preparation, publish, subscription registration, and inbound
message operations.

Response expectations are derived only from requests. Each request permits
its mapped success response plus `busy` and `fault`; a received response
cannot construct or broaden the expectation. Correlation checks request ID,
session ID, generation, and applicable contract/message IDs. Successful
`hello_ack` is the sole identity-assignment branch: its session ID must be
nonempty and its generation nonzero. `busy` or `fault` in response to
`hello` has no session identity. Every later response, including its error
branches, matches the request session identity exactly; error branches carry
no contract/message IDs.

The v2 ledger covers hello, health, publisher preparation, publish,
subscription registration/removal, inbound messages, busy rejection,
capability/protocol rejection, and terminal faults. Subscriptions require the
negotiated `subscribe` capability and therefore cannot use v1.

Inbound `message` events declare `encoding: "cdr"` and
`representation: "xcdr1-le"`, plus contract/message IDs, topic, canonical
type, per-contract sequence, and `receiveTimeNs`. The payload retains the
complete serialized bytes, including the four-byte `00 01 00 00`
little-endian XCDR1 encapsulation header. The decoder requires at least four
payload bytes and validates that exact prefix. Unsupported representations
are rejected.

The stable error ledger binds each code to its terminal classification and
legal response operation:

| Error code | Terminal | Allowed response |
| --- | --- | --- |
| `busy` | yes | `busy` |
| `unsupported_protocol` | yes | `fault` |
| `missing_capability` | yes | `fault` |
| `invalid_frame` | yes | `fault` |
| `invalid_contract` | no | `publisher_ready` |
| `publisher_unavailable` | no | `publisher_ready` |

An `ok` response carries no error metadata. An `error` response requires
`errorCode`, `message`, and `terminal`. Requests and events carry none of the
response metadata fields. `busy` and `fault` always use `status: "error"`;
neither operation can be encoded as a successful response.

Replay, duplicate-request retention, subscription ordering fences, queue
fairness, and complete runtime memory/time bounds are deliberately outside
this model/codec slice and must not be inferred from successful parsing.
