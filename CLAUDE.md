# CLAUDE.md

Project rules for Claude Code in this repository.

## 1. Architecture document

The authoritative design document for this project is [.claude/ARCHITECTURE.md](.claude/ARCHITECTURE.md).

**Always read `.claude/ARCHITECTURE.md` before starting work** — at the beginning of every
task, before writing or changing any code, so the change fits the assemblies, patterns and
dependency direction it describes.

**Keep it current.** If the work changes anything the document describes, update
`.claude/ARCHITECTURE.md` in the same change as the code. Do not leave it for later.
Update it when a change touches:

- assembly layout or dependency direction (§3)
- the game loop / phase states (§4)
- a system's responsibilities (§5)
- which patterns are used and where — including *removing* one (§6)
- config fields on ScriptableObjects or named constants (§2, §7)
- the event catalogue — any event added, removed or renamed (§8)
- a polling-vs-events decision (§9)
- folder structure (§11)
- what is in or out of scope (§1, §12)
- dependencies or third-party packages (§15)

**Preserve its voice.** The document explains *why* each choice was made, not just what
exists. When editing it, keep that reasoning — record the decision and its trade-off, and
if something is deliberately not done, say so rather than deleting the section.

If a change makes the document wrong and you cannot fully update it, say so explicitly in
your summary rather than leaving a stale document.

## 2. Per-system guides

Each system in ARCHITECTURE.md §5 gets its own guide at `.claude/systems/<system>.md`,
so the project can be understood system by system without reading all the code.

**When a system is first implemented, create its guide in the same change.**
**When a system changes, update its guide in the same change.**

Use the system's name, kebab-cased — e.g. `.claude/systems/game-state-machine.md`,
`tower.md`, `enemy.md`, `wave-runner.md`, `enemy-factory.md`, `projectile-pool.md`,
`economy.md`, `build-controller.md`, `input-service.md`, `hud-presenter.md`.

Each guide covers:

- **Responsibility** — what this system owns, and what it deliberately does not.
- **Key types** — the classes/interfaces/ScriptableObjects involved, with file paths.
- **Patterns used** — and *why* that pattern earned its place here (mirrors §6's reasoning).
- **Collaborators** — what it depends on, and what depends on it. Events it raises and
  subscribes to, named exactly as in §8.
- **Data** — config fields and ScriptableObject assets that drive it.
- **Gotchas** — pooling/reset rules, tick budgets, ordering assumptions, mobile constraints.
- **Status** — implemented, partial, or planned.

Rules for guides:

- ARCHITECTURE.md stays the single source of truth for cross-cutting decisions; a guide
  goes deep on one system and links back to the relevant `§` rather than restating it.
- Do not create a guide for a system that has no code yet — a guide describes what exists.
- Keep them short. If a guide is drifting past ~150 lines, the system is probably doing
  too much; say so.
