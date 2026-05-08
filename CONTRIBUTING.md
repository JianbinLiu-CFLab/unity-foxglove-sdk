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
   ```
   
4. Ensure the Source Generator project builds:

```bash
dotnet build Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj
```
   
5. Open a PR against `main` with a clear description of the change

## Testing

- New features must include dotnet runtime tests (Phase validation pattern)
- Tests are in `Packages/dev.unity2foxglove.sdk/Tests/Runtime/`
- Manual Unity Editor smoke tests are required for Unity-specific changes (Play Mode, IL2CPP build)

## License

By contributing, you agree that your contributions will be licensed under the Apache License 2.0.
