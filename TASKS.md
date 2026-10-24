# Release tasks

- [x] Port deterministic domain model to C# / .NET 8
- [x] Port connected persistent dungeon generation, encounters, and objective
- [x] Port turn loop, combat, items, AI, hunger, status, and win/loss
- [x] Build terminal UI, save/resume, scores, and developer fixture
- [x] Build and run .NET test suite (`tests/TestRunner.csproj` standalone runner; see HANDOFF.md for why `dotnet test` itself cannot resolve MSTest offline)
- [x] Document building and shipping the program as an executable (`dotnet build DeepDelver` / `-c release` launcher plus its `.dll` folder; see README and HANDOFF.md)
- [x] Cover terminal input encoding from the repository (`tests/input-check.py`: real-pseudo-terminal gate over the built game, plus piped-stdin equivalence and the `--dev` fixture; exit code = failed checks)
