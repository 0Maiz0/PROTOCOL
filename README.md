# PROTOCOL — Esports Team Simulator

**You build the team. The match tests it.**

A CS2-inspired esports management sim in Unity (C#). You are the coach: scout, sign and train a five-player roster, set the team's protocols (aggression, economy discipline, lurker usage) — then watch your team play a real-time simulated match and call its own game through an AI in-game leader. In a match you get three tactical timeouts and nothing more.

**Status:** in development, playable. The match engine and the career loop (roster, scouting, contracts, weekly budget, qualifier bracket, saves) work end to end.

## How to run

1. Unity **6000.3.12f1** (Unity 6), URP.
2. Open the project, load `Assets/Scenes/MainMenu.unity`, press Play.
3. Scene flow: `MainMenu` → `Home` (management hub) → `SampleScene` (match).

## What's inside

### Real-time match engine — not a dice roll
Full round lifecycle (freeze time → live → post-round), side swap at halftime, CS economy (loss-bonus escalation, Full / Force / Half / Eco buys, kill and plant rewards), plant / defuse and post-plant behaviour. Buys change which weapon each bot actually carries into the round.

### Per-shot combat where crosshair placement decides duels
Combat is sustained fire, shot by shot (`EngagementSystem`):

```
hitChance = weaponAccuracy × (0.5 + Aim/200) × sprayPenalty × distanceFalloff
          × consistencyFactor × movementPenalty × clutchFactor
```

Target acquisition time depends on reflexes, the angle the enemy was caught at (walking into a held crosshair vs. being flanked) and whether the team already had intel on that enemy (pre-aim). Counter-strafing, blinding flashes and trade discipline are modelled.

### An AI in-game leader that calls the game
`IGLBrain` picks the opening strategy from the coach's playbook, weighted by team protocols and the IGL's leadership stat, then runs the round: execute commit timing (utility lands first), CT rotations that always leave an anchor, plant pushes, save calls. Sites that keep losing stop being called. If the IGL dies, a surviving player takes over — slower, on thinner information.

### Honest information model
Bots never see through walls. A shared team knowledge store holds only what someone actually spotted; position reports go stale within seconds, and the reporter's comms stat sets their quality.

### Scene-authored utility
Grenade targets are empty GameObjects tagged by purpose (`UtilityPoint`: Entry flash, Cut smoke, Hold molotov, Anchor). `GrenadeManager` builds throw plans from them with priorities when utility is short.

### Career layer
Procedural scouting pool, contract negotiation driven by player ego (a rejected offer raises the ask), role assignment that feeds straight into the match engine, weekly economy, a four-team single-elimination qualifier, and JSON saves with version migration (`SaveSystem`, `saveVersion`).

## Architecture

```
Assets/Scripts/
  Match/       RoundManager-driven match flow: state, economy, IGL, comms, HUD, logging
  BotControl/  BotController (NavMesh), state machine, combat behaviour, strategy execution
  Rounds/      round lifecycle, bomb, post-plant
  Grenades/    utility planning and effects
  Career/      CareerManager, scouting, contracts, qualifier, saves
  Data/        Enums, Constants, ScriptableObject definitions (strategies, weapons, equipment)
  Players/     PlayerData (static, ScriptableObject) and PlayerProfile (runtime)
  PlayerGP/    coach panels and observer camera
  Map/         procedural blockout generators
```

Rules the codebase follows:

- **Static data in ScriptableObjects, runtime state in plain C# classes.** `PlayerData`, `StrategyData` are assets; `PlayerProfile`, `MatchState` are plain classes (`MatchState` is not a MonoBehaviour).
- **All enums live in one file** (`Data/Enums.cs`), all tuning numbers in `Data/Constants.cs`.
- **Freeze time is respected everywhere:** no bot logic runs while `IsFrozen` is set.
- **Strategies are data:** each opening is a `StrategyData` asset with waypoint paths (`Assets/Strats/`).
- All in-game UI is IMGUI for now; a uGUI pass is planned once the systems settle.

## How it was tuned

Emergent AI is debugged in playtests, not in the editor. The on-screen comms feed (`CommsFeed`) voices every bot decision, and `MatchLogger` writes a round-by-round telemetry log. Each numbered playtest in the commit history fixed concrete failures found that way — for example, a "save" call overridden by a "bomb timer" push 14 seconds later, or a dead IGL silently switching off all defensive rotations. The recurring fix was explicit state ownership and making accepted calls final.

## Development process

Game design and every playtest are mine, and I set the direction for each system. The code is written together with [Claude Code](https://claude.com/claude-code) as a coding assistant, which is why most commits carry a co-author line; the project rules it works under live in `Assets/CLAUDE.md`.

## Author

Matvey Izmaylov — game developer (Unity C#, Unreal Engine 5). More games: [maiz-studio.itch.io](https://maiz-studio.itch.io)
