Create a new strategy for the match engine:

1. Ask which side (T or CT), zone, and type
2. Define waypoint paths for all 5 bot slots
3. Define lurker path if applicable
4. Create the StrategyData ScriptableObject .asset file
5. If this is a new StrategyType, add it to Enums.cs
6. Update StrategyExecutor if the type needs new behavior logic
7. Add mid-round call options appropriate for this strategy type

Use existing strategies as reference for waypoint naming convention (WP_NodeA, WP_Axis, etc.)
