## 1. Zweck

Diese Seite beschreibt die erste Package-Installation und die erste Live-Verbindung von einer Unity-Szene zu Foxglove.

## 2. Ablauf

Du installierst das Package, fügst einen `FoxgloveManager` hinzu, veröffentlichst einen Transform, optional einen Scene Cube und ein Camera Image, und verbindest Foxglove Desktop.

> [!TIP]
> Wenn Unity oder Foxglove Desktop noch nicht installiert sind, beginne mit [01_Voraussetzungen](01_Voraussetzungen.md).

## 3. Package installieren

### 3.1 Installation über Git URL

1. Unity öffnen.
2. **Window > Package Manager** öffnen.

![open-package-manager](../Pictures/01/open-package-manager.png)
<figcaption>Abbildung 1: Package Manager öffnen</figcaption>

3. **+ > Install package from git URL...** auswählen und eingeben:

```http
https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk.git?path=/Packages/dev.unity2foxglove.sdk
```

![package-manager-add-menu](../Pictures/01/package-manager-add-menu.png)
<figcaption>Abbildung 2: Add-Menü im Package Manager</figcaption>

4. Warten, bis Unity die Abhängigkeiten auflöst.

![package-imported-confirmation](../Pictures/01/package-imported-confirmation.png)
<figcaption>Abbildung 3: Package-Import bestätigt</figcaption>

### 3.2 Installation aus einem lokalen Checkout

Verwende diesen Weg, wenn das Repository bereits lokal geklont wurde.

1. Unity öffnen.
2. **Window > Package Manager** öffnen.
3. **+ > Add package from disk...** auswählen.
4. `Packages/dev.unity2foxglove.sdk/package.json` auswählen.

![select-package-json](../Pictures/01/select-package-json.png)
<figcaption>Abbildung 4: package.json auswählen</figcaption>

5. Warten, bis Unity die Abhängigkeiten auflöst.

![package-imported-confirmation](../Pictures/01/package-imported-confirmation.png)
<figcaption>Abbildung 5: Package-Import bestätigt</figcaption>

### 3.3 Alternative: `manifest.json` bearbeiten

Diese Methode wird für normale Nutzung nicht empfohlen. Unity kann relative `file:` Pfade in absolute Pfade umschreiben, wodurch das Projekt weniger portabel wird. Verwende die Package Manager UI, sofern es keinen besonderen Grund für die Manifest-Datei gibt.

```json
{
  "dependencies": {
    "dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
  }
}
```

Passe den Pfad an die relative Lage deines Unity-Projekts zum Package an.

## 4. Server Component hinzufügen

1. Ein leeres GameObject mit dem Namen `Foxglove` erstellen.
2. **FoxgloveManager** hinzufügen.

![add-foxglove-manager](../Pictures/01/add-foxglove-manager.png)
<figcaption>Abbildung 6: Foxglove Manager Component hinzufügen</figcaption>

3. Für den ersten Test die Standardwerte behalten:
   - Host: `127.0.0.1`
   - Port: `8765`
   - Start On Enable: enabled
   - Coordinate Mode: `LeftHand`

![foxglove-manager-default-settings](../Pictures/01/foxglove-manager-default-settings.png)
<figcaption>Abbildung 7: Standardwerte des Foxglove Manager</figcaption>

**Play** drücken. In der Unity Console sollte eine Meldung erscheinen, dass der Server unter `ws://127.0.0.1:8765` gestartet wurde.

![websocket-server-started](../Pictures/01/websocket-server-started.png)
<figcaption>Abbildung 8: WebSocket Server gestartet</figcaption>

## 5. Transform veröffentlichen

1. Einen Cube erstellen.
2. **Foxglove Transform Publisher** zum Cube hinzufügen.

![add-transform-publisher](../Pictures/01/add-transform-publisher.png)
<figcaption>Abbildung 9: Transform Publisher Component hinzufügen</figcaption>

3. Für den ersten Test diese Werte verwenden:
   - Topic: leer lassen oder `/tf` verwenden
   - Parent Frame Id: `unity_world`
   - Child Frame Id: leer lassen, um den GameObject-Namen zu verwenden
   - Publish Rate Hz: `10`

![transform-publisher-tf-config](../Pictures/01/transform-publisher-tf-config.png)
<figcaption>Abbildung 10: Transform Publisher `/tf` Konfiguration</figcaption>

Den Cube im Play Mode bewegen oder rotieren.

## 6. Foxglove Desktop verbinden

1. Foxglove Desktop öffnen.
2. **Open connection** anklicken.
3. **Foxglove WebSocket** auswählen.
4. `ws://127.0.0.1:8765` eingeben.
5. **Open** anklicken.

Prüfen, dass `/tf` im Topics panel erscheint. Für die rohe Transform-Nachricht ein **Raw Messages** panel hinzufügen und `/tf` auswählen. Für die Frame-Visualisierung ein **3D** panel hinzufügen.

