# Modding and custom databases

Step 33. How to build, share and install custom databases: the export format, community packages (`.ebmpack`),
database validation and the rules that keep a game in progress safe. Code: `src/Ebm.Services/Modding/IModding.cs`
(contracts), `src/Ebm.DataImport/Modding` (`DatabaseExportService`, `DatabaseValidator`, `ModPackageService`), screen
*Mods and export* (`ModsViewModel`, `Themes/Mods.xaml`). Tests: `tests/Ebm.Tests.Integration/Modding/ModdingTests.cs`.

## 1. What can be customised

| Content | How | Format reference |
|---|---|---|
| Countries, arenas, clubs | Import files or the editor (*Clubs*) | docs/IMPORT_FORMATS.md §2 |
| Leagues, divisions, competitions | Import files or the editor (*Leagues and competitions*) | docs/IMPORT_FORMATS.md §2 |
| League and cup rules | The `rules` object of a competition (points, playoffs, promotion/relegation, cup rounds, registration limits) | docs/COMPETITION_FORMATS.md, docs/CUP_FORMATS.md |
| Seasons and participants | Import files | docs/IMPORT_FORMATS.md §2 |
| Players (profile, 27 attributes, tendencies), contracts, past club spells | Import files or the editor (*Players*, *Contracts (editor)*) | docs/IMPORT_FORMATS.md §2, docs/PLAYER_MODEL.md |

Not part of the format: staff, staff contracts, squad roles, tactics, fixtures, matches, statistics, standings, news,
finances ledger and history records. The game generates or derives them (fixtures by the competition engine, staff
from the staff market, history from played seasons).

## 2. Packages (`.ebmpack`)

A package is a zip file with the extension `.ebmpack` that contains a manifest and one or more import files.

```
Serie A 2026-27.ebmpack
├── ebmpack.json      manifest (required, at the root)
├── dataset.json      ebm-dataset JSON (docs/IMPORT_FORMATS.md §1)
└── players.csv       optional CSV files, named after their record type
```

`ebmpack.json`:

```json
{
  "format": "ebm-package",
  "formatVersion": 1,
  "name": "Serie A 2026-27",
  "author": "Your name",
  "version": "1.0",
  "description": "Real Serie A clubs with updated rosters.",
  "minGameVersion": "0.33.0",
  "files": [ "dataset.json", "players.csv" ]
}
```

| Field | Required | Rule |
|---|---|---|
| `format` | yes | `ebm-package` |
| `formatVersion` | yes | `1` (a newer version is refused with "Update the game") |
| `name` | yes | 1–60 characters |
| `author`, `version`, `description` | no | Free text shown in the game |
| `minGameVersion` | no | Oldest game version that can install the package (`major.minor.patch`); empty = any |
| `files` | yes | At least one `.json` or `.csv` file **at the root of the zip**; files are imported together in dependency order (docs/IMPORT_FORMATS.md §1) |

The game reads the listed files only. It never runs anything from a package. Paths with folders or `..`, other file types
and files over 200 MB are refused.

Packages live in the mods folder `<DataRoot>\mods` (`%LOCALAPPDATA%\EuropeanBasketballManager\mods` by default).
On the *Mods and export* screen, *Add package…* copies a file there and *Open folder* opens it.

### Making a package

* **From the game:** set up the world (import, editor), then *Create a package* (name, author, version,
  description). The game exports the database to `dataset.json`, writes the manifest with `minGameVersion` = this
  game's version and saves `<name>.ebmpack` in the mods folder.
* **By hand:** write the import files (templates in `data/templates`), add `ebmpack.json` and zip them. Give every
  club, player and competition an `id`, so a later version of the package updates them instead of adding copies.

## 3. Export

*Export to JSON…* writes one `ebm-dataset` (version 1) file with all the importable records of the current game, using
the import field names, so the file imports back unchanged. An exported database imported into a new game and exported
again gives an identical file (`An_exported_database_imports_into_a_new_game_unchanged`).

What is written: countries, arenas, clubs, leagues, divisions, competitions (rules as a JSON object), seasons,
participants, players who have not retired, their **active** player contracts (running or starting later) and their closed
club spells. Records keep their IDs. Sections and records are sorted (by code, name or date, then ID), so two exports of
the same data compare line by line.

Not written: retired players, ended contracts (expired, terminated, renewed, transferred, loaned out), loan
agreements, staff and all generated or derived game state (§1).

## 4. Validation

Three levels:

1. **Records** (every import, editor save and package check): fields, ranges, references and duplicates. A reference
   to a record that is not in the database or in the same import is rejected:
   `'playerId' refers to an unknown player …` (docs/IMPORT_FORMATS.md §3).
2. **Package** (*Check*): the manifest rules (§2) and a dry run of the import against the current game. Nothing is
   written. A package can be installed only when the manifest is valid, every file was read and no record is rejected.
3. **Database** (*Check now*, and after every install): rules that single records cannot break on their own.

   | Severity | Check |
   |---|---|
   | Error | A competition's rules cannot be read |
   | Error | A player has two active contracts whose dates overlap |
   | Error | A retired player is still under contract |
   | Error | A club in a current competition has fewer than 5 players under contract today (it cannot play) |
   | Warning | A club in a current competition has fewer than 8 players under contract today |
   | Warning | A league competition is not linked to a division (no promotion or relegation) |
   | Warning | A competition's country differs from its league's country, or a domestic competition has a club of another country |
   | Warning | A club's home arena is in another country |

## 5. Installing and protecting the game in progress

| Install | What happens |
|---|---|
| **Into current game** | 1. The game in progress is saved as `Before <package> <date time>` (a normal save, see *Saved games*). 2. The package is imported with *update existing records* and *all or nothing*. 3. The database is validated. If the import writes nothing, the restore point is deleted and the game is unchanged. If the import adds **new** validation errors (errors already present before are ignored), the restore point is loaded at once and the install is reported as undone. |
| **As a new game** | 1. Restore point as above. 2. The game is replaced by an empty database (saves are kept). 3. The package is imported (add new, all or nothing). If the import fails, the restore point is loaded and the previous game comes back. Validation warnings and errors are shown but do not undo the install (fix them in the editor). Then start a career. |

Install as a new game asks for a second click, since it replaces the game in progress.

Guarantees, each proven by `ModdingTests`:

* **The game in progress is never left half-changed.** Imports write in one transaction, and anything that writes is
  preceded by a restore point.
* **Existing careers stay the same.** A save is a separate copy of a game, so packages installed later (as a new game
  or into another game) never change it. Installing into the current game keeps the career's manager, club, season
  and date. Records with the same ID are updated, so a package can change the club the career manages (name, budget,
  squad). Back up with a save first if that matters, or use the restore point.
* Invalid references are rejected before anything is written.

## 6. Compatibility rules

* **Format versions.** `ebm-dataset` version 1 and `ebm-package` formatVersion 1. A later game reads these versions
  for as long as possible. A breaking change raises the version, and older games refuse the file with a message.
* **Fields.** New optional fields may be added in later versions. An older game ignores unknown fields and sections
  and reports them as notes (they do not block an install). Fields are never renamed within a version.
* **IDs** are the identity of a record across packages and versions: keep them stable. Without an `id`, records are
  matched by their duplicate keys (docs/IMPORT_FORMATS.md §4), and a second install is then rejected as a duplicate.
* **Game version.** `minGameVersion` blocks installation on older games. A package made in this game gets the current
  version (0.33.0 and later).
* **Saves** are not packages. They hold the whole game state, including careers, and are handled on *Saved games*
  (docs/SAVE_FILES.md).
