# HANDOFF - Deep Delver C#/.NET 8 port

Date: 2026-10-23
Working directory: /home/chris/code/testhack

## Current objective

Finish the in-progress port (clean build, working terminal UI, engine harness)
and explain and fix why arrow keys never moved the hero in the real interactive
run (`dotnet run --project DeepDelver`). All of it is done and verified: the
engine harness reports `Checks: 332   Failures: 0`, and a new real-pseudo-
terminal input gate `tests/input-check.py` reports `input checks: 10   failures:
0`.

## Completed tasks

- Ported domain model, generation, combat, AI, items, hunger, status, save and
  win conditions: previously done (see TASKS.md); `DeepDelver/Game.cs` is the
  whole simulation and was not modified during this session.
- Built `DeepDelver/Program.cs`: thin console renderer and input adapter over
  the existing engine API (`Game.Glyph(x, y)`, `Game.Act(command)`, `Game.Save`/
  `Game.Load`, `Program.Dev` fixture, app-data save/score resolution). Arrow keys
  were reworked twice in this session (see "Arrow-key input"): the parser now
  accepts every terminal arrow encoding, and the adapter has two input paths so
  a real terminal gets raw single-byte reads instead of editor-backed `Console
  ReadLine`.
- Added `tests/input-check.py`: a real-pseudo-terminal (pty) gate for the
  adapter's input encoding, complementing the engine-only harness.
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

Commands (run with the system `/usr/bin/dotnet`, SDK 8.0.426; the repo also
keeps an untracked launcher at `.tools/dotnet/dotnet` over vendored SDK packs,
and that one rejects some of these invocations, so the system tool is what was
used here):

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
- `python3 tests/input-check.py` -> `input checks: 10   failures: 0`, exit 0
  (seed 4242, Warden). The arrow plan up x2 / down x2 / left x7 / right x3 ends
  at column 39 on turn 14 for numpad digits, for arrows with Enter, and for
  arrows with **no** Enter at all; the `ESC O A`, `ESC [ 1 ; 3 A` and `ESC ESC [
  A` forms each spend 2 of 4 presses with no complaint; `i` prints the
  inventory block and `quit` exits; and `stty -g` captured immediately before
  and after the run is byte-identical.
- Saves go to `~/.local/DeepDelver/save.json`, outside the repository. The
  verification and clone runs each wrote a save there, so `save.json` currently
  exists in that directory. `scores.txt` now exists there too: the pty gate runs
  the `--dev` fixture, which ends in death and appends one line
  (`Warden 0 16 dead`). Quitting with `q` still records no score, so ordinary
  scripted runs leave the file untouched. Either way this state stays outside the
  repository, and `git status` stays clean.
