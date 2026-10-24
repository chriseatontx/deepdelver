# Deep Delver

An original C# / .NET 8, ASCII, turn-based dungeon adventure. Recover the Star of the Deep from the tenth descent and return to the surface.

## Run

```bash
dotnet run --project DeepDelver
```

Choose Warden, Arcanist, or Ranger and optionally enter a seed. Move with arrows or numpad; `.` rests, `g` picks up, `i` manages inventory, `s` searches, `<`/`>` use stairs, `S` saves, `L` loads, and `?` shows controls. At a terminal one keypress acts at once (arrows, numpad 1-9 and `.` need no Enter); words such as `equip 3`, `i` or `q` finish with Enter. Saves and score history live in the user application-data directory, not in the repository.

## Check

```bash
dotnet build DeepDelver
dotnet run --project tests/TestRunner.csproj   # engine: Checks: 332  Failures: 0
python3 tests/input-check.py                   # terminal input: input checks: 10  failures: 0
```

Both exit with the number of failed checks, so they are CI gates. `dotnet test`
is not resolvable in an SDK-only distribution; see HANDOFF.md.
