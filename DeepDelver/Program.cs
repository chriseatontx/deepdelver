using DeepDelver;

sealed class Terminal {
    const int MapW = 56, MapH = 28;

    static string AppDir() {
        var env = System.Environment.GetEnvironmentVariables();
        if (env.Contains("APPDATA")) return System.IO.Path.Combine(env["APPDATA"].ToString(), "DeepDelver");
        if (env.Contains("HOME")) return System.IO.Path.Combine(env["HOME"].ToString(), ".local", "DeepDelver");
        return "DeepDelver";
    }

    static string SaveFile() => System.IO.Path.Combine(AppDir(), "save.json");
    static string ScoreFile() => System.IO.Path.Combine(AppDir(), "scores.txt");

    static void EnsureAppDir() {
        var dir = AppDir();
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
    }

    static void Help() {
        System.Console.WriteLine("Controls: arrows or numpad 1-9 move (5 waits; one keypress acts at once), `.` rest, `g` get, `s` search, `<` ascend, `>` descend.");
        System.Console.WriteLine("Inventory: `i` lists items; then say `equip ID`, `use ID`, `fire ID` or `drop ID`.");
        System.Console.WriteLine("Game: `S` save, `L` load, `?` help, `q` quit. Every accepted action costs one turn.");
    }

    // The Strata compiler folds a "\u001b" string literal into an empty string when it is
    // passed as an argument, so escape detection has to scan characters against '\u001b'.
    static bool HasEscape(string s) {
        for (var i = 0; i < s.Length; i++) if (s[i] == '\u001b') return true;
        return false;
    }

    // Terminals send arrow keys as ESC-[-letter (normal mode) or ESC-O-letter (application
    // mode, e.g. inside screen/tmux), with optional modifier parameters such as ESC-[1;3C.
    static string Arrow(string line) {
        var esc = -1;
        for (var i = 0; i < line.Length; i++) if (line[i] == '\u001b') esc = i;
        if (esc < 0) return "";
        var tail = line.Substring(esc + 1);
        while (tail.Length > 0 && (tail[0] == '\u001b' || tail.StartsWith("[") || tail.StartsWith("O"))) tail = tail.Substring(1);
        var k = 0;
        while (k < tail.Length && "0123456789;".IndexOf(tail.Substring(k, 1)) >= 0) k++;
        if (k >= tail.Length) return "";
        return tail.Substring(k, 1).ToLowerInvariant() switch {
            "a" => "up", "b" => "down", "c" => "right", "d" => "left", _ => ""
        };
    }

    static string Move(string line) => line switch {
        "1" => "sw", "2" => "down", "3" => "se", "4" => "left", "5" => "rest", "6" => "right",
        "7" => "nw", "8" => "up", "9" => "ne", "." => "rest", "g" => "get", "s" => "search",
        "<" => "ascend", ">" => "descend",
        _ => Arrow(line) != "" ? Arrow(line)
          : line switch { "up" => "up", "down" => "down", "left" => "left", "right" => "right", "nw" => "nw", "ne" => "ne", "sw" => "sw", "se" => "se", _ => "" }
    };

    static int ItemId(string rest) {
        try { return int.Parse(rest.Trim()); } catch { return 0; }
    }

    static void Record(int depth, int turn, string mode, HeroClass heroClass) {
        EnsureAppDir();
        var stamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var entry = $"{stamp} {heroClass} {depth} {turn} {mode}";
        var path = ScoreFile();
        var text = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
        var kept = System.String.Join("\n", new[] { entry }.Concat(text.Split("\n")).Take(25));
        System.IO.File.WriteAllText(path, kept + "\n");
    }

    static Game Fresh(string[] args) {
        System.Console.WriteLine("Choose your class: (W)arden, (A)rcanist, (R)anger");
        var pick = Line();
        var heroClass = pick.Trim().ToLowerInvariant() switch {
            "w" => HeroClass.Warden, "warden" => HeroClass.Warden,
            "a" => HeroClass.Arcanist, "arcanist" => HeroClass.Arcanist,
            _ => HeroClass.Ranger
        };
        System.Console.WriteLine("Seed (blank for the default):");
        var seedText = Line();
        var seed = System.String.IsNullOrEmpty(seedText.Trim()) ? 1 : ItemId(seedText);
        return new Game((uint)seed, heroClass);
    }

    static Game? LoadGame() {
        var path = SaveFile();
        if (!System.IO.File.Exists(path)) {
            System.Console.WriteLine($"No saved game at {path}.");
            return null;
        }
        return Game.Load(System.IO.File.ReadAllText(path));
    }

