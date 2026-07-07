# Match Engine Rules

When editing match engine code (RoundManager, BotController, EngagementSystem, DuelResolver, BotStateMachine, StrategyExecutor, BombManager, MatchEconomy, MatchState):

## Round Flow
- Freeze → Coach picks strategy → Live → Mid-round call → Late-round call → Round end → Post-round delay → Next round
- `RoundPhase` enum controls phase: FreezeTime, Live, PostRound
- Round timer starts at `ROUND_TIME` (90s), mid-round trigger at 20s in, late-round at 60s in
- Match is first to 13 rounds, halftime at round 12 with full side swap

## Bot Behavior
- Bots follow waypoint paths from `StrategyData` ScriptableObjects
- Movement is NavMesh-based — `NavMeshAgent` on bot prefab
- Stop-to-shoot for rifles (bot stops before firing), run-and-gun for SMGs
- Shift-walk: bots run early round, shift only when near actual enemies
- 270° FOV when stationary + enemy-scanning rotation to prevent blind spots
- `IsFrozen` flag blocks ALL bot actions during freeze time — check it first in any bot logic

## Combat (EngagementSystem)
- `EngagementSystem` on each bot scans for LoS targets every 0.2s; smoke blocks LoS via the GrenadeEffect static registry
- Fire is per-shot: hit chance from aim/spray/distance/consistency/movement, clutch multiplier in 1vX
- Reaction time scales with Reflexes; victims of non-lethal hits turn toward the shooter and engage back
- Combat is sustained (HP/damage per shot), not instant-kill — DuelTrigger (legacy one-roll duels) was removed
- Idle/peeking bots pre-aim comms intel or sweep their held angle (no wallhack tracking)
- Equipment bonuses come from `EquipmentBonuses` ScriptableObject on each team

## Strategies
- `StrategyData`: ScriptableObject with 5 waypoint paths (one per bot slot) + lurker path
- Staging is PER TEAM SIDE: `ExecuteStrategy(strategy, bots, side)`, `CommitStagedBots(side)`, `HasStagedBots(side)` — player and AI share one executor without wiping each other
- AI auto-commits its own Execute/Control strats 10-18s in (utility first, then push)
- `StrategyExecutor`: reads paths, assigns bots, handles type-specific behavior:
  - Rush = immediate sprint to site
  - Execute = stage at positions, wait for commit call, then push with utility
  - Fake = noise on one site, rotate to other
  - Control = take map area, hold, wait for mid-round decision
  - Default = spread across map, gather info
- Axis Control shows "HIT A" / "HIT B" in mid-round panel, not generic "commit"

## Economy
- CS-style loss bonus escalation resets on win
- Buy type affects duel modifier: Full +15%, Force +5%, Half 0%, Eco -10%
- `MatchEconomy` tracks credits per team, handles buy logic
