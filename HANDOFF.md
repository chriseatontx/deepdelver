# HANDOFF - Deep Delver C#/.NET 8 port

Date: 2026-10-23
Working directory: /home/chris/code/testhack

## Current objective

Finish the in-progress port: a clean build, a working terminal UI, and a test
harness that actually exercises the engine. The remaining checklist item
("Build and run .NET test suite") is now complete and verified.

## Completed tasks

- Ported domain model, generation, combat, AI, items, hunger, status, save and
  win conditions: previously done (see TASKS.md); `DeepDelver/Game.cs` is the
  whole simulation and was not modified during this session.
- Built `DeepDelver/Program.cs`: thin console renderer and input adapter over
  the existing engine API (`Game.Glyph(x, y)`, `Game.Act(command)`, `Game.Save`/
  `Game.Load`, `Program.Dev` fixture, app-data save/score resolution). Arrow keys
  were reworked in this session (see "Arrow-key input") so the adapter now
  accepts every terminal arrow encoding, not just one of them.
- Completed the test harness `tests/TestRunner.cs` (it was missing every test
  body). It compiles the real engine source, not a copy, and reports
  `Checks: 332   Failures: 0` with exit code 0.
- Replaced the unusable hand-written `tests/TestRunner.csproj` with a clean SDK
  project (the previous file was not readable MSBuild XML; `dotnet build` failed
  with `MSB4025`/`MSB4067`), and dropped the `CompileAdditionalSources` block.

## Work state

- `tests/Game.cs` is a symlink to `../DeepDelver/Game.cs`, which is how the
  engine reaches the test assembly. There is no separate Engine.cs and there is
  no `DeepDelver/Engine.csproj`.
- `dotnet build tests/TestRunner.csproj` reports `0 Warning(s)` / `0 Error(s)`;
  `dotnet build DeepDelver` reports `0 Error(s)` with 4 pre-existing nullable
  warnings (CS8602/CS8604) on the two `env[...].ToString()` lookups in
  `Terminal.AppDir`, which exist in the committed file as it stands. The runner
  exits 0.
- Stair rules are asserted as fixtures (the hero is repositioned onto a stair
  through the public state accessors) instead of a survival walk, because a
  BFS walk dies before depth 3 on every seed. The temporary probe that proved
  this was deleted, and the helper walk code (`Advance`, `DistanceField`,
  `StairsConnected`) stays because the surface/monster checks still use it.

## Verified

Commands (the repo ships its own SDK launcher at `.tools/dotnet/dotnet`, SDK
8.0.426, and there is no system `dotnet` on this machine):

