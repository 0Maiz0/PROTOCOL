using UnityEngine;
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

    private void Start()
    {
        if (bombManager != null)
        {
            bombManager.OnBombPlanted += OnBombPlanted;
        }
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
        // Handle CT rotation after bomb plant
        if (bombPlantDetected && !hasRotated)
        {
            rotationTimer -= Time.deltaTime;
            if (rotationTimer <= 0f)
            {
                RotateToBombSite();
                hasRotated = true;
            }
        }
    }

    /// <summary>
    /// Pick and execute a strategy for the AI team.
    /// Called by RoundManager at round start.
    /// </summary>
    public StrategyData PickAndExecute(TeamSide aiSide, List<BotController> bots)
    {
        currentBots = bots;
        hasRotated = false;
        bombPlantDetected = false;

        StrategyData[] available = (aiSide == TeamSide.Defender) ? ctStrategies : tStrategies;

        if (available == null || available.Length == 0)
        {
            Debug.LogWarning("AICoach: No strategies available!");
            return null;
        }

        StrategyData picked = PickStrategy(available, aiSide);

        if (picked != null && strategyExecutor != null)
        {
            strategyExecutor.ExecuteStrategy(picked, bots);
            Debug.Log($"AI COACH ({aiSide}): Selected {picked.strategyName}");
        }

        return picked;
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
