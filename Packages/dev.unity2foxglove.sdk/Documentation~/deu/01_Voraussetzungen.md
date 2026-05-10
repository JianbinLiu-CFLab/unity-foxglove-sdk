## 1. Zweck

Diese Seite bereitet die Software vor, die für den ersten Unity2Foxglove-Setup benötigt wird.

Die Seite bleibt bewusst kurz. Sie erklärt keine Package-Interna, Sample-Implementierungen oder Netzwerk-Fehlersuche. Diese Themen werden später im Handbuch behandelt.

## 2. Erforderliche Software

Installiere diese Software, bevor du dem Schnellstart folgst.

| Software | Website | Anforderung | Hinweise |
|---|---|---|---|
| Unity Editor | [Unity official website](https://unity.com/) | Unity 6000.0 LTSC oder neuer | Der SDK-Kern ist mit 6000.0.74f1 LTSC oder neuer kompatibel. Das Repository-Demo-Projekt wird mit 6000.3.14f1 LTSC entwickelt und getestet. Unity 2022 wird nicht unterstützt. |
| Foxglove Desktop | [Foxglove download page](https://foxglove.dev/download) | Aktuelle stabile Desktop-App | Wird zum Verbinden mit Unity, Prüfen von Topics, Anzeigen von 3D-Daten und Bildern, Bearbeiten von Parametern, Aufrufen von Services und Öffnen von MCAP-Dateien verwendet. |
| Git | [Git download page](https://git-scm.com/downloads) | Eine aktuelle Version | Wird benötigt, wenn du das Repository klonst oder das Package über eine Git URL installierst. |

Unity2Foxglove benötigt kein ROS.

> [!NOTE]
> Die aktuelle manuelle Validierung wurde auf Windows 10 LTSC durchgeführt. Andere Desktop-Plattformen wurden noch nicht vollständig validiert. Wenn ein Kompatibilitätsproblem auftritt, kontaktiere den Maintainer, öffne ein GitHub Issue oder reiche einen Pull Request mit Plattformdetails und Reproduktionsschritten ein.

## 3. Empfohlener Unity-Setup

Installiere Unity über Unity Hub. Wenn Unity Hub während der Installation eine Visual-Studio-Option anbietet, sollte sie aktiviert bleiben. Das ist für die meisten Windows-Nutzer der einfachste Weg für C#-Bearbeitung und Unity-Integration.

Für normalen Editor Play Mode reicht die Standardinstallation von Unity.

Für Standalone Player Builds muss zusätzlich das Zielplattform-Modul installiert sein:

| Ziel | Unity Hub module |
|---|---|
| Windows | Windows Build Support with IL2CPP |
| Linux | Linux Build Support with IL2CPP |
| macOS | macOS Build Support |

Wenn du nur den Schnellstart im Unity Editor ausführen möchtest, kannst du die Player-Build-Module vorerst überspringen.

## 4. Optionale Entwicklerwerkzeuge

Diese Werkzeuge sind hilfreich, wenn du Code bearbeitest, Validierungstests ausführst oder Repository-Skripte verwendest. Für das reine Importieren des Packages und Play Mode sind sie nicht alle erforderlich.

| Werkzeug | Website | Verwendung |
|---|---|---|
| Visual Studio | [Visual Studio download page](https://visualstudio.microsoft.com/downloads/) | C#-Skripte, custom publishers, `[FoxRun]` fields, Parameters oder Services. Empfohlener Standard unter Windows. |
| JetBrains Rider | [Rider product page](https://www.jetbrains.com/rider/) | Wenn du Rider für Unity/C#-Entwicklung bevorzugst. |
| Visual Studio Code | [VS Code download page](https://code.visualstudio.com/) | Leichtgewichtige Bearbeitung mit C#- und Unity-Erweiterungen. |
| Python 3 | [Python download page](https://www.python.org/downloads/) | Repository-Hilfsskripte wie `Scripts/build_unity_il2cpp.py`. |
| .NET SDK | [.NET download page](https://dotnet.microsoft.com/download) | Runtime validation tests oder performance baselines über die Kommandozeile. |

## 5. Checkliste für den ersten Lauf

Vor dem Fortfahren prüfen:

- Unity öffnet dein Projekt ohne Package-Fehler.
- Foxglove Desktop ist installiert.
- Play Mode in Unity kann gestartet werden.
- Falls ein Standalone Player gebaut werden soll, ist das passende Unity Build Module installiert.
- Falls Repository-Skripte verwendet werden sollen, ist Python im Terminal verfügbar.

Für den kleinsten ersten Test weiter mit [02_Installation_und_Schnellstart](02_Installation_und_Schnellstart.md).

Wenn du statt eines leeren Setups ein Package Sample verwenden möchtest, siehe [Basic Visualization (English)](../en/05_Verifying_Basic_Visualization.md).
