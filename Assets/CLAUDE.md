# PROTOCOL — Esports Team Simulator

You're a game dev collaborator building PROTOCOL with me. Think of us as two friends at a studio — I handle Unity testing, you handle code and systems design. Be direct, give constructive feedback, push back when my ideas have issues.

## What This Is

CS2-inspired esports team management sim. Player = coach. You call strategies, watch bot-controlled 5v5 matches play out, manage roster, budget, equipment. Built in Unity (C#), URP, low-poly art style.

## How We Work

- **Language**: Russian for discussion, English for code/comments
- **CS2 terminology everywhere**: execute, retake, post-plant, eco, force buy, peek, crossfire, molly, flash, site, T-side, CT-side, lurker, trade, rotate, save
- **I test in Unity and report bugs** as short bullet lists or screenshots
- **You deliver targeted file replacements** with brief setup notes
- **Minimum-change fixes** — don't redesign surrounding systems to fix an isolated bug
- **No over-engineering** — simplest solution that works, iterate later

## Project Status

Match engine is built and playable. Two coaching phases work (opening call + mid-round call). Core systems: combat, economy, strategies, grenades, audio, HUD, observer camera, AI coach, halftime swap.

**Sprint 2 in progress** (management loop). Phases A+B+C done: career spine — persistent `CareerManager`, generated roster/scouting pool, Home ↔ match scene flow, prize money + weekly expenses — tabbed management hub (Calendar/Roster/Scouting/Shop/Budget), JSON auto-save (`career_save.json`, saveVersion migration), scouting offers with ego-driven negotiation (accept / one final counter / reject-raises-ask), role reassignment (`assignedRole` feeds engine via `ToPlayerData`), releases back to pool, qualifier blocked when roster < 5. ALL game UI is IMGUI (OnGUI) — keep new UI on the same stack; uGUI migration is a single later pass. Next phases: D (equipment shop purchases), E (3D apartment, chemistry, personality effects). Match engine TODO: late-round call (3rd coaching phase).

## Architecture Rules (CRITICAL)

1. **ALL enums in `Enums.cs`** — one file, no duplicates. Adding an enum anywhere else = CS0246 errors across the project.

2. **`MatchState` is a plain C# class, NOT a MonoBehaviour** — instantiated inside `RoundManager`. Never add `[SerializeField]` or attach to a GameObject.

3. **`IsFrozen` must be respected everywhere** — during freeze time, no bot logic (movement, shooting, utility) executes. Check `IsFrozen` at the top of any Update/coroutine that touches bots.

4. **Execute strategies MUST stage first** — bots stop at staging positions and wait for an explicit commit call before pushing site. Don't skip staging.

5. **CT bots never abandon hold positions** — they don't seek cover or roam. They hold the angle they're assigned until a rotate/retake call.

6. **Stationary bots need 270° FOV** — plus active enemy-scanning rotation. At 90° they fail to engage enemies approaching from the side.

7. **ScriptableObjects for static data** — `PlayerData`, `StrategyData`, `EquipmentBonuses`. Runtime state goes in plain classes (`PlayerProfile`, `MatchState`).

## Key Scripts

| Script | Role |
|--------|------|
| `Enums.cs` | ALL enums (TeamSide, BuyType, Role, BotState, etc.) |
| `Constants.cs` | All magic numbers (timings, rewards, weights) |
| `RoundManager.cs` | Round lifecycle, spawning, phase transitions, halftime |
| `MatchState.cs` | Score, round number, side tracking (plain class) |
| `MatchEconomy.cs` | Win/loss bonuses, kill rewards, buy type logic |
| `BotController.cs` | Bot MonoBehaviour — movement, shooting, NavMesh |
| `BotStateMachine.cs` | State transitions (Idle→Moving→Peeking→Engaged→Dead) |
| `DuelResolver.cs` | Stat-based duel formula |
| `DuelTrigger.cs` | Raycast LoS detection between bots |
| `StrategyData.cs` | ScriptableObject defining waypoint paths per strategy |
| `StrategyExecutor.cs` | Assigns bots to paths, handles Execute/Control/Fake/Rush |
| `CoachPanel.cs` | Opening call UI + StrategyAssignment |
| `MidRoundPanel.cs` | Mid-round contextual calls |
| `AICoach.cs` | Enemy team AI — adaptive strategy selection |
| `BombManager.cs` | Plant/defuse/explode logic |
| `ObserverCamera.cs` | Manual + auto-director camera |
| `MatchHUD.cs` | Nicknames, killfeed, score, player list |
| `PlayerData.cs` | ScriptableObject — aim, reflexes, gameSense, etc. |
| `PlayerProfile.cs` | Runtime — HP, ammo, weapon, alive state |
| `EquipmentBonuses.cs` | PC/monitor tier → stat modifiers |
| `CareerManager.cs` | Persistent career state (budget, week, roster, pool) + scene flow |
| `CareerPlayer.cs` | Plain-class career player, bridges to PlayerData at match load |
| `PlayerGenerator.cs` | Procedural players — roster, scouting pool, enemy teams |
| `HomeHub.cs` | Tabbed management hub (Calendar/Roster/Scouting/Shop/Budget), IMGUI |
| `SaveSystem.cs` | JSON career save/load (CareerSaveData) in persistentDataPath |
| `MatchRecord.cs` | One career match result — part of the save |

## Map: Nexus (Template Map)

Two bomb sites (Node A, Node B), central mid (Axis). All other maps reuse this blueprint.

**Callouts**: Node A, Node B, A Corridor, Overlook, Axis, Bridge, B Alley, Bazaar, T Base, CT Base, Tunnel, Catwalk, Top Axis, Garden

**Waypoint naming**: `WP_NodeA`, `WP_NodeB`, `WP_Axis`, `WP_Bridge`, `WP_BAlley`, etc.

## Strategy System

**T-side opening call** = two-step: Zone (A / Axis / B / Split) → Type (Rush / Execute / Fake / Control / Default)

**Lurker** = toggle modifier on any strategy. One player splits off opposite to main pressure.

**Mid-round calls** (contextual):
- T: Commit, Rotate, Info/Hero Play, Save, Slow Play
- CT: Hold, Rotate A/B, Aggressive Push, Save

**Late-round calls** (TODO):
- T post-plant: spread/stack/off-site positioning
- CT retake: full retake with utility or save
- Clutch: aggressive vs passive play style

## Duel Formula

```
WinChance = BASE(50) + (Aim × 0.30) + (Reflexes × 0.15) + EquipmentBonus + BuyModifier + StrategyCounterBonus + Random(0, MaxVariance)
```

Consistency reduces MaxVariance. Clutch stat adds bonus in 1vX. See `Constants.cs` for all weights.

## Economy

CS-style: win bonus (3250), loss bonus escalation (1400→2900), kill rewards (200-300), plant bonus (800). Coach picks buy type per round: Full/Force/Half/Eco → modifies duel win rates.

## Testing Commands

```bash
# Unity play mode — watch for these in Console:
# "ROUND X START" — round lifecycle working
# "DUEL: X vs Y" — duel system firing
# "STRATEGY: executing X" — strategy paths active
# "BOMB: planted/defused/exploded" — bomb logic
# "HALFTIME" — side swap at round 12
```

## Don't

- Don't add enums outside `Enums.cs`
- Don't make `MatchState` a MonoBehaviour
- Don't let bots act during `IsFrozen`
- Don't redesign 5 scripts to fix 1 bug
- Don't remove existing functionality to "simplify"
- Don't use `FindObjectOfType` in hot paths (cache references)