    static void SaveGame(Game game) {
        EnsureAppDir();
        System.IO.File.WriteAllText(SaveFile(), game.Save());
        System.Console.WriteLine("Saved.");
    }

    static string Read() {
        var line = System.Console.ReadLine();
        return line == null ? "" : line;
    }

    // With stdin attached to a real terminal, Console.ReadLine() is editor-backed: it
    // advertises application mode (ESC [ ? 1 h =) and eats ESC-[ as a history macro, so
    // it returns an empty line and arrows never reach the game (Turn stays 0). Piped stdin
    // is fine, so keep ReadLine there; on a real tty, take the tty out of canonical (and
    // echoing) mode with stty and read bytes straight from /dev/tty instead, then hand the
    // original terminal settings back when the program exits.
    static bool Raw = false;
    static System.IO.FileStream? Tty = null;
    static int Held = -1;
    static string TtyCfg = "";

    static int Sh(string script) {
        var child = System.Diagnostics.Process.Start("sh", new[] { "-c", script });
        child.WaitForExit();
        return child.ExitCode;
    }

    static bool TtyIn() => Sh("[ -t 0 ]") == 0;

    static void EnterRaw() {
        Raw = TtyIn();
        if (!Raw) return;
        EnsureAppDir();
        TtyCfg = System.IO.Path.Combine(AppDir(), ".tty-settings");
        Sh("stty -g >'" + TtyCfg + "' 2>/dev/null; stty -icanon -echo min 1 time 0 2>/dev/null");
        Tty = System.IO.File.OpenRead("/dev/tty");
    }

    static void LeaveRaw() {
        if (Tty != null) { Tty.Close(); Tty = null; }
        if (TtyCfg == "") return;
        var saved = System.IO.File.Exists(TtyCfg) ? System.IO.File.ReadAllText(TtyCfg).Trim() : "";
        if (saved != "") {
            var child = System.Diagnostics.Process.Start("stty", new[] { saved });
            child.WaitForExit();
        }
        Sh("rm -f '" + TtyCfg + "'");
    }

    const string AscLower = "abcdefghijklmnopqrstuvwxyz";
    const string AscUpper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    const string AscDigit = "0123456789";

    static string Chr(int b) => b switch {
        32 => " ", 33 => "!", 37 => "%", 40 => "(", 41 => ")", 42 => "*", 43 => "+", 44 => ",",
        45 => "-", 46 => ".", 47 => "/", 58 => ":", 59 => ";", 60 => "<", 61 => "=", 62 => ">",
        63 => "?", 91 => "[", 93 => "]", 95 => "_",
        _ => b >= 48 && b <= 57 ? AscDigit.Substring(b - 48, 1)
          : b >= 97 && b <= 122 ? AscLower.Substring(b - 97, 1)
          : b >= 65 && b <= 90 ? AscUpper.Substring(b - 65, 1)
          : ""
    };

    static bool EscParam(int n) => n == 27 || n == 91 || n == 79 || n == 59 || (n >= 48 && n <= 57);

    static int Next() {
        if (Held > -1) { var b = Held; Held = -1; return b; }
        if (Tty == null) return -1;
        return Tty.ReadByte();
    }

    static void Push(int b) { Held = b; }

    // One raw arrow keypress arrives as ESC-[ / ESC-O / ESC ESC (params) then a letter.
    static string ArrowKey() {
        while (true) {
            var n = Next();
            if (n == 65 || n == 97) return "up";
            if (n == 66 || n == 98) return "down";
            if (n == 67 || n == 99) return "right";
            if (n == 68 || n == 100) return "left";
            if (n < 0 || !EscParam(n)) { if (n > -1) Push(n); return ""; }
        }
    }

    // Enter-terminated word, echoed by hand because the tty no longer echoes.
    static string Word(int b) {
        var word = "";
        var c = b;
        while (c > -1 && c != 10 && c != 13 && c != 27) {
            if (c == 8 || c == 127) {
                if (word.Length > 0) {
                    word = word.Substring(0, word.Length - 1);
                    System.Console.Write("\b \b");
                }
            } else {
                var piece = Chr(c);
                word = word + piece;
                System.Console.Write(piece);
            }
            c = Next();
        }
        return word;
    }

    // The game command: one keypress acts at once (arrows, numpad digits, `.`), while
    // words such as `equip 3` still wait for Enter.
    static string Cmd() {
        if (!Raw) return Read();
        var b = Next();
        while (b == 10 || b == 13) b = Next();
        if (b < 0) return "";
        if (b == 27) {
            var arrow = ArrowKey();
            if (arrow != "") return arrow;
            b = Next();
            while (b == 10 || b == 13) b = Next();
            if (b < 0) return "";
        }
        if (b >= 49 && b <= 57) return "" + (b - 48);
        if (b == 46) return ".";
        return Word(b);
    }

