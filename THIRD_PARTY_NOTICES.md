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

## IonKiwi.lz4.managed

- **URL**: https://github.com/IonKiwi/lz4.managed
- **NuGet**: https://www.nuget.org/packages/IonKiwi.lz4.managed
- **License**: BSD-2-Clause
- **Package owner**: IonKiwi / Ewout van der Linden
- **Upstream note**: The package README states that LZ4 was written by Yann Collet and that this package contains translated C# sources.
- **Usage**: The compiled DLL is bundled at `Runtime/Plugins/compression/IonKiwi.lz4.dll`, used for MCAP LZ4 compression/decompression.

```
BSD 2-Clause License

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
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
