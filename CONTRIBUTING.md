# Contributing to Unity2Foxglove

## Code Style

- C# code follows standard .NET conventions (PascalCase for public members, `_camelCase` for private fields)
- XML doc comments (`/// <summary>`) on all public types, methods, and properties
- No `<param>`, `<returns>`, or `<exception>` tags — summary-only style
- Section separators use `// ── Name ──` (box-drawing characters)

## Pull Request Process

1. Create a feature branch from `main`
2. Keep commits focused — one logical change per commit
3. Run the test suite before submitting:

   ```bash
   dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
   dotnet test Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj
   ```
   
4. Ensure the Source Generator project builds:

```bash
dotnet build Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj
```
   
5. Open a PR against `main` with a clear description of the change

## Testing

- New pure behavior/unit checks should use xUnit in `Packages/dev.unity2foxglove.sdk/Tests/Unit/`
- Source-shape and architecture checks should use Roslyn-backed xUnit tests when practical
- Repository hygiene, true socket/integration, Unity, ROS2, and Foxglove Desktop checks stay in the runtime validation runner
- Keep migrated checks mapped in `Packages/dev.unity2foxglove.sdk/Tests/Unit/MIGRATION.md`
- Manual Unity Editor smoke tests are required for Unity-specific changes (Play Mode, IL2CPP build)

### Python identity compatibility

- Compatibility mode preserves declared public names while allowing private helper renames and unused-import cleanup.
- A public-name removal requires a prior entry in `Scripts/phase192/identity_waivers.json`; the compatibility check reads waiver authority from the base revision only.
- A waiver for a facade that is absent from the base revision is invalid. A stale symbol entry for an existing facade is inert, so the removal and later waiver cleanup can both merge.
- Strict mode rejects any surface removal and ignores compatibility waivers.
- Run `python -B Scripts/phase192/compare_identity_surfaces.py --base <base> --head <head> --repository . --compatibility` to reproduce the compatibility check locally.

### Unity compile gate

- Pull requests that change Unity import inputs need a successful self-hosted `unity-compile` run; see
  [Unity Self-Hosted Runner](docs/unity-self-hosted-runner.md) for runner setup, auto-start and machine changes.

## License

By contributing, you agree that your contributions will be licensed under the Apache License 2.0.