    // Prompts always want a whole line, never a single keystroke.
    static string Line() {
        if (!Raw) return Read();
        var b = Next();
        while (b == 10 || b == 13) b = Next();
        if (b < 0) return "";
        return Word(b);
    }

    static void Render(Game game) {
        System.Console.Clear();
        var state = game.State;
        var hero = state.Hero;
        for (var y = 0; y < MapH; y++) {
            var row = "";
            for (var x = 0; x < MapW; x++) row += game.Glyph(x, y).ToString();
            System.Console.WriteLine(row);
        }
        var star = hero.HasStar ? " star" : "";
        System.Console.WriteLine($"Depth {state.Depth}  Turn {state.Turn}  Hp {hero.Hp}/{hero.MaxHp}  Mana {hero.Mana}/{hero.MaxMana}  Hunger {hero.Hunger}  Poison {hero.Poison}  Lvl {hero.Level} Xp {hero.Xp}/{hero.NextXp}{star}");
        for (var i = 0; i < Math.Min(5, state.Messages.Count); i++) System.Console.WriteLine(state.Messages[i]);
        System.Console.Write("> ");
    }

    static void Show(Game game) {
        var hero = game.State.Hero;
        System.Console.WriteLine($"Carrying {hero.Inventory.Count} items (equip weapon {hero.Weapon}, armor {hero.Armor}):");
        for (var i = 0; i < hero.Inventory.Count; i++) {
            var item = hero.Inventory[i];
            var mark = item.Id == hero.Weapon ? " [w]" : item.Id == hero.Armor ? " [a]" : item.Id == hero.Weapon ? " [w]" : "";
            System.Console.WriteLine($"  {item.Id} {item.Name} power {item.Power} charges {item.Charges}{mark}");
        }
        System.Console.WriteLine("Use `equip ID`, `use ID`, `fire ID` or `drop ID`.");
    }

    static void Session(Game game) {
        while (true) {
            Render(game);
            var line = Cmd().Trim();
            if (System.String.IsNullOrEmpty(line)) continue;
            if (line == "q" || line == "quit") return;
            if (line == "?" || line == "help") { Help(); continue; }
            if (line == "i" || line == "inventory") { Show(game); continue; }
            if (line == "S" || line == "save") { SaveGame(game); continue; }
            if (line == "L" || line == "load") {
                var reloaded = LoadGame();
                if (reloaded == null) continue;
                game = reloaded;
                System.Console.WriteLine("Loaded.");
                continue;
            }
            var split = line.IndexOf(" ");
            var head = split < 0 ? line : line.Substring(0, split);
            var rest = split < 0 ? "" : line.Substring(split + 1);
            var command = Move(head);
            var itemId = 0;
            if (System.String.IsNullOrEmpty(command)) {
                var action = head.ToLowerInvariant();
                if (action != "equip" && action != "use" && action != "fire" && action != "drop") {
                    if (HasEscape(head)) {
                        System.Console.WriteLine("Arrow sequence not understood. Use numpad 1-9, or type up/down/left/right.");
                        continue;
                    }
                    System.Console.WriteLine($"Unrecognized: {head}. Type ? for controls.");
                    continue;
                }
                command = action;
                itemId = ItemId(rest);
            }
            game.Act(command, itemId);
            if (game.State.Mode != "playing") {
                System.Console.Clear();
                System.Console.WriteLine(game.State.Messages.Count > 0 ? game.State.Messages[0] : "The run is over.");
                System.Console.WriteLine($"Final: depth {game.State.Depth}, turn {game.State.Turn}.");
                Record(game.State.Depth, game.State.Turn, game.State.Mode, game.State.Hero.Class);
                return;
            }
        }
    }

    static void Dev() {
        System.Console.WriteLine("Developer fixture: scripted session, not a normal new game.");
        var game = new Game(4242, HeroClass.Warden);
        for (var i = 0; i < 4; i++) game.Act("right");
        game.Act("search");
        game.Act("rest");
        game.Act("descend");
        game.Act("get", 0);
        System.Console.Clear();
        Render(game);
        System.Console.WriteLine($"Fixture state: depth {game.State.Depth}, turn {game.State.Turn}, hp {game.State.Hero.Hp}, seed {game.State.Seed}.");
    }

    static void Main(string[] args) {
        if (args.Contains("--dev")) { Dev(); return; }
        EnterRaw();
        try {
            System.Console.WriteLine("Deep Delver - recover the Star of the Deep and return to the surface.");
            var game = Fresh(args);
            Session(game);
        } finally {
            LeaveRaw();
        }
    }
}
