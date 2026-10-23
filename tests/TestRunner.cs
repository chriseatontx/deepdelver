using DeepDelver;

// Test harness for the Deep Delver engine.
//
// The simulation lives in DeepDelver/Game.cs; this harness compiles that same
// source into its own assembly (tests/Game.cs is a symlink to it) so the checks
// exercise the shipping engine rather than a copy of it.
//
// Exit code == number of failed checks, so CI can gate on the runner alone.

sealed class TestRunner {
    const int MapW = 56, MapH = 28;
    static string GameName = "Deep Delver";
    static int failures = 0;
    static int checks = 0;
    static Game g0 = new Game(1, HeroClass.Warden);

    static void Check(bool condition, string label) {
        checks++;
        if (!condition) {
            failures++;
            System.Console.WriteLine("FAIL " + label);
        }
    }

    static void ExpectTrue(bool condition, string label) { Check(condition, label); }
    static void ExpectFalse(bool condition, string label) { Check(!condition, label); }

    static void SameNumber(int actual, int expected, string label) {
        checks++;
        if (actual != expected) {
            failures++;
            System.Console.WriteLine("FAIL " + label + ": expected " + expected + ", got " + actual);
        }
    }

    static void Note(string text) { System.Console.WriteLine("NOTE " + text); }

    static bool SameString(string actual, string expected, string label) {
        if (actual == expected) return true;
        System.Console.WriteLine("FAIL " + label + ": expected " + expected + ", got " + actual);
        failures++;
        return false;
    }

    static bool HasMessage(Game g, string needle) {
        return g.State.Messages.Any(m => m.Contains(needle));
    }

    static int Key(int x, int y) { return y * MapW + x; }

    static bool Walkable(Floor f, int x, int y) {
        if (x < 0 || x >= MapW || y < 0 || y >= MapH) return false;
        var tile = f.Tiles[y][x];
        return tile == '.' || tile == '/' || tile == '<' || tile == '>' || tile == '^' || tile == '+';
    }

    static int ItemCount(Floor f) {
        var total = 0;
        foreach (var pile in f.Items.Values) total += pile.Count;
        return total;
    }

    static int ItemAt(Game g, int x, int y) {
        var f = g.State.Floors[g.State.Depth];
        if (f.Items.TryGetValue(x + "," + y, out var pile)) return pile.Count;
        return 0;
    }

