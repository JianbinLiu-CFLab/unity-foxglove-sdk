# Third-Party Notices

This project uses the following third-party open source software.

---

## foxglove/mcap

- **URL**: https://github.com/foxglove/mcap
- **License**: MIT
- **Usage**: MCAP binary format specification, opcode constants, and record structure definitions are referenced from the official spec and implementation. This project is an independent C# implementation and does not directly translate the official TypeScript/Rust code.

```
MIT License

Copyright (c) Foxglove Technologies Inc

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## foxglove/foxglove-sdk

- **URL**: https://github.com/foxglove/foxglove-sdk
- **License**: MIT
- **Usage**: Foxglove WebSocket protocol specification, opcode constants, and capability definitions are referenced from the official spec and implementation. JsonSchema definitions come from the official schema repository. This project is an independent C# implementation and does not directly translate the official code.

**Schema assets** — the following files are Foxglove schema definitions imported from the official source (each contains the upstream `$comment` field):

- `Packages/dev.unity2foxglove.sdk/Runtime/Schemas/CompressedImage.json`
- `Packages/dev.unity2foxglove.sdk/Runtime/Schemas/FrameTransform.json`
- `Packages/dev.unity2foxglove.sdk/Runtime/Schemas/SceneUpdate.json`

`Packages/dev.unity2foxglove.sdk/Runtime/Schemas/FoxgloveSchemaDefinitions.cs` embeds these schemas as base64-encoded string constants for runtime registration. Unity2Foxglove does not claim authorship of the schema definitions.

```
MIT License

Copyright (c) Foxglove Technologies Inc

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## K4os.Compression.LZ4 and K4os.Compression.LZ4.Streams

- **URL**: https://github.com/MiloszKrajewski/K4os.Compression.LZ4
- **NuGet**:
  - https://www.nuget.org/packages/K4os.Compression.LZ4
  - https://www.nuget.org/packages/K4os.Compression.LZ4.Streams
- **License**: MIT
- **Package owner / author**: Milosz Krajewski
- **Usage**: The compiled DLLs are bundled at `Runtime/Plugins/compression/K4os.Compression.LZ4.dll` and `Runtime/Plugins/compression/K4os.Compression.LZ4.Streams.dll`, used for MCAP LZ4 frame compression/decompression.

```
MIT License

Copyright (c) Milosz Krajewski

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## K4os.Hash.xxHash

- **URL**: https://github.com/MiloszKrajewski/K4os.Hash.xxHash
- **NuGet**: https://www.nuget.org/packages/K4os.Hash.xxHash
- **License**: MIT
- **Package owner / author**: Milosz Krajewski
- **Usage**: The compiled DLL is bundled at `Runtime/Plugins/compression/K4os.Hash.xxHash.dll` as a transitive dependency of `K4os.Compression.LZ4.Streams`.

```
MIT License

Copyright (c) Milosz Krajewski

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## System.IO.Pipelines

- **URL**: https://github.com/dotnet/runtime
- **NuGet**: https://www.nuget.org/packages/System.IO.Pipelines
- **License**: MIT
- **Package owner / author**: Microsoft
- **Copyright**: (c) Microsoft Corporation. All rights reserved.
- **Usage**: The compiled DLL is bundled at `Runtime/Plugins/compression/System.IO.Pipelines.dll` as a transitive dependency of `K4os.Compression.LZ4.Streams` for Unity plugin loading.

```
MIT License

Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## ZstdSharp.Port

- **URL**: https://github.com/oleg-st/ZstdSharp
- **NuGet**: https://www.nuget.org/packages/ZstdSharp.Port
- **License**: MIT
- **Package owner**: oleg-st
- **Author**: Oleg Stepanischev
- **Upstream note**: ZstdSharp is a C# port of the Zstandard compression library.
- **Usage**: The compiled DLL is bundled at `Runtime/Plugins/compression/ZstdSharp.dll`, used for MCAP Zstd compression/decompression.

```
MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Newtonsoft.Json

- **URL**: https://github.com/JamesNK/Newtonsoft.Json
- **License**: MIT
- **Usage**: Provided via Unity Package Manager (`com.unity.nuget.newtonsoft-json`), used for JSON serialization/deserialization.
