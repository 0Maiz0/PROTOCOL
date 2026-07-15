using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// IGL BRAIN v3 — the player's team calls its own game, with a PERSONALITY.
///
/// Reads: the coach's protocols + own Leadership (sharpness of reads) +
/// own Ego (voice flavor) + the enemy's economy + which sites keep failing.
/// Calls: opening strategy, execute timing, defensive rotations with an
/// anchor, plant pushes on man advantage / burning clock, and saves when
/// the round is lost. A dead IGL makes no calls — sloppy autopilot only.
/// The coach's only override remains the tactical timeout.
/// </summary>
public class IGLBrain : MonoBehaviour
{
    private List<BotController> myBots;
    private List<BotController> enemyBots;
    private TeamSide mySide;
    private BotController igl;

    private StrategyExecutor strategyExecutor;
    private GrenadeManager grenadeManager;
    private BombManager bombManager;
    private MatchEconomy economy;

    private float evalTimer;
    private float rotateCooldown;
    private Coroutine commitCo;

    private float roundStartTime;
    private float lastPlantPushTime;
    private bool saveCalled;
    private float lastSaveReissue;

    // Adaptation memory: zones that keep losing us rounds get called off
    private readonly Dictionary<StrategyZone, int> zoneFails = new Dictionary<StrategyZone, int>();
    private StrategyData lastPick;

    private void Start()
    {
        strategyExecutor = FindObjectOfType<StrategyExecutor>();
        grenadeManager = FindObjectOfType<GrenadeManager>();
        bombManager = FindObjectOfType<BombManager>();
        economy = FindObjectOfType<MatchEconomy>();
    }

    /// <summary>On an eco there's nothing worth saving — play the round out.</summary>
    private bool NothingToSave =>
        economy != null && economy.GetEconomy(mySide).currentBuyType == BuyType.Eco;

    /// <summary>Called by RoundManager at freeze time, before the opening pick.</summary>
    public void SetTeam(List<BotController> bots, List<BotController> enemies, TeamSide side)
    {
        myBots = bots;
        enemyBots = enemies;
        mySide = side;
        igl = null;
        rotateCooldown = 6f;
        evalTimer = Constants.CT_EVAL_INTERVAL;
        roundStartTime = Time.time;
        lastPlantPushTime = -100f;
        saveCalled = false;
        lastSaveReissue = -100f;
        if (commitCo != null) { StopCoroutine(commitCo); commitCo = null; }
        if (bots == null) return;
        foreach (var b in bots)
            if (b?.Profile != null && b.Profile.AssignedRole == Role.IGL) { igl = b; break; }
        if (igl == null)
            foreach (var b in bots)
                if (b?.Profile != null) { igl = b; break; }
    }

    /// <summary>RoundManager reports results — the IGL learns what isn't working.</summary>
    public void OnRoundResult(bool won, StrategyData played)
    {
        var strat = played != null ? played : lastPick;
        if (strat == null) return;
        zoneFails[strat.zone] = won ? 0 : GetFails(strat.zone) + 1;
    }

    private int GetFails(StrategyZone z) => zoneFails.TryGetValue(z, out int f) ? f : 0;

    private float Leadership => igl?.Profile != null ? igl.Profile.Data.leadership / 100f : 0.3f;

    // ===================== PERSONALITY VOICE =====================

    /// <summary>Big ego talks big, high leadership stays composed, the rest just call it.</summary>
    private string Flavor(string plain, string cocky, string composed)
    {
        if (igl?.Profile == null) return plain;
        if (igl.Profile.Data.ego >= 65) return cocky;
        if (igl.Profile.Data.leadership >= 65) return composed;
        return plain;
    }

    private void Voice(string message)
    {
        // Dead IGL → whoever's alive makes the call (leaderless fallback)
        var speaker = (igl?.Profile != null && igl.Profile.IsAlive) ? igl : FirstAlive();
        if (speaker?.Profile?.Data == null) return;
        string tag = speaker == igl ? " (IGL)" : "";
        CommsFeed.Post($"{speaker.Profile.Data.nickname}{tag}", message, mySide);
    }

    // ===================== OPENING CALL =====================

