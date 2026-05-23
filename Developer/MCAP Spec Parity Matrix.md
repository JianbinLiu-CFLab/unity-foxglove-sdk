# MCAP Spec Parity Matrix

Updated: 2026-05-23

References:

- MCAP format specification: https://mcap.dev/spec
- MCAP Python writer API: https://mcap.dev/docs/python/mcap-apidoc/mcap.writer

Phase 117 records the local Unity2Foxglove MCAP stance after Phase 116 introduced the local `McapDataLoader` facade. This is format compatibility evidence, not a claim of full official MCAP library parity or remote Foxglove DataLoader support.

## Record Matrix

| Record | Opcode | Writer status | Reader status | Validation status | Notes |
|---|---:|---|---|---|---|
| Header | `0x01` | covered | covered | tested | profile/library record at file start |
| Footer | `0x02` | covered | covered | tested | summary offsets and summary CRC |
| Schema | `0x03` | covered | covered | tested | written in data/chunk context and repeated in summary |
| Channel | `0x04` | covered | covered | tested | written in data/chunk context and repeated in summary |
| Message | `0x05` | covered in chunks | covered in chunks and direct fallback | tested | Phase 117 adds valid direct-message local reads |
| Chunk | `0x06` | covered | covered | tested | none/lz4/zstd through `McapCompression` |
| Message Index | `0x07` | covered | structurally preserved | tested | offsets are emitted and surfaced through chunk indexes |
| Chunk Index | `0x08` | covered | covered | tested | fast indexed local path remains preferred |
| Attachment | `0x09` | covered | covered | tested | CRC mismatch remains detectable through `McapAttachment.CrcValid` |
| Attachment Index | `0x0A` | covered | covered | tested | direct fallback synthesizes index summaries |
| Statistics | `0x0B` | covered | covered | tested | direct fallback synthesizes statistics when no summary exists |
| Metadata | `0x0C` | covered | covered | tested | Phase 114 FoxRun metadata remains ordinary MCAP metadata |
| Metadata Index | `0x0D` | covered | covered | tested | direct fallback synthesizes index summaries |
| Summary Offset | `0x0E` | covered | skipped safely | tested | grouping evidence, not required for local summary parse |
| Data End | `0x0F` | covered | recognized | tested | data-section CRC validation remains deferred |
| Private records | `0x80-0xFF` | not written | skipped safely | tested | application-specific records are not interpreted |
| Invalid opcode | `0x00` | not written | rejected | tested | malformed by MCAP spec |

## Writer Option Stance

| Official writer option | Phase 117 stance |
|---|---|
| `chunk_size` | supported through `McapRecorder` chunk size |
| `compression` | supported: none, lz4, zstd |
| `index_types` | fixed all practical indexes for Unity-created files |
| `repeat_channels` | fixed enabled |
| `repeat_schemas` | fixed enabled |
| `use_chunking` | fixed enabled for Unity-created message streams |
| `use_statistics` | fixed enabled |
| `use_summary_offsets` | fixed enabled |
| `enable_crcs` | enabled for chunks, attachments, and summary |
| `enable_data_crcs` | deferred; DataEnd CRC remains zero |

## Non-Goals

- No remote DataLoader, HTTP range serving, or Remote Access Gateway support.
- No official WASM/component-model host ABI.
- No decoded payload views.
- No multi-file timeline merge.
- No public compatibility verdict; Phase 120 owns that gate.
