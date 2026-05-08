
## Wer das lesen sollte

Lesen Sie dies, bevor Sie Unity2Foxglove installieren, das Demoprojekt öffnen oder zum ersten Mal einen IL2CPP-Player bauen.

## Was Sie tun werden

Sie werden prüfen, welche Unity-Version, Foxglove-App, IDE, Kommandozeilen-Tools und optionalen Module Sie für jeden Workflow benötigen.

## 0.1 Für alle erforderlich

| Tool             | Empfohlen                                                         | Warum Sie es brauchen                                                | Hinweise                                                              |
| ---------------- | ------------------------------------------------------------------- | -------------------------------------------------------------- | ------------------------------------------------------------------ |
| Unity Editor     | Unity 6000.0 LTSC oder neuer (entwickelt auf 6000.3.14f1 LTSC; kompatibel mit 6000.0.74f1 LTSC) | Öffnet Ihr Projekt, importiert das Paket und führt den Play Mode aus.   | Unity 2022 wird nicht unterstützt. |
| Foxglove Desktop | Neueste stabile Desktop-App                                           | Verbindet sich über Foxglove WebSocket mit Unity und zeigt Panels an. | Verwenden Sie `ws://127.0.0.1:8765` für die standardmäßige lokale Verbindung.        |
| Git              | Beliebige aktuelle Version                                                  | Klont das Repository oder verfolgt Paketänderungen.               | Erforderlich, wenn Sie von einem Repository-Pfad installieren.                      |

Sie benötigen kein ROS, um Unity2Foxglove zu verwenden.

## 0.2 Unity-Module

Für den Editor Play Mode ist die Standardinstallation von Unity ausreichend.

Für Player-Builds installieren Sie das Zielplattform-Modul im Unity Hub:

| Ziel | Unity Hub-Modul |
|---|---|
| Windows | Windows Build Support with IL2CPP |
| Linux | Linux Build Support with IL2CPP |
| macOS | macOS Build Support |

Wenn das Build-Skript vor dem Kompilieren von C# fehlschlägt, prüfen Sie zuerst, ob das Zielplattform-Modul installiert ist.

## 0.3 IDE und C#-Bearbeitung

Eine IDE ist optional zum Ausführen der Beispiele, wird jedoch empfohlen, wenn Sie Skripte schreiben.

Gute Optionen:

- Visual Studio mit der Unity-Workload
- JetBrains Rider
- Visual Studio Code mit C#- und Unity-Erweiterungen

Verwenden Sie die IDE für:

- Schreiben benutzerdefinierter Publisher-Skripte
- Hinzufügen von `[FoxRun]`-Debug-Feldern
- Registrieren von Parametern und Services
- Prüfen von Kompilierfehlern

## 0.4 Kommandozeilen-Tools

| Tool | Erforderlich für | So prüfen Sie |
|---|---|---|
| Python 3 | `Scripts/build_unity_il2cpp.py` | `python --version` |
| .NET SDK | Laufzeit-Validierungstests | `dotnet --version` |
| PowerShell oder Terminal | Build- und Testbefehle | Öffnen Sie eine Shell im Repository-Stammverzeichnis |

Sie benötigen Python nur, wenn Sie das Build-Skript des Repositories verwenden. Sie benötigen das .NET SDK nur, wenn Sie das Paket-Testprojekt ausführen.

## 0.5 Paketabhängigkeiten

Unity löst die Hauptpaketabhängigkeit automatisch auf:

- `com.unity.nuget.newtonsoft-json`

Das Paket enthält auch Laufzeit-Plugin-Assemblys für die MCAP-Komprimierungsunterstützung. Wenn Sie nur das Live-Foxglove-Streaming verwenden, können Sie ohne Bedenken hinsichtlich der Komprimierung beginnen.

## 0.6 Optionale Demo-Abhängigkeiten

Das Full-Demo-Beispiel und das `Unity2Foxglove`-Demoprojekt verwenden mehr Unity-Features als das Basic-Beispiel.

| Feature | Verwendet von | Hinweise |
|---|---|---|
| Input System | Maus-Ziehen-Demo-Steuerung | Erforderlich für die vollständige interaktive Demo. |
| URP | Vollständige Demo-Visuals | Das Basic-Beispiel ist bewusst kleiner gehalten. |
| MCAP-Dateien | Aufnahme-/Wiedergabe-Demos | Sie können dies überspringen, bis Sie Offline-Wiedergabe benötigen. |

Wenn Sie den einfachstmöglichen ersten Test wünschen, verwenden Sie [03_Verifying_Basic_Visualization](03_Verifying_Basic_Visualization.md).

## 0.7 Netzwerkannahmen

Die Standard-Serveradresse lautet:

```text
ws://127.0.0.1:8765
```

Das bedeutet, dass Foxglove und Unity auf demselben Rechner laufen.

Damit sich ein anderer Rechner verbinden kann:

1. Setzen Sie `FoxgloveManager > Host` auf `0.0.0.0`.
2. Behalten oder ändern Sie den Port.
3. Erlauben Sie den Port in Ihrer Firewall.
4. Verbinden Sie Foxglove mit `ws://<unity-machine-ip>:8765`.

Verwenden Sie dies nur in vertrauenswürdigen Netzwerken.

## 0.8 Schnelle Bereitschafts-Checkliste

- Unity öffnet Ihr Projekt ohne Paketfehler.
- Foxglove Desktop ist installiert.
- Sie können in Unity auf Play drücken.
- Sie wissen, ob Sie das Basic-Beispiel, das Full-Demo-Beispiel oder `Unity2Foxglove` verwenden.
- Wenn Sie IL2CPP bauen, sind Python und das Ziel-Unity-Build-Modul installiert.

Wenn diese Checkliste erfüllt ist, fahren Sie fort mit [01_Installation_und_Schnellstart](01_Installation_und_Schnellstart.md).
