using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// GRENADE MANAGER v3
/// 
/// FIX: Grenades are now THROWN from a bot's position to the target.
/// A bot physically throws the nade — you see it arc through the air.
/// Bot chosen = alive teammate closest to optimal throw position.
/// 
/// Flow: find thrower bot → create grenade at bot position → arc to target → land → activate
/// </summary>
public class GrenadeManager : MonoBehaviour
{
    [Header("Grenade Settings")]
    [SerializeField] private float smokeRadius = 4f;
    [SerializeField] private float smokeDuration = 15f;
    [SerializeField] private float flashRadius = 8f;
    [SerializeField] private float molotovRadius = 3f;
    [SerializeField] private float molotovDuration = 7f;
    [SerializeField] private float molotovDPS = 30f;

    [Header("Throw Settings")]
    [SerializeField] private float throwArcHeight = 8f;       // Peak height of arc
    [SerializeField] private float throwFlightTime = 1.2f;     // Seconds in air
    [SerializeField] private float utilitySpacing = 0.6f;

    [Header("Budget")]
    [SerializeField] private int fullBuyNades = 4;
    [SerializeField] private int forceBuyNades = 2;
    [SerializeField] private int halfBuyNades = 1;
    [SerializeField] private int ecoBuyNades = 0;

    private List<GameObject> activeGrenades = new List<GameObject>();
    private BuyType attackerBuyType = BuyType.Full;
    private BuyType defenderBuyType = BuyType.Full;
    private List<BotController> attackerBots;
    private List<BotController> defenderBots;
    private bool ctReactiveUsed = false;

    // Shared per-team nade inventory — plans and reactive utility draw from one pool
    private int attackerNades, defenderNades;
    private bool attackerPanicUsed, defenderPanicUsed;
    private bool attackerClutchFlashUsed, defenderClutchFlashUsed;
    private float panicTimer;

    public void SetBotReferences(List<BotController> a, List<BotController> d) { attackerBots = a; defenderBots = d; }

    /// <summary>
    /// Utility comes from the LEFTOVER bank after guns, not the buy tier alone —
    /// a round-2 "full" off a pistol win means rifles with an empty util pouch.
    /// CT exception: holding sites without nades is throwing the round, so CTs
    /// on force+ keep a utility floor (CS meta: CT buys util before upgrades).
    /// </summary>
    public void AllocateUtility(BuyType aB, BuyType dB, int aBankLeft, int dBankLeft)
    {
        attackerBuyType = aB; defenderBuyType = dB;
        attackerNades = Mathf.Min(GetBudget(aB), aBankLeft / Constants.UTIL_BANK_PER_NADE);
        int ctFloor = dB == BuyType.Full ? Constants.CT_UTIL_FLOOR_FULL
                    : dB == BuyType.Force ? Constants.CT_UTIL_FLOOR_FORCE : 0;
        defenderNades = Mathf.Max(ctFloor,
            Mathf.Min(GetBudget(dB), dBankLeft / Constants.UTIL_BANK_PER_NADE));
    }

    /// <summary>Live shared-inventory count — the HUD's LOW/OK/FULL readout.</summary>
    public int GetNadeCount(TeamSide side) => side == TeamSide.Attacker ? attackerNades : defenderNades;

    // ===================== UTILITY DOCTRINE & POINTS =====================

    // Protocols.aggression per side — decides what scarce utility buys
    private float attackerAggression = 50f, defenderAggression = 50f;

    /// <summary>IGL reports the team's doctrine each round (from TeamProtocols).</summary>
    public void SetAggression(TeamSide side, float aggression)
    {
        if (side == TeamSide.Attacker) attackerAggression = aggression;
        else defenderAggression = aggression;
    }

    /// <summary>Scene-authored points for a zone+purpose, best priority first.</summary>
    private static List<UtilityPoint> PointsFor(StrategyZone zone, UtilityPurpose purpose)
    {
        var result = new List<UtilityPoint>();
        foreach (var p in UtilityPoint.All)
            if (p != null && p.purpose == purpose && ZoneMatches(p.zone, zone))
                result.Add(p);
        result.Sort((a, b) => b.priority.CompareTo(a.priority));
        return result;
    }

