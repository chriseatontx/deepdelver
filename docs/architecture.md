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
