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

    public void AllocateUtility(BuyType aB, BuyType dB)
    {
        attackerBuyType = aB; defenderBuyType = dB;
        attackerNades = GetBudget(aB); defenderNades = GetBudget(dB);
    }

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
        yield return StartCoroutine(ThrowGrenade(clutcher, GrenadeType.Flash, enemyPos));
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
            if (CommsManager.Instance.CountKnownEnemiesNear(side, b.transform.position, 20f) < 2) continue;

            used = true;
            CommsFeed.Post(b.Profile.Data.nickname, "smoking myself off!", side);
            StartCoroutine(ThrowGrenade(b, GrenadeType.Smoke, b.transform.position - b.transform.forward * 1f));
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

        if (side == TeamSide.Defender)
        {
            int budget = GetBudget(defenderBuyType);
            if (budget > 0) StartCoroutine(CTOpeningUtility(budget));
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
        string crossingSmoke = zone == StrategyZone.NodeA ? "WP_Garden" : "WP_Bazaar";
        string entryFlash = zone == StrategyZone.NodeA ? "WP_NodeA" : "WP_NodeB";
        Vector3 smokeTarget = GetWP(crossingSmoke);

        // Just-in-time: wait until someone (Support preferred) is actually near
        // the cross — a smoke thrown from spawn fades before anyone uses it
        BotController thrower = null;
        float waited = 0f;
        while (waited < Constants.UTIL_JIT_TIMEOUT)
        {
            thrower = PickThrower(TeamSide.Attacker, Role.Support, smokeTarget);
            if (thrower != null &&
                Vector3.Distance(thrower.transform.position, smokeTarget) < Constants.UTIL_THROW_RANGE)
                break;
            waited += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }

        if (budget >= 1 && thrower != null)
        {
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Smoke, smokeTarget));
            yield return new WaitForSeconds(utilitySpacing);
        }
        if (budget >= 2 && thrower != null)
        {
            yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Flash, GetWP(entryFlash)));
        }
    }

    private IEnumerator CommitUtility(StrategyZone zone, int budget)
    {
        string site, rotCut, holdSpot;
        if (zone == StrategyZone.NodeA || zone == StrategyZone.Split)
        { site = "WP_NodeA"; rotCut = "WP_Garden"; holdSpot = "WP_Overlook"; }
        else if (zone == StrategyZone.NodeB)
        { site = "WP_NodeB"; rotCut = "WP_Bazaar"; holdSpot = "WP_NodeB"; }
        else
        { site = "WP_Axis"; rotCut = "WP_Axis"; holdSpot = "WP_Axis"; }

        // Targets first, then throwers by PROXIMITY (role preferred) —
        // no more cross-map mortars from the B player onto A
        Vector3 cutTarget = GetWP(rotCut);
        Vector3 mollyTarget = GetWP(holdSpot);
        if (CommsManager.Instance != null &&
            CommsManager.Instance.TryGetKnownEnemyNear(TeamSide.Attacker, GetWP(site), 14f, out Vector3 camper))
            mollyTarget = camper;
        Vector3 siteFlash = GetWP(site);
        if (CommsManager.Instance != null &&
            CommsManager.Instance.TryGetKnownEnemyNear(TeamSide.Attacker, GetWP(site), 14f, out Vector3 knownCT))
            siteFlash = knownCT;

        BotController smoker = PickThrower(TeamSide.Attacker, Role.Support, cutTarget);
        BotController mollyer = PickThrower(TeamSide.Attacker, Role.IGL, mollyTarget);
        BotController flasher = PickThrower(TeamSide.Attacker, Role.Entry, siteFlash);

        // 1. Smoke rotation cut
        if (budget >= 1 && smoker != null)
        {
            yield return StartCoroutine(ThrowGrenade(smoker, GrenadeType.Smoke, cutTarget));
            yield return new WaitForSeconds(utilitySpacing);
        }
        // 2. Molly the hold spot — or the KNOWN camper if comms has one near the site
        if (budget >= 2 && mollyer != null)
        {
            yield return StartCoroutine(ThrowGrenade(mollyer, GrenadeType.Molotov, mollyTarget));
            yield return new WaitForSeconds(utilitySpacing);
        }
        // 3. Flash site — at the KNOWN holder if comms has one
        if (budget >= 3 && flasher != null)
        {
            yield return StartCoroutine(ThrowGrenade(flasher, GrenadeType.Flash, siteFlash));
            yield return new WaitForSeconds(0.3f);
        }
        // 4. Pop flash
        if (budget >= 4 && flasher != null)
        {
            yield return StartCoroutine(ThrowGrenade(flasher, GrenadeType.Flash, siteFlash));
        }
    }

    private IEnumerator CTOpeningUtility(int budget)
    {
        // Reactive denial — a molly on a timer just burns out before anyone arrives.
        // Each choke is watched; the molly lands when enemies are ACTUALLY coming
        // through it (footstep/contact reports feed CommsManager).
        if (budget >= 1) StartCoroutine(ChokeDenial("WP_ACorr"));
        if (budget >= 2) StartCoroutine(ChokeDenial("WP_BAlley"));
        yield break;
    }

    private IEnumerator ChokeDenial(string chokeName)
    {
        Vector3 choke = GetWP(chokeName);
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
                    Debug.Log($"CT DENIAL: enemies heard at {chokeName} — molly incoming!");
                    yield return StartCoroutine(ThrowGrenade(thrower, GrenadeType.Molotov, choke));
                    // Molly down — pre-aim the choke and punish whoever pushes through
                    if (thrower.Profile != null && thrower.Profile.IsAlive &&
                        thrower.TryGetComponent(out EngagementSystem eyes))
                        eyes.AlertAt(choke);
                }
                yield break;
            }
            watched += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        // Nobody came through — utility saved
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
    private IEnumerator ThrowGrenade(BotController thrower, GrenadeType type, Vector3 target)
    {
        if (thrower == null || !thrower.Profile.IsAlive || target == Vector3.zero)
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
