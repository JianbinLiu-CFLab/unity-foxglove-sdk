# OpenH264 Header Provenance

These headers are a local source snapshot from Cisco OpenH264 v2.6.0. They are kept in source form so the editor helper can compile without committing OpenH264 binaries.

## Snapshot Hashes

| File | SHA256 |
| --- | --- |
| `include/wels/codec_api.h` | `21f29b20c24f7c7946f2e243d0bc2532fb3542f6c28af338209477e70d9036c9` |
| `include/wels/codec_app_def.h` | `a40581a24263866dca19911928f7bc4eb354ff78d9dd56dbf0f55fc4fd923726` |
| `include/wels/codec_def.h` | `f974d269b5935e8dc7265b8bfc02f60e5185b4d6165d30541d2758a4506f1979` |
| `include/wels/codec_ver.h` | `9a241e20b7c9221a5786cccd9eae3afed91afba3525b5b9b16c2101976516f94` |

The helper validates the loaded runtime with `WelsGetCodecVersion` and requires major `2`, minor `6`, revision `0`.