- `dotnet build DeepDelver` -> Build succeeded, 0 error(s) (4 pre-existing
  nullable warnings on `AppDir`'s environment lookups).
- `dotnet build tests/TestRunner.csproj` -> Build succeeded, 0 warning(s),
  0 error(s).
- `dotnet run --project tests/TestRunner.csproj` -> `Checks: 332 Failures: 0`,
  exit code 0.
- `dotnet run --project DeepDelver --dev` -> prints
  `Developer fixture: scripted session, not a normal new game.` then a
  deterministic map (seed 4242, Warden, depth 0, turn 6).
- `dotnet run --project DeepDelver` (piped stdin) -> menu, `?` help, rest,
  pickup, `i`, `s`, `S`/`L` (`Saved.` then `Loaded.`), `q` exit 0.
- Saves go to `~/.local/DeepDelver/save.json`, outside the repository. That
  file was created only by these verification runs and has been deleted; the
  application-data directory is recreated on the next save.
- `git push` -> `origin/main` matches the local `HEAD`, and `tests/Game.cs` is
  stored as a symlink (mode `120000`) so the clone compiles the real engine
  source; a fresh clone of the pushed tree builds and passes the runner.

Checks cover: construction and surface/monster fields, stair connectivity,
deterministic floor generation and class identity, movement/refusals/
blocked-hit accounting, rest and hunger, pickup and drop and quiver
bookkeeping, melee/spell/fire/kill accounting, search and equipment, descent
and ascent rules including the guardian chamber and the Star win condition,
save round-trip and corrupt-save rejection, and the identical-script rule.

## Arrow-key input (why the arrows did not move the hero)

Asked: "arrow keys don't move the hero - was that ever tested?" No. Every earlier
input test piped single characters (`w`, `4242`, `.`, `g`, `S`, `L`, `?`), so an
arrow sequence never reached the parser; when one was piped it only ever printed
`Unrecognized`. Nothing in `tests/TestRunner.cs` covers the adapter either, so
arrow handling had no test coverage at all.

What the committed code did (commit `edfb388`, `Move` in `Program.cs`): a chain of
`line.StartsWith("\u001b[A")`-style tests, i.e. only the normal-mode CSI form
`ESC [ A-D`. Real terminals in application mode (screen, tmux, many GUI terminals
and Windows consoles) send `ESC O A-D`, and shift/alt-modified arrows send
`ESC [ 1 ; 3 A`, so those fell through to `Unrecognized`. That is why the README's
"arrows or numpad 1-9 move" contract was only half true.

The reworked adapter (`Terminal.Arrow` plus `Terminal.HasEscape` in `Program.cs`)
scans for `'\u001b'` by character, strips the introducer (`[`, `O` or a repeated
`ESC` for Alt-prefixed arrows), skips modifier parameters (`0-9`, `;`) and maps the
final letter (`a`/`b`/`c`/`d`, case-insensitive) to up/down/right/left. Unparseable
arrow bytes now say "Arrow sequence not understood. Press Enter after each arrow,
or use numpad 1-9." instead of the generic `Unrecognized` line, and the `?` help
text says the same. `Game.cs` is untouched: arrows are translated in the adapter
before the engine sees a symbolic command.

Compiler trap found while fixing this: a lone `"\u001b"` **string literal used as
an argument** behaves as an empty string (`"abc".IndexOf("\u001b")` returns 0, not
-1, and `LastIndexOf` returns the length), so `line.LastIndexOf("\u001b")` returned
`line.Length` and `Substring(esc + 1)` threw `ArgumentOutOfRangeException` (exit
134, "Unhandled exception"). The standalone literal is fine (`"\u001b".Length` is
1) and longer literals in the same call work (`StartsWith("\u001b[")` is correct),
so the workaround is character scanning against the `'\u001b'` **char literal**,
which compares correctly against raw stdin bytes 27.

Verified after the fix (scripted stdin into
`dotnet run --project DeepDelver/DeepDelver.csproj`, seed 4242, Warden; probes
live in `/tmp/opencode/inputprobe.py` and `/tmp/opencode/dirprobe.py`):

- `ESC [ A/B/C/D`, `ESC O A/B/C/D`, `ESC [ 1 ; 3 A/B/C/D` and Alt-prefixed
  `ESC ESC [ A/B/C/D` are all accepted; each advances the turn counter.
- Twelve attempts in each direction land the hero on the same tile and spend the
  same number of turns as the matching numpad key: up 2 tiles, down 2 tiles,
  left 7 tiles, right 3 tiles, identical for numpad/CSI/SS3/modifier forms.
- Bare letters `A B C D` stay `Unrecognized: C. Type ? for controls.` and spend
  no turn, and a lone `ESC` prints the arrow hint without crashing (exit 0).

## Known limitations

- `dotnet test` cannot run here. This is the SDK-only distribution (no
  `~/.cache/dotnet-package-cache`, so MSTest is not resolvable): plain
  `dotnet test` fails with `MSB1003: Specify a project or solution file`, and
  `dotnet test tests/TestRunner.csproj` exits 0 while running nothing. The
  working test entry point is the standalone runner:
  `dotnet build tests/TestRunner.csproj && dotnet run --project tests/TestRunner.csproj`.
  CI should gate on that runner, whose exit code is the number of failed
  checks.
- MSBuild rejects `dotnet build --project X`; the build target is positional
  (`dotnet build DeepDelver`), while `run` takes `--project`.
- This workspace had no version control when the harness work started; it was
  initialized with `git init -b main` on 2026-10-23 and the sources, tests and
  docs are committed there. The commit needed an author, so the repository has
  local identity `chris <chris@localhost>` (derived from the account name);
  change it with `git config --local user.name/email` before pushing anywhere
  that cares about attribution. The repository is now pushed to
  `https://github.com/chriseatontx/deepdelver` (`origin`, `main`), so that author
  string is already published and appears on GitHub as an unlinked `chris`.
- `.tools/dotnet` is intentionally not tracked: it is 578 MB / 4,704 files of
  vendored SDK-only dotnet tooling, kept repo-local so the build works offline.
  A fresh clone must provide its own .NET 8 SDK and put a launcher at
  `.tools/dotnet/dotnet`, or use a system `dotnet` with the same commands.
- `dotnet build --dev` is an MSBuild switch error ("For switch syntax, type
  MSBuild -help"), so `--dev` is a program argument, not a build flag: both
  `dotnet run --project DeepDelver --dev` and the explicit
  `dotnet run --project DeepDelver -- --dev` reach `Program.Main`.
- Two stale cross-references remain inside comments of `DeepDelver/Game.cs`:
  `DeepDelver/Engine Notes.md` and `../docs/ui-map.md`. Neither file exists in
  this repository; `docs/architecture.md` is the only documentation that
  survives. `Game.cs` was left untouched, so these remain as-is.

## Unfinished and suggested next steps

1. If a full SDK becomes available, move the checks to MSTest
   (`tests/TestRunner.cs` -> `*Test.cs` files under a `DeepDelver.Tests`
   package project) so the AGENTS.md `dotnet test` instruction is satisfied
   directly; until then the runner is the equivalent gate.
2. Remote work is done: `origin` is `https://github.com/chriseatontx/deepdelver`
   and `main` (both commits) is pushed and tracked, so `git push` is a no-op
   until more changes land. The push used the `gh` CLI credential helper; the
   published author is still `chris <chris@localhost>`, so re-run
   `git config --local user.name/email` and amend if you want GitHub to link the
   commits to your account.
3. Optional engine follow-up, deliberately not done here: stair travel
   (`descend`/`ascend`) returns before the end-of-turn step in `Game.Act`, so
   it costs zero turns and zero hunger, while AGENTS.md says every accepted
   action spends one turn. Changing that is an engine change and would ripple
   through turn, hunger, save and score accounting.
4. Optional: make `docs/architecture.md` the authority for the two dangling
   comment references above, or restore those note files.
5. The arrow-key verification lives in throwaway stdin probes under
   `/tmp/opencode` (`inputprobe.py`, `dirprobe.py`), so it is not re-runnable
   from a clone. Promote an equivalent check into the repository (a small stdin
   fixture runner, or a dedicated adapter assembly that includes `Program.cs`
   the way `tests/Game.cs` includes `Game.cs`) if CI must cover input encoding.
