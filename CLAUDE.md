# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A **Unity Asset Store product**, not a game. The shippable deliverable is the single folder
`Assets/AmbientTraffic/`, distributed as `AmbientTrafficJam.unitypackage` at the repo root.
Everything outside `Assets/AmbientTraffic/` — the demo scene host project, `Marketing/`,
`Packages/com.unity.asset-store-tools/` — is scaffolding that supports building, validating,
and marketing that one folder.

Unity **6000.3.22f1**, **URP only** (see "URP is a hard dependency" below).

## Commands

There is no test suite, linter, build script, or CI in this repo. All workflows run through the
Unity Editor GUI:

- **Validate / export / upload the package:** `Asset Store Tools` window (the publisher tooling is
  embedded at `Packages/com.unity.asset-store-tools/`, so it is version-controlled with the project).
- **Try the package end to end:** open `Assets/AmbientTraffic/Demo/AmbientTrafficDemo.unity` and press Play.
- **Re-export the `.unitypackage`:** right-click `Assets/AmbientTraffic` → Export Package, or export
  via Asset Store Tools. Commit the regenerated `AmbientTrafficJam.unitypackage`.

`*.csproj` and `*.sln` are gitignored; `ambient-traffic-asset.slnx` is not, so it gets committed when
Unity regenerates it.

## Architecture

The entire simulation is one MonoBehaviour: `Assets/AmbientTraffic/Runtime/AmbientTraffic.cs`.
There are **no per-car MonoBehaviours** — cars are plain `Car` objects in per-lane `List<Car>`s,
all advanced from a single `Update()`. Adding a component per car would be a significant regression
in the asset's selling point (bounded cost, no physics, no per-object scripting overhead).

Two assemblies, namespace `FuR.AmbientTraffic`:

| Assembly | Path | Notes |
|---|---|---|
| `FuR.AmbientTraffic` | `Runtime/` | Zero references. Must stay dependency-free — this is a drop-in asset. |
| `FuR.AmbientTraffic.Editor` | `Editor/` | Editor-only platform; references the runtime assembly. Holds only the `MinMaxRange` property drawer. |

### The direction-agnostic coordinate trick

Lanes run both ways. Rather than branching on direction everywhere, the code works in
`s = Dir * z`, so **"forward" is always `+s`** for both a with-traffic lane (`Dir = +1`, right side,
`+z`) and an oncoming lane (`Dir = -1`, left side, `-z`). `AdvanceLane` iterates **lead → rear** so
each follower reacts to its leader's already-updated position in the same frame, and the car ahead
is always the neighbour at index `i + Dir`. Read `AdvanceLane`/`RecycleLane` together before
touching either.

### Invariants that are easy to break

- **Each lane's `Cars` list stays sorted by ascending world z.** Car-following correctness depends on
  it. This is maintained without ever sorting: cars never overtake (a follower always brakes first),
  so recycling just pops the lowest-z car off the front of the list and appends it as the new
  highest-z car. Any change that reorders, inserts, or removes mid-list breaks the following logic.
- **`brakeGap` must stay far smaller than `gapDesired`.** `gapDesired` is the packed at-rest spacing;
  `brakeGap` is where braking-to-stop completes. The headroom between them is what makes a lane
  behave as a moving conveyor rather than a frozen gridlock.
- **The move budget is consumed by actual forward progress, not elapsed time.** A car blocked
  bumper-to-bumper burns none of its budget while waiting, so it still travels its full pull-up once
  the gap opens. Converting this to a timer produces visible micro-lurching.
- **Colour tint must use shared material *variants*, never `MaterialPropertyBlock`.** MPBs break SRP
  batching, which would cost a draw call per car. `EnsureTintPool` builds one variant material per
  `tintColors` entry and only ever swaps the body renderer's slot 0. The runtime-created materials are
  destroyed in `OnDestroy` — keep that cleanup.
- **All randomness must come from the private `System.Random _rng`, never `UnityEngine.Random`.**
  "Never disturbs your seeded gameplay RNG" is a documented product promise; it covers spawn, prefab
  pick, phase timing, tint, and audio pitch.
- **`AddImpactCollider` must run while the car is still at the origin**, before it is moved into the
  lane, so `InverseTransformPoint` yields a correct *local* box centre.
- Impact colliders are **disabled by default and off during normal play** (zero physics cost); they
  are woken only by `TriggerImpactCollisions`. Cars get kinematic Rigidbodies deliberately — a bare
  moving static collider would force PhysX to rebuild its static tree every frame.

### Public API surface

Only two methods are intended as public API: `TriggerImpactCollisions(Transform, float)` and
`ClearTraffic()`. Everything else users touch is inspector fields.

## URP is a hard dependency

The demo materials use URP's `Lit` shader (guid `933532a4fcc9baf4fa0491de14d08ed7`), and the tint
feature drives `_BaseColor` and relies on the SRP Batcher. Because the product ships as a
`.unitypackage`, it **cannot declare a UPM dependency on URP** — the requirement is communicated
through documentation and the Asset Store listing metadata only.

**Known false positive:** Asset Store Tools' `Check SRP Compatible Materials` test flags all five
demo materials as non-SRP-compatible. This is an artifact of how the test runs, not a real problem.
`ExternalProjectValidator` copies `Assets/AmbientTraffic/` into a throwaway project under
`Temp/<guid>/` whose `Packages/manifest.json` contains only built-in Unity modules — **no URP**. URP's
`Lit` shader therefore cannot resolve there, the materials fall back to the error shader, which has
no `RenderPipeline` subshader tag, and `CheckSRPCompatibleMaterials.IsSrpCompatible` returns false.
The result status is `Warning`, which does not gate upload. Do not "fix" this by moving materials off
URP/Lit.

## Conventions

- **Tooltips are the user-facing manual.** The `[Tooltip]` strings in `AmbientTraffic.cs` and
  `TrafficLane.cs` are unusually long by design and explain *why* a knob exists and how it interacts
  with the others. Keep them written for a buyer who has never read the source, and keep them generic
  — this package was extracted from a specific game, and commit `a4f7005` deliberately genericized all
  comments and tooltips for public release. Don't reintroduce game-specific references.
- **Docs must be kept in sync when behaviour or inspector fields change.** There are five places:
  `README.md` (GitHub landing), `Assets/AmbientTraffic/README.md` (in-package),
  `Assets/AmbientTraffic/Documentation/Documentation.md` **and its exported `Documentation.pdf`**,
  `Assets/AmbientTraffic/CHANGELOG.md`, and `Assets/AmbientTraffic/Demo/DemoReadme.txt`. The PDF is a
  manual export of the Markdown — regenerate it in the same commit (see `e8d9b68`).
- **Commit `.meta` files alongside every asset**, including folders. GUID stability is what keeps
  buyers' scene references intact across package updates.
- Demo car prefabs use a **single-material body** so the tint lands on slot 0 (`393b611`).
  `FindBodyRenderer` identifies the body as the renderer with more than one material slot, falling
  back to the first renderer.
- Demo audio and models are original assets; provenance is noted in `DemoReadme.txt` (`c513171`).

## Git workflow

Per the user's global instructions: work directly on `main`, commit and push to `main`, never create
branches or PRs unless explicitly asked.