- `git push` -> `origin/main` matches the local `HEAD`, and `tests/Game.cs` is
  stored as a symlink (mode `120000`) so the clone compiles the real engine
  source; a fresh clone of the pushed tree builds and passes the runner. Commits
  carry the `opencode` author identity (see the version-control note below).

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
arrow bytes now say "Arrow sequence not understood." instead of the generic
`Unrecognized` line, and the `?` help text says the same. `Game.cs` is untouched:
arrows are translated in the adapter before the engine sees a symbolic command.
(After the second round of fixes, both texts dropped the "press Enter after each
arrow" advice: arrows act without Enter, so the hint is now "Use numpad 1-9, or
type up/down/left/right.")

Compiler trap found while fixing this: a lone `"\u001b"` **string literal used as
an argument** behaves as an empty string (`"abc".IndexOf("\u001b")` returns 0, not
-1, and `LastIndexOf` returns the length), so `line.LastIndexOf("\u001b")` returned
`line.Length` and `Substring(esc + 1)` threw `ArgumentOutOfRangeException` (exit
134, "Unhandled exception"). The standalone literal is fine (`"\u001b".Length` is
1) and longer literals in the same call work (`StartsWith("\u001b[")` is correct),
so the workaround is character scanning against the `'\u001b'` **char literal**,
which compares correctly against raw stdin bytes 27.

Verified after the first round of fixes (scripted stdin into
`dotnet run --project DeepDelver/DeepDelver.csproj`, seed 4242, Warden; those
probes were throwaway `/tmp/opencode` scripts and are superseded by the repo-resident
gate `tests/input-check.py`):

- `ESC [ A/B/C/D`, `ESC O A/B/C/D`, `ESC [ 1 ; 3 A/B/C/D` and Alt-prefixed
  `ESC ESC [ A/B/C/D` are all accepted; each advances the turn counter.
- Twelve attempts in each direction land the hero on the same tile and spend the
  same number of turns as the matching numpad key: up 2 tiles, down 2 tiles,
  left 7 tiles, right 3 tiles, identical for numpad/CSI/SS3/modifier forms.
- Bare letters `A B C D` stay `Unrecognized: C. Type ? for controls.` and spend
  no turn, and a lone `ESC` prints the arrow hint without crashing (exit 0).

### Why arrows still did not move the hero in the real interactive run

That first round did not close the user's complaint, and scripted stdin could
never show the reason: piping has no line editor. Replaying the same session
through `os.forkpty` (child stdin is a genuine tty) exposed it:

1. With stdin on a tty, `Console.ReadLine()` is editor-backed. Its first read
   emits the readline advertisement `ESC [ ? 1 h =` (application keypad plus vi
   mode), and `ESC [ A` is then swallowed by that line editor as a
   history-navigation macro. `ReadLine` hands the game an **empty line**, so
   `Move` returns `""`, `Game.Act("")` is rejected and spends no turn, and the
   hero never moves. The escape sequence never reaches the parser at all.
2. The only feedback that run produced (`Arrow sequence not understood.`) was
   written immediately before the next redraw, whose leading `Console.Clear()`
   erases the screen, so nothing persistent remained in the terminal. That is
   why the user saw a map that simply ignored the arrow key.
3. Piped stdin has no editor, so the same bytes reach `Arrow` intact - which is
   precisely why every earlier scripted probe reported the arrows as accepted
   while the interactive game still ignored them.

The adapter now has two input paths, chosen by asking `[ -t 0 ]` whether stdin is
a tty:

- tty: `EnterRaw()` captures the current settings with `stty -g` into
  `<app-data>/.tty-settings`, then re-applies them through `stty` with `-icanon
  -echo min 1 time 0`, which makes the tty deliver one byte at a time with no
  editor. Bytes are read from `/dev/tty` through
  `System.IO.File.OpenRead("/dev/tty")` and `FileStream.ReadByte()`, with a
  one-slot push-back for over-reads. An arrow escape acts as soon as its final
  letter arrives; numpad digits and `.` act on a single keypress; letters and
  every other first byte read an Enter-terminated line, so words such as
  `equip 3` still work. Because echo is off, the adapter echoes that line itself
  (with a `"\b \b"` correction for backspace).
- not a tty: unchanged `Console.ReadLine()`, so scripted verification still works.

`LeaveRaw()` writes the captured settings back through `stty` and is called from
a `finally` around the whole run, so the terminal is handed back on every exit
path, including an exception. `Game.cs` is untouched.

Verified after the second round of fixes (the built executable
`DeepDelver/bin/Debug/net8.0/DeepDelver` inside a real pty, seed 4242, Warden;
also recorded by `tests/input-check.py`):

- The hero reaches column 39 on turn 14 for numpad digits, for arrow bytes
  followed by Enter, and for arrow bytes with **no Enter at all**; no complaint
  string appears anywhere in the transcript.
- `ESC O A` (application mode), `ESC [ 1 ; 3 A` (modified) and `ESC ESC [ A`
  (Alt-prefixed) each spend 2 of their 4 presses - the other 2 are blocked by the
  wall, and a blocked step spends no turn - with no complaint.
- `i\r` prints the inventory block and `quit\r` exits 0, so Enter-terminated
  words still work on a raw tty.
- `stty -g` captured in the shell immediately before and immediately after the
  run is byte-identical, i.e. the program gave the terminal settings back.

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
  docs are committed there. The repository's local identity is now
  `opencode <177364689+chriseatontx@users.noreply.github.com>` (set with
  `git config --local user.name/email`), so future commits are authored by
  `opencode` and GitHub resolves that noreply email to your own account. The
  earlier commits were authored `chris <chris@localhost>`; the whole history was
  re-authored to the new identity and force-pushed, so the SHAs on `origin/main`
  are the rewritten ones and the previous `chris`-authored commits are no longer
  referenced by any branch.
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
- The raw input path assumes POSIX tooling: `Sh` runs `sh -c` to test `[ -t 0 ]`,
  and the raw mode itself needs `stty` and `/dev/tty`. It was verified only on
  Linux. On a system without `sh` the `Sh` helper throws instead of falling back,
  so Windows input handling is unverified; without a tty the adapter keeps the
  line-oriented `Console.ReadLine()` path, where an arrow escape still needs
  Enter before the parser sees it. The engine contract is unaffected either way.
- `tests/input-check.py` needs python3 (for `os.forkpty`, the only thing here that
  reproduces a real tty) plus a built executable at
  `DeepDelver/bin/Debug/net8.0/DeepDelver`, so run `dotnet build DeepDelver`
  first. The engine gate stays pure C#.
- The pty gate runs the `--dev` fixture, which appends one line to the score
  history in the real application-data directory (`~/.local/DeepDelver/scores.txt`
  here). That directory is outside the repository, so `git status` stays clean,
  but the scratch history is not reset by the gate. The `.tty-settings` file the
  adapter writes into that directory is deleted by `LeaveRaw` on exit.

## Unfinished and suggested next steps

1. If a full SDK becomes available, move the checks to MSTest
   (`tests/TestRunner.cs` -> `*Test.cs` files under a `DeepDelver.Tests`
   package project) so the AGENTS.md `dotnet test` instruction is satisfied
   directly; until then the runner is the equivalent gate.
2. Remote work is done: `origin` is
   `https://github.com/chriseatontx/deepdelver` and `main` is pushed and tracked
   (`git push` pushed the work committed in this session; `--force-with-lease` was
   only needed for the identity rewrite). The push authenticated through
   the `gh` CLI credential helper already installed for `github.com`. Because the
   author identity was changed to `opencode`, the whole branch was re-authored and
   replaced with `git push --force-with-lease`; trees, messages and dates are
   unchanged, only author/committer metadata differs, so treat a `chris`-authored
   SHA as stale rather than as a missing commit.
3. Optional engine follow-up, deliberately not done here: stair travel
   (`descend`/`ascend`) returns before the end-of-turn step in `Game.Act`, so
   it costs zero turns and zero hunger, while AGENTS.md says every accepted
   action spends one turn. Changing that is an engine change and would ripple
   through turn, hunger, save and score accounting.
4. Optional: make `docs/architecture.md` the authority for the two dangling
   comment references above, or restore those note files.
5. Done: input-encoding verification is repo-resident. `tests/input-check.py` is
   a checked-in gate that drives the built game through a real pseudo-terminal
   (`os.forkpty`) plus a piped-stdin equivalence pass and the `--dev` fixture; its
   exit code is the number of failed checks. The throwaway `/tmp/opencode` probes
   are outside the repository and were never committed; the checked-in gate
   replaces them - run `dotnet build DeepDelver && python3 tests/input-check.py`
   from a clone to re-verify arrow handling.
6. If a POSIX-free CI is required, the raw path needs a Windows-specific single-key
   reader instead of `sh`/`stty`/`/dev/tty`. That would touch only the input half
   of the `Program.cs` adapter, and cannot be verified on this machine, so it was
   not attempted.
