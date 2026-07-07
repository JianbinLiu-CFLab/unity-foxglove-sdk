# Remote Gateway Native Plugin

This directory contains the managed C ABI bindings for the optional Foxglove
remote gateway package.

The native `foxglove.dll`, import library, and debug symbols are generated
artifacts and must not be committed. Build them locally when running real cloud
link validation:

```powershell
python Scripts/remotegateway/build_foxglove_c_win64.py --copy-to-package
```

The managed shutdown order is part of the safety contract: dispose the native
gateway handle first, then release callback GCHandle roots after blocking
`foxglove_gateway_stop` has returned.
