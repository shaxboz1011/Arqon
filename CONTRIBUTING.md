# Contributing

1. `dotnet test` must pass; new behaviour comes with a test that failed first.
2. Core code (`src/Arqon.Core`) never touches the UI and reaches Windows only through an interface, so tests can use fakes.
3. Anything that changes system settings (registry, drivers) must be explained to the user before it happens and be reversible.
4. Steering must fail open: if anything goes wrong, Windows routing takes over.
5. Run the UI without hardware: `dotnet run --project src/Arqon.App -- --demo`. `--screenshots <dir>` renders every page to PNG.
