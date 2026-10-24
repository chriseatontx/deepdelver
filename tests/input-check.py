#!/usr/bin/env python3
"""Input-encoding checks for the terminal adapter (Program.cs), not the engine.

The engine gate is tests/TestRunner.cs; this gate covers what that runner cannot
see: how bytes reach the adapter. It drives the built game in a REAL
pseudo-terminal (os.forkpty, so the child's stdin is a genuine tty) because the
arrow-key bug only shows up there: with stdin on a tty, Console.ReadLine() is
editor-backed and swallows ESC-[ as a history macro, so piped-stdin probes alone
never reproduce it. Checks:

  1. arrow bytes land the hero exactly where numpad 1-9 would (same turn count)
  2. the same holds when the player does NOT press Enter after each arrow
  3. application-mode, modifier and Alt-prefixed arrow forms are accepted
  4. Enter-terminated words (inventory, quit) still work on a raw tty
  5. the program hands the terminal settings back when it exits
  6. piped stdin still works, and the --dev fixture is unchanged

Build the game first (`dotnet build DeepDelver`, or `dotnet build DeepDelver -c
release`; it drives whichever of the two launchers exists and is newest), then run
this:
python3 tests/input-check.py     exit code = number of failed checks
"""
import os, re, select, shutil, subprocess, sys, tempfile, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# The build emits a native launcher beside its .dll under a config-named folder:
# Debug goes to bin/Debug/net8.0 while release goes to bin/release/net8.0 (MSBuild
# lowercases the release config name), so accept whichever exists and prefer the
# newest build.
CANDIDATES = [os.path.join(ROOT, "DeepDelver", "bin", cfg, "net8.0", "DeepDelver")
              for cfg in ("Debug", "release")]
EXE = max((p for p in CANDIDATES if os.path.exists(p)), key=os.path.getmtime,
          default=None)
SCRATCH = tempfile.mkdtemp()
CFG = os.path.join(SCRATCH, "before.cfg")
AFTER = os.path.join(SCRATCH, "after.cfg")

# direction -> (arrow bytes, numpad digit)
KEYS = {"up": (b"\x1b[A", "8"), "down": (b"\x1b[B", "2"),
        "left": (b"\x1b[D", "4"), "right": (b"\x1b[C", "6")}
VARIANTS = {"application mode": b"\x1bOA", "modifier": b"\x1b[1;3A",
            "alt-prefixed": b"\x1b\x1b[A"}
PLAN = [("up", 2), ("down", 2), ("left", 7), ("right", 3)]
COMPLAINTS = ("Arrow sequence not understood", "Unrecognized")


def run(sends, boot=6.0, key=0.45, end=1.5, wrapper=False):
    """Start the game on a real pty, feed raw byte sends, return its transcript."""
    argv = [EXE]
    if wrapper:
        argv = ["/usr/bin/sh", "-c",
                f"stty -g >'{CFG}' 2>/dev/null; {EXE}; stty -g >'{AFTER}' 2>/dev/null"]
    pid, fd = os.forkpty()
    if pid == 0:
        os.execvp(argv[0], argv)
        os._exit(1)

    out = bytearray()

    def drain(dur):
        t0 = time.time()
        while time.time() - t0 < dur:
            r, _, _ = select.select([fd], [], [], 0.05)
            if not r:
                continue
            try:
                d = os.read(fd, 65536)
            except OSError:
                return
            if not d:
                return
            out.extend(d)

    drain(boot)
    for s in sends:
        try:
            os.write(fd, s)
        except OSError:
            break
        drain(key)
    drain(end)
    try:
        os.close(fd)
    except OSError:
        pass
    os.waitpid(pid, 0)
    return bytes(out)


def clean(b):
    s = b.decode("utf-8", "replace")
    s = re.sub(r"\x1b\][^\x07]*\x07", "", s)
    s = re.sub(r"\x1b\[[0-9;?]*[A-Za-z]", "", s)
    return s


def last_frame(b):
    """Hero column and turn counter from the final rendered frame."""
    s = clean(b)
    col = -1
    for row in s.split("\r\n"):
        if "@" in row:
            col = row.index("@")
    ms = re.findall(r"Depth (\d+)\s+Turn (\d+)", s)
    return col, (int(ms[-1][1]) if ms else -1)


def seq(digits):
    out = []
    for name, count in PLAN:
        for _ in range(count):
            out.append(str(KEYS[name][1]).encode() if digits else KEYS[name][0])
    return out


def session(keys, enter=True):
    sends = [b"w\n", b"4242\n"]
    for k in keys:
        sends.append(k + (b"\r" if enter else b""))
    sends.append(b"q\n")
    return run(sends)


def feed(lines):
    """Piped stdin: the adapter must behave exactly as it does on a tty."""
    p = subprocess.run([EXE], input=b"\n".join(lines) + b"\n",
                       capture_output=True, text=False, timeout=60)
    out = p.stdout.decode("utf-8", "replace")
    ms = re.findall(r"Depth (\d+)\s+Turn (\d+)", out)
    bad = [k for k in COMPLAINTS if k in out]
    return (int(ms[-1][1]) if ms else -1), bad, p.returncode, out


