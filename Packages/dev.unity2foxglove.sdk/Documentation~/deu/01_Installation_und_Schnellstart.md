# 1. Erste Schritte

## Wer das lesen sollte

Lesen Sie dies, wenn Sie Unity2Foxglove zum ersten Mal zu einem Unity-Projekt hinzufügen.

## Was Sie tun werden

Sie werden das Paket installieren, einen `FoxgloveManager` hinzufügen, einen Transform veröffentlichen, optional einen Scene-Cube und ein Camera-Image veröffentlichen und Foxglove Desktop verbinden.

> [!TIP]
> Wenn Sie Unity oder Foxglove Desktop noch nicht installiert haben, beginnen Sie mit [00_Voraussetzungen](00_Voraussetzungen.md).

## 1.1 Das Paket installieren

### 1.1.1 Aus einem lokalen Paket installieren

1. Öffnen Sie Unity.
2. Öffnen Sie **Window > Package Manager**.

![open-package-manager](../Pictures/01/open-package-manager.png)
<figcaption>Abbildung 1: Öffnen des Package Managers</figcaption>

3. Klicken Sie auf **+ > Add package from disk...** oder wählen Sie unten „Install package from git URL" und geben Sie die git URL ein:  

```http
https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk.git?path=/Packages/dev.unity2foxglove.sdk
```

![package-manager-add-menu](../Pictures/01/package-manager-add-menu.png)
<figcaption>Abbildung 2: Hinzufügen-Menü des Package Managers</figcaption>

4. Wählen Sie `Packages/dev.unity2foxglove.sdk/package.json`.

![select-package-json](../Pictures/01/select-package-json.png)
<figcaption>Abbildung 3: Auswahl der package.json-Datei</figcaption>

5. Warten Sie, bis Unity die Abhängigkeiten aufgelöst hat.

![package-imported-confirmation](../Pictures/01/package-imported-confirmation.png)
<figcaption>Abbildung 4: Bestätigung des Paketimports</figcaption>

### 1.1.2 Alternative: `manifest.json` bearbeiten

Diese Methode wird für den allgemeinen Gebrauch nicht empfohlen – Unity kann relative `file:`-Pfade in absolute Pfade umschreiben, und das Ergebnis ist nicht portabel. Bevorzugen Sie die obige Package Manager-UI, es sei denn, Sie haben einen besonderen Grund, das Manifest zu bearbeiten.

```json
{
  "dependencies": {
    "dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk"
  }
}
```

Passen Sie den Pfad an, um abzugleichen, wo sich Ihr Projekt relativ zum Paket befindet.

## 1.2 Die Server-Komponente hinzufügen

1. Erstellen Sie ein leeres GameObject mit dem Namen `Foxglove`.
2. Fügen Sie **FoxgloveManager** hinzu.

![add-foxglove-manager](../Pictures/01/add-foxglove-manager.png)
<figcaption>Abbildung 5: Hinzufügen der Foxglove Manager-Komponente</figcaption>

3. Behalten Sie die Standardeinstellungen für den ersten Test bei:
   - Host: `127.0.0.1`
   - Port: `8765`
   - Start On Enable: aktiviert
   - Coordinate Mode: `LeftHand`

![foxglove-manager-default-settings](../Pictures/01/foxglove-manager-default-settings.png)
<figcaption>Abbildung 6: Standardeinstellungen des Foxglove Managers</figcaption>

Drücken Sie **Play**. Die Unity-Konsole sollte eine Server-gestartet-Meldung für `ws://127.0.0.1:8765` anzeigen.

![websocket-server-started](../Pictures/01/websocket-server-started.png)
<figcaption>Abbildung 7: Bestätigung des WebSocket-Serverstarts</figcaption>

## 1.3 Einen Transform veröffentlichen

1. Erstellen Sie einen Cube.
2. Fügen Sie **Foxglove Transform Publisher** zum Cube hinzu.

![add-transform-publisher](../Pictures/01/add-transform-publisher.png)
<figcaption>Abbildung 8: Hinzufügen der Transform Publisher-Komponente</figcaption>

3. Verwenden Sie diese Werte für den ersten Test:
   - Topic: leer lassen oder `/tf` verwenden
   - Parent Frame Id: `unity_world`
   - Child Frame Id: leer lassen, um den GameObject-Namen zu verwenden
   - Publish Rate Hz: `10`

![transform-publisher-tf-config](../Pictures/01/transform-publisher-tf-config.png)
<figcaption>Abbildung 9: Transform Publisher `/tf`-Konfiguration</figcaption>

Bewegen oder drehen Sie den Cube, während Unity im Play Mode ist.

## 1.4 Foxglove Desktop verbinden

1. Öffnen Sie Foxglove Desktop.
2. Klicken Sie auf **Open connection**.
3. Wählen Sie **Foxglove WebSocket**.
4. Geben Sie `ws://127.0.0.1:8765` ein.
5. Klicken Sie auf **Open**.

Bestätigen Sie, dass `/tf` im Topics-Panel erscheint. Um die rohe Transform-Nachricht zu prüfen, fügen Sie ein **Raw Messages**-Panel hinzu und wählen Sie `/tf` aus. Um das Frame zu visualisieren, fügen Sie ein **3D**-Panel hinzu.

![foxglove-tf-topic-connected](../Pictures/01/foxglove-tf-topic-connected.png)
<figcaption>Abbildung 10: `/tf`-Topic in Foxglove verbunden</figcaption>

Erwartete Topics:

- `/tf` mit Schema `foxglove.FrameTransform`

