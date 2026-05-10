# 2. Samples and Demo Project

## Who should read this

Read this before choosing what to open in Unity.

Unity2Foxglove gives you three practical entry points:

- your own Unity project with the SDK package installed;
- importable Package Manager samples under `Samples~`;
- the repository demo project under `Unity2Foxglove/`.

This page helps you pick the right one before you follow the detailed setup and verification steps.

## What you will decide

You will decide whether to:

- add Unity2Foxglove to your own project;
- import the minimal Basic sample;
- import the complete Full Demo sample;
- open the repository demo project.

The right choice depends on whether you want a first connection test, a complete feature tour, or a development/acceptance environment.

## 2.1 Use Your Own Unity Project

Use your own project when your goal is integration.

Purpose:

- add Foxglove streaming to an existing Unity application;
- attach publishers to your own objects and cameras;
- record or replay MCAP from your own scene.

Application:

- normal SDK use;
- existing robotics, simulation, digital-twin, or game tooling projects;
- projects where you already control scene structure and dependencies.

Steps:

1. Install the package from `Packages/dev.unity2foxglove.sdk/package.json` or the Git URL with `?path=/Packages/dev.unity2foxglove.sdk`.
2. Create a `Foxglove` GameObject.
3. Add `FoxgloveManager`.
4. Add publishers to the objects you want to stream.
5. Enter Play Mode.
6. Connect Foxglove Desktop to `ws://127.0.0.1:8765`.

Expected result:

- Foxglove connects to Unity.
- Topics appear for the publishers you added.
- Your own scene remains the source of truth.

## 2.2 Use Basic Visualization

Use **Basic Visualization** when you want the smallest importable sample.

Purpose:

- prove the package imports correctly;
- prove Unity can stream transform, scene, and camera topics;
- avoid extra sample dependencies.

Application:

- first smoke test after installing the package;
- quick check in a clean Unity project;
- minimal reference for adding the core components yourself.

Steps:

1. Open **Window > Package Manager**.
2. Select **Unity2Foxglove SDK**.
3. Expand **Samples**.
4. Import **Basic Visualization**.
5. Open `BasicVisualization.unity`.
6. Press Play.
7. Connect Foxglove Desktop to `ws://127.0.0.1:8765`.

Expected result:

- `/tf` is visible.
- `/scene` is visible.
- `/unity/camera` is visible.
- The simple Foxglove layout can show 3D, Image, Plot, and raw topic data.

Not covered by Basic:

- Parameters;
- Services;
- FoxRun examples;
- MCAP recording/replay workflow;
- Input System or URP sample setup.

Use Full Demo or the repository demo project for those.

## 2.3 Use Full Demo Visualization

Use **Full Demo Visualization** when you want the complete importable package sample.

Purpose:

- verify the main SDK features in a user project;
- inspect a scene that combines publishers, parameters, services, FoxRun, camera streaming, and MCAP workflows;
- use a preconfigured Foxglove layout with most panels already arranged.

Application:

- evaluating the SDK as a package user;
- learning how the main components work together;
- validating Parameters, Services, FoxRun, and camera streaming without opening the repository demo project.

Requirements:

- Input System package;
- Universal Render Pipeline package;
- Unity 6000.0 LTSC or later.

Steps:

1. Install Input System and URP if your project does not already have them.
2. Open **Window > Package Manager**.
3. Select **Unity2Foxglove SDK**.
4. Import **Full Demo Visualization**.
5. Open `FullDemoVisualization.unity`.
6. Press Play.
7. Connect Foxglove Desktop to `ws://127.0.0.1:8765`.
8. Import `FoxgloveFullLayout.json`.

Expected result:

- `/tf`, `/scene`, and `/unity/camera` stream live.
- `/debug/position` and `/debug/health` show FoxRun output.
- `/cube/color` and `/cube/scale` appear in Parameters.
- `/cube/reset_pose` can be called from the Service Call panel.
- The cube can be moved or reset while Foxglove updates.

## 2.4 Use the Repository Demo Project

Use `Unity2Foxglove/` when you cloned this repository and want the full development and manual acceptance project.

Purpose:

- run the exact project used by this repository's manual validation;
- test IL2CPP build scripts;
- validate new SDK features before they are promoted into package samples;
- reproduce maintainer acceptance workflows.

Application:

- contributor development;
- release validation;
- IL2CPP Player smoke tests;
- manual Foxglove and MCAP acceptance.

Steps:

1. Open Unity Hub.
2. Add the `Unity2Foxglove/` folder as a Unity project.
3. Let Unity resolve the local package dependency.
4. Open the sample scene.
5. Press Play.
6. Connect Foxglove Desktop to `ws://127.0.0.1:8765`.

Expected result:

- the ready-made demo scene runs without importing samples into another project;
- all repository acceptance workflows are available;
- package samples remain clean user-facing examples, not development scratch space.

## 2.5 Promotion Policy

New SDK features should be proven in `Unity2Foxglove/` first.

Promote a feature to **Full Demo Visualization** when:

- it is stable;
- it has passed manual Foxglove validation;
- it helps package users understand a real SDK workflow;
- it does not require repository-only generated files or build artifacts.

Promote a feature to **Basic Visualization** only when:

- it supports the minimal first-run story;
- it does not add extra dependencies;
- it does not make the sample harder to understand.

Do not copy these into package samples:

- generated FoxRun `.g.cs` files;
- local build outputs;
- `FoxRun_link.xml` generated for a specific project;
- repository-only performance resources;
- local manual-acceptance notes or machine-specific evidence files.

## 2.6 Quick Decision Table

| Goal | Open this |
|------|-----------|
| Add the SDK to your own app | Your own Unity project |
| First connection smoke test | Basic Visualization |
| Complete package feature tour | Full Demo Visualization |
| Contributor/release validation | `Unity2Foxglove/` |
| IL2CPP build verification | `Unity2Foxglove/` with `Scripts/build_unity_il2cpp.py` |
