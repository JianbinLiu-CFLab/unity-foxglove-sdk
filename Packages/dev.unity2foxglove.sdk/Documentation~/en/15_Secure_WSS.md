## 1. Purpose

Use this page when you want Unity2Foxglove to listen on `wss://` directly from Unity Editor or a standalone Player.

Plain `ws://127.0.0.1:8765` remains the default and is still the right choice for local development. The official Foxglove SDK builds hosted app URLs with `ws://127.0.0.1:<port>` for plain loopback servers; WSS is not required for hosted Foxglove Web on local loopback. WSS is for demos, labs, or LAN deployments where the Unity process should own TLS instead of placing Caddy, nginx, or another gateway in front of it.

## 2. Connection Modes

| Mode | Use it when | Notes |
|---|---|---|
| Plain WebSocket | Local development on the same machine. | Default: `ws://127.0.0.1:8765`. |
| Unity-native WSS | You need direct TLS from the Unity process. | Uses a PFX certificate and `ManagedWssBackend`. |
| Foxglove Remote Access | You need official remote access workflows. | Separate from this SDK feature. |

One `FoxgloveManager` runs one listener mode at a time. Plain and secure listeners are mutually exclusive for one manager instance.

## 3. Inspector Setup

1. Select the GameObject with `FoxgloveManager`.
2. Set **Transport Mode** to `SecureWebSocket`.
3. Keep **Host** as `127.0.0.1` for local tests.
4. Keep **Port** as `8765`, or choose another free port.
5. Click **Generate Local Dev Certificate** in **Security / WSS**.
6. Confirm that **Certificate Pfx Path** and **Root Ca File Path** were filled automatically.
7. Confirm the displayed **Root CA SHA-256** fingerprint.
8. Import/trust the generated root CA manually only after fingerprint verification.
9. Enter Play Mode.
10. Use the Inspector **Open Foxglove Web** or **Copy Foxglove Web URL** action. It will use `wss://127.0.0.1:8765` only when `SecureWebSocket` is selected.

The generated local-development certificate is written under:

```text
UserSettings/Unity2Foxglove/Certificates/
```

`UserSettings/` is ignored by git. Do not copy the generated PFX private-key file into tracked source folders.

The Inspector generator uses OpenSSL on Windows, macOS, and Linux. Install OpenSSL and make sure `openssl` or `openssl.exe` is on `PATH` before using the button.

If the shared token gate is enabled, connect with:

```text
wss://127.0.0.1:8765?token=<token>
```

The Inspector and logs redact token values as `REDACTED`.

Foxglove Web sends an `Origin` header based on the page host, not the project path, user, layout, or query string. `FoxgloveManager` enables **Allow Hosted Foxglove Web** by default, which adds `https://app.foxglove.dev` at runtime even for older scenes whose custom origin list was serialized before this setting existed. For custom/private web deployments, add that web app's origin once. If you paste a full page URL, the SDK normalizes it to `scheme://host[:port]` before matching. Foxglove Desktop and CLI-style clients either send no Origin or use `file://`, so they are not blocked by the browser-origin allowlist.

## 4. Root CA Distribution

The distributor serves:

```text
http://127.0.0.1:8766/
http://127.0.0.1:8766/rootCA.crt
```

The HTTP download does not prove the CA is trustworthy. Before importing the CA, compare the SHA-256 fingerprint shown in Unity with the fingerprint shown on the distributor root page.

Keep the distributor bound to `127.0.0.1` unless you intentionally need another machine to download the CA. Binding to `0.0.0.0` exposes the CA page to the network.

## 5. Manual Verification Step

Use a manual verification step whenever another person or another device is asked to trust the Unity2Foxglove root CA. The goal is to prove that the CA file being imported is the same CA file configured in the Unity process.

Recommended approval flow:

1. Start Unity with **Transport Mode** set to `SecureWebSocket`.
2. Keep the root CA distributor on `127.0.0.1` for same-device setup, or use a known trusted LAN address only when a second device must download the CA.
3. Copy the `Root CA SHA-256` value from the Unity Inspector or Unity Console.
4. Send the fingerprint to the reviewer through a trusted channel that is separate from the HTTP CA download page.
5. Ask the reviewer to open the distributor root page and compare the displayed SHA-256 fingerprint exactly.
6. Import the CA only when the fingerprints match character-for-character.
7. Connect to the redacted URL shown by the Inspector, replacing `REDACTED` with the shared token only on the client device.
8. If the fingerprint differs, stop the server, discard the downloaded CA, rotate the certificate, and repeat the verification.

Recommended approval record:

```text
Unity2Foxglove WSS manual verification
Date:
Reviewer:
Unity host and port:
Root CA distributor URL:
Root CA SHA-256:
Certificate subject/SAN:
Certificate expiry:
Shared token delivery channel:
Result: approved / rejected
```

Do not paste the shared token into the approval record. Record only how it was delivered.

## 6. Test Certificate Examples

The Inspector generator is the preferred path for local development. It uses OpenSSL internally. Use these commands only when you need custom certificate names, validity, or subject alternative names.

```bash
openssl req -x509 -newkey rsa:2048 -sha256 -days 30 -nodes \
  -keyout unity2foxglove-localhost.key \
  -out unity2foxglove-localhost.crt \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export \
  -out unity2foxglove-localhost.pfx \
  -inkey unity2foxglove-localhost.key \
  -in unity2foxglove-localhost.crt \
  -passout pass:changeit \
  -keypbe PBE-SHA1-3DES \
  -certpbe PBE-SHA1-3DES \
  -macalg sha1
```

Use the `.pfx` path in Unity. Use the `.crt` path for root CA distribution and OS trust import.

## 7. Token Gate Limitations

The shared token is a lightweight local/LAN gate:

- It is sent in the WebSocket URL query string.
- In plain `ws://`, it is cleartext.
- It may appear in browser history, screenshots, proxy logs, or client diagnostics.
- In `wss://`, TLS protects it in transit after the TLS handshake succeeds.
- It is not OAuth, mTLS, user identity, or authorization.

Prefer token gating with WSS, and do not use sensitive account passwords as tokens.

## 8. Troubleshooting

| Symptom | Check |
|---|---|
| WSS fails immediately | Confirm the PFX path, password, private key, and certificate validity dates. |
| `unsupported HMAC` while loading PFX | Regenerate the PFX with the Inspector button or use the OpenSSL command above; Unity Mono cannot read OpenSSL 3's default PKCS#12 algorithms. |
| Browser refuses WSS | Import/trust the root CA and connect using a host covered by the certificate, or switch back to plain `WebSocket` for same-machine local development. |
| Foxglove Web is rejected with `Rejected WebSocket Origin 'https://app.foxglove.dev'` | Enable **Allow Hosted Foxglove Web**. For custom web deployments, add that deployment's origin to **Allowed Browser Origins**. |
| Token connection fails | Confirm the query token exactly matches the Inspector token. |
| Plain mode stopped working | Switch Transport Mode back to `WebSocket`; one manager runs one mode at a time. |
| Another process uses the port | Change the manager port or stop the other listener. |
