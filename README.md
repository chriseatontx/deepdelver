# Deep Delver

An original C# / .NET 8, ASCII, turn-based dungeon adventure. Recover the Star of the Deep from the tenth descent and return to the surface.

## Run

```bash
dotnet run --project DeepDelver
```

Choose Warden, Arcanist, or Ranger and optionally enter a seed. Move with arrows or numpad; `.` rests, `g` picks up, `i` manages inventory, `s` searches, `<`/`>` use stairs, `S` saves, `L` loads, and `?` shows controls. At a terminal one keypress acts at once (arrows, numpad 1-9 and `.` need no Enter); words such as `equip 3`, `i` or `q` finish with Enter. Saves and score history live in the user application-data directory, not in the repository.

## Build

```bash
dotnet build DeepDelver                 # -> DeepDelver/bin/Debug/net8.0/
dotnet build DeepDelver -c release      # -> DeepDelver/bin/release/net8.0/
```

The project sets `<OutputType>Exe</OutputType>`, so each build emits an executable form: a
native launcher named `DeepDelver` beside `DeepDelver.dll`, `DeepDelver.runtimeconfig.json`,
a `.deps.json` and a `.pdb`. The config folder is spelled `Debug` with a capital D but `release`
with a lowercase one, which is MSBuild's own naming. The launcher is a real ELF binary, yet it
loads the sibling `.dll` when it starts, so the unit you ship is the folder rather than one file:
the launcher, `DeepDelver.dll` and `DeepDelver.runtimeconfig.json` run from any location once
copied together (the `.deps.json` and `.pdb` are not needed at runtime), while a launcher whose
`.dll` is absent fails with `The application to execute does not exist`. You can also run a build directly instead
of copying it: `dotnet run --project DeepDelver` uses the debug tree, `dotnet run -c release --project
DeepDelver` the release one. A single-file native binary is not available with this SDK: `dotnet build
DeepDelver --native` is rejected with `MSBUILD : error MSB1001: Unknown switch`, so single-image AOT
output needs the newer SDK's `dotnet build --native` or `docker build`. Building inside the
repository with `-o <dir>` leaves an untracked folder `.gitignore` does not cover, so pass an
output path outside the repo or clean it up.

## Check

```bash
dotnet build DeepDelver
dotnet run --project tests/TestRunner.csproj   # engine: Checks: 332  Failures: 0
python3 tests/input-check.py                   # terminal input: input checks: 10  failures: 0
```

Both exit with the number of failed checks, so they are CI gates. `dotnet test`
is not resolvable in an SDK-only distribution; see HANDOFF.md.