    // Split hits the A side of the map — A points serve it
    private static bool ZoneMatches(StrategyZone pointZone, StrategyZone strat) =>
        pointZone == strat || (strat == StrategyZone.Split && pointZone == StrategyZone.NodeA);

    public void ResetForRound()
    {
        StopAllCoroutines(); ClearAllGrenades();
        ctReactiveUsed = false;
        attackerPanicUsed = false; defenderPanicUsed = false;
        attackerClutchFlashUsed = false; defenderClutchFlashUsed = false;
    }

    private bool TrySpendNade(TeamSide side)
    {
        if (side == TeamSide.Attacker)
        {
            if (attackerNades <= 0) return false;
            attackerNades--; return true;
        }
        if (defenderNades <= 0) return false;
        defenderNades--; return true;
    }

    // ===================== PANIC SMOKE =====================

    private void Update()
    {
        panicTimer -= Time.deltaTime;
        if (panicTimer > 0f) return;
        panicTimer = 1f;
        CheckPanicSmoke(attackerBots, TeamSide.Attacker, ref attackerPanicUsed);
        CheckPanicSmoke(defenderBots, TeamSide.Defender, ref defenderPanicUsed);
        CheckClutchFlash(attackerBots, defenderBots, TeamSide.Attacker, ref attackerClutchFlashUsed);
        CheckClutchFlash(defenderBots, attackerBots, TeamSide.Defender, ref defenderClutchFlashUsed);
    }

    /// <summary>
    /// The clutch play: last man alive flashes the KNOWN enemy position and
    /// peeks off the pop — not a blind toss at a map point. Once per round.
    /// </summary>
    private void CheckClutchFlash(List<BotController> mine, List<BotController> enemies, TeamSide side, ref bool used)
    {
        if (used || mine == null || enemies == null || CommsManager.Instance == null) return;

        BotController clutcher = null;
        int aliveMine = 0;
        foreach (var b in mine)
            if (b?.Profile != null && b.Profile.IsAlive) { aliveMine++; clutcher = b; }
        if (aliveMine != 1 || clutcher == null || clutcher.IsFrozen) return;

        int aliveEnemies = 0;
        foreach (var e in enemies)
            if (e?.Profile != null && e.Profile.IsAlive) aliveEnemies++;
        if (aliveEnemies < 2) return;

        if (!CommsManager.Instance.TryGetKnownEnemyNear(side, clutcher.transform.position, 25f, out Vector3 knownPos))
            return;

        used = true;
        CommsFeed.Post(clutcher.Profile.Data.nickname, "flash out — peeking off it!", side);
        StartCoroutine(ClutchFlashPeek(clutcher, knownPos));
    }

    private IEnumerator ClutchFlashPeek(BotController clutcher, Vector3 enemyPos)
    {
        yield return StartCoroutine(ThrowGrenade(clutcher, GrenadeType.Flash, enemyPos, allowUnderFire: true));
        // Flash popped — swing onto the blinded angle
        if (clutcher != null && clutcher.Profile != null && clutcher.Profile.IsAlive &&
            clutcher.TryGetComponent(out EngagementSystem eyes))
            eyes.AlertAt(enemyPos);
    }

    /// <summary>
    /// A hurt, outnumbered player smokes themselves off to break the crossfire —
    /// the "smoke under yourself while being executed" play. Once per team per round.
    /// </summary>
    private void CheckPanicSmoke(List<BotController> bots, TeamSide side, ref bool used)
    {
        if (used || bots == null || CommsManager.Instance == null) return;
        foreach (var b in bots)
        {
            if (b?.Profile == null || !b.Profile.IsAlive || b.IsFrozen) continue;
            if (b.Profile.HP > Constants.PANIC_SMOKE_HP) continue;
            if (CommsManager.Instance.CountKnownEnemiesNear(side, b.transform.position,
                Constants.PANIC_SMOKE_ENEMY_RANGE) < 2) continue;
            // A teammate close enough to trade = fight it out, don't burn the smoke
            // (playtest #17: 10 panic smokes in 6 rounds — desperation play, not a habit)
            if (EngagementSystem.HasFriendlySupportNear(b, Constants.PANIC_SMOKE_SUPPORT_RANGE)) continue;

            used = true;
            CommsFeed.Post(b.Profile.Data.nickname, "smoking myself off!", side);
            StartCoroutine(ThrowGrenade(b, GrenadeType.Smoke, b.transform.position - b.transform.forward * 1f, allowUnderFire: true));
            return;
        }
    }

