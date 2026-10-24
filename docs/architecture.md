# Architecture

`Game.cs` owns all serializable state and accepts symbolic commands; it has no Console API dependency. A stored integer PRNG makes generation and saves reproducible. Floors are independently stored maps plus entities/items, so stair travel preserves changes. Travel re-anchors the hero to the arrival floor's own stair pair: `descend` (from a `>` marker) lands on the deeper floor's `<` marker, `ascend` (from a `<` marker) lands on the shallower floor's `>` marker, and both return before the end-of-turn step, so stair travel never advances the turn or hunger counters. `Program.cs` is a thin console renderer/input adapter. Saves are versioned JSON in the user application-data folder; scores use a separate bounded local history. The developer fixture is an explicit `--dev` command-line flag and never changes normal new-game creation.

## Testing and runtime flow

The harness in `tests/TestRunner.cs` is a standalone executable, not an MSTest
package: its entry point is `static int Main(string[] args)` and it returns the
number of failed checks as its exit code, so CI gates on the runner directly.
It reaches the simulation through `tests/Game.cs`, a symlink to
`../DeepDelver/Game.cs`, so the checks compile the shipping engine rather than a
copy of it, and the csproj carries no `CompileAdditionalSources` block.
`dotnet build tests/TestRunner.csproj` and `dotnet run --project tests/TestRunner.csproj`
are the two working test commands; `dotnet build` takes the project
positionally, while `dotnet run` takes `--project`.

`dotnet test` is not usable in this environment: the vendored SDK ships only
build/run packs, so MSTest cannot be resolved offline. Plain `dotnet test`
fails with `MSB1003: Specify a project or solution file`, and
`dotnet test tests/TestRunner.csproj` exits 0 while executing nothing, which
must not be read as a passing suite. The dotnet SDK launcher under `.tools/dotnet`
is repo-local tooling that `.gitignore` deliberately leaves out (it is 578 MB of
SDK packs, not part of the port), so a clone provides its own .NET 8 SDK and
runs the same commands; build outputs under `DeepDelver/bin`, `DeepDelver/obj`,
`tests/bin` and `tests/obj` are ignored likewise, and runtime save/score state
never lands in the repository at all.

Assertions are written against engine invariants that hold for every seed:
turn and hunger accounting, refusal versus acceptance, seeded geometry
equality, save round-trips, BFS stair connectivity, and input being frozen in
`Game.Act` once `Mode` is `dead` or `won`. Stair descent is asserted as a
fixture that repositions the hero onto the stair rather than as a survival
walk, because a greedy walk reaches depth 2 at best before the hero is killed;
that makes the descent rule a data-model and runtime-flow contract instead of a
playability claim. The engine's own dialect (`sealed` classes, `uint`/`ushort`
state, `class*`/`struct*` aliases) is deliberately not restyled: `sealed` is
legal on classes only, so methods that take or return `Floor`, `Monster` or
`Item` need explicit return types and the harness mirrors those types verbatim.

## Input encoding

Arrow keys are an adapter concern only: the engine still receives symbolic
commands. Terminals send arrows as `ESC [ A-D` in normal mode, `ESC O A-D` in
application mode, and `ESC [ 1 ; 3 A` with modifier parameters, so
`Terminal.Arrow` accepts all of them (plus the Alt-prefixed `ESC ESC [` form),
skipping introducers and numeric/`;` parameters and mapping the trailing letter
case-insensitively. Escape detection must scan characters against the `'\u001b'`
char literal: in this compiler a lone `"\u001b"` string literal collapses to the
empty string when passed as an argument, so `IndexOf`/`LastIndexOf`/`Contains`
calls using it silently misbehave (`LastIndexOf` returns the string length), and
`Substring(lastIndexOf(...) + 1)` then throws. The char literal compares
correctly against raw stdin, where an arrow arrives as bytes 27, 91, 67.

A second, deeper cause of the complaint was found in the real interactive run:
with stdin attached to a terminal, `Console.ReadLine()` is editor-backed (it
emits the `ESC [ ? 1 h =` vi-mode advertisement) and eats `ESC [ A` as a
history-navigation macro, so it returns an **empty line** and no move is ever
attempted; piped stdin never shows this, which is why every earlier scripted
probe passed. The adapter therefore keeps `ReadLine` for piped stdin but, when
`[ -t 0 ]` says stdin is a tty, captures the terminal settings with `stty -g`,
puts the tty in `stty -icanon -echo min 1 time 0`, and reads single bytes from
`/dev/tty`; the saved settings are handed back through `stty` on every exit path
via `try`/`finally`. Arrow escapes act as soon as the final letter arrives,
numpad digits and `.` act on the same single keypress, and Enter-terminated
words (`equip 3`, `i`, `q`) are still read whole because prompts must never
consume a single keystroke.

`tests/TestRunner.cs` compiles the engine only, so adapter behaviour is gated
separately by `tests/input-check.py`, which drives the built game through a real
pseudo-terminal (`os.forkpty`, so the child's stdin is a genuine tty) and also
re-checks piped stdin equivalence and the `--dev` fixture; its exit code is the
number of failed checks. Scripted stdin alone cannot reproduce the editor-backed
failure, so the pty is what makes the contract verifiable from a clone. The raw
path is POSIX-only (`sh -c` for the `[ -t 0 ]` probe, `stty`, `/dev/tty`) and was
verified on Linux; with no tty the adapter remains line-oriented.

## Build outputs

`DeepDelver.csproj` sets `<OutputType>Exe</OutputType>`, so each build emits an
executable form rather than a library: a native launcher named `DeepDelver`
beside `DeepDelver.dll`, `DeepDelver.runtimeconfig.json` (plus `.deps.json` and
a `.pdb`) inside `DeepDelver/bin/<config>/net8.0`. The config folder is
`Debug/net8.0` with a capital D but `release/net8.0` with a lowercase one, which
MSBuild naming rules cause; `tests/input-check.py` accepts either and drives the
newest. The launcher is a real ELF binary, yet it loads the sibling `.dll` at
startup, so it is not a single-image artifact: `DeepDelver` plus
`DeepDelver.runtimeconfig.json` run from any location once copied together, and
a launcher whose `.dll` is absent fails with `The application to execute does not
exist`, which the same gate turns into a clear message. A true single-file
native build (`--native`, AOT) is not available in the .NET SDK in use, which
answers `MSB1001: Unknown switch`; releasing that shape needs the newer SDK's
`dotnet build --native` or `docker build`, not the current toolchain.
