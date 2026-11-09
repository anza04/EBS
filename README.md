# European Basketball Manager

Offline, single-player basketball management game for Windows, covering the top two domestic
divisions of European countries.

**Status:** Step 05 (player model and attributes) complete. The application creates and migrates the game
database, imports data and edits it, and shows player profiles and comparisons (27 attributes, tendencies, position
ratings); the competition engine (schedules, standings, playoffs, promotion and relegation) runs as a service.
See [PROJECT_STATE.md](PROJECT_STATE.md) for the current state and [docs/ROADMAP.md](docs/ROADMAP.md) for the plan.

## Requirements

* Windows 10 or 11 (x64)
* .NET SDK 10.0.203 or a later 10.0 feature band (see `global.json`)

## Quick start

```powershell
dotnet build EuropeanBasketballManager.slnx
dotnet test  EuropeanBasketballManager.slnx
dotnet run   --project src/Ebm.Desktop
```

The main window is the database editor. To load the sample league, open the *Import* tab, choose
`data\sample\italy-sample.json` and press *Import*. CSV templates are in `data\templates`.

User data (logs, game database) is stored in `%LOCALAPPDATA%\EuropeanBasketballManager`.
To use another folder, run with `--Ebm:Paths:DataRoot=D:\EbmData`.

## Documentation

| Document | Content |
|---|---|
| [docs/ROADMAP.md](docs/ROADMAP.md) | Development roadmap and step specifications |
| [docs/TECHNICAL_ARCHITECTURE.md](docs/TECHNICAL_ARCHITECTURE.md) | Stack, layers, conventions, build commands |
| [docs/DATABASE_SCHEMA.md](docs/DATABASE_SCHEMA.md) | Entities, fields, relationships, constraints, migrations |
| [docs/IMPORT_FORMATS.md](docs/IMPORT_FORMATS.md) | CSV/JSON import formats, validation rules, database editor |
| [docs/COMPETITION_FORMATS.md](docs/COMPETITION_FORMATS.md) | Competition rules schema, supported formats, adding a league |
| [docs/PLAYER_MODEL.md](docs/PLAYER_MODEL.md) | Player fields, attributes, tendencies, rating and position formulas |
| [PROJECT_STATE.md](PROJECT_STATE.md) | Current project status |
| [CHANGELOG.md](CHANGELOG.md) | Changes per step |
| [logs/](logs/) | Detailed log of each development step |
