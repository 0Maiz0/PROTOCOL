using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// IGL BRAIN v2 — the player's team CALLS ITS OWN GAME.
///
/// The coach builds the team, sets protocols and watches. The IGL:
///  - picks the opening strategy from the playbook (protocols + own leadership)
///  - times the commit on executes (utility first, then the push)
///  - rotates the defense on info with an anchor left behind
/// All of it voiced in the comms feed.
///
/// Leadership drives everything: read thresholds, commit timing, fake usage.
/// A dead IGL makes no calls — the team runs on a sloppy autopilot.
/// The coach's only override is a tactical timeout (3 per match).
/// </summary>
public class IGLBrain : MonoBehaviour
{
    private List<BotController> myBots;
    private TeamSide mySide;
    private BotController igl;

    private StrategyExecutor strategyExecutor;
    private GrenadeManager grenadeManager;

    private float evalTimer;
    private float rotateCooldown;
    private Coroutine commitCo;

    private void Start()
    {
        strategyExecutor = FindObjectOfType<StrategyExecutor>();
        grenadeManager = FindObjectOfType<GrenadeManager>();
    }

    /// <summary>Called by RoundManager at freeze time, before the opening pick.</summary>
    public void SetTeam(List<BotController> bots, TeamSide side)
    {
        myBots = bots;
        mySide = side;
        igl = null;
        rotateCooldown = 6f; // settle before reacting to info
        evalTimer = Constants.CT_EVAL_INTERVAL;
        if (commitCo != null) { StopCoroutine(commitCo); commitCo = null; }
        if (bots == null) return;
        foreach (var b in bots)
            if (b?.Profile != null && b.Profile.AssignedRole == Role.IGL) { igl = b; break; }
        if (igl == null)
            foreach (var b in bots)
                if (b?.Profile != null) { igl = b; break; }
    }

    private float Leadership => igl?.Profile != null ? igl.Profile.Data.leadership / 100f : 0.3f;

    private void Voice(string message)
    {
        if (igl?.Profile?.Data != null)
            CommsFeed.Post($"{igl.Profile.Data.nickname} (IGL)", message, mySide);
    }

    // ===================== OPENING CALL =====================