def piped(digit):
    lines = [b"w", b"4242"]
    for name, count in PLAN:
        for _ in range(count):
            lines.append(KEYS[name][1].encode() if digit else KEYS[name][0])
    lines.append(b"q")
    return feed(lines)


def paired(keys):
    """Walls legitimately block a step and a blocked step spends no turn, so the
    only sound assertion is that the bytes and their numpad pair are accepted
    equally. Every key in `keys` must be an up-arrow."""
    lines = [b"w", b"4242"]
    for k in keys:
        lines += [k] * 4
    lines.append(b"q")
    arrow = feed(lines)
    lines = [b"w", b"4242"]
    for _ in keys:
        lines += [b"8"] * 4
    lines.append(b"q")
    numpad = feed(lines)
    print("   bytes turn", arrow[0], arrow[1] or "no complaints",
          "| numpad turn", numpad[0], numpad[1] or "no complaints")
    return arrow[0] == numpad[0] and not arrow[1] and not numpad[1]


def report(tag, out, want_turn):
    s = clean(out)
    bad = [k for k in COMPLAINTS if k in s]
    col, turn = last_frame(out)
    ok = turn == want_turn and not bad
    print(f"[{tag}] col={col} turn={turn} want-turn={want_turn} "
          f"complaints={bad or 'none'} -> {'OK' if ok else 'FAIL'}")
    return ok


def rd(path):
    try:
        return open(path).read().strip()
    except FileNotFoundError:
        return "<missing>"


if EXE is None:
    print("no launcher built yet; run `dotnet build DeepDelver` or "
          "`dotnet build DeepDelver -c release` first")
    raise SystemExit(1)

print("driving:", EXE)

checks = []

print("== 0) numpad baseline, Enter after each ==")
_, base_turn = last_frame(run([b"w\n", b"4242\n"] + seq(True) + [b"q\n"]))
print("   numpad turn:", base_turn)

print("\n== 1) arrows over a real tty, Enter after each ==")
checks.append(("arrow-with-enter",
               report("arrow-enter", session(seq(False)), base_turn)))

print("\n== 2) arrows over a real tty, NO Enter (what a player actually types) ==")
checks.append(("arrow-without-enter",
               report("arrow-no-enter", session(seq(False), enter=False), base_turn)))

print("\n== 3) application-mode / modifier / Alt-prefixed arrows ==")
for name, form in VARIANTS.items():
    out = session([form] * 2, enter=False)
    _, turn = last_frame(out)
    bad = [k for k in COMPLAINTS if k in clean(out)]
    ok = turn == 2 and not bad
    print(f"[{name}] turn={turn} want=2 complaints={bad or 'none'} "
          f"-> {'OK' if ok else 'FAIL'}")
    checks.append((name, ok))

print("\n== 4) Enter-terminated words still work on a raw tty ==")
out = run([b"w\n", b"4242\n", b"i\r", b"quit\r"], key=0.8)
ok = "Carrying" in clean(out)
print("[words] inventory block shown:", ok, "->", "OK" if ok else "FAIL")
checks.append(("words-on-tty", ok))

print("\n== 5) terminal settings handed back on exit ==")
run([b"w\n", b"4242\n", b"q\n"], wrapper=True, boot=8.0, end=2.0)
before, after = rd(CFG), rd(AFTER)
ok = before != "<missing>" and before == after
print("   before:", before[:60], "| after:", after[:60], "->", "OK" if ok else "FAIL")
for p in (CFG, AFTER):
    if os.path.exists(p):
        os.remove(p)
checks.append(("stty-restored", ok))

print("\n== 6) piped stdin equivalence and the --dev fixture ==")
pad_turn, pad_bad, _, _ = piped(True)
arrow_turn, arrow_bad, _, _ = piped(False)
print("   piped numpad:", pad_turn, pad_bad or "no complaints")
print("   piped arrows:", arrow_turn, arrow_bad or "no complaints")
checks.append(("piped-equivalence",
               pad_turn == arrow_turn and not pad_bad and not arrow_bad))
checks.append(("piped-variants", paired([KEYS["up"][0]] + list(VARIANTS.values()))))
p = subprocess.run([EXE, "--dev"], capture_output=True, text=True, timeout=60)
tail = p.stdout.strip().split("\n")[-1]
print("   dev fixture:", tail, "rc", p.returncode)
checks.append(("dev-fixture", "depth 0, turn 6, hp 22, seed 4242" in tail
               and p.returncode == 0))

fails = sum(1 for name, ok in checks if not ok)
shutil.rmtree(SCRATCH, ignore_errors=True)
for name, ok in checks:
    if not ok:
        print("FAIL", name)
print(f"\ninput checks: {len(checks)}   failures: {fails}")
raise SystemExit(fails)
