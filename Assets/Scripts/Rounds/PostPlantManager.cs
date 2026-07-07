using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// POST-PLANT & RETAKE v4
/// 
/// FIXES:
/// - AWP with pistol/eco → pushes site like everyone else (only holds with actual AWP)
/// - CT retake throws utility (flash + smoke) before pushing
/// - Bots at approach waypoint don't stall — they continue to site
/// - GrenadeManager.OnRetake() called for retake utility
/// </summary>
public class PostPlantManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BombManager bombManager;
    [SerializeField] private GrenadeManager grenadeManager;

    [Header("Post-Plant (T-side)")]
    [SerializeField] private string[] postPlantA = { "WP_PP_A_1", "WP_PP_A_2", "WP_PP_A_3", "WP_PP_A_4" };
    [SerializeField] private string[] postPlantB = { "WP_PP_B_1", "WP_PP_B_2", "WP_PP_B_3", "WP_PP_B_4" };

    [Header("Retake Approaches")]
    [SerializeField] private string retakeA_Main = "WP_Garden";
    [SerializeField] private string retakeA_Split = "WP_Catwalk";
    [SerializeField] private string retakeB_Main = "WP_Bazaar";
    [SerializeField] private string retakeB_Split = "WP_Tunnel";

    [Header("Timing")]
    [SerializeField] private float postPlantDelay = 1.0f;
    [SerializeField] private float ctReactionDelay = 1.5f;

    private List<BotController> attackerBots;
    private List<BotController> defenderBots;
    private string plantedSite = "";
    private bool defusePeekActive = false;
    private Dictionary<string, Transform> wpCache = new Dictionary<string, Transform>();

    private void Start()
    {
        if (bombManager != null)
        {
            bombManager.OnBombPlanted += OnBombPlanted;
            bombManager.OnDefuseProgress += OnDefuseProgress;
        }
        if (grenadeManager == null) grenadeManager = FindObjectOfType<GrenadeManager>();
        CacheWaypoints();
    }

    private void OnDestroy()
    {
        if (bombManager != null)
        { bombManager.OnBombPlanted -= OnBombPlanted; bombManager.OnDefuseProgress -= OnDefuseProgress; }
    }

    public void SetBotReferences(List<BotController> a, List<BotController> d) { attackerBots = a; defenderBots = d; }
    public void ResetForRound() { defusePeekActive = false; plantedSite = ""; StopAllCoroutines(); }

    private void CacheWaypoints()
    {
        wpCache.Clear();
        foreach (var obj in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (obj.name.StartsWith("WP_")) wpCache[obj.name] = obj.transform;
    }

    private void OnBombPlanted(string site)
    {
        plantedSite = site;
        StartCoroutine(DoPostPlant(site));
        StartCoroutine(DoRetake(site));
    }

    private void OnDefuseProgress(float progress)
    {
        if (defusePeekActive || progress < 0.05f) return;
        defusePeekActive = true;

        if (!wpCache.TryGetValue(plantedSite == "A" ? "WP_NodeA" : "WP_NodeB", out Transform siteT)) return;
        int count = 0;
        foreach (var bot in attackerBots)
        {
            if (bot == null || bot.Profile == null || !bot.Profile.IsAlive) continue;
            bot.AssignWaypoints(new Transform[] { siteT });
            bot.StartExecuting();
            count++;
        }
        Debug.Log($"DEFUSE PEEK: {count} T bots rushing bomb!");
    }

    // ===================== T POST-PLANT =====================

    private IEnumerator DoPostPlant(string site)
    {
        yield return new WaitForSeconds(postPlantDelay);
        if (attackerBots == null) yield break;

        string[] positions = site == "A" ? postPlantA : postPlantB;
        List<BotController> alive = GetAlive(attackerBots);
        if (alive.Count == 0) yield break;

        List<Transform> spots = new List<Transform>();
        foreach (string wp in positions)
            if (wpCache.TryGetValue(wp, out Transform t)) spots.Add(t);
        if (wpCache.TryGetValue(site == "A" ? "WP_NodeA" : "WP_NodeB", out Transform siteT))
            spots.Insert(0, siteT);

        HashSet<int> used = new HashSet<int>();
        foreach (var bot in alive)
        {
            float best = float.MaxValue; int bestIdx = -1;
            for (int i = 0; i < spots.Count; i++)
            {
                if (used.Contains(i)) continue;
                float d = Vector3.Distance(bot.transform.position, spots[i].position);
                if (d < best) { best = d; bestIdx = i; }
            }
            if (bestIdx >= 0)
            { used.Add(bestIdx); bot.AssignWaypoints(new Transform[] { spots[bestIdx] }); bot.StartExecuting(); }
        }
    }

    // ===================== CT RETAKE =====================

    private IEnumerator DoRetake(string site)
    {
        yield return new WaitForSeconds(ctReactionDelay);
        if (defenderBots == null) yield break;

        List<BotController> alive = GetAlive(defenderBots);
        if (alive.Count == 0) yield break;

        string mainWP, splitWP, siteWP;
        if (site == "A") { mainWP = retakeA_Main; splitWP = retakeA_Split; siteWP = "WP_NodeA"; }
        else { mainWP = retakeB_Main; splitWP = retakeB_Split; siteWP = "WP_NodeB"; }

        wpCache.TryGetValue(mainWP, out Transform mainT);
        wpCache.TryGetValue(splitWP, out Transform splitT);
        wpCache.TryGetValue(siteWP, out Transform siteT);
        if (siteT == null) yield break;

        // === RETAKE UTILITY: Flash + Smoke at site before push ===
        if (grenadeManager != null)
        {
            StrategyZone retakeZone = site == "A" ? StrategyZone.NodeA : StrategyZone.NodeB;
            grenadeManager.OnRetakeCalled(retakeZone, alive);
        }

        // Wait for utility to land
        yield return new WaitForSeconds(1.5f);

        // Re-check alive
        alive = GetAlive(defenderBots);
        if (alive.Count == 0) yield break;

        int splitAt = Mathf.Max(1, alive.Count / 2);

        // ALL push simultaneously — approach → site (full path, no stopping at approach)
        for (int i = 0; i < alive.Count; i++)
        {
            var bot = alive[i];
            if (!bot.Profile.IsAlive) continue;

            // AWP check: only hold back if they ACTUALLY have an AWP weapon
            // Eco/pistol AWPer pushes like everyone else
            bool hasActualAWP = bot.Profile.CurrentWeapon.weaponName == "AWP";

            if (hasActualAWP && bot.Profile.AssignedRole == Role.AWPer)
            {
                // AWP with actual AWP → hold approach angle
                Transform hold = (i < splitAt) ? mainT : splitT;
                if (hold != null)
                { bot.AssignWaypoints(new Transform[] { hold }); bot.StartExecuting(); }
                continue;
            }

            // Everyone else (including AWPer with pistol): push site
            Transform approach = (i < splitAt) ? mainT : splitT;
            List<Transform> path = new List<Transform>();
            if (approach != null) path.Add(approach);
            path.Add(siteT);

            bot.AssignWaypoints(path.ToArray());
            bot.StartExecuting();
        }

        Debug.Log($"RETAKE: {alive.Count} CTs pushing {site} with utility!");
    }

    private List<BotController> GetAlive(List<BotController> bots)
    {
        List<BotController> r = new List<BotController>();
        foreach (var b in bots) if (b != null && b.Profile != null && b.Profile.IsAlive) r.Add(b);
        return r;
    }
}
