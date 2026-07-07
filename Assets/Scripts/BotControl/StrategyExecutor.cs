using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// STRATEGY EXECUTOR v2
/// 
/// KEY CHANGE: Execute/Control strategies STOP at staging position.
/// Bots go to the SECOND-TO-LAST waypoint and WAIT.
/// When coach calls COMMIT → remaining waypoints (site) are executed.
/// 
/// Rush strategies run the full path immediately (no staging).
/// </summary>
public class StrategyExecutor : MonoBehaviour
{
    [Header("Waypoint Parent")]
    [SerializeField] private Transform waypointParent;

    private Dictionary<string, Transform> waypointMap = new Dictionary<string, Transform>();
    private bool lurkerEnabled = false;

    // Track bots waiting at staging for commit
    private List<BotController> stagedBots = new List<BotController>();
    private List<Transform[]> remainingPaths = new List<Transform[]>();
    private StrategyData lastStrategy;

    private void Awake() { CacheWaypoints(); }

    public void CacheWaypoints()
    {
        waypointMap.Clear();
        foreach (var obj in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (obj.name.StartsWith("WP_")) waypointMap[obj.name] = obj.transform;
        Debug.Log($"StrategyExecutor: Cached {waypointMap.Count} waypoints");
    }

    public void SetLurkerEnabled(bool enabled) { lurkerEnabled = enabled; }

    /// <summary>
    /// Execute a strategy. For Execute/Control: bots stop at staging (second-to-last WP).
    /// For Rush/Default: full path immediately.
    /// </summary>
    public void ExecuteStrategy(StrategyData strategy, List<BotController> bots)
    {
        if (strategy == null) { Debug.LogError("StrategyExecutor: No strategy!"); return; }

        lastStrategy = strategy;
        stagedBots.Clear();
        remainingPaths.Clear();

        bool isExecute = (strategy.type == StrategyType.Execute || strategy.type == StrategyType.Control);
        string[][] allPaths = strategy.GetAllPaths();

        for (int i = 0; i < bots.Count && i < 5; i++)
        {
            if (bots[i] == null || !bots[i].Profile.IsAlive) continue;

            string[] pathNames;

            if (lurkerEnabled && strategy.supportsLurker && i == strategy.lurkerBotIndex
                && strategy.lurkerPath != null && strategy.lurkerPath.Length > 0)
            {
                pathNames = strategy.lurkerPath;
            }
            else
            {
                pathNames = (i < allPaths.Length) ? allPaths[i] : allPaths[allPaths.Length - 1];
            }

            if (pathNames == null || pathNames.Length == 0) continue;

            Transform[] fullWaypoints = ResolveWaypoints(pathNames);
            if (fullWaypoints.Length == 0) continue;

            if (isExecute && fullWaypoints.Length >= 2)
            {
                // === EXECUTE: stop at staging (all waypoints EXCEPT the last one) ===
                Transform[] stagingPath = new Transform[fullWaypoints.Length - 1];
                System.Array.Copy(fullWaypoints, stagingPath, stagingPath.Length);

                Transform[] sitePath = new Transform[] { fullWaypoints[fullWaypoints.Length - 1] };

                bots[i].AssignWaypoints(stagingPath);
                bots[i].StartExecuting();

                // Save remaining path for commit
                stagedBots.Add(bots[i]);
                remainingPaths.Add(sitePath);

                Debug.Log($"  Bot {i} → staging at {pathNames[pathNames.Length - 2]} (waiting for commit)");
            }
            else
            {
                // === RUSH / DEFAULT: full path immediately ===
                bots[i].AssignWaypoints(fullWaypoints);
                bots[i].StartExecuting();
            }
        }

        Debug.Log($"Strategy Executed: {strategy.strategyName} | Type: {strategy.type} | Staged: {stagedBots.Count}");
    }

    /// <summary>
    /// Called when coach commits — send staged bots to the site.
    /// </summary>
    public void CommitStagedBots()
    {
        int committed = 0;
        for (int i = 0; i < stagedBots.Count; i++)
        {
            var bot = stagedBots[i];
            if (bot == null || !bot.Profile.IsAlive) continue;
            if (i >= remainingPaths.Count) continue;

            bot.AssignWaypoints(remainingPaths[i]);
            bot.StartExecuting();
            committed++;
        }

        Debug.Log($"COMMIT: {committed} bots pushing site!");
        stagedBots.Clear();
        remainingPaths.Clear();
    }

    /// <summary>
    /// Are there bots waiting at staging for a commit call?
    /// Used by MidRoundPanel to show "Execute" button.
    /// </summary>
    public bool HasStagedBots() { return stagedBots.Count > 0; }

    /// <summary>
    /// Get the zone of the last executed strategy (for utility targeting).
    /// </summary>
    public StrategyZone GetLastStrategyZone()
    {
        return lastStrategy != null ? lastStrategy.zone : StrategyZone.NodeA;
    }

    private Transform[] ResolveWaypoints(string[] names)
    {
        List<Transform> result = new List<Transform>();
        foreach (string name in names)
        {
            string trimmed = name.Trim();
            if (waypointMap.TryGetValue(trimmed, out Transform wp))
                result.Add(wp);
            else
                Debug.LogWarning($"StrategyExecutor: Waypoint '{trimmed}' not found!");
        }
        return result.ToArray();
    }
}
