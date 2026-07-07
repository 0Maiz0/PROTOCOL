using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// STRATEGY EXECUTOR v3
///
/// Staging is tracked PER TEAM SIDE — both coaches (player + AI) share this
/// executor, so each side keeps its own staged bots, remaining paths and last
/// strategy. (v2 had one shared list: the AI's ExecuteStrategy call wiped the
/// player's staged Execute every round.)
///
/// Execute/Control strategies stop at staging (all waypoints except the last)
/// and wait for CommitStagedBots(side). Rush/Default/Fake run the full path.
/// </summary>
public class StrategyExecutor : MonoBehaviour
{
    [Header("Waypoint Parent")]
    [SerializeField] private Transform waypointParent;

    private Dictionary<string, Transform> waypointMap = new Dictionary<string, Transform>();
    private bool lurkerEnabled = false;

    private class StagingGroup
    {
        public readonly List<BotController> bots = new List<BotController>();
        public readonly List<Transform[]> paths = new List<Transform[]>();
        public StrategyData lastStrategy;
    }

    private readonly Dictionary<TeamSide, StagingGroup> stagingBySide = new Dictionary<TeamSide, StagingGroup>();

    private void Awake() { CacheWaypoints(); }

    public void CacheWaypoints()
    {
        waypointMap.Clear();
        foreach (var obj in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (obj.name.StartsWith("WP_")) waypointMap[obj.name] = obj.transform;
        Debug.Log($"StrategyExecutor: Cached {waypointMap.Count} waypoints");
    }

    public void SetLurkerEnabled(bool enabled) { lurkerEnabled = enabled; }

    private StagingGroup GetGroup(TeamSide side)
    {
        if (!stagingBySide.TryGetValue(side, out StagingGroup g))
        {
            g = new StagingGroup();
            stagingBySide[side] = g;
        }
        return g;
    }

    /// <summary>
    /// Execute a strategy for one team. For Execute/Control: bots stop at staging
    /// (second-to-last WP) until CommitStagedBots(side). For Rush/Default: full path.
    /// The lurker toggle is a player-team feature — AI passes allowLurker: false.
    /// </summary>
    public void ExecuteStrategy(StrategyData strategy, List<BotController> bots, TeamSide side, bool allowLurker = true)
    {
        if (strategy == null) { Debug.LogError("StrategyExecutor: No strategy!"); return; }

        StagingGroup group = GetGroup(side);
        group.bots.Clear();
        group.paths.Clear();
        group.lastStrategy = strategy;

        bool isExecute = (strategy.type == StrategyType.Execute || strategy.type == StrategyType.Control);
        bool useLurker = allowLurker && lurkerEnabled;
        string[][] allPaths = strategy.GetAllPaths();

        for (int i = 0; i < bots.Count && i < 5; i++)
        {
            if (bots[i] == null || !bots[i].Profile.IsAlive) continue;

            // A committed rush runs through mollies — hesitation kills the timing
            bots[i].HazardBypass = (strategy.type == StrategyType.Rush);

            string[] pathNames;

            if (useLurker && strategy.supportsLurker && i == strategy.lurkerBotIndex
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
                group.bots.Add(bots[i]);
                group.paths.Add(sitePath);

                Debug.Log($"  Bot {i} ({side}) → staging at {pathNames[pathNames.Length - 2]} (waiting for commit)");
            }
            else
            {
                // === RUSH / DEFAULT: full path immediately ===
                bots[i].AssignWaypoints(fullWaypoints);
                bots[i].StartExecuting();
            }
        }

        Debug.Log($"Strategy Executed ({side}): {strategy.strategyName} | Type: {strategy.type} | Staged: {group.bots.Count}");
    }

    /// <summary>
    /// Coach commits — send that side's staged bots to the site.
    /// </summary>
    public void CommitStagedBots(TeamSide side)
    {
        StagingGroup group = GetGroup(side);
        int committed = 0;
        for (int i = 0; i < group.bots.Count; i++)
        {
            var bot = group.bots[i];
            if (bot == null || !bot.Profile.IsAlive) continue;
            if (i >= group.paths.Count) continue;

            bot.AssignWaypoints(group.paths[i]);
            bot.StartExecuting();
            committed++;
        }

        Debug.Log($"COMMIT ({side}): {committed} bots pushing site!");
        group.bots.Clear();
        group.paths.Clear();
    }

    /// <summary>
    /// Are there bots of this side waiting at staging for a commit call?
    /// </summary>
    public bool HasStagedBots(TeamSide side) => GetGroup(side).bots.Count > 0;

    /// <summary>
    /// Zone of the last strategy this side executed (for utility targeting).
    /// </summary>
    public StrategyZone GetLastStrategyZone(TeamSide side)
    {
        var s = GetGroup(side).lastStrategy;
        return s != null ? s.zone : StrategyZone.NodeA;
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