![tf-frametransform-topic](../Pictures/01/tf-frametransform-topic.png)
<figcaption>Abbildung 11: `/tf` FrameTransform-Topic-Verifikation</figcaption>


Bewegen oder drehen Sie den Cube in der Scene-Ansicht mit dem Move Tool oder Rotate Tool, während Unity im Play Mode ist. Die Transform-Änderungen werden sofort an `/tf` veröffentlicht.

![cube-transform-update-foxglove](../Pictures/01/cube-transform-update-foxglove.png)
<figcaption>Abbildung 12: Cube Transform Live-Aktualisierung</figcaption>

## 1.5 Optional: Einen Scene-Cube veröffentlichen

Fügen Sie **Foxglove Scene Cube Publisher** zum Cube hinzu, wenn das 3D-Panel von Foxglove ein einfaches Cube-Primitiv anstelle eines reinen Frame-Transforms anzeigen soll.

![add-scene-cube-publisher](../Pictures/01/add-scene-cube-publisher.png)
<figcaption>Abbildung 13: Hinzufügen der Scene Cube Publisher-Komponente</figcaption>

Empfohlene Werte für den ersten Test:

- Topic: `/scene`
- Frame Id: leer lassen, um den Objekt-/Frame-Namen wiederzuverwenden
- Color: grün
- Size: `(1, 1, 1)`

![scene-cube-publisher-config](../Pictures/01/scene-cube-publisher-config.png)
<figcaption>Abbildung 14: Scene Cube Publisher `/scene`-Konfiguration</figcaption>

Gehen Sie zurück zu Foxglove und fügen Sie ein neues Panel hinzu.

![foxglove-add-panel](../Pictures/01/foxglove-add-panel.png)
<figcaption>Abbildung 15: Hinzufügen eines Foxglove-Panels</figcaption>

Wählen Sie das 3D-Panel aus.

![select-3d-panel](../Pictures/01/select-3d-panel.png)
<figcaption>Abbildung 16: Auswahl des 3D-Panels</figcaption>

Erwartete Topics:

- `/scene` mit Schema `foxglove.SceneUpdate`

Wenn Sie den Cube nicht im 3D-Panel sehen, suchen Sie `/scene` in der Topics-Liste im linken Panel und klicken Sie auf das Sichtbarkeitssymbol, um es zu aktivieren.

![enable-scene-topic-3d](../Pictures/01/enable-scene-topic-3d.png)
<figcaption>Abbildung 17: `/scene`-Topic im 3D-Panel aktivieren</figcaption>
## 1.6 Optional: Ein Camera-Image veröffentlichen

1. Wählen Sie eine Unity-Kamera aus.
2. Fügen Sie **Foxglove Camera Publisher** hinzu.

![add-camera-publisher](../Pictures/01/add-camera-publisher.png)
<figcaption>Abbildung 18: Hinzufügen der Camera Publisher-Komponente</figcaption>

3. Verwenden Sie:
   - Topic: `/unity/camera`
   - Frame Id: `unity_camera`
   - Publish Rate Hz: `10`
   - Width: `640`
   - Height: `480`
   - JPEG Quality: `70`

![camera-publisher-config](../Pictures/01/camera-publisher-config.png)
<figcaption>Abbildung 19: Camera Publisher `/unity/camera`-Konfiguration</figcaption>

Gehen Sie zurück zu Foxglove und fügen Sie ein neues Panel hinzu.

![foxglove-add-camera-panel](../Pictures/01/foxglove-add-camera-panel.png)
<figcaption>Abbildung 20: Hinzufügen des Kamera-Visualisierungspanels</figcaption>

Wählen Sie das Image-Panel aus.

![select-image-panel](../Pictures/01/select-image-panel.png)
<figcaption>Abbildung 21: Auswahl des Image-Panels</figcaption>

Erwartete Topics:

- `/tf` mit Schema `foxglove.FrameTransform`
- `/scene` mit Schema `foxglove.SceneUpdate`, falls Sie einen Scene Publisher hinzugefügt haben
- `/unity/camera` mit Schema `foxglove.CompressedImage`, falls Sie einen Camera Publisher hinzugefügt haben

Bewegen oder drehen Sie den Cube in der Scene-Ansicht mit dem Move Tool oder Rotate Tool, während Unity im Play Mode ist. Beobachten Sie, wie sich der Cube im 3D-Panel von Foxglove bewegt und die Position in den Panels aktualisiert wird.

![foxglove-live-updates](../Pictures/01/foxglove-live-updates.png)
<figcaption>Abbildung 22: Foxglove Live-Aktualisierungsverifikation</figcaption>

## 1.7 Wie Erfolg aussieht

- Das Foxglove **Topics**-Panel listet Ihre Unity-Topics auf.
- Das **3D**-Panel kann das Cube-Frame oder -Primitiv anzeigen.
- Das **Image**-Panel kann `/unity/camera` anzeigen.
- Das Bewegen des Cubes in Unity aktualisiert Foxglove live.

## 1.8 Nächste Schritte

- Verwenden Sie [02_Foxglove_Desktop_Operation](02_Foxglove_Desktop_Operation.md), um Panels und Layouts einzurichten.
- Verwenden Sie [03_Verifying_Basic_Visualization](03_Verifying_Basic_Visualization.md), wenn Sie eine paketierte Minimalszene möchten.
- Verwenden Sie [10_Inspector_Reference](10_Inspector_Reference.md), wenn Sie Komponentenfelder anpassen müssen.
