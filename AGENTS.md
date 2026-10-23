# Deep Delver contributor notes

Keep simulation code in `DeepDelver/Game.cs` independent of console rendering. The UI sends commands to `Game.Act`; every accepted action spends one turn. Preserve seeded determinism and the versioned save schema. Run `dotnet test` and `dotnet build` before committing.