    // ===================== API =====================

    public void OnStrategyExecuted(StrategyData strategy, TeamSide side)
    {
        if (side == TeamSide.Attacker && strategy.type == StrategyType.Rush)
        {
            int budget = GetBudget(attackerBuyType);
            if (budget > 0) StartCoroutine(RushUtility(strategy.zone, budget));
        }
        else if (side == TeamSide.Attacker && attackerBuyType == BuyType.Full)
        {
            // Default/Control/Fake used to walk into open AWP angles with ZERO
            // util (R09: S1mple + Perfecto picked at 63m/52m with no smoke).
            // Smoke the long sightlines on any full-buy opening.
            if (GetBudget(attackerBuyType) > 0) StartCoroutine(OpeningSmokes());
        }

        if (side == TeamSide.Defender)
        {
            int budget = GetBudget(defenderBuyType);
            if (budget > 0) StartCoroutine(CTOpeningUtility(budget));
        }
    }

    /// <summary>
    /// Full-buy opening smokes on the long mid/site sightlines so the team
    /// isn't fed to a held AWP angle on default rounds.
    /// </summary>
    private IEnumerator OpeningSmokes()
    {
        yield return new WaitForSeconds(2f);
        // WP_TopAxis dropped — the reworked map doesn't need that smoke (playtest #20)
        string[] sightlines = { "WP_Axis", "WP_Bridge" };
        foreach (var wp in sightlines)
        {
            Vector3 target = GetWP(wp);
            if (target == Vector3.zero) continue;
            var thrower = PickThrower(TeamSide.Attacker, Role.Support, target);
            if (thrower == null) continue;
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Smoke, target));
            yield return new WaitForSeconds(utilitySpacing);
        }
    }

    public void OnCommitCalled(StrategyZone zone, TeamSide side)
    {
        if (side == TeamSide.Attacker)
        {
            int budget = GetBudget(attackerBuyType);
            if (budget > 0) StartCoroutine(CommitUtility(zone, budget));
        }
    }

    public void TriggerCTReactive(StrategyZone zone)
    {
        if (ctReactiveUsed) return;
        ctReactiveUsed = true;
        int budget = GetBudget(defenderBuyType);
        if (budget > 0) StartCoroutine(CTReactiveUtility(zone, budget));
    }

    // ===================== UTILITY PLANS =====================

    private IEnumerator RushUtility(StrategyZone zone, int budget)
    {
        // Scene-authored points first, legacy waypoints as fallback
        var cutPts = PointsFor(zone, UtilityPurpose.Cut);
        var entryPts = PointsFor(zone, UtilityPurpose.Entry);
        Vector3 smokeTarget = cutPts.Count > 0 ? cutPts[0].transform.position
            : GetWP(zone == StrategyZone.NodeA ? "WP_Garden" : "WP_Bazaar");
        Vector3 flashTarget = entryPts.Count > 0 ? entryPts[0].transform.position
            : GetWP(zone == StrategyZone.NodeA ? "WP_NodeA" : "WP_NodeB");

        // Doctrine: aggressive rushes flash the entry first — a pistol-round
        // single nade becomes the teammate's entry pop, not a fading smoke
        bool aggressive = attackerAggression >= Constants.UTIL_DOCTRINE_AGGRO;
        Vector3 first = aggressive ? flashTarget : smokeTarget;

        // Just-in-time: wait until someone (Support preferred) is actually near
        // the target — a nade thrown from spawn is wasted before anyone uses it
        BotController thrower = null;
        float waited = 0f;
        while (waited < Constants.UTIL_JIT_TIMEOUT)
        {
            thrower = PickThrower(TeamSide.Attacker, Role.Support, first);
            if (thrower != null &&
                Vector3.Distance(thrower.transform.position, first) < Constants.UTIL_THROW_RANGE)
                break;
            waited += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        if (thrower == null) yield break;

        if (aggressive)
        {
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Flash, flashTarget));
            yield return new WaitForSeconds(utilitySpacing);
            if (GetNadeCount(TeamSide.Attacker) > 0)
                yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Smoke, smokeTarget));
        }
        else
        {
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Smoke, smokeTarget));
            yield return new WaitForSeconds(utilitySpacing);
            if (GetNadeCount(TeamSide.Attacker) > 0)
                yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Flash, flashTarget));
        }
    }

    private IEnumerator CommitUtility(StrategyZone zone, int budget)
    {
        // Legacy fallback targets — used while the scene has no UtilityPoints
        string site, rotCut, holdSpot;
        if (zone == StrategyZone.NodeA || zone == StrategyZone.Split)
        { site = "WP_NodeA"; rotCut = "WP_Garden"; holdSpot = "WP_Overlook"; }
        else if (zone == StrategyZone.NodeB)
        { site = "WP_NodeB"; rotCut = "WP_Bazaar"; holdSpot = "WP_NodeB"; }
        else
        { site = "WP_Axis"; rotCut = "WP_Axis"; holdSpot = "WP_Axis"; }
        Vector3 sitePos = GetWP(site);

        // Target lists: scene-authored points first (priority order), legacy WPs as fallback
        var cuts = new List<Vector3>();
        foreach (var p in PointsFor(zone, UtilityPurpose.Cut)) cuts.Add(p.transform.position);
        if (cuts.Count == 0) cuts.Add(GetWP(rotCut));

        var holds = new List<Vector3>();
        foreach (var p in PointsFor(zone, UtilityPurpose.Hold)) holds.Add(p.transform.position);
        if (holds.Count == 0) holds.Add(GetWP(holdSpot));

        var entries = new List<Vector3>();
        foreach (var p in PointsFor(zone, UtilityPurpose.Entry)) entries.Add(p.transform.position);
        if (entries.Count == 0) entries.Add(sitePos);

        // Comms override: a KNOWN defender near the site eats the molly and the first flash
        if (CommsManager.Instance != null &&
            CommsManager.Instance.TryGetKnownEnemyNear(TeamSide.Attacker, sitePos, 14f, out Vector3 knownCT))
        {
            holds.Insert(0, knownCT);
            entries.Insert(0, knownCT);
        }

        // The IGL's doctrine decides what scarce utility buys: aggressive teams
        // flash the entry first, patient teams cut the map first
        var plan = new List<(GrenadeType type, Vector3 target, Role thrower)>();
        void AddCut(int i) { if (i < cuts.Count) plan.Add((GrenadeType.Smoke, cuts[i], Role.Support)); }
        void AddHold(int i) { if (i < holds.Count) plan.Add((GrenadeType.Molotov, holds[i], Role.IGL)); }
        void AddEntry(int i) { if (i < entries.Count) plan.Add((GrenadeType.Flash, entries[i], Role.Entry)); }

        if (attackerAggression >= Constants.UTIL_DOCTRINE_AGGRO)
        { AddEntry(0); AddEntry(1); AddCut(0); AddHold(0); AddCut(1); }
        else
        { AddCut(0); AddHold(0); AddEntry(0); AddEntry(1); AddCut(1); }

        // Throwers by PROXIMITY (role preferred) — no cross-map mortars
        foreach (var step in plan)
        {
            if (GetNadeCount(TeamSide.Attacker) <= 0) yield break;
            BotController thrower = PickThrower(TeamSide.Attacker, step.thrower, step.target);
            if (thrower == null) continue;
            yield return StartCoroutine(ThrowGrenade(thrower, step.type, step.target));
            yield return new WaitForSeconds(step.type == GrenadeType.Flash ? 0.3f : utilitySpacing);
        }
    }

    private IEnumerator CTOpeningUtility(int budget)
    {
        // Reactive denial — a molly on a timer just burns out before anyone arrives.
        // Each spot is watched; the nade lands when enemies are ACTUALLY coming
        // through it (footstep/contact reports feed CommsManager).
        // Scene-authored Anchor points first — as many as the user placed (capped);
        // legacy two chokes as fallback.
        var anchors = new List<UtilityPoint>();
        foreach (var p in UtilityPoint.All)
            if (p != null && p.purpose == UtilityPurpose.Anchor) anchors.Add(p);
        anchors.Sort((a, b) => b.priority.CompareTo(a.priority));

        if (anchors.Count > 0)
        {
            for (int i = 0; i < anchors.Count && i < Constants.CT_ANCHOR_POINT_CAP; i++)
                StartCoroutine(ChokeDenialAt(anchors[i].transform.position, anchors[i].type));
        }
        else
        {
            if (budget >= 1) StartCoroutine(ChokeDenialAt(GetWP("WP_ACorr"), GrenadeType.Molotov));
            if (budget >= 2) StartCoroutine(ChokeDenialAt(GetWP("WP_BAlley"), GrenadeType.Molotov));
        }
        yield break;
    }

    private IEnumerator ChokeDenialAt(Vector3 choke, GrenadeType type)
    {
        if (choke == Vector3.zero) yield break;

        float watched = 0f;
        while (watched < Constants.CT_CHOKE_WATCH_TIME)
        {
            if (CommsManager.Instance != null &&
                CommsManager.Instance.AnyKnownEnemyNear(TeamSide.Defender, choke, Constants.CT_CHOKE_TRIGGER_RANGE))
            {
                BotController thrower = FindThrowerNear(TeamSide.Defender, choke);
                if (thrower != null)
                {
                    Debug.Log($"CT DENIAL: enemies heard at anchor point — {type} incoming!");
                    yield return StartCoroutine(ThrowGrenade(thrower, type, choke));
                    // Nade down — pre-aim the spot and punish whoever pushes through
                    if (thrower.Profile != null && thrower.Profile.IsAlive &&
                        thrower.TryGetComponent(out EngagementSystem eyes))
                        eyes.AlertAt(choke);
                }
                // One molly per push used to be it for the whole round — the
                // anchor re-arms once the fire burns out (playtest #19: Ts
                // walked the site take uncontested after the opening molly)
                if (GetNadeCount(TeamSide.Defender) <= 0) yield break;
                yield return new WaitForSeconds(Constants.CT_CHOKE_REARM);
                watched = 0f;
                continue;
            }
            watched += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        // Nobody came through — utility saved
    }

    // ===================== SUPPORT FLASH =====================

    public static GrenadeManager Instance { get; private set; }
    private void Awake() { Instance = this; }

    private float attackerSupportFlashAt = -100f, defenderSupportFlashAt = -100f;

    /// <summary>
    /// Flash before a contested re-peek: a bot answering a help call against
    /// 2+ known enemies pops util first instead of walking in dry one by one.
    /// Once per side per SUPPORT_FLASH_COOLDOWN.
    /// </summary>
    public void RequestSupportFlash(BotController bot, Vector3 fightPos)
    {
        if (bot?.Profile == null || !bot.Profile.IsAlive || CommsManager.Instance == null) return;

        float last = bot.Side == TeamSide.Attacker ? attackerSupportFlashAt : defenderSupportFlashAt;
        if (Time.time - last < Constants.SUPPORT_FLASH_COOLDOWN) return;
        if (CommsManager.Instance.CountKnownEnemiesNear(bot.Side, fightPos, 14f) < Constants.SUPPORT_FLASH_ENEMIES) return;

        if (bot.Side == TeamSide.Attacker) attackerSupportFlashAt = Time.time;
        else defenderSupportFlashAt = Time.time;

        CommsFeed.Post(bot.Profile.Data.nickname, "flashing in — go on the pop!", bot.Side);
        StartCoroutine(ThrowGrenade(bot, GrenadeType.Flash, fightPos));
    }

    private IEnumerator CTReactiveUtility(StrategyZone zone, int budget)
    {
        string choke = zone == StrategyZone.NodeA ? "WP_ACorr" :
                       zone == StrategyZone.NodeB ? "WP_BAlley" : "WP_TopAxis";

        BotController thrower = FindThrowerNear(TeamSide.Defender, GetWP(choke));

        if (budget >= 1 && thrower != null)
        {
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Smoke, GetWP(choke)));
            yield return new WaitForSeconds(utilitySpacing);
        }
        if (budget >= 2 && thrower != null)
        {
            Vector3 behindSmoke = GetWP(choke);
            if (behindSmoke != Vector3.zero)
            {
                behindSmoke += (thrower.transform.position - behindSmoke).normalized * -3f;
                yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Molotov, behindSmoke));
            }
        }
    }

    // ===================== CT RETAKE UTILITY =====================

    /// <summary>
    /// Called by PostPlantManager when CT retake starts.
    /// Throws flash + smoke at the site to help CTs push.
    /// </summary>
    public void OnRetakeCalled(StrategyZone site, List<BotController> ctBots)
    {
        int budget = GetBudget(defenderBuyType);
        if (budget > 0 && ctBots != null && ctBots.Count > 0)
            StartCoroutine(RetakeUtility(site, budget, ctBots));
    }

    private IEnumerator RetakeUtility(StrategyZone zone, int budget, List<BotController> ctBots)
    {
        string siteWP = zone == StrategyZone.NodeA ? "WP_NodeA" : "WP_NodeB";
        string chokeWP = zone == StrategyZone.NodeA ? "WP_ACorr" : "WP_BAlley";

        // Find CT thrower closest to site
        BotController thrower = null;
        float bestDist = float.MaxValue;
        Vector3 sitePos = GetWP(siteWP);
        foreach (var b in ctBots)
        {
            if (b == null || !b.Profile.IsAlive) continue;
            float d = Vector3.Distance(b.transform.position, sitePos);
            if (d < bestDist) { bestDist = d; thrower = b; }
        }

        if (thrower == null) yield break;

        // Flash the site first — at the KNOWN defender if comms has one
        if (budget >= 1)
        {
            Vector3 flashTarget = GetWP(siteWP);
            if (CommsManager.Instance != null &&
                CommsManager.Instance.TryGetKnownEnemyNear(TeamSide.Defender, GetWP(siteWP), 14f, out Vector3 knownT))
                flashTarget = knownT;
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Flash, flashTarget));
            yield return new WaitForSeconds(0.3f);
        }

        // Smoke the T post-plant angle
        if (budget >= 2)
        {
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Smoke, GetWP(chokeWP)));
        }

        // Molly to flush the T — at their KNOWN position if comms has one (behind the box!)
        if (budget >= 3)
        {
            yield return new WaitForSeconds(utilitySpacing);
            Vector3 flushTarget = GetWP(siteWP);
            if (CommsManager.Instance != null &&
                CommsManager.Instance.TryGetKnownEnemyNear(TeamSide.Defender, GetWP(siteWP), 14f, out Vector3 camper))
                flushTarget = camper;
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Molotov, flushTarget));
        }

        Debug.Log($"RETAKE UTILITY: {budget} nades thrown at {zone}");
    }

    // ===================== THROW GRENADE WITH ARC =====================

    /// <summary>
    /// Throw a grenade from a bot's position to target with visible arc.
    /// Bot briefly pauses to "throw", then grenade arcs through air and lands.
    /// </summary>
    private IEnumerator ThrowGrenade(BotController thrower, GrenadeType type, Vector3 target, bool allowUnderFire = false)
    {
        if (thrower == null || !thrower.Profile.IsAlive || target == Vector3.zero)
            yield break;
        // Guns beat grenades at contact: a KNOWN enemy breathing on the thrower
        // means shoot first (playtest #18: the last CT flash-smoke-mollied a
        // pistol T at 2m for 14 seconds instead of killing him). Panic smoke
        // and the clutch flash are contact plays BY DESIGN — they pass the flag.
        if (!allowUnderFire && CommsManager.Instance != null &&
            CommsManager.Instance.AnyKnownEnemyNear(thrower.Side, thrower.transform.position, Constants.UTIL_ABORT_ENEMY_RANGE))
            yield break;
        if (!SafeForTeammates(type, target, thrower)) yield break; // never nade your own guys
        if (Vector3.Distance(thrower.transform.position, target) > Constants.UTIL_MAX_THROW_RANGE)
            yield break; // out of throw range — no cross-map mortars
        if (!TrySpendNade(thrower.Side)) yield break; // shared team inventory
        MatchLogger.Log($"NADE: {thrower.Profile.Data.nickname} ({thrower.Side}) throws {type}");

        TeamSide throwerSide = thrower.Side; // capture — thrower can die mid-flight
        Vector3 start = thrower.transform.position + Vector3.up * 1.5f;

        // GameSense = accuracy of the throw
        float gsNorm = thrower.Profile.Data.gameSense / 100f;
        float inaccuracy = Mathf.Lerp(3.5f, 0.5f, gsNorm);
        target += new Vector3(Random.Range(-inaccuracy, inaccuracy), 0, Random.Range(-inaccuracy, inaccuracy));

        // Create grenade projectile (visible sphere flying through air)
        GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = $"GrenadeProjectile_{type}";
        projectile.transform.position = start;
        projectile.transform.localScale = Vector3.one * 0.25f;

        // Remove collider
        Collider col = projectile.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        // Color based on type
        Renderer rend = projectile.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = type switch
            {
                GrenadeType.Smoke => new Color(0.6f, 0.6f, 0.6f, 0.9f),
                GrenadeType.Flash => new Color(1f, 1f, 0.8f, 0.9f),
                GrenadeType.Molotov => new Color(1f, 0.4f, 0.1f, 0.9f),
                _ => Color.white
            };
            rend.material = mat;
        }

        // Animate arc: start → peak → target
        float elapsed = 0f;
        while (elapsed < throwFlightTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / throwFlightTime;

            // Lerp horizontal, parabola vertical
            Vector3 pos = Vector3.Lerp(start, target, t);
            pos.y += throwArcHeight * 4f * t * (1f - t); // Parabola: peaks at t=0.5

            projectile.transform.position = pos;
            yield return null;
        }

        // Destroy projectile, spawn effect at landing position
        Object.Destroy(projectile);
        SpawnEffect(type, target, throwerSide);
    }

    /// <summary>
    /// Don't throw onto alive teammates: mollies burn them, flashes blind them,
    /// smokes ruin their angle. The thrower itself is exempt (panic self-smoke).
    /// </summary>
    private bool SafeForTeammates(GrenadeType type, Vector3 target, BotController thrower)
    {
        var mates = thrower.Side == TeamSide.Attacker ? attackerBots : defenderBots;
        if (mates == null) return true;

        float radius = type switch
        {
            GrenadeType.Smoke => smokeRadius,
            GrenadeType.Flash => flashRadius,
            _ => molotovRadius
        };
        float safe = type == GrenadeType.Molotov ? radius + 1.5f : radius * 0.75f;

        foreach (var m in mates)
        {
            if (m == null || m == thrower || m.Profile == null || !m.Profile.IsAlive) continue;
            if (Vector3.Distance(m.transform.position, target) < safe) return false;
        }
        return true;
    }

    // ===================== SPAWN EFFECT AT LANDING =====================

    private void SpawnEffect(GrenadeType type, Vector3 position, TeamSide throwerSide)
    {
        GameObject obj = new GameObject($"Grenade_{type}");
        obj.transform.position = position;
        GrenadeEffect fx = obj.AddComponent<GrenadeEffect>();
        fx.Type = type;
        fx.ThrowerSide = throwerSide;

        switch (type)
        {
            case GrenadeType.Smoke: fx.Radius = smokeRadius; fx.Duration = smokeDuration; break;
            case GrenadeType.Flash: fx.Radius = flashRadius; fx.Duration = 0.5f; break;
            // DPS from Constants — the scene serializes a stale 30 in molotovDPS
            case GrenadeType.Molotov: fx.Radius = molotovRadius; fx.Duration = molotovDuration; fx.DamagePerSecond = Constants.MOLOTOV_DPS; break;
        }

        activeGrenades.Add(obj);

        // Audio hook
        MatchAudioManager.PlayGrenadeSound(type, position);
    }

    // ===================== FIND THROWER =====================

    /// <summary>
    /// Thrower by PROXIMITY to the target: preferred role if within throw range,
    /// else the nearest teammate in range, else nobody (skip the nade).
    /// </summary>
    private BotController PickThrower(TeamSide side, Role preferred, Vector3 target)
    {
        var bots = side == TeamSide.Attacker ? attackerBots : defenderBots;
        if (bots == null || target == Vector3.zero) return null;

        BotController rolePick = null, nearest = null;
        float best = float.MaxValue;
        foreach (var b in bots)
        {
            if (b?.Profile == null || !b.Profile.IsAlive) continue;
            float d = Vector3.Distance(b.transform.position, target);
            if (b.Profile.AssignedRole == preferred && d <= Constants.UTIL_MAX_THROW_RANGE)
                rolePick = b;
            if (d < best) { best = d; nearest = b; }
        }
        if (rolePick != null) return rolePick;
        return best <= Constants.UTIL_MAX_THROW_RANGE ? nearest : null;
    }

    /// <summary>Find alive bot of given side and preferred role to throw the nade.</summary>
    private BotController FindThrower(TeamSide side, Role preferredRole)
    {
        var bots = (side == TeamSide.Attacker) ? attackerBots : defenderBots;
        if (bots == null) return null;

        // First try: preferred role (never someone mid-duel — shooting > utility)
        foreach (var b in bots)
        {
            if (b == null || !b.Profile.IsAlive || b.IsInCombat) continue;
            if (b.Profile.AssignedRole == preferredRole) return b;
        }

        // Fallback: any alive bot (not Entry — they should be pushing)
        foreach (var b in bots)
        {
            if (b == null || !b.Profile.IsAlive || b.IsInCombat) continue;
            if (b.Profile.AssignedRole != Role.Entry) return b;
        }

        // Last resort: anyone alive
        foreach (var b in bots)
        {
            if (b != null && b.Profile.IsAlive) return b;
        }
        return null;
    }

    /// <summary>Find alive bot closest to a world position (for CT site-specific utility).</summary>
    private BotController FindThrowerNear(TeamSide side, Vector3 nearPosition)
    {
        var bots = (side == TeamSide.Attacker) ? attackerBots : defenderBots;
        if (bots == null || nearPosition == Vector3.zero) return null;

        BotController best = null;
        float bestDist = float.MaxValue;
        foreach (var b in bots)
        {
            if (b == null || !b.Profile.IsAlive || b.IsInCombat) continue; // shooting > utility
            float d = Vector3.Distance(b.transform.position, nearPosition);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    // ===================== HELPERS =====================

    private int GetBudget(BuyType b) => b switch
    {
        BuyType.Full => fullBuyNades, BuyType.Force => forceBuyNades,
        BuyType.Half => halfBuyNades, _ => ecoBuyNades
    };

    private Vector3 GetWP(string name)
    {
        GameObject wp = GameObject.Find(name);
        return wp != null ? wp.transform.position : Vector3.zero;
    }

    public void ClearAllGrenades()
    {
        foreach (var o in activeGrenades) if (o != null) Destroy(o);
        activeGrenades.Clear();
        foreach (var fx in FindObjectsByType<GrenadeEffect>(FindObjectsSortMode.None)) Destroy(fx.gameObject);
    }
}
