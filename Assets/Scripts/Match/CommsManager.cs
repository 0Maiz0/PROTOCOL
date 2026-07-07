using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// COMMS MANAGER — Team communication for bot AI.
/// 
/// Functions:
/// - Enemy spotted: share position with team (Communication stat = info quality)
/// - Call for help: bot under fire requests nearby teammate support
/// - Plant call: T tells team they're planting
/// - Bomb spotted: CT shares bomb location
/// 
/// High Communication stat = faster/more accurate info sharing.
/// Low Communication = delayed, inaccurate position reports.
/// </summary>
public class CommsManager : MonoBehaviour
{
    public struct EnemyInfo
    {
        public Vector3 lastPosition;
        public float timeSpotted;
        public bool isAlive;
        public int enemyID;
    }

    public struct HelpRequest
    {
        public BotController requester;
        public Vector3 position;
        public float timeRequested;
    }

    // Shared enemy info per team
    private Dictionary<int, EnemyInfo> attackerKnownEnemies = new Dictionary<int, EnemyInfo>();
    private Dictionary<int, EnemyInfo> defenderKnownEnemies = new Dictionary<int, EnemyInfo>();

    // Active help requests
    private List<HelpRequest> attackerHelpRequests = new List<HelpRequest>();
    private List<HelpRequest> defenderHelpRequests = new List<HelpRequest>();

    private const float INFO_DECAY = 8f;      // Seconds before old info expires
    private const float HELP_TIMEOUT = 5f;     // Seconds before help request expires
    private const float HELP_RANGE = 20f;      // Range to respond to help calls

    public static CommsManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        // Cleanup old help requests
        float t = Time.time;
        attackerHelpRequests.RemoveAll(h => t - h.timeRequested > HELP_TIMEOUT || !h.requester.Profile.IsAlive);
        defenderHelpRequests.RemoveAll(h => t - h.timeRequested > HELP_TIMEOUT || !h.requester.Profile.IsAlive);
    }

    public void ResetForRound()
    {
        attackerKnownEnemies.Clear();
        defenderKnownEnemies.Clear();
        attackerHelpRequests.Clear();
        defenderHelpRequests.Clear();
    }

    // ===================== REPORT ENEMY =====================

    /// <summary>
    /// Bot reports seeing an enemy. Info quality depends on Communication stat.
    /// </summary>
    public void ReportEnemy(BotController spotter, BotController enemy)
    {
        if (spotter.Profile == null || enemy.Profile == null) return;

        float commSkill = spotter.Profile.EffectiveCommunication / 100f;

        // Position accuracy: high comm = exact position, low comm = offset
        float inaccuracy = Mathf.Lerp(5f, 0f, commSkill);
        Vector3 reportedPos = enemy.transform.position +
            new Vector3(Random.Range(-inaccuracy, inaccuracy), 0, Random.Range(-inaccuracy, inaccuracy));

        var info = new EnemyInfo
        {
            lastPosition = reportedPos,
            timeSpotted = Time.time,
            isAlive = true,
            enemyID = enemy.GetInstanceID()
        };

        var dict = (spotter.Side == TeamSide.Attacker) ? attackerKnownEnemies : defenderKnownEnemies;
        dict[enemy.GetInstanceID()] = info;
    }

    /// <summary>
    /// Report an enemy kill — team knows this enemy is dead.
    /// </summary>
    public void ReportKill(BotController killer, BotController victim)
    {
        var dict = (killer.Side == TeamSide.Attacker) ? attackerKnownEnemies : defenderKnownEnemies;
        if (dict.ContainsKey(victim.GetInstanceID()))
        {
            var info = dict[victim.GetInstanceID()];
            info.isAlive = false;
            dict[victim.GetInstanceID()] = info;
        }
    }

    // ===================== REQUEST HELP =====================

    /// <summary>
    /// Bot calls for help — nearby teammates should rotate toward them.
    /// </summary>
    public void RequestHelp(BotController bot)
    {
        if (bot.Profile == null || !bot.Profile.IsAlive) return;

        var request = new HelpRequest
        {
            requester = bot,
            position = bot.transform.position,
            timeRequested = Time.time
        };

        if (bot.Side == TeamSide.Attacker)
            attackerHelpRequests.Add(request);
        else
            defenderHelpRequests.Add(request);
    }

    // ===================== QUERIES =====================

    /// <summary>
    /// Get the nearest known enemy position for a bot's team.
    /// Returns false if no recent info available.
    /// </summary>
    public bool GetNearestKnownEnemy(BotController bot, out Vector3 position)
    {
        position = Vector3.zero;
        var dict = (bot.Side == TeamSide.Attacker) ? attackerKnownEnemies : defenderKnownEnemies;

        float bestDist = float.MaxValue;
        bool found = false;

        foreach (var kvp in dict)
        {
            if (!kvp.Value.isAlive) continue;
            if (Time.time - kvp.Value.timeSpotted > INFO_DECAY) continue;

            float dist = Vector3.Distance(bot.transform.position, kvp.Value.lastPosition);
            if (dist < bestDist)
            {
                bestDist = dist;
                position = kvp.Value.lastPosition;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// Check if a teammate nearby needs help. Returns the help position.
    /// </summary>
    public bool GetNearbyHelpRequest(BotController bot, out Vector3 helpPosition)
    {
        helpPosition = Vector3.zero;
        var requests = (bot.Side == TeamSide.Attacker) ? attackerHelpRequests : defenderHelpRequests;

        float bestDist = float.MaxValue;
        bool found = false;

        foreach (var req in requests)
        {
            if (req.requester == bot) continue; // Don't help yourself
            float dist = Vector3.Distance(bot.transform.position, req.position);
            if (dist < HELP_RANGE && dist < bestDist)
            {
                bestDist = dist;
                helpPosition = req.position;
                found = true;
            }
        }
        return found;
    }

    /// <summary>
    /// How many alive enemies does this team know about?
    /// </summary>
    public int GetKnownEnemyCount(TeamSide side)
    {
        var dict = (side == TeamSide.Attacker) ? attackerKnownEnemies : defenderKnownEnemies;
        int count = 0;
        foreach (var kvp in dict)
        {
            if (kvp.Value.isAlive && Time.time - kvp.Value.timeSpotted < INFO_DECAY)
                count++;
        }
        return count;
    }
}