    public StrategyData PickOpeningStrategy(TeamSide side, TeamProtocols protocols, CoachPanel playbook,
        BuyType buy, BuyType enemyBuy)
    {
        StrategyData[] pool = playbook != null ? playbook.GetStrategies(side) : null;
        if (pool == null || pool.Length == 0) return null;

        float aggr = protocols != null ? protocols.aggression / 100f : 0.5f;
        float lead = Leadership;
        bool enemyBroke = enemyBuy == BuyType.Eco || enemyBuy == BuyType.Half;

        // Utility doctrine follows the coach's aggression: flash-first entries
        // vs smoke-first map cuts when the nade pouch is thin
        if (grenadeManager != null)
            grenadeManager.SetAggression(side, protocols != null ? protocols.aggression : 50f);

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

            // Anti-eco read: they're broke — hit fast, don't feed them picks
            if (enemyBroke && (pool[i].type == StrategyType.Rush || pool[i].type == StrategyType.Execute))
                w *= 1.4f;

            // This zone keeps failing — a sharp IGL stops slamming into the wall
            w /= 1f + GetFails(pool[i].zone) * 0.5f * (0.5f + lead);

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
        lastPick = picked;

        if (picked != null)
        {
            string call = $"{BuyLabel(buy)} — {picked.strategyName.Trim()}";
            Voice(Flavor($"{call}, listen up",
                         $"{call}. trust me, this one's free",
                         $"{call}. stick to the plan"));

            if (enemyBroke)
                Voice("they're broke — expect stacks, punish fast");

            foreach (var kvp in zoneFails)
                if (kvp.Value >= 2 && picked != null && kvp.Key != picked.zone)
                {
                    Voice($"{ZoneName(kvp.Key)} keeps failing us — we adjust");
                    break;
                }
        }
        return picked;
    }

    private static string ZoneName(StrategyZone z) => z switch
    {
        StrategyZone.NodeA => "A",
        StrategyZone.NodeB => "B",
        StrategyZone.Axis => "mid",
        _ => "split"
    };

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

        // Commit timing. T executes wait for the call; CT Control/stack strats
        // must commit FAST — staged CTs were standing one waypoint short of
        // their stack positions forever (the "I never saw a stack" bug).
        if (commitCo == null && strategyExecutor != null && strategyExecutor.HasStagedBots(mySide))
        {
            bool voiced = igl.Profile.IsAlive;
            float delay = mySide == TeamSide.Defender ? 4f
                        : voiced ? Mathf.Lerp(16f, 9f, Leadership) : 8f;
            commitCo = StartCoroutine(CallExecute(delay, voiced));
        }

