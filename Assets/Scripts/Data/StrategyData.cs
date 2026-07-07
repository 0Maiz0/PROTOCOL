using UnityEngine;

/// <summary>
/// Defines a single strategy: where each bot goes.
/// Create one ScriptableObject per strategy (e.g., "Rush_A", "B_Execute", "Default").
/// 
/// Waypoints array has 5 entries — one path per bot slot (0-4).
/// Each WaypointPath is an array of transforms the bot follows in order.
/// </summary>
[CreateAssetMenu(fileName = "NewStrategy", menuName = "ETS/Strategy Data")]
public class StrategyData : ScriptableObject
{
    [Header("Identity")]
    public string strategyName = "Rush A";
    public string description = "Fast 5-man push through A Corridor";
    public TeamSide side = TeamSide.Attacker; // T or CT strat
    public StrategyZone zone = StrategyZone.NodeA;
    public StrategyType type = StrategyType.Rush;

    [Header("Lurker Toggle")]
    public bool supportsLurker = true;
    public int lurkerBotIndex = 4; // which bot slot becomes lurker (0-4)

    [Header("Waypoint Names — 5 paths, one per bot slot")]
    [Tooltip("List the waypoint GameObject names in order for bot 0")]
    public string[] bot0Path; // e.g., {"WP_ACorr", "WP_Overlook", "WP_NodeA"}
    public string[] bot1Path;
    public string[] bot2Path;
    public string[] bot3Path;
    public string[] bot4Path;

    [Header("Lurker Path (used when lurker toggle is ON)")]
    public string[] lurkerPath; // e.g., {"WP_BAlley"} — lurker watches opposite side

    public string[][] GetAllPaths()
    {
        return new string[][] { bot0Path, bot1Path, bot2Path, bot3Path, bot4Path };
    }
}
