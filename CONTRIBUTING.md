# Contributing

1. `dotnet test` must pass; new behaviour comes with a test that failed first.
2. Core code (`src/Arqon.Core`) never touches the UI and reaches Windows only through an interface, so tests can use fakes.
3. Anything that changes system settings (registry, drivers) must be explained to the user before it happens and be reversible.
4. Steering must fail open: if anything goes wrong, Windows routing takes over.
5. Run the UI without hardware: `dotnet run --project src/Arqon.App -- --demo`. `--screenshots <dir>` renders every page to PNG.

6. A new language for the interface is a copy of `src/Arqon.App/Resources/Strings.en.xaml` with the texts translated (Uzbek, English and Russian exist), plus an entry in the list `Languages` in `SettingsViewModel` and in the check in `Loc.Apply`. A test fails if a key is missing or a placeholder like `{0}` differs.
7. Build the single-file release exe with `powershell -ExecutionPolicy Bypass -File toolsuild.ps1`. A release is made by pushing a tag such as `v0.1.0` (see `.github/workflows/release.yml`).