    static int[] DistanceField(Floor f, int tx, int ty) {
        var dist = new int[MapW * MapH];
        for (var i = 0; i < dist.Length; i++) dist[i] = -1;
        var queue = new List<int>();
        var start = ty * MapW + tx;
        dist[start] = 0;
        queue.Add(start);
        var head = 0;
        while (head < queue.Count) {
            var cell = queue[head];
            head++;
            var x = cell % MapW;
            var y = (cell - x) / MapW;
            for (var dy = -1; dy <= 1; dy++) {
                for (var dx = -1; dx <= 1; dx++) {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || nx >= MapW || ny < 0 || ny >= MapH) continue;
                    var key = ny * MapW + nx;
                    if (dist[key] >= 0) continue;
                    if (!Walkable(f, nx, ny)) continue;
                    dist[key] = dist[cell] + 1;
                    queue.Add(key);
                }
            }
        }
        return dist;
    }

    static bool StairsConnected(Floor f) {
        if (f.UpX == f.DownX && f.UpY == f.DownY) return true;
        var dist = DistanceField(f, f.DownX, f.DownY);
        return dist[Key(f.UpX, f.UpY)] >= 0;
    }

    static bool SameFloorGeometry(Floor a, Floor b) {
        if (a.UpX != b.UpX || a.UpY != b.UpY) return false;
        if (a.DownX != b.DownX || a.DownY != b.DownY) return false;
        if (a.Monsters.Count != b.Monsters.Count) return false;
        if (ItemCount(a) != ItemCount(b)) return false;
        for (var y = 0; y < MapH; y++) {
            for (var x = 0; x < MapW; x++) if (a.Tiles[y][x] != b.Tiles[y][x]) return false;
        }
        for (var i = 0; i < a.Monsters.Count; i++) {
            if (a.Monsters[i].X != b.Monsters[i].X) return false;
            if (a.Monsters[i].Y != b.Monsters[i].Y) return false;
            if (a.Monsters[i].Hp != b.Monsters[i].Hp) return false;
            if (a.Monsters[i].Kind != b.Monsters[i].Kind) return false;
        }
        return true;
    }

    static void NewGameSurface(uint seed) {
        g0 = new Game(seed, HeroClass.Warden);
        var s = g0.State;
        var h = s.Hero;
        var f = s.Floors[0];
        ExpectTrue(s.Floors.Count == 1, "A new game starts with exactly one floor");
        SameNumber(s.Turn, 0, "A new game starts on turn zero");
        SameNumber(s.Depth, 0, "A new game starts at the surface");
        ExpectTrue(s.Mode == "playing", "A new game is playable");
        ExpectTrue(s.Seed == seed, "The requested seed is kept verbatim");
        ExpectTrue(s.Rng != seed, "Building the first floor advances the random stream");
        ExpectTrue(s.Messages.Count > 0, "A new game announces itself");
        SameNumber(h.Hp, 28, "A warden starts at full strength");
        SameNumber(h.MaxHp, 28, "A warden's maximum hit points are fixed");
        SameNumber(h.Mana, 4, "A warden starts with four mana");
        SameNumber(h.Level, 1, "A warden starts at level one");
        SameNumber(h.Xp, 0, "A warden starts with no experience");
        ExpectTrue(h.Hunger > 900 - 1, "The hero starts fed rather than starving");
        ExpectTrue(h.Inventory.Count >= 3, "The hero starts carrying gear");
        ExpectTrue(h.Weapon > 0, "The hero starts holding a weapon");
        ExpectTrue(h.Armor > 0, "The warden starts wearing armour");
        ExpectFalse(h.HasStar, "A fresh hero has not found the star");
        ExpectTrue(f.Depth == 0, "The starting floor is numbered zero");
        ExpectTrue(f.Tiles.Length == MapH && f.Tiles[0].Length == MapW, "Every floor is one fixed rectangle");
        ExpectTrue(f.Monsters.Count > 0, "The first floor holds monsters");
        ExpectTrue(ItemCount(f) > 0, "The first floor holds loot");
        ExpectTrue(f.UpX >= 0 && f.UpX < MapW && f.UpY >= 0 && f.UpY < MapH, "The ascent stair lies inside the map");
        ExpectTrue(f.DownX >= 0 && f.DownX < MapW && f.DownY >= 0 && f.DownY < MapH, "The descent stair lies inside the map");
        ExpectTrue(f.Tiles[f.UpY][f.UpX] == '<', "The ascent stair is marked on its tile");
        ExpectTrue(f.Tiles[f.DownY][f.DownX] == '>', "The descent stair is marked on its tile");
        ExpectTrue(StairsConnected(f), "The descent stair can be walked to from the ascent stair");
        ExpectTrue(Walkable(f, h.X, h.Y), "The hero starts on a walkable tile");
        ExpectTrue(g0.Glyph(h.X, h.Y) == '@', "The hero renders on its own tile");
        ExpectTrue(h.X == f.UpX && h.Y == f.UpY, "The hero starts on the ascent stair");
        var occupied = false;
        foreach (var m in f.Monsters) if (m.X == h.X && m.Y == h.Y) occupied = true;
        ExpectFalse(occupied, "The hero does not start inside a monster");
    }

    static void ClassVariants() {
        var warden = new Game(4242, HeroClass.Warden);
        var arcanist = new Game(4242, HeroClass.Arcanist);
        var ranger = new Game(4242, HeroClass.Ranger);
        SameNumber(warden.State.Hero.Hp, 28, "A warden begins with its own hardiness");
        SameNumber(arcanist.State.Hero.Hp, 18, "An arcanist begins with its own hardiness");
        SameNumber(ranger.State.Hero.Hp, 22, "A ranger begins with its own hardiness");
        SameNumber(warden.State.Hero.Mana, 4, "A warden begins with its own mana");
        SameNumber(arcanist.State.Hero.Mana, 14, "An arcanist begins with its own mana");
        SameNumber(ranger.State.Hero.Mana, 7, "A ranger begins with its own mana");
        ExpectTrue(warden.State.Hero.Class == HeroClass.Warden, "The warden keeps its class");
        ExpectTrue(arcanist.State.Hero.Class == HeroClass.Arcanist, "The arcanist keeps its class");
        ExpectTrue(ranger.State.Hero.Class == HeroClass.Ranger, "The ranger keeps its class");
        ExpectTrue(arcanist.State.Hero.Inventory.Count == 3, "The arcanist begins carrying three things");
        ExpectTrue(ranger.State.Hero.Inventory.Count == 4, "The ranger begins carrying four things");
        ExpectTrue(ranger.State.Hero.Armor > 0, "The ranger begins wearing armour");
        SameNumber(warden.State.Hero.X, arcanist.State.Hero.X, "Every class enters in the same column");
        SameNumber(warden.State.Hero.Y, arcanist.State.Hero.Y, "Every class enters in the same row");
        SameNumber(arcanist.State.Hero.X, ranger.State.Hero.X, "All three classes enter at the same column");
        ExpectTrue(SameFloorGeometry(warden.State.Floors[0], arcanist.State.Floors[0]), "One seed builds one dungeon regardless of class");
    }

    static void DeterministicSeeds() {
        var a = new Game(4242, HeroClass.Warden);
        var b = new Game(4242, HeroClass.Warden);
        ExpectTrue(a.State.Rng == b.State.Rng, "One seed primes one random stream");
        ExpectTrue(SameFloorGeometry(a.State.Floors[0], b.State.Floors[0]), "One seed builds one dungeon");
        var c = new Game(4243, HeroClass.Warden);
        ExpectFalse(SameFloorGeometry(a.State.Floors[0], c.State.Floors[0]), "Neighbouring seeds build different dungeons");
        var zero = new Game(0, HeroClass.Warden);
        ExpectTrue(zero.State.Seed == 1, "A zero seed is replaced by the smallest usable one");
    }

    static void MonsterFields() {
        g0 = new Game(4242, HeroClass.Warden);
        var f = g0.State.Floors[0];
        ExpectTrue(f.Monsters.Count > 2, "The entry hall holds several monsters");
        foreach (var m in f.Monsters) {
            var who = m.Kind;
            ExpectTrue(m.Id > 0, "Every monster has an identity (" + who + ")");
            ExpectTrue(m.Hp > 0, "Every monster starts alive (" + who + ")");
            ExpectTrue(m.MaxHp >= m.Hp, "Monsters report a consistent maximum (" + who + ")");
            ExpectTrue(m.Attack >= 0, "Monsters report a non-negative attack (" + who + ")");
            ExpectTrue(m.Defense >= 0, "Monsters defend without negative armour (" + who + ")");
            ExpectTrue(m.Xp >= 0, "Slaying awards no negative experience (" + who + ")");
            ExpectTrue(m.X >= 0 && m.X < MapW && m.Y >= 0 && m.Y < MapH, "Every monster stands inside the map (" + who + ")");
            ExpectTrue(Walkable(f, m.X, m.Y), "Every monster stands on a walkable tile (" + who + ")");
        }
    }

    static void RefusedCommands() {
        var g = new Game(4242, HeroClass.Warden);
        var turn = g.State.Turn;
        g.Act("descend");
        SameNumber(g.State.Turn, turn, "Descending where no stair stands costs no turn");
        SameNumber(g.State.Depth, 0, "A refused descent leaves depth unchanged");
        ExpectTrue(g.State.Messages[0] == "No stairs here.", "A refused descent says there is no stair");
        ExpectTrue(g.State.Mode == "playing", "A refused descent keeps the run playable");
        g.Act("ascend");
        SameNumber(g.State.Turn, turn, "Ascending the surface without the star costs no turn");
        SameNumber(g.State.Depth, 0, "A refused ascent leaves depth unchanged");
        ExpectTrue(g.State.Mode == "playing", "A refused ascent keeps the run playable");
        ExpectTrue(g.State.Messages[0] == "You cannot leave without the Star.", "Leaving without the star is refused plainly");
        var carried = g.State.Hero.Inventory.Count;
        g.Act("equip", 99999);
        ExpectTrue(g.State.Messages[0] == "That item is gone.", "An unknown item is refused plainly");
        SameNumber(g.State.Hero.Inventory.Count, carried, "An unknown item removes nothing");
        SameNumber(g.State.Turn, turn, "An unknown item costs no turn");
        ExpectTrue(g.State.Mode == "playing", "An unknown item ends nothing");
        g.Act("nonsense");
        SameNumber(g.State.Turn, turn, "An unknown command costs no turn");
        ExpectTrue(g.State.Mode == "playing", "An unknown command ends nothing");
        g.Act("left");
        SameNumber(g.State.Turn, turn + 1, "A legal step costs exactly one turn");
    }

    static void AcceptedActions() {
        g0 = new Game(4242, HeroClass.Warden);
        var accepted = 0;
        for (var step = 0; step < 4; step++) {
            g0.Act("right");
            accepted++;
            SameNumber(g0.State.Turn, accepted, "Each accepted move costs exactly one turn");
        }
        g0.Act("left");
        accepted++;
        SameNumber(g0.State.Turn, accepted, "Stepping back also costs exactly one turn");
        var mana = g0.State.Hero.Mana;
        var hunger = g0.State.Hero.Hunger;
        g0.Act("rest");
        accepted++;
        SameNumber(g0.State.Turn, accepted, "Resting costs exactly one turn");
        SameNumber(g0.State.Hero.Hunger, hunger - 1, "Every accepted action burns one point of hunger");
        ExpectTrue(g0.State.Hero.Mana >= mana, "Resting restores rather than spends mana");
        ExpectTrue(g0.State.Mode == "playing", "Resting keeps the run playable");
        var searchTurn = g0.State.Turn;
        g0.Act("search");
        SameNumber(g0.State.Turn, searchTurn + 1, "Searching costs exactly one turn");
        ExpectTrue(g0.State.Mode == "playing", "Searching keeps the run playable");
        g0.Act("blocked-attempt");
        ExpectTrue(g0.State.Turn == searchTurn + 1, "An unknown command costs no turn");
    }

    static void PickupRules() {
        g0 = new Game(4242, HeroClass.Warden);
        var carried = g0.State.Hero.Inventory.Count;
        var there = ItemAt(g0, g0.State.Hero.X, g0.State.Hero.Y);
        g0.Act("get", 0);
        if (there == 0) {
            SameNumber(g0.State.Hero.Inventory.Count, carried, "Picking up from an empty tile adds nothing");
            ExpectTrue(g0.State.Messages[0] == "Nothing here.", "An empty pickup says so plainly");
            ExpectTrue(g0.State.Turn == 0, "An empty pickup costs no turn");
        } else {
            SameNumber(g0.State.Hero.Inventory.Count, carried + 1, "One item per pickup");
            ExpectTrue(g0.State.Hero.Inventory[carried] != null, "The picked item is really carried");
            ExpectTrue(g0.State.Messages[0] == "You pick up " + g0.State.Hero.Inventory[carried].Name + ".", "Picking up names the item");
            ExpectTrue(g0.State.Turn == 1, "A real pickup costs exactly one turn");
        }
        ExpectTrue(g0.State.Mode == "playing", "Pickup rules never end the run");
    }

    // The stair rules are checked as fixtures: the hero is placed directly on a
    // stair through the public state accessors, because reaching a stair on foot
    // would depend on combat outcomes.
    static void DescentRules() {
        var g = new Game(4242, HeroClass.Warden);
        for (var depth = 0; depth < 9; depth++) {
            var f = g.State.Floors[g.State.Depth];
            SameNumber(g.State.Depth, depth, "Fixture descent starts on the expected floor");
            ExpectTrue(f.UpX >= 0 && f.UpX < MapW && f.UpY >= 0 && f.UpY < MapH,
                "Every floor records an ascent stair in bounds (depth " + depth + ")");
            ExpectTrue(f.DownX >= 0 && f.DownX < MapW && f.DownY >= 0 && f.DownY < MapH,
                "Every floor records a descent stair in bounds (depth " + depth + ")");
            ExpectTrue(f.Tiles[f.UpY][f.UpX] == '<' && f.Tiles[f.DownY][f.DownX] == '>',
                "Every floor keeps both stair markers (depth " + depth + ")");
            g.State.Hero.X = f.DownX;
            g.State.Hero.Y = f.DownY;
            var turn = g.State.Turn;
            var count = g.State.Floors.Count;
            g.Act("descend");
            SameNumber(g.State.Depth, depth + 1, "A completed descent moves exactly one floor down");
            SameNumber(g.State.Turn, turn, "Descending is instant and costs no turn");
            SameNumber(g.State.Floors.Count, count + 1, "Descending generates exactly one new floor");
            var nf = g.State.Floors[depth + 1];
            SameNumber(nf.Depth, depth + 1, "Each generated floor keeps its number");
            ExpectTrue(g.State.Hero.X == nf.UpX && g.State.Hero.Y == nf.UpY,
                "The hero arrives on the new ascent stair");
            ExpectTrue(nf.Tiles[nf.UpY][nf.UpX] == '<' && nf.Tiles[nf.DownY][nf.DownX] == '>',
                "A generated floor keeps both stair markers");
            ExpectTrue(!nf.Monsters.Any(m => m.X == nf.UpX && m.Y == nf.UpY),
                "Arrival is never inside a monster");
            ExpectTrue(g.State.Mode == "playing", "Descending never ends the run early");
        }

        var bottom = g.State.Floors[g.State.Depth];
        g.State.Hero.X = bottom.DownX;
        g.State.Hero.Y = bottom.DownY;
        var turnBefore = g.State.Turn;
        g.Act("descend");
        SameNumber(g.State.Depth, 9, "The guardian chamber is the deepest floor");
        SameNumber(g.State.Floors.Count, 10, "The full dungeon is ten floors");
        SameNumber(g.State.Turn, turnBefore, "Refusing the bottom stair costs no turn");
        ExpectTrue(g.State.Mode == "playing", "Refusing the bottom stair keeps the run alive");
        ExpectTrue(HasMessage(g, "The guardian chamber is the bottom."),
            "The guardian chamber explains that it is the bottom");

        g.Act("ascend");
        ExpectTrue(HasMessage(g, "No stairs here."), "Stairs are refused when the hero is not on one");
        SameNumber(g.State.Depth, 9, "A refused ascent keeps the hero on the floor");
        SameNumber(g.State.Turn, turnBefore, "A refused ascent costs no turn");
        g.State.Hero.X = bottom.UpX;
        g.State.Hero.Y = bottom.UpY;
        g.Act("ascend");
        SameNumber(g.State.Depth, 8, "Ascending moves exactly one floor up");
        SameNumber(g.State.Floors.Count, 10, "Ascending reuses the floor already generated");
        SameNumber(g.State.Turn, turnBefore, "Ascending is instant and costs no turn");
        var back = g.State.Floors[8];
        ExpectTrue(g.State.Hero.X == back.DownX && g.State.Hero.Y == back.DownY,
            "Ascending arrives on the shallower floor's descent stair");

        g.State.Hero.X = g.State.Floors[0].UpX;
        g.State.Hero.Y = g.State.Floors[0].UpY;
        g.State.Depth = 0;
        g.Act("ascend");
        SameNumber(g.State.Depth, 0, "Ascending from the surface stays on the surface");
        SameNumber(g.State.Turn, turnBefore, "Refusing an ascent costs no turn");
        ExpectTrue(g.State.Mode == "playing", "The run is not over without the Star");
        ExpectTrue(HasMessage(g, "You cannot leave without the Star."),
            "The surface explains that the Star is required");
        ExpectFalse(HasMessage(g, "Victory"), "The run is not won without the Star");

        g.State.Hero.HasStar = true;
        g.Act("ascend");
        SameString(g.State.Mode, "won", "Bearing the Star into daylight wins the run");
        ExpectTrue(HasMessage(g, "You bear the Star into daylight. Victory!"),
            "Victory is announced when the Star leaves the dungeon");
    }


    static void SaveRoundTrip() {
        var original = new Game(202, HeroClass.Arcanist);
        for (var i = 0; i < 3; i++) original.Act("right");
        original.Act("search");
        original.Act("rest");
        var text = original.Save();
        ExpectTrue(text.Length > 100, "A save record is a substantial document");
        ExpectTrue(text[0] == '{', "A save record is a JSON object");
        var reloaded = Game.Load(text);
        ExpectTrue(reloaded.State.Version == original.State.Version, "Saves carry their format version");
        SameNumber(reloaded.State.Turn, original.State.Turn, "Loading preserves the turn counter");
        SameNumber(reloaded.State.Depth, original.State.Depth, "Loading preserves the depth");
        SameNumber(reloaded.State.Floors.Count, original.State.Floors.Count, "Loading preserves every generated floor");
        ExpectTrue(reloaded.State.Seed == original.State.Seed, "Loading preserves the seed");
        ExpectTrue(reloaded.State.Rng == original.State.Rng, "Loading preserves the random stream");
        SameNumber(reloaded.State.Hero.Hp, original.State.Hero.Hp, "Loading preserves hit points");
        SameNumber(reloaded.State.Hero.Hunger, original.State.Hero.Hunger, "Loading preserves the hunger clock");
        SameNumber(reloaded.State.Hero.X, original.State.Hero.X, "Loading preserves the hero column");
        SameNumber(reloaded.State.Hero.Y, original.State.Hero.Y, "Loading preserves the hero row");
        SameNumber(reloaded.State.Hero.Inventory.Count, original.State.Hero.Inventory.Count, "Loading preserves the carried items");
        ExpectTrue(SameFloorGeometry(reloaded.State.Floors[0], original.State.Floors[0]), "Loading rebuilds the same floor");
        ExpectTrue(reloaded.Save() == text, "Saving a loaded game writes back the same record");
        reloaded.Act("rest");
        original.Act("rest");
        ExpectTrue(reloaded.State.Rng == original.State.Rng, "A reloaded game continues the same random stream");
        ExpectTrue(reloaded.State.Messages[0] == original.State.Messages[0], "A reloaded game carries the same log");
        var survived = true;
        try {
            Game.Load("{ not a save");
            survived = false;
        } catch (InvalidDataException) {
            survived = true;
        } catch {
            survived = true;
        }
        ExpectTrue(survived, "A corrupt save never produces a playable game");
    }

    static void IdenticalScripts() {
        var a = new Game(99, HeroClass.Warden);
        var b = new Game(99, HeroClass.Warden);
        for (var i = 0; i < 6; i++) {
            a.Act("right");
            b.Act("right");
            a.Act("rest");
            b.Act("rest");
        }
        SameNumber(a.State.Turn, b.State.Turn, "Identical action scripts take the same number of turns");
        ExpectTrue(a.State.Rng == b.State.Rng, "Identical action scripts keep the stream in lockstep");
        ExpectTrue(a.State.Hero.Hp == b.State.Hero.Hp, "Identical action scripts suffer the same damage");
        ExpectTrue(a.State.Messages[0] == b.State.Messages[0], "Identical action scripts write the same log");
        SameNumber(a.State.Floors.Count, b.State.Floors.Count, "Identical action scripts visit the same floors");
        ExpectTrue(SameFloorGeometry(b.State.Floors[0], a.State.Floors[0]), "Identical action scripts keep the same geometry");
    }

    static void DeathStopsInput() {
        var g = new Game(31337, HeroClass.Arcanist);
        var guard = 0;
        while (g.State.Mode == "playing" && guard < 1200) {
            g.Act("rest");
            guard++;
        }
        ExpectTrue(g.State.Mode == "dead", "Neglecting the depths eventually ends the run");
        ExpectTrue(g.State.Hero.Hp <= 0, "The run ended because the hero fell");
        var frozen = g.State.Turn;
        g.Act("right");
        g.Act("rest");
        g.Act("search");
        g.Act("get", 0);
        SameNumber(g.State.Turn, frozen, "Commands after death are refused and cost no turn");
        ExpectTrue(g.State.Mode == "dead", "A finished run stays finished");
    }

    static int Main(string[] args) {
        System.Console.WriteLine("Running " + GameName + " engine checks.");
        NewGameSurface(4242);
        ClassVariants();
        DeterministicSeeds();
        MonsterFields();
        RefusedCommands();
        AcceptedActions();
        PickupRules();
        DescentRules();
        SaveRoundTrip();
        IdenticalScripts();
        DeathStopsInput();
        System.Console.WriteLine("Checks: " + checks + "   Failures: " + failures);
        if (failures == 0) System.Console.WriteLine("All " + GameName + " engine checks passed.");
        return failures;
    }
}
