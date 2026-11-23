# Contributing to Bigarr

Read this first, whether you are a person or a coding agent. It lists what the build checks and what only you can
check.

## What this is

A .NET 10 API with a React front end, one SQLite database, Bun as the package manager.

- `src/Bigarr.Api/Features/<Feature>/`: one folder per feature (endpoints, service, contracts, persistence).
- `src/Bigarr.Domain`, `src/Bigarr.Decision`: domain types and the release decision engine.
- `src/Bigarr.Parsing`: release parsers vendored from Radarr and Sonarr. Not edited by hand (see `VENDORED.md` there).
- `tests/Bigarr.Api.Tests/<Feature>/`, `tests/Bigarr.Parsing.Tests/`: the tests.
- `web/src/features/`: the front end, one folder per feature. `STATE-MACHINES.md`: every state machine in the app.

## Before you push

CI runs exactly this, in this order. Run it from the repository root.

```bash
(cd web && bun install --frozen-lockfile && bun run lint && bun run build && bun run test)
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release
(cd web && bun run gen:api) && git diff --exit-code -- web/src/api/generated
dotnet test -c Release --no-build
```

## Rules the build enforces

- **Time goes through `TimeProvider`.** `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now` and
  `DateTimeOffset.UtcNow` are banned (`BannedSymbols.txt`).
- **Warnings are errors** (`Directory.Build.props`). `dotnet build` must print 0 warnings.
- **`dotnet format`** must have nothing to change.
- **Biome** (`bun run lint`) must be clean. Its rules are in `web/biome.json`.
- **Generated API types.** After any change to a request, response or route: `dotnet build`, then
  `cd web && bun run gen:api`, and commit the result.

## Rules nothing enforces

Ask yourself each question before you open a pull request.

- Did you touch a state, a transition or a guard? Update `STATE-MACHINES.md` in the same pull request. A bug fix
  adds an entry under "Fixed" with the next free number and the test that guards it.
- Does the code touch the disk or the download client and then save? After the first such side effect, every save
  and commit runs on `CancellationToken.None`. The request's token must not be able to leave disk and database apart.
  See the ground rules in `STATE-MACHINES.md`.
- Did you change the film side or the series side? Open the twin and decide: the same change, or a line in the pull
  request saying why not. Share code between the two only where it is identical.
- Did you add or rename an event the API publishes? Add it to `web/src/hooks/serverEvents.ts`. The test in
  `web/test/serverEvents.test.ts` compares that list with `src/Bigarr.Domain/Events/DomainEvents.cs`.
- Did you change an API contract? Besides the generated types, check the hand-written types in
  `web/src/api/client.ts`.
- New text in the UI? Add a key in `web/src/i18n/en.json`. Never put a literal string in a component.
- A new endpoint that does not require the admin role? Say why in the pull request. An endpoint with no policy
  requires the admin; the policies are in `src/Bigarr.Api/Infrastructure/Auth/BigarrClaims.cs`.
- A schema change? One migration per pull request. If `main` gained a migration meanwhile, delete yours and
  regenerate it after the rebase: two migrations conflict on the model snapshot.

## Do not edit by hand

- `web/src/api/generated/`: written by `bun run gen:api`.
- `src/Bigarr.Parsing/`: vendored, see `VENDORED.md`.
- `src/Bigarr.Api/Infrastructure/Persistence/Migrations/*.Designer.cs` and `BigarrDbContextModelSnapshot.cs`:
  written by `dotnet ef`.

To add a migration: `dotnet tool restore`, then
`dotnet ef migrations add <Name> --project src/Bigarr.Api --output-dir Infrastructure/Persistence/Migrations`.

## Git

- Branch names start with `feat/`, `fix/` or `chore/`.
- Commit subjects are conventional and in one plain sentence: `fix(import): keep the file when the save fails`.
  The subjects since the last tag choose the next release version (`.github/scripts/next-version.sh`): `feat` bumps
  the minor, `!` marks a breaking change, anything else bumps the patch.
- One pull request per change. Do not bundle independent fixes.
- Keep up with `main` by rebasing. Never merge `main` into your branch.

## Tests

- Run them with `dotnet test` (API) and `cd web && bun run test` (front end).
- Integration tests boot the real app through `BigarrApiFactory` (`tests/Bigarr.Api.Tests/Auth/`), on a fresh
  config directory with its own SQLite database and a seeded admin. They never share a database.
- Control time with a `TimeProvider` the test passes to the factory (`ConfiguredBigarrApiFactory` takes one). Do not
  read the real clock in a test.
- A bug fix comes with a test that fails without the fix.
