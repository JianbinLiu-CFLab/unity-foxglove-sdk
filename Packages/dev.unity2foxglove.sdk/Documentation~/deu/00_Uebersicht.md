## 1. Zweck

Dieses Handbuch ist der Einstiegspunkt für Unity2Foxglove. Es hilft beim Installieren des Packages, beim Verbinden von Unity mit Foxglove, beim Aufzeichnen oder Wiedergeben von MCAP-Dateien und beim Erstellen eines IL2CPP Players.

## 2. Ablauf

Der übliche Weg ist: Package installieren, Foxglove verbinden, Unity-Daten veröffentlichen, Parameters und Services verwenden, MCAP aufzeichnen und wiedergeben, einen Player bauen und typische Probleme beheben.

> [!NOTE]
> Der SDK-Kern zielt auf Unity 6000.0 LTSC oder neuer ab und ist mit 6000.0.74f1 LTSC oder neuer kompatibel. Unity 2022 wird nicht unterstützt. Das mitgelieferte `Unity2Foxglove` Demo-Projekt wird mit Unity 6000.3.14f1 LTSC entwickelt und getestet.

## 3. Einstieg

- [01_Voraussetzungen](01_Voraussetzungen.md): Unity, Foxglove Desktop und optionale Entwicklerwerkzeuge installieren.
- [02_Installation_und_Schnellstart](02_Installation_und_Schnellstart.md): Package installieren und `/tf`, `/scene` sowie `/unity/camera` sehen.
- [Samples and Demo Project (English)](../en/03_Samples_and_Demo_Project.md): das passende Projekt oder Package Sample auswählen.
- [Foxglove Desktop Operation (English)](../en/04_Foxglove_Desktop_Operation.md): Foxglove Desktop Panels und Layouts verwenden.
- [Basic Visualization (English)](../en/05_Verifying_Basic_Visualization.md): das minimale Package Sample importieren.

## 4. Laufzeitsteuerung

- [Parameters and Services (English)](../en/06_Parameters_and_Services.md): `/cube/color`, `/cube/scale` bearbeiten und `/cube/reset_pose` aufrufen.
- [FoxRun (English)](../en/07_FoxRun_Zero_Code_Publishing.md): Debug-Topics mit `[FoxRun]` veröffentlichen.
- [Inspector Reference (English)](../en/12_Inspector_Reference.md): wichtige Unity Inspector-Felder verstehen.

## 5. Aufzeichnung, Wiedergabe und Builds

- [MCAP Recording and Replay (English)](../en/08_MCAP_Recording_and_Replay.md): Unity-Daten aufzeichnen, MCAP in Foxglove öffnen und in Unity wiedergeben.
- [IL2CPP Build Guide (English)](../en/09_IL2CPP_Build_Guide.md): einen Standalone Player mit `Scripts/build_tools/unity_il2cpp.py` bauen und prüfen.

## 6. Weiterführende Themen

- [Architecture (English)](../en/10_Architecture.md): Runtime, Protokoll, MCAP und FoxRun internals.
- [Troubleshooting (English)](../en/11_Troubleshooting.md): symptomorientierte Fehlerbehebung.
- [Schema Coverage (English)](../en/13_Schema_Coverage.md): offizielle Protobuf-Schema-Abdeckung und Grenzen der typed publishers.

## 7. Projektauswahl

- Eigenes Unity-Projekt: für die Integration des SDK in eine bestehende Anwendung.
- `Samples~/BasicVisualization`: für das kleinste importierbare Sample.
- `Samples~/FullDemoVisualization`: für den vollständigen Package-Workflow mit Parameters, Services, FoxRun, MCAP, Input System und URP.
- `Unity2Foxglove`: für Repository-Entwicklung, manuelle Abnahme und Build-Tests.
