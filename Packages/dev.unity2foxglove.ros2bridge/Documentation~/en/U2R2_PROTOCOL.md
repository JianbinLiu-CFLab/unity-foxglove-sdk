# U2R2 protocol authority

The ROS 2 Bridge package and its standalone sidecar share one byte-level
protocol authority:

`Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json`

The fixture is normative for the `U2R2` binary envelope, all v2 operation
headers, request/response correlation, connection states, stable error codes,
and terminal classification. The C# and C++ codecs consume the fixture
independently and must reproduce every v2 frame byte for byte.

The top-level 19-case v1 negative catalog remains byte-for-byte frozen to its
Phase186-A capture. `v2.legacyV1NegativeExecution` adds the versioned
executable binding without rewriting that frozen source: each catalog ID has
a concrete mutation or stream action, an expected failure stage, and explicit
`csharp`/`cpp` consumer roles.

## Provenance and clean-room status

The Phase186-B protocol implementation is original. Design review used only
the architectural ideas listed in
[the provenance ledger](../../../../Tools/ros2_bridge/unity2foxglove_ros2_bridge/PROVENANCE.json);
no implementation code or comments were copied from the inspected upstream
project.

The inspected reference was the official
[Unity-Technologies/ROS-TCP-Connector](https://github.com/Unity-Technologies/ROS-TCP-Connector.git)
repository at revision
`c27f00c6cf750d2d0564349b3039d19aa3925e7c`, commit date
`2022-02-02T09:25:06-08:00`, subject `Release 0.7.0`, under
`Apache-2.0`. The linked `PROVENANCE.json` records the exact commit, tree,
LICENSE blob, inspected files, implementation hashes, and classifications.

## Envelope

Every frame has a 16-byte little-endian fixed header:

| Offset | Width | Meaning |
| --- | ---: | --- |
| 0 | 4 | ASCII magic `U2R2` |
| 4 | 1 | envelope version `1` |
| 5 | 3 | reserved flags, all zero |
| 8 | 4 | JSON header byte length |
| 12 | 4 | payload byte length |

The JSON header is strict RFC 8259 UTF-8. Only double-quoted strings and
property names are accepted; comments, trailing commas, `NaN`, `Infinity`,
`undefined`, invalid escapes, and unpaired escaped surrogates are rejected.
Only ASCII space, tab, carriage return, and line feed are JSON whitespace.
The header cannot start with a UTF-8 BOM. Its complete value domain is
objects, arrays, strings, Booleans, null, and unsigned 64-bit integers.
An integer uses only the decimal token `0` or `[1-9][0-9]*`; a negative sign
(including `-0`), fraction, exponent, or value above `18446744073709551615`
is invalid. The maximum JSON container depth is 64. Encoding and decoding
enforce the same type, number, depth, and UTF-8 rules. Invalid UTF-8,
duplicate properties, a non-object root, a second JSON root, impossible
lengths, truncation, and bytes after the declared payload are terminal
`invalid_frame` faults.

Canonical writers sort object property names by the ordinal bytes of their
unescaped UTF-8 encoding and preserve array order. The emitted header is
ASCII-only: every non-ASCII code point uses lowercase `\uXXXX`; a non-BMP
code point uses its lowercase UTF-16 surrogate pair (for example,
`\ud83d\ude00`). Sorting is performed before escaping.

## v2 dialect

The first v2 frame is `hello` with protocol version `2`, a nonzero unsigned
64-bit request ID, and explicit capabilities. `hello_ack` returns the exact
request ID plus a sidecar-assigned nonempty `sessionId` and process-local,
nonzero `connectionGeneration`. The identity accompanies all later
session-bound operations.

Requests and correlated responses use nonzero unsigned 64-bit `requestId`.
Data frames use a nonzero unsigned 64-bit `messageId`. Request-ID counters
fault with `request_id_exhausted` before emitting the reserved `2^64 - 1`
request ID; data counters fault with `counter_exhausted` before wrap. A
received `2^64 - 1` request ID is terminally rejected before the replay
high-water mark changes. A socket has exactly one dialect: after v2
`hello`, a v1 retry requires a new socket.

Field applicability is fail-closed. `requestId` appears only on requests and
responses. `messageId` appears on `publish`, successful `publish_result`, and
inbound `message`; `contractId` appears on subscription requests, successful
subscription responses, and inbound `message`. Every error response omits
both IDs. `logTimeNs` is publish-only, while `receiveTimeNs` and
`representation` are inbound-message-only. `encoding` is allowed only on
publisher preparation, publish, subscription registration, and inbound
message operations.
Contract topics are canonical ASCII ROS names no longer than 255 characters.

Response expectations are constructed only from requests. Each request
allows its mapped success response plus `busy` and `fault`; a response cannot
construct or broaden an expectation. Correlation includes the request ID,
session ID, connection generation, and applicable contract/message IDs.
Successful `hello_ack` is the only identity-assignment branch and requires a
nonempty sidecar-assigned session ID and nonzero generation. A `busy` or
`fault` response to `hello` carries no session identity. Later session-bound
responses, including `busy` and `fault`, match the request session identity
exactly; error branches carry no contract/message IDs.

The v2 operation set is:

- `hello` / `hello_ack`;
- `health_ping` / `health_pong`;
- `prepare_publisher` / `publisher_ready`;
- `publish` / `publish_result`;
- `register_subscription` / `subscription_ready`;
- `unregister_subscription` / `subscription_removed`;
- inbound `message`;
- `busy` and terminal `fault`.

An inbound `message` carries `encoding: "cdr"`,
`representation: "xcdr1-le"`, a per-contract sequence, and
`receiveTimeNs`. Its payload preserves the complete serialized CDR bytes,
including the four-byte `00 01 00 00` little-endian XCDR1 encapsulation
header. The decoder requires at least four payload bytes and validates that
exact prefix. Other representations fail closed.

Subscriptions require a successful v2 negotiation with the `subscribe`
capability. A capability or protocol mismatch is terminal and never falls
back on the same connection.

## frozen v1 compatibility

v1 remains an explicit legacy dialect with string request IDs. A first
`health_ping` is a one-shot probe and does not acquire the data lease. A first
`prepare_publisher` or `publish` acquires the sole data-session lease. v1 bytes
remain frozen by the same authority fixture; v2 does not reinterpret them.

## Frozen limits and implementation status

The stable error ledger binds all 23 codes to terminal classification and
legal wire responses. “local only” means the code is an internal authority
result and cannot be placed on the wire.

| Error code | Terminal | Allowed wire response |
| --- | --- | --- |
| `busy` | yes | `busy` |
| `unsupported_protocol` | yes | `fault` |
| `missing_capability` | yes | `fault` |
| `invalid_frame` | yes | `fault` |
| `invalid_contract` | no | `publisher_ready`, `publish_result`, `subscription_ready`, `subscription_removed` |
| `contract_identity_mismatch` | yes | local only |
| `publisher_unavailable` | no | `publisher_ready` |
| `invalid_request_id` | yes | local only |
| `request_id_exhausted` | yes | local only |
| `counter_exhausted` | yes | local only |
| `request_id_conflict` | yes | `fault` |
| `response_mismatch` | yes | local only |
| `request_in_flight` | no | `publisher_ready`, `publish_result`, `subscription_ready`, `subscription_removed` |
| `stale_request` | no | `publisher_ready`, `publish_result`, `subscription_ready`, `subscription_removed` |
| `capacity_exceeded` | no | `publisher_ready`, `publish_result`, `subscription_ready`, `subscription_removed` |
| `contract_not_ready` | yes | local only |
| `unknown_contract` | yes | `subscription_removed`, `fault` |
| `contract_sequence_fault` | no | local only |
| `contract_sequence_exhausted` | no | local only |
| `invalid_configuration` | yes | local only |
| `dialect_downgrade` | yes | local only |
| `peer_closed` | yes | local only |
| `timeout` | yes | local only |

Successful responses contain none of `errorCode`, `message`, or `terminal`.
Error responses require all three, and non-response operations contain no
response metadata. `busy` and `fault` always use `status: "error"` and can
never be represented as successful responses.

The Phase186-B authority freezes exactly 27 positive limits:

| Limit | Value |
| --- | ---: |
| `maxConnections` | 9 |
| `maxDataSessions` | 1 |
| `maxProbes` | 8 |
| `maxContracts` | 64 |
| `maxOutstandingRequests` | 8 |
| `maxReplayEntries` | 16 |
| `maxReplayBytes` | 4194304 |
| `maxTombstones` | 32 |
| `fixedFrameBytes` | 16 |
| `maxHeaderBytes` | 65536 |
| `maxPayloadBytes` | 67108864 |
| `maxTransientBytes` | 134348832 |
| `maxInFlightBytes` | 134348832 |
| `maxQueuedBytes` | 268697664 |
| `maxTotalQueueDepth` | 128 |
| `maxPerContractQueueDepth` | 8 |
| `maxPerContractQueueBytes` | 134348832 |
| `reservedControlQueueDepth` | 8 |
| `reservedControlQueueBytes` | 1048576 |
| `controlBurstLimit` | 2 |
| `handshakeTimeoutMs` | 5000 |
| `partialFrameTimeoutMs` | 2000 |
| `readTimeoutMs` | 5000 |
| `writeTimeoutMs` | 5000 |
| `joinTimeoutMs` | 5000 |
| `shutdownTimeoutMs` | 10000 |
| `maxJsonDepth` | 64 |

Configured header and payload limits must fit the unsigned 32-bit envelope
lengths, and configured JSON depth cannot exceed 64.

The production Bridge runtime consumes this v2 session, lease, replay, and
queue authority. C# and C++ tests consume the same frozen limits, errors, and
scenario fixtures; successful parsing alone is not treated as runtime
enforcement evidence. The JSON fixture is a test oracle, not runtime
configuration or generated production source; both implementations retain
reviewed constants and the cross-language gates detect drift.
