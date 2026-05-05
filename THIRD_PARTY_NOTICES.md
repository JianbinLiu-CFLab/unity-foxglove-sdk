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
- **License**: MIT
- **Usage**: The compiled DLL is bundled at `Runtime/Plugins/compression/IonKiwi.lz4.dll`, used for MCAP LZ4 compression/decompression.

---

## ZstdSharp.Port

- **URL**: https://github.com/oleg-st/ZstdSharp
- **License**: MIT
- **Usage**: The compiled DLL is bundled at `Runtime/Plugins/compression/ZstdSharp.dll`, used for MCAP Zstd compression/decompression.

---

## Newtonsoft.Json

- **URL**: https://github.com/JamesNK/Newtonsoft.Json
- **License**: MIT
- **Usage**: Provided via Unity Package Manager (`com.unity.nuget.newtonsoft-json`), used for JSON serialization/deserialization.