    /// <summary>
    /// The IGL picks the round's strategy, weighted by team protocols
    /// (aggression) and personality (good IGLs sell fakes).
    /// </summary>
    public StrategyData PickOpeningStrategy(TeamSide side, TeamProtocols protocols, CoachPanel playbook, BuyType buy)
    {
        StrategyData[] pool = playbook != null ? playbook.GetStrategies(side) : null;
        if (pool == null || pool.Length == 0) return null;

        float aggr = protocols != null ? protocols.aggression / 100f : 0.5f;
        float lead = Leadership;

        float total = 0f;
        float[] weights = new float[pool.Length];
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null) { weights[i] = 0f; continue; }
            float w = pool[i].type switch
            {
                StrategyType.Rush => 0.4f + aggr * 1.2f,
                StrategyType.Execute => 0.8f + aggr * 0.6f,
                StrategyType.Fake => 0.5f + lead * 0.8f, // good IGLs sell fakes
                StrategyType.Control => 1.2f - aggr * 0.6f,
                _ => 1.0f - aggr * 0.3f                   // Default
            };
            weights[i] = Mathf.Max(0.05f, w);
            total += weights[i];
        }

        float roll = Random.Range(0f, total);
        float cum = 0f;
        StrategyData picked = pool[0];
        for (int i = 0; i < pool.Length; i++)
        {
            cum += weights[i];
            if (roll <= cum && pool[i] != null) { picked = pool[i]; break; }
        }

        if (picked != null)
            Voice($"{BuyLabel(buy)} — {picked.strategyName.Trim()}, listen up");
        return picked;
    }

    private static string BuyLabel(BuyType buy) => buy switch
    {
        BuyType.Full => "full buy",
        BuyType.Force => "force buy",
        BuyType.Half => "half buy — SMGs",
        _ => "eco — save round"
    };

    // ===================== IN-ROUND BRAIN =====================

    private void Update()
    {
        if (myBots == null || igl == null || igl.Profile == null || igl.IsFrozen) return;

        // Commit timing on executes. Dead IGL → the team eventually goes in
        // on its own, later and messier — that's why you protect the caller.
        if (mySide == TeamSide.Attacker && commitCo == null &&
            strategyExecutor != null && strategyExecutor.HasStagedBots(mySide))
        {
            bool voiced = igl.Profile.IsAlive;
            float delay = voiced ? Mathf.Lerp(16f, 9f, Leadership) : 8f;
            commitCo = StartCoroutine(CallExecute(delay, voiced));
        }

        // Defensive reads — only a live IGL rotates the team
        if (igl.Profile.IsAlive && mySide == TeamSide.Defender)
        {
            if (rotateCooldown > 0f) rotateCooldown -= Time.deltaTime;
            evalTimer -= Time.deltaTime;
            if (evalTimer <= 0f)
            {
                evalTimer = Constants.CT_EVAL_INTERVAL;
                EvaluateDefense();
            }
        }
    }

    private IEnumerator CallExecute(float delay, bool voiced)
    {
        yield return new WaitForSeconds(delay);
        if (strategyExecutor == null || !strategyExecutor.HasStagedBots(mySide)) yield break;

        if (voiced && igl?.Profile != null && igl.Profile.IsAlive)
            Voice("EXECUTE EXECUTE — GO GO GO!");
        else if (FirstAlive() is BotController b)
            CommsFeed.Post(b.Profile.Data.nickname, "IGL's down — we're going in!", mySide);

        if (grenadeManager != null)
            grenadeManager.OnCommitCalled(strategyExecutor.GetLastStrategyZone(mySide), mySide);
        yield return new WaitForSeconds(0.8f); // utility lands first
        strategyExecutor.CommitStagedBots(mySide);
    }

    private BotController FirstAlive()
    {
        foreach (var b in myBots)
            if (b?.Profile != null && b.Profile.IsAlive) return b;
        return null;
    }

    private void EvaluateDefense()
    {
        if (rotateCooldown > 0f || CommsManager.Instance == null) return;
        GameObject aWP = GameObject.Find("WP_NodeA"), bWP = GameObject.Find("WP_NodeB");
        if (aWP == null || bWP == null) return;

        int sigA = CommsManager.Instance.CountKnownEnemiesNear(mySide, aWP.transform.position, Constants.CT_SIGNAL_RANGE)
                 + CountEnemyUtilNear(aWP.transform.position);
        int sigB = CommsManager.Instance.CountKnownEnemiesNear(mySide, bWP.transform.position, Constants.CT_SIGNAL_RANGE)
                 + CountEnemyUtilNear(bWP.transform.position);

        int threshold = Leadership > 0.6f ? 2 : 3; // sharp IGLs read thinner info
        if (sigA - sigB >= threshold) RotateDefense("A", aWP.transform, bWP.transform.position);
        else if (sigB - sigA >= threshold) RotateDefense("B", bWP.transform, aWP.transform.position);
    }

    private int CountEnemyUtilNear(Vector3 pos)
    {
        TeamSide enemy = mySide == TeamSide.Attacker ? TeamSide.Defender : TeamSide.Attacker;
        int c = 0;
        var fx = GrenadeEffect.ActiveEffects;
        for (int i = 0; i < fx.Count; i++)
            if (fx[i] != null && fx[i].IsActive && fx[i].ThrowerSide == enemy &&
                Vector3.Distance(fx[i].transform.position, pos) < Constants.CT_SIGNAL_RANGE)
                c++;
        return c;
    }

    private void RotateDefense(string site, Transform target, Vector3 anchorSite)
    {
        rotateCooldown = Mathf.Lerp(16f, 10f, Leadership);

        // One anchor stays on the far site — never fully abandon it
        BotController anchor = null;
        float best = float.MaxValue;
        foreach (var bot in myBots)
        {
            if (bot?.Profile == null || !bot.Profile.IsAlive) continue;
            float d = Vector3.Distance(bot.transform.position, anchorSite);
            if (d < best) { best = d; anchor = bot; }
        }

        int rotated = 0;
        foreach (var bot in myBots)
        {
            if (bot?.Profile == null || !bot.Profile.IsAlive || bot == anchor) continue;
            if (Vector3.Distance(bot.transform.position, target.position) < 12f) continue;
            bot.AssignWaypoints(new Transform[] { target });
            bot.StartExecuting();
            rotated++;
        }

        if (rotated > 0)
            Voice($"they're hitting {site} — rotate! {(anchor?.Profile != null ? anchor.Profile.Data.nickname : "someone")}, anchor far side");
    }
}
