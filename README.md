# MobileDemo

A small, finished 2D tower-defense game for mobile, built in Unity 6 (6000.5.10f1) with C#.
Portrait, touch-first, targeting 60 FPS on mid-range Android/iOS hardware.

This repository is a portfolio demo. It was written in **one week** — and the point it is
trying to make is about *process* as much as gameplay.

---


## Architecture at a glance

Three runtime assemblies, so the dependency direction is enforced by the compiler rather than
by discipline:

```
MobileDemo.UI          HUD, build menu, end screen       -> depends on Gameplay, Core
MobileDemo.Gameplay    towers, enemies, waves, economy,  -> depends on Core
                       building, phases
MobileDemo.Core        event bus, pooling, interfaces,   -> depends on nothing
                       config
```

Plus `MobileDemo.Editor` (editor-only, excluded from player builds) and
`MobileDemo.Tests.EditMode`.

The hard rule: **UI never reads gameplay state directly, and Gameplay never references UI.**
They meet only over a typed event bus. If a `using MobileDemo.UI;` ever appears in a Gameplay
file, the build breaks — which is the point.

**Patterns, used where they earn their place:**

| Pattern | Where | Why there |
|---|---|---|
| State | round phases, and the enemy's own micro machine | keeps the whole game loop readable, one small file per phase |
| Object Pool | enemies, projectiles | allocation during a wave is the enemy on mobile |
| Factory | enemies, projectiles, towers | turns a ScriptableObject definition into a live object |
| Command | tower placement, on a LIFO undo stack | undo is a real feature here, not a demo of undo |
| Observer | the event bus, and every UI subscriber | it is what makes the UI/Gameplay boundary above possible |
| ScriptableObject data | towers, enemies, projectiles, waves, run config | no magic numbers in gameplay code |

Equally deliberate are the places a pattern was *not* reached for: the enemy registry, the
placement rules and the level runner are all plain classes, and the composition root is a plain
`Bootstrap` component rather than a service locator or a DI container.

---

## Quality gates

- **292 EditMode tests, green.** They cover the event bus, the object pool (including growth,
  double release and call ordering), every command's execute/undo, the economy, the phase
  machine's contract, the wave runner, the build controller's two-tap flow, and the level swap.
  They run with no scene loaded — which is the practical payoff of the architecture, not a
  theoretical one.
- **All five assemblies compile** under `dotnet build`, the test assembly included.
- **`.editorconfig`-driven lint is clean** across all five assemblies.

Run them:

```powershell
./Tools/unity.ps1 -Tests      # EditMode suite, headless
./Tools/lint.ps1              # read-only; pass -Fix to rewrite
```

`Tools/unity.ps1` resolves the editor from `ProjectSettings/ProjectVersion.txt` rather than
taking whatever is newest, because opening the project with a different editor silently
rewrites that file and can upgrade every asset.

---

## What this demo is actually demonstrating

This project was built in **one week**, and most of the code in it was written by AI.

That is the interesting part, not a disclaimer. The demo exists to show what becomes possible
when a developer does the work that AI cannot do for you:

- **Design the architecture first, and write it down.** The assembly layout, the dependency
  direction, the phase machine, the event catalogue and the polling-vs-events line were all
  decided and documented *before* the code that implements them existed. An AI given a
  well-specified boundary will respect it; an AI given a blank scene invents a different one
  every session.
- **Structure the project so the boundaries are mechanical.** Four `.asmdef` files turn "UI
  must not reach into gameplay" from a code-review opinion into a compile error. Rules a
  compiler enforces are the ones that survive contact with a fast-moving collaborator, human
  or otherwise.
- **Supervise every change.** The work went in as seven vertical slices, each one reviewed, run
  and certified — tests green, all assemblies compiling, lint clean, and in most cases the
  slice watched end to end in a scripted play session. Where a slice could *not* be verified
  that way, the document says so in as many words rather than claiming it was.
- **Keep the reasoning, not just the result.** `.claude/ARCHITECTURE.md` and the per-system
  guides in `.claude/systems/` record *why* each decision was made, what it cost, and what was
  deliberately not done. That written record is what lets the next session — human or AI — make
  a change that fits, rather than one that merely compiles.

Used this way, AI is a speed multiplier that does not trade quality away: one week bought seven
finished slices, 292 tests, and an architecture document that still matches the code. Used
without that scaffolding, it produces a larger pile of code faster. The difference is the
architecture, the structure and the supervision — which is the real subject of this demo.

---

## Documentation

- [.claude/ARCHITECTURE.md](.claude/ARCHITECTURE.md) — the authoritative design document:
  constraints, assembly layout, the phase machine, every pattern and where it was declined, the
  event catalogue, mobile budgets, and a slice-by-slice build log with what each one cost.
- [.claude/systems/](.claude/systems/) — one short guide per system: responsibility, key types,
  patterns, collaborators, data, gotchas, status.
- [CLAUDE.md](CLAUDE.md) — the working rules that keep those two documents current.