![foxglove-tf-topic-connected](../Pictures/01/foxglove-tf-topic-connected.png)
<figcaption>Abbildung 11: `/tf` Topic in Foxglove verbunden</figcaption>

Zentrales erwartetes Topic:

- `/tf` mit Schema `foxglove.FrameTransform`

![tf-frametransform-topic](../Pictures/01/tf-frametransform-topic.png)
<figcaption>Abbildung 12: `/tf` FrameTransform Topic prüfen</figcaption>

Den Cube im Play Mode mit Move Tool oder Rotate Tool bewegen. Transform-Änderungen werden sofort nach `/tf` veröffentlicht.

![cube-transform-update-foxglove](../Pictures/01/cube-transform-update-foxglove.png)
<figcaption>Abbildung 13: Live-Update des Cube Transform</figcaption>

## 7. Optional: Scene Cube veröffentlichen

Füge **Foxglove Scene Cube Publisher** zum Cube hinzu, wenn Foxglove im 3D panel einen einfachen Cube primitive anzeigen soll und nicht nur einen frame transform.

![add-scene-cube-publisher](../Pictures/01/add-scene-cube-publisher.png)
<figcaption>Abbildung 14: Scene Cube Publisher Component hinzufügen</figcaption>

Empfohlene Werte für den ersten Test:

- Topic: `/scene`
- Frame Id: leer lassen, um object/frame name wiederzuverwenden
- Color: green
- Size: `(1, 1, 1)`

![scene-cube-publisher-config](../Pictures/01/scene-cube-publisher-config.png)
<figcaption>Abbildung 15: Scene Cube Publisher `/scene` Konfiguration</figcaption>

Zurück zu Foxglove wechseln und ein neues panel hinzufügen.

![foxglove-add-panel](../Pictures/01/foxglove-add-panel.png)
<figcaption>Abbildung 16: Foxglove panel hinzufügen</figcaption>

Das 3D panel auswählen.

![select-3d-panel](../Pictures/01/select-3d-panel.png)
<figcaption>Abbildung 17: 3D panel auswählen</figcaption>

Optional erwartetes Topic nach dem Hinzufügen des Scene Cube publisher:

- `/scene` mit Schema `foxglove.SceneUpdate`

Wenn der Cube im 3D panel nicht sichtbar ist, `/scene` in der Topics-Liste links suchen und das Sichtbarkeitssymbol aktivieren.

![enable-scene-topic-3d](../Pictures/01/enable-scene-topic-3d.png)
<figcaption>Abbildung 18: `/scene` Topic im 3D panel aktivieren</figcaption>

## 8. Optional: Camera Image veröffentlichen

1. Eine Unity Camera auswählen.
2. **Foxglove Camera Publisher** hinzufügen.

![add-camera-publisher](../Pictures/01/add-camera-publisher.png)
<figcaption>Abbildung 19: Camera Publisher Component hinzufügen</figcaption>

3. Verwenden:
   - Topic: `/unity/camera`
   - Frame Id: `unity_camera`
   - Publish Rate Hz: `10`
   - Width: `640`
   - Height: `480`
   - JPEG Quality: `70`

![camera-publisher-config](../Pictures/01/camera-publisher-config.png)
<figcaption>Abbildung 20: Camera Publisher `/unity/camera` Konfiguration</figcaption>

Zurück zu Foxglove wechseln und ein neues panel hinzufügen.

![foxglove-add-camera-panel](../Pictures/01/foxglove-add-camera-panel.png)
<figcaption>Abbildung 21: Camera visualization panel hinzufügen</figcaption>

Das Image panel auswählen.

![select-image-panel](../Pictures/01/select-image-panel.png)
<figcaption>Abbildung 22: Image panel auswählen</figcaption>

Erwartete Topics an dieser Stelle:

- Core: `/tf` mit Schema `foxglove.FrameTransform`
- Optional scene publisher: `/scene` mit Schema `foxglove.SceneUpdate`
- Optional camera publisher: `/unity/camera` mit Schema `foxglove.CompressedImage`

Den Cube im Play Mode bewegen oder rotieren. Im Foxglove 3D panel sollte sich der Cube bewegen, und die Positionswerte sollten sich aktualisieren.

![foxglove-live-updates](../Pictures/01/foxglove-live-updates.png)
<figcaption>Abbildung 23: Foxglove Live-Update prüfen</figcaption>

## 9. Erwartetes Ergebnis

- Das Foxglove **Topics** panel listet die Unity Topics.
- Das **3D** panel kann den Cube frame oder primitive anzeigen.
- Das **Image** panel kann `/unity/camera` anzeigen.
- Bewegungen des Cube in Unity aktualisieren Foxglove live.

## 10. Nächste Schritte

- Für Panels und Layouts siehe [Foxglove Desktop Operation (English)](../en/04_Foxglove_Desktop_Operation.md).
- Für ein minimales Package Sample siehe [Basic Visualization (English)](../en/05_Verifying_Basic_Visualization.md).
- Für Component-Felder siehe [Inspector Reference (English)](../en/12_Inspector_Reference.md).
