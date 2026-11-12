# Release and installation (Milestone 1)

## 1. Building the Windows release

```powershell
powershell -ExecutionPolicy Bypass -File build/publish.ps1
```

The script:
1. publishes `src/Ebm.Desktop` for `win-x64` as a **self-contained single-file** executable (no .NET install needed;
   native SQLite included) into `artifacts/publish/win-x64` (`Ebm.Desktop.exe`, `appsettings.json`, `data\`);
2. runs the executable with `--smoke-test` against a throw-away data folder (database created and migrated, main window
   rendered, exit code 0) and fails the build otherwise;
3. zips the folder to `artifacts/EuropeanBasketballManager-<version>-win-x64.zip` (about 60 MB).

`artifacts/` is ignored by git.

## 2. Installing and running

1. Unzip the archive anywhere (e.g. `C:\Games\EuropeanBasketballManager`).
2. Run `Ebm.Desktop.exe`. Windows SmartScreen may warn about an unsigned application (*More info → Run anyway*).
3. User data (game database, saves, logs) goes to `%LOCALAPPDATA%\EuropeanBasketballManager`; use
   `Ebm.Desktop.exe --Ebm:Paths:DataRoot=D:\EbmData` for another folder. Uninstall = delete the folder (and the data
   folder to remove saves).

## 3. First career

1. *Import* tab: import `data\sample\italy-sample.json`, then `data\sample\italy-squads.json`.
2. *Career* tab: choose a club, enter a name, *Start career*.
3. Manage the club in *Squad*, *Tactics*, *Club contracts* and *Transfer market*.
4. *Continue* until match day; play the match live in *Matches* (or *Play match* for an instant result), then
   *Continue* again. *Assistant manages (holiday)* lets the assistant run the club and play its matches.
5. When the season is complete: *Season review*, then *Start next season* and continue through the offseason.
6. *Saved games*: *Save* a copy of the game, *Load* it later, *New game* to start over.

## 4. Release checklist (Step 14)

| Check | How |
|---|---|
| Build Debug/Release without warnings | `dotnet build -c Release` |
| All tests | `dotnet test` (unit, integration, launch) |
| Long simulation | `EBM_SIM_SEASONS=6 dotnet test --filter CareerSimulation` |
| Executable starts on a clean machine profile | `build/publish.ps1` smoke test |
| Career creation, management, matches, transfers, full season, second season, save/load | `MilestoneOneTests` |
