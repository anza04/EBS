# Save files

Step 14. Interface `Ebm.Services.Saves.ISaveGameService`; implementation `Ebm.Persistence.SaveGameStore`; screen
*Saved games* (`SaveGamesViewModel`, `Themes/Saves.xaml`).

## 1. Model

* The **live game** is the game database `<DataRoot>\databases\ebm.db`. Every action is stored there at once, so the
  game is never lost when the application closes (only an unfinished live match is).
* A **save** is a copy of the live game: `<DataRoot>\saves\<name>.ebmsave` (a SQLite database). Names: 1–60 letters,
  digits, spaces, `-` and `_`.
* Copies use the SQLite online backup API, so a save is consistent even while the game database is open, and loading
  overwrites the live database in place (the file is never deleted under open connections; the connection pools are
  cleared before and after).

## 2. Operations

| Operation | Behaviour |
|---|---|
| Save | Backup of the live game to `<name>.ebmsave.tmp`, then renamed (an interrupted save never replaces a good one). An existing name is refused unless overwriting (the screen overwrites). |
| Load | Refuses files that are not game databases or whose schema is newer than the game's migrations. Copies the save over the live game, applies pending migrations (older saves are upgraded) and reloads the career date (`ICareerService.LoadAsync`). Unsaved progress is lost. |
| Delete | Removes the save file. |
| New game | Replaces the live game with an empty, migrated database (saves are kept). |
| List | Every `*.ebmsave`, newest first, with the career ("manager · club · date") and schema version read from the file. |

## 3. Compatibility

Saves carry their schema version (`__EFMigrationsHistory`). Any save from Step 02 onwards can be loaded: it is
upgraded by the same migrations as the live database (proven by `DatabaseInitializerTests`). Saves from a newer game
version are refused with a message.