        evalTimer -= Time.deltaTime;
        if (mySide == TeamSide.Defender)
        {
            // A dead IGL used to shut the CT brain off completely — playtest #20
            // R17: IGL died at 21s, zero rotations, T walked the site. Someone
            // alive still calls it, just slower and off thinner reads.
            if (rotateCooldown > 0f) rotateCooldown -= Time.deltaTime;
            if (evalTimer <= 0f)
            {
                evalTimer = Constants.CT_EVAL_INTERVAL;
                EvaluateDefense();
            }
        }
        else if (evalTimer <= 0f)
        {
            // Plant pushes/saves run even with a dead IGL — R06: carrier AND
            // IGL died at 11s and three Ts wandered 80s without planting
            evalTimer = Constants.CT_EVAL_INTERVAL;
            EvaluateAttack();
        }
    }

    private IEnumerator CallExecute(float delay, bool voiced)
    {
        yield return new WaitForSeconds(delay);
        if (strategyExecutor == null || !strategyExecutor.HasStagedBots(mySide)) yield break;

        // CT: no utility barrage — just settle into the final hold positions
        if (mySide == TeamSide.Defender)
        {
            if (voiced && igl?.Profile != null && igl.Profile.IsAlive)
                Voice("get set — final positions, hold your angles");
            strategyExecutor.CommitStagedBots(mySide);
            yield break;
        }

        if (voiced && igl?.Profile != null && igl.Profile.IsAlive)
            Voice(Flavor("EXECUTE EXECUTE — GO GO GO!",
                         "watch this — EXECUTE, NOW!",
                         "on my count — execute, trade everything"));
        else if (FirstAlive() is BotController b)
            CommsFeed.Post(b.Profile.Data.nickname, "IGL's down — we're going in!", mySide);

        if (grenadeManager != null)
            grenadeManager.OnCommitCalled(strategyExecutor.GetLastStrategyZone(mySide), mySide);
        yield return new WaitForSeconds(0.8f); // utility lands first
        strategyExecutor.CommitStagedBots(mySide);
    }

    // ===================== T-SIDE GAME STATE =====================

    /// <summary>
    /// The 4v1-and-no-plant fix: with a man advantage or a burning clock the
    /// IGL sends everyone to the more open site to get the bomb down. And when
    /// the round is clearly lost — save the guns instead of feeding.
    /// </summary>
    private void EvaluateAttack()
    {
        if (bombManager == null || bombManager.IsPlanted || bombManager.IsExploded) return;
        if (bombManager.IsPlantInProgress) return; // plant is happening — do NOT yank the team elsewhere

        int mine = CountAlive(myBots);
        int theirs = CountAlive(enemyBots);
        if (mine == 0) return;
        float elapsed = Time.time - roundStartTime;

        // Round is lost — save the guns (only if there ARE guns to save)
        if (!saveCalled && GunsWorthSaving() && mine <= 2 && theirs - mine >= 2 && elapsed > 30f)
        {
            saveCalled = true;
            Voice(Flavor("numbers are bad — save the guns",
                         "this one's dead — exit, keep the rifles",
                         "we reset — save, win the next one"));
            SendTeamTo("WP_TopAxis", bypassHazards: false);
            return;
        }

        // A called save is FINAL — the clock-burning push below was overriding
        // it 14s later and marching the savers back into the site to die
        // (playtest #17 R05: save at 37s, "hit A NOW!" at 51/71/91s)
        if (saveCalled)
        {
            // Re-issue occasionally: bots that were mid-fight ignored the first exit order
            if (Time.time - lastSaveReissue > 6f)
            {
                lastSaveReissue = Time.time;
                SendTeamTo("WP_TopAxis", bypassHazards: false);
            }
            return;
        }

        // Man advantage or burning clock and STILL no plant — take a site NOW
        bool manAdvantage = mine - theirs >= 2 && elapsed > 20f;
        bool clockBurning = elapsed > 50f;
        if ((manAdvantage || clockBurning) && Time.time - lastPlantPushTime > 20f)
        {
            lastPlantPushTime = Time.time;
            string site = PickOpenSite(out string wp);
            Voice(manAdvantage
                ? Flavor($"we're up bodies — take {site}, get it down!",
                         $"free round — walk {site}, plant it",
                         $"numbers are ours — {site}, plant, positions")
                : Flavor($"clock's burning — hit {site} NOW!",
                         $"time! {site}, go go go",
                         $"clock check — {site}, execute now"));
            SendTeamToSite(wp, site);
        }
    }

    private string PickOpenSite(out string wpName)
    {
        GameObject aWP = GameObject.Find("WP_NodeA"), bWP = GameObject.Find("WP_NodeB");

        // The bomb goes where the CARRIER is — never call a site away from the bomb
        var carrier = bombManager != null ? bombManager.Carrier : null;
        if (carrier != null && carrier.Profile != null && carrier.Profile.IsAlive && aWP != null && bWP != null)
        {
            bool nearA = Vector3.Distance(carrier.transform.position, aWP.transform.position) <=
                         Vector3.Distance(carrier.transform.position, bWP.transform.position);
            wpName = nearA ? "WP_NodeA" : "WP_NodeB";
            return nearA ? "A" : "B";
        }

        int sigA = 0, sigB = 0;
        if (CommsManager.Instance != null)
        {
            if (aWP != null) sigA = CommsManager.Instance.CountKnownEnemiesNear(mySide, aWP.transform.position, Constants.CT_SIGNAL_RANGE);
            if (bWP != null) sigB = CommsManager.Instance.CountKnownEnemiesNear(mySide, bWP.transform.position, Constants.CT_SIGNAL_RANGE);
        }
        bool goA = sigA <= sigB;
        wpName = goA ? "WP_NodeA" : "WP_NodeB";
        return goA ? "A" : "B";
    }

    private void SendTeamTo(string wpName, bool bypassHazards)
    {
        GameObject wp = GameObject.Find(wpName);
        if (wp == null) return;
        foreach (var b in myBots)
        {
            if (b?.Profile == null || !b.Profile.IsAlive) continue;
            b.HazardBypass = bypassHazards;
            b.AssignWaypoints(new Transform[] { wp.transform });
            b.StartExecuting();
        }
    }

    /// <summary>
    /// Plant push with a real setup: the carrier walks the site, everyone else
    /// takes the post-plant spots around it. One shared waypoint left 5 Ts
    /// piled on the carrier "standing on the bomb" (playtest #17 R04).
    /// </summary>
    private void SendTeamToSite(string wpName, string site)
    {
        GameObject wp = GameObject.Find(wpName);
        if (wp == null) return;
        var carrier = bombManager != null ? bombManager.Carrier : null;

        int spread = 1;
        foreach (var b in myBots)
        {
            if (b?.Profile == null || !b.Profile.IsAlive) continue;
            Transform target = wp.transform;
            if (b != carrier)
            {
                GameObject spot = GameObject.Find($"WP_PP_{site}_{spread}");
                if (spot != null) { target = spot.transform; spread = spread % 4 + 1; }
            }
            b.HazardBypass = true;
            b.AssignWaypoints(new Transform[] { target });
            b.StartExecuting();
        }
    }

    private int CountAlive(List<BotController> bots)
    {
        if (bots == null) return 0;
        int c = 0;
        foreach (var b in bots)
            if (b?.Profile != null && b.Profile.IsAlive) c++;
        return c;
    }

    private BotController FirstAlive()
    {
        foreach (var b in myBots)
            if (b?.Profile != null && b.Profile.IsAlive) return b;
        return null;
    }

    // ===================== CT-SIDE DEFENSE =====================

    private void EvaluateDefense()
    {
        bool leaderless = igl?.Profile == null || !igl.Profile.IsAlive;

        // Hold is hopeless (badly outnumbered pre-plant) — save the guns,
        // carryover makes this real money next round. Nothing to save on an
        // eco (playtest #20 R04: "what are they saving" — pistols) → fight.
        if (!saveCalled && bombManager != null && !bombManager.IsPlanted && GunsWorthSaving())
        {
            int mine = CountAlive(myBots), theirs = CountAlive(enemyBots);
            if (mine > 0 && mine <= 2 && theirs - mine >= 2 && Time.time - roundStartTime > 40f)
            {
                saveCalled = true;
                Voice(Flavor("we can't hold this — save the guns", "dead round — keep the rifles, exit", "save call — regroup, next one's ours"));
                SendTeamTo("WP_Garden", bypassHazards: false);
                return;
            }
        }

        // Saving CTs stay saved — rotations were sending them back to the sites
        if (saveCalled) return;

        if (rotateCooldown > 0f || CommsManager.Instance == null) return;
        GameObject aWP = GameObject.Find("WP_NodeA"), bWP = GameObject.Find("WP_NodeB");
        if (aWP == null || bWP == null) return;

        int sigA = CommsManager.Instance.CountKnownEnemiesNear(mySide, aWP.transform.position, Constants.CT_SIGNAL_RANGE)
                 + CountEnemyUtilNear(aWP.transform.position);
        int sigB = CommsManager.Instance.CountKnownEnemiesNear(mySide, bWP.transform.position, Constants.CT_SIGNAL_RANGE)
                 + CountEnemyUtilNear(bWP.transform.position);

        // Sharp IGLs read thinner info; a leaderless team needs it obvious
        int threshold = (Leadership > 0.6f ? 2 : 3) + (leaderless ? 1 : 0);
        if (sigA - sigB >= threshold) RotateDefense("A", aWP.transform, bWP.transform.position);
        else if (sigB - sigA >= threshold) RotateDefense("B", bWP.transform, aWP.transform.position);
    }

    /// <summary>Rifles are worth exiting with — eco pistols are not.</summary>
    private bool GunsWorthSaving()
    {
        foreach (var b in myBots)
        {
            if (b?.Profile == null || !b.Profile.IsAlive) continue;
            string w = b.Profile.CurrentWeapon.weaponName;
            if (w == "AK-47/M4" || w == "AWP" || w == "Galil/Famas") return true;
        }
        return false;
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
        if (igl?.Profile == null || !igl.Profile.IsAlive) rotateCooldown *= 1.5f; // leaderless = sluggish

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
            Voice(Flavor($"they're hitting {site} — rotate! {AnchorName(anchor)}, anchor far side",
                         $"{site}, all of it — rotate NOW, {AnchorName(anchor)} holds",
                         $"read is {site} — rotating {rotated}, {AnchorName(anchor)} anchors"));
    }

    private static string AnchorName(BotController anchor)
        => anchor?.Profile?.Data != null ? anchor.Profile.Data.nickname : "someone";
}
