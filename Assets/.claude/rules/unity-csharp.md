# Unity C# Conventions

## Code Style
- C# with Unity conventions: PascalCase for public methods/properties, camelCase for private fields
- `[SerializeField]` for inspector-exposed private fields
- `[Header("Section Name")]` to group inspector fields
- XML doc comments (`/// <summary>`) on public methods
- Keep MonoBehaviours thin — logic in plain C# classes where possible

## Architecture Patterns
- ScriptableObjects for static game data (player stats, strategies, equipment)
- Plain C# classes for runtime state (MatchState, PlayerProfile)
- Enums in single `Enums.cs` — NEVER define enums in other files
- Constants in single `Constants.cs` — no magic numbers in scripts
- `[SerializeField]` references over `FindObjectOfType` — cache at Start() if needed
- Coroutines for sequenced gameplay (round flow, utility throws, delays)

## Common Pitfalls
- Adding a new enum? Put it in `Enums.cs` or get CS0246 across the project
- Editing MatchState? It's a plain class, not MonoBehaviour. No [SerializeField], no AddComponent
- Null checks on bot references — bots die and get disabled mid-round
- NavMeshAgent: check `isOnNavMesh` before setting destination
- Coroutines: check `if (this == null) return;` for safety when objects can be destroyed

## File Organization
```
Assets/Scripts/
├── Rounds/         — RoundManager, PostPlantManager, BombManager
├── Match/          — MatchState, MatchEconomy, EngagementSystem, CommsManager, MatchHUD, AICoach, Minimap, BombSIte
├── BotControl/     — BotController, BotStateMachine, DuelResolver, DuelTrigger, StrategyExecutor, BotCombatBehavior, FootstepSystem, CoverPoint
├── Data/           — Enums, Constants, StrategyData, WeaponData, EquipmentBonuses
├── Players/        — PlayerData, PlayerProfile, PlayerHUD
├── PlayerGP/       — CoachPanel, MidRoundPanel, StrategyAssignment, ObserverCamera
├── Grenades/       — GrenadeManager, GrenadeEffect
└── Career/         — CareerManager, CareerPlayer, PlayerGenerator, HomeHub (Sprint 2)

Assets/MatchAudioManager.cs  — sits at Assets root (legacy placement)
```
