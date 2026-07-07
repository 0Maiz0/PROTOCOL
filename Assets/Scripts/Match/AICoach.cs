using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// AI Coach for the enemy team. Picks strategies based on:
/// - Round situation (score, economy)
/// - Previous round results (did they lose to a specific strat?)
/// - Bomb plant status (triggers rotations)
/// 
/// Also handles CT bot positioning — sends them to hold spots instead of standing at spawn.
/// Add to MatchManager.
/// </summary>
public class AICoach : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StrategyExecutor strategyExecutor;
    [SerializeField] private BombManager bombManager;
    [SerializeField] private GrenadeManager grenadeManager;

    [Header("CT Strategies")]
    [SerializeField] private StrategyData[] ctStrategies;

    [Header("T Strategies (for when AI plays T side)")]
    [SerializeField] private StrategyData[] tStrategies;

    [Header("Settings")]
    [SerializeField] private float rotationDelay = 1.5f; // delay before CT rotates after bomb plant

    // Track what happened last round for adaptation
    private string lastRoundLostTo = ""; // strategy name that beat us
    private int consecutiveLosses = 0;
    private bool hasRotated = false;

    // CT rotation state
    private bool bombPlantDetected = false;
    private float rotationTimer = 0f;
    private List<BotController> currentBots;
    private string plantedSite = "";

    private Coroutine autoCommitCo;
    private PostPlantManager postPlantManager;

    // CT defensive brain — reacts to comms info (footsteps, contacts, enemy utility)
    private TeamSide currentAiSide = TeamSide.Defender;
    private float evalTimer;
    private float rotateCooldown;

    // T-side game-state brain (parity with the player's IGL)
    private List<BotController> enemyBots;
    private BuyType currentEnemyBuy = BuyType.Full;
    private float roundStartTime;
    private float atkEvalTimer;
    private float lastPlantPushTime;
    private bool saveCalled;

    private void Start()
    {
        if (bombManager != null)
        {
            bombManager.OnBombPlanted += OnBombPlanted;
        }
        if (grenadeManager == null) grenadeManager = FindObjectOfType<GrenadeManager>();
        // PostPlantManager runs the grouped retake — our simple rotation is only a fallback,
        // otherwise the two systems issue conflicting move orders on plant
        postPlantManager = FindObjectOfType<PostPlantManager>();
    }

    private void OnDestroy()
    {
        if (bombManager != null)
        {
            bombManager.OnBombPlanted -= OnBombPlanted;
        }
    }

    private void Update()
    {
        // Handle CT rotation after bomb plant (fallback only — PostPlantManager owns retakes)
        if (postPlantManager == null && bombPlantDetected && !hasRotated)
        {
            rotationTimer -= Time.deltaTime;
            if (rotationTimer <= 0f)
            {
                RotateToBombSite();
                hasRotated = true;
            }
        }

        // CT defensive brain — read the info like an IGL: "do we all go A? is this a fake?"
        if (currentAiSide == TeamSide.Defender && currentBots != null && !bombPlantDetected)
        {
            if (rotateCooldown > 0f) rotateCooldown -= Time.deltaTime;
            evalTimer -= Time.deltaTime;
            if (evalTimer <= 0f)
            {
                evalTimer = Constants.CT_EVAL_INTERVAL;
                EvaluateDefense();
            }
        }

        // T-side game state: plant pushes and saves — parity with the player's IGL,
        // so 4v1 rounds where the AI forgets the bomb exists don't happen either way
        if (currentAiSide == TeamSide.Attacker && currentBots != null)
        {
            atkEvalTimer -= Time.deltaTime;
            if (atkEvalTimer <= 0f)
            {
                atkEvalTimer = Constants.CT_EVAL_INTERVAL;
                EvaluateAIAttack();
            }
        }
    }

    // ===================== T-SIDE GAME STATE =====================

    private void EvaluateAIAttack()
    {
        if (bombManager == null || bombManager.IsPlanted || bombManager.IsExploded) return;

        int mine = CountAliveBots(currentBots);
        int theirs = CountAliveBots(enemyBots);
        if (mine == 0) return;
        float elapsed = Time.time - roundStartTime;

        // Round is lost — save the guns
        if (!saveCalled && mine <= 2 && theirs - mine >= 2 && elapsed > 30f)
        {
            saveCalled = true;
            CommsFeed.PostOpponent("numbers are bad — saving the guns");
            SendAITeamTo("WP_TopAxis", false);
            return;
        }

        // Man advantage or burning clock and no plant — take a site NOW
        bool manAdvantage = mine - theirs >= 2 && elapsed > 20f;
        bool clockBurning = elapsed > 50f;
        if ((manAdvantage || clockBurning) && Time.time - lastPlantPushTime > 20f)
        {
            lastPlantPushTime = Time.time;
            string wpName = PickOpenSiteWP(out string site);
            CommsFeed.PostOpponent(manAdvantage
                ? $"man advantage — committing {site}, planting"
                : $"clock's burning — hitting {site} now");
            SendAITeamTo(wpName, true);
        }
    }

    private string PickOpenSiteWP(out string site)
    {
        GameObject aWP = GameObject.Find("WP_NodeA"), bWP = GameObject.Find("WP_NodeB");
        int sigA = 0, sigB = 0;
        if (CommsManager.Instance != null)
        {
            if (aWP != null) sigA = CommsManager.Instance.CountKnownEnemiesNear(currentAiSide, aWP.transform.position, Constants.CT_SIGNAL_RANGE);
            if (bWP != null) sigB = CommsManager.Instance.CountKnownEnemiesNear(currentAiSide, bWP.transform.position, Constants.CT_SIGNAL_RANGE);
        }
        bool goA = sigA <= sigB;
        site = goA ? "A" : "B";
        return goA ? "WP_NodeA" : "WP_NodeB";
    }

    private void SendAITeamTo(string wpName, bool bypassHazards)
    {
        GameObject wp = GameObject.Find(wpName);
        if (wp == null) return;
        foreach (var b in currentBots)
        {
            if (b?.Profile == null || !b.Profile.IsAlive) continue;
            b.HazardBypass = bypassHazards;
            b.AssignWaypoints(new Transform[] { wp.transform });
            b.StartExecuting();
        }
    }

    private int CountAliveBots(List<BotController> bots)
    {
        if (bots == null) return 0;
        int c = 0;
        foreach (var b in bots)
            if (b?.Profile != null && b.Profile.IsAlive) c++;
        return c;
    }

    // ===================== CT INFO REACTIONS =====================

    /// <summary>
    /// Compare info signals per site (known enemies from comms + enemy utility landing)
    /// and rotate with one anchor left behind. Over-rotating on noise is exactly
    /// what makes FAKE strategies a real option for the attackers.
    /// </summary>
    private void EvaluateDefense()
    {
        if (rotateCooldown > 0f || CommsManager.Instance == null) return;

        GameObject aWP = GameObject.Find("WP_NodeA"), bWP = GameObject.Find("WP_NodeB");
        if (aWP == null || bWP == null) return;

        int sigA = CommsManager.Instance.CountKnownEnemiesNear(TeamSide.Defender, aWP.transform.position, Constants.CT_SIGNAL_RANGE)
                 + CountEnemyUtilNear(aWP.transform.position);
        int sigB = CommsManager.Instance.CountKnownEnemiesNear(TeamSide.Defender, bWP.transform.position, Constants.CT_SIGNAL_RANGE)
                 + CountEnemyUtilNear(bWP.transform.position);

        if (sigA - sigB >= Constants.CT_ROTATE_SIGNAL_DIFF)
            RotateDefense("A", aWP.transform, bWP.transform.position);
        else if (sigB - sigA >= Constants.CT_ROTATE_SIGNAL_DIFF)
            RotateDefense("B", bWP.transform, aWP.transform.position);
    }

    private int CountEnemyUtilNear(Vector3 pos)
    {
        int c = 0;
        var fx = GrenadeEffect.ActiveEffects;
        for (int i = 0; i < fx.Count; i++)
            if (fx[i] != null && fx[i].IsActive && fx[i].ThrowerSide == TeamSide.Attacker &&
                Vector3.Distance(fx[i].transform.position, pos) < Constants.CT_SIGNAL_RANGE)
                c++;
        return c;
    }

    private void RotateDefense(string site, Transform target, Vector3 anchorSite)
    {
        rotateCooldown = Constants.CT_ROTATE_COOLDOWN;

        // One anchor stays on the far site — never fully abandon it
        BotController anchor = null;
        float best = float.MaxValue;
        foreach (var bot in currentBots)
        {
            if (bot == null || bot.Profile == null || !bot.Profile.IsAlive) continue;
            float d = Vector3.Distance(bot.transform.position, anchorSite);
            if (d < best) { best = d; anchor = bot; }
        }

        int rotated = 0;
        foreach (var bot in currentBots)
        {
            if (bot == null || bot.Profile == null || !bot.Profile.IsAlive || bot == anchor) continue;
            if (Vector3.Distance(bot.transform.position, target.position) < 12f) continue; // already there
            bot.AssignWaypoints(new Transform[] { target });
            bot.StartExecuting();
            rotated++;
        }

        if (rotated > 0)
        {
            Debug.Log($"AI COACH (CT): info points {site} — rotating {rotated} player(s), " +
                      $"{(anchor != null ? anchor.Profile.Data.nickname : "nobody")} anchors far site");
            CommsFeed.PostOpponent($"reading {site} — rotating {rotated}, anchor stays far");
        }
    }

    /// <summary>
    /// Pick and execute a strategy for the AI team.
    /// Called by RoundManager at round start.
    /// </summary>
    public StrategyData PickAndExecute(TeamSide aiSide, List<BotController> bots,
        List<BotController> enemies = null, BuyType enemyBuy = BuyType.Full)
    {
        currentBots = bots;
        currentAiSide = aiSide;
        enemyBots = enemies;
        currentEnemyBuy = enemyBuy;
        hasRotated = false;
        bombPlantDetected = false;
        rotateCooldown = 6f; // settle into positions before reacting to info
        evalTimer = Constants.CT_EVAL_INTERVAL;
        roundStartTime = Time.time;
        atkEvalTimer = Constants.CT_EVAL_INTERVAL;
        lastPlantPushTime = -100f;
        saveCalled = false;

        StrategyData[] available = (aiSide == TeamSide.Defender) ? ctStrategies : tStrategies;

        if (available == null || available.Length == 0)
        {
            Debug.LogWarning("AICoach: No strategies available!");
            return null;
        }

        StrategyData picked = PickStrategy(available, aiSide);

        if (picked != null && strategyExecutor != null)
        {
            strategyExecutor.ExecuteStrategy(picked, bots, aiSide, allowLurker: false);
            Debug.Log($"AI COACH ({aiSide}): Selected {picked.strategyName}");
            CommsFeed.PostOpponent($"call: {picked.strategyName.Trim()}");

            // Execute/Control strats stage and wait — the AI must make its own
            // commit call, or its bots stand at staging forever.
            if (autoCommitCo != null) StopCoroutine(autoCommitCo);
            if (picked.type == StrategyType.Execute || picked.type == StrategyType.Control)
                autoCommitCo = StartCoroutine(AutoCommit(aiSide,
                    Random.Range(Constants.AI_COMMIT_DELAY_MIN, Constants.AI_COMMIT_DELAY_MAX)));
        }

        return picked;
    }

    /// <summary>
    /// AI's own commit call: utility first, then push — mirrors the player's flow.
    /// </summary>
    private IEnumerator AutoCommit(TeamSide side, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (strategyExecutor == null || !strategyExecutor.HasStagedBots(side)) yield break;

        if (grenadeManager != null)
            grenadeManager.OnCommitCalled(strategyExecutor.GetLastStrategyZone(side), side);

        yield return new WaitForSeconds(0.8f); // let utility land
        strategyExecutor.CommitStagedBots(side);
        Debug.Log($"AI COACH ({side}): COMMIT — executing!");
        CommsFeed.PostOpponent("EXECUTE — committing the hit!");
    }

    private StrategyData PickStrategy(StrategyData[] available, TeamSide side)
    {
        if (available.Length == 1) return available[0];

        // Simple AI logic:
        // 1. If we lost last round to a specific zone, try to counter it
        // 2. If losing badly (3+ losses), play more aggressively
        // 3. Otherwise, weighted random

        // Build weight list
        float[] weights = new float[available.Length];
        float totalWeight = 0f;

        for (int i = 0; i < available.Length; i++)
        {
            float w = 1f; // base weight

            // Boost strategies that counter what beat us
            if (side == TeamSide.Defender && !string.IsNullOrEmpty(lastRoundLostTo))
            {
                if (lastRoundLostTo.Contains("A") && available[i].zone == StrategyZone.NodeA)
                    w += 2f; // Stack A if they keep hitting A
                if (lastRoundLostTo.Contains("B") && available[i].zone == StrategyZone.NodeB)
                    w += 2f;
            }

            // If losing badly, prefer aggressive strats
            if (consecutiveLosses >= 3)
            {
                if (available[i].type == StrategyType.Rush || available[i].type == StrategyType.Execute)
                    w += 1.5f;
            }

            // Anti-eco read: they're broke — rush them down, no free picks
            if ((currentEnemyBuy == BuyType.Eco || currentEnemyBuy == BuyType.Half) &&
                (available[i].type == StrategyType.Rush || available[i].type == StrategyType.Execute))
                w += 1.2f;

            // Default/standard gets a small base boost (reliable)
            if (available[i].type == StrategyType.Default || available[i].type == StrategyType.Control)
                w += 0.5f;

            weights[i] = w;
            totalWeight += w;
        }

        // Weighted random pick
        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        for (int i = 0; i < available.Length; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
                return available[i];
        }

        return available[0];
    }

    /// <summary>
    /// Called by RoundManager after round ends to update AI memory.
    /// </summary>
    public void OnRoundResult(TeamSide winner, TeamSide aiSide, string enemyStrategyUsed)
    {
        if (winner != aiSide)
        {
            consecutiveLosses++;
            lastRoundLostTo = enemyStrategyUsed;
        }
        else
        {
            consecutiveLosses = 0;
            lastRoundLostTo = "";
        }
    }

    // ===================== CT ROTATION ON BOMB PLANT =====================

    private void OnBombPlanted(string site)
    {
        bombPlantDetected = true;
        plantedSite = site;
        rotationTimer = rotationDelay;

        Debug.Log($"AI COACH: Bomb planted on {site} — rotating CT in {rotationDelay}s...");
    }

    private void RotateToBombSite()
    {
        if (currentBots == null) return;

        // Find the waypoint for the planted site
        string targetWP = plantedSite == "A" ? "WP_NodeA" : "WP_NodeB";

        GameObject wpObj = GameObject.Find(targetWP);
        if (wpObj == null)
        {
            Debug.LogWarning($"AICoach: Can't find waypoint {targetWP} for rotation!");
            return;
        }

        int rotatingCount = 0;

        foreach (var bot in currentBots)
        {
            if (bot == null || bot.Profile == null || !bot.Profile.IsAlive) continue;

            // Check if bot is already near the site (within 10 units)
            float distToSite = Vector3.Distance(bot.transform.position, wpObj.transform.position);
            if (distToSite < 10f)
            {
                // Already close — hold position
                continue;
            }

            // Rotate this bot to the site
            bot.AssignWaypoints(new Transform[] { wpObj.transform });
            bot.StartExecuting();
            rotatingCount++;
        }

        Debug.Log($"AI COACH: {rotatingCount} CTs rotating to {plantedSite} site!");
    }
}
