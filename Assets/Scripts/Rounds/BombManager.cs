using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// BOMB MANAGER v2
/// 
/// FIX: Bot STOPS MOVING when planting. Sets BotController.IsPlanting = true.
/// Also increased plant radius check tolerance.
/// </summary>
public class BombManager : MonoBehaviour
{
    [Header("Bomb Sites")]
    public BombSite siteA;
    public BombSite siteB;

    [Header("Timing")]
    public float plantTime = 3f;
    public float defuseTime = 5f;
    public float bombTimer = 40f;

    [Header("Runtime")]
    [SerializeField] private BombState bombState = BombState.NotPlanted;
    [SerializeField] private float currentTimer = 0f;
    [SerializeField] private string plantedSite = "";

    public BombState CurrentState => bombState;
    public float Timer => currentTimer;
    public bool IsPlanted => bombState == BombState.Planted;
    public bool IsExploded => bombState == BombState.Exploded;
    public bool IsDefused => bombState == BombState.Defused;
    public string PlantedSiteName => plantedSite;

    public event System.Action<string> OnBombPlanted;
    public event System.Action<string> OnPlantStarted; // plant begins — guards spread NOW
    public event System.Action OnBombExploded;
    public event System.Action OnBombDefused;
    public event System.Action<float> OnPlantProgress;
    public event System.Action<float> OnDefuseProgress;

    private float plantProgress = 0f;
    private float defuseProgress = 0f;
    private bool isPlanting = false;
    private bool isDefusing = false;
    private BotController currentPlanter = null;

    // Bomb carrier — only the carrier can plant. Dies → bomb drops, must be picked up.
    private BotController bombCarrier = null;
    private bool bombDropped = false;
    private Vector3 droppedPos;
    private GameObject bombMarker;
    private float nextFetchOrder;

    public BotController Carrier => bombCarrier;
    public bool IsPlantInProgress => isPlanting;

    public void ResetForRound()
    {
        bombState = BombState.NotPlanted;
        currentTimer = 0f; plantedSite = "";
        plantProgress = 0f; defuseProgress = 0f;
        isPlanting = false; isDefusing = false;
        bombCarrier = null; bombDropped = false;
        DestroyMarker();

        // Release planter lock
        if (currentPlanter != null)
        { currentPlanter.IsPlanting = false; currentPlanter = null; }
    }

    /// <summary>
    /// Assign the bomb at freeze time. Support carries by default (mid-pack),
    /// so the entry can frag freely — falls back to any alive attacker.
    /// </summary>
    public void AssignCarrier(List<BotController> attackers)
    {
        bombCarrier = null;
        bombDropped = false;
        DestroyMarker();
        foreach (var b in attackers)
            if (b?.Profile != null && b.Profile.IsAlive && b.Profile.AssignedRole == Role.Support)
            { bombCarrier = b; break; }
        if (bombCarrier == null)
            foreach (var b in attackers)
                if (b?.Profile != null && b.Profile.IsAlive) { bombCarrier = b; break; }

        if (bombCarrier != null)
            CommsFeed.Post(bombCarrier.Profile.Data.nickname, "I've got the bomb", TeamSide.Attacker);
    }

    private void CreateBombMarker()
    {
        DestroyMarker();
        bombMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bombMarker.name = "DroppedBomb";
        bombMarker.transform.position = droppedPos + Vector3.up * 0.2f;
        bombMarker.transform.localScale = new Vector3(0.4f, 0.25f, 0.3f);
        var col = bombMarker.GetComponent<Collider>();
        if (col != null) Destroy(col);
        var r = bombMarker.GetComponent<Renderer>();
        if (r != null) { var m = new Material(Shader.Find("Sprites/Default")); m.color = Color.red; r.material = m; }
    }

    private void DestroyMarker()
    {
        if (bombMarker != null) { Destroy(bombMarker); bombMarker = null; }
    }

    private void Update()
    {
        if (bombState == BombState.Planted)
        {
            currentTimer -= Time.deltaTime;
            if (currentTimer <= 0f)
            {
                bombState = BombState.Exploded;
                Debug.Log("BOMB EXPLODED! T side wins!");
                OnBombExploded?.Invoke();
            }
        }
    }

    public void TryAutoPlant(List<BotController> attackerBots, List<BotController> defenderBots = null)
    {
        if (bombState != BombState.NotPlanted) return;

        // Carrier down? Bomb is on the ground — nearest T fetches it
        if (bombCarrier == null || bombCarrier.Profile == null || !bombCarrier.Profile.IsAlive)
            HandleDroppedBomb(attackerBots);

        if (bombCarrier == null || bombCarrier.Profile == null || !bombCarrier.Profile.IsAlive)
            return;

        // ONLY the carrier plants — and only at the site they're actually standing in
        BombSite targetSite = null;
        if (siteA != null && siteA.IsPlayerInSite(bombCarrier.transform.position)) targetSite = siteA;
        else if (siteB != null && siteB.IsPlayerInSite(bombCarrier.transform.position)) targetSite = siteB;

        if (targetSite != null)
        {
            // Plant guard: don't START a plant with a CT breathing on the site —
            // R08: sneaky 17s plant under a 2-man hold got defused with 5 Ts alive.
            // A plant already in progress is a commitment and continues.
            if (isPlanting || SiteSafeToPlant(defenderBots))
                ProcessPlant(bombCarrier, targetSite);
        }
        else if (isPlanting)
        {
            if (currentPlanter != null)
            { currentPlanter.IsPlanting = false; currentPlanter = null; }
            isPlanting = false;
            plantProgress = 0f;
        }
    }

    private bool SiteSafeToPlant(List<BotController> defenders)
    {
        if (defenders == null || bombCarrier == null) return true;
        foreach (var d in defenders)
        {
            if (d?.Profile == null || !d.Profile.IsAlive) continue;
            if (Vector3.Distance(d.transform.position, bombCarrier.transform.position) >= 12f) continue;

            // Only a CT the team KNOWS about delays the plant. A silent camper
            // nobody spotted was invisibly freezing it forever — playtest #17
            // R04: 5 Ts stood on the site 47s without planting against 1 hidden
            // CT. Unknown lurker → plant starts; if he peeks it, that's the fight.
            if (CommsManager.Instance == null ||
                CommsManager.Instance.AnyKnownEnemyNear(TeamSide.Attacker, d.transform.position, Constants.INTEL_PREAIM_RADIUS))
                return false; // clear the KNOWN threat first
        }
        return true;
    }

    private void HandleDroppedBomb(List<BotController> attackers)
    {
        if (!bombDropped)
        {
            if (bombCarrier == null) return; // standalone testing — no carrier assigned
            droppedPos = bombCarrier.transform.position;
            bombDropped = true;
            bombCarrier = null;
            CreateBombMarker();
            CommsFeed.PostSystem("BOMB DOWN!");
            Debug.Log("BOMB: carrier died — bomb dropped");
            return;
        }

        // Find the nearest alive T — pick up if close, otherwise send them to fetch
        BotController nearest = null;
        float best = float.MaxValue;
        foreach (var b in attackers)
        {
            if (b?.Profile == null || !b.Profile.IsAlive) continue;
            float d = Vector3.Distance(b.transform.position, droppedPos);
            if (d < best) { best = d; nearest = b; }
        }
        if (nearest == null) return;

        if (best < Constants.BOMB_PICKUP_RANGE)
        {
            bombCarrier = nearest;
            bombDropped = false;
            DestroyMarker();
            CommsFeed.Post(nearest.Profile.Data.nickname, "got the bomb!", TeamSide.Attacker);
            return;
        }

        if (Time.time >= nextFetchOrder && bombMarker != null)
        {
            nextFetchOrder = Time.time + 2f;
            nearest.AssignWaypoints(new Transform[] { bombMarker.transform });
            nearest.StartExecuting();
        }
    }

    private void ProcessPlant(BotController planter, BombSite site)
    {
        // Fresh plant OR the planter changed (previous one died mid-plant) —
        // progress restarts and the new planter gets stopped properly
        if (!isPlanting || currentPlanter != planter)
        {
            if (currentPlanter != null && currentPlanter != planter)
                currentPlanter.IsPlanting = false;

            isPlanting = true;
            plantProgress = 0f;
            currentPlanter = planter;

            // >>> STOP THE BOT — they must stand still to plant <<<
            planter.IsPlanting = true;
            var agent = planter.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) { agent.isStopped = true; agent.ResetPath(); }

            CommsFeed.Post(planter.Profile.Data.nickname, $"planting the bomb on {site.siteName}!", TeamSide.Attacker);
            Debug.Log($"PLANTING on {site.siteName}... (bot stopped)");
            OnPlantStarted?.Invoke(site.siteName);
        }

        plantProgress += Time.deltaTime / plantTime;
        OnPlantProgress?.Invoke(plantProgress);

        if (plantProgress >= 1f)
        {
            bombState = BombState.Planted;
            currentTimer = bombTimer;
            plantedSite = site.siteName;
            isPlanting = false;

            // Release planter
            if (currentPlanter != null)
            { currentPlanter.IsPlanting = false; currentPlanter = null; }

            Debug.Log($"BOMB PLANTED on {site.siteName}! {bombTimer}s until explosion!");
            OnBombPlanted?.Invoke(site.siteName);
        }
    }

    public void TryAutoDefuse(List<BotController> defenderBots)
    {
        if (bombState != BombState.Planted) return;

        BombSite plantedBombSite = plantedSite == "A" ? siteA : siteB;
        if (plantedBombSite == null) return;

        bool ctOnSite = false;
        foreach (var bot in defenderBots)
        {
            if (bot == null || bot.Profile == null || !bot.Profile.IsAlive) continue;
            if (plantedBombSite.IsPlayerInSite(bot.transform.position))
            { ctOnSite = true; break; }
        }

        if (ctOnSite)
        {
            if (!isDefusing) { isDefusing = true; defuseProgress = 0f; Debug.Log("DEFUSING..."); }
            defuseProgress += Time.deltaTime / defuseTime;
            OnDefuseProgress?.Invoke(defuseProgress);

            if (defuseProgress >= 1f)
            {
                bombState = BombState.Defused;
                isDefusing = false;
                Debug.Log("BOMB DEFUSED! CT wins!");
                OnBombDefused?.Invoke();
            }
        }
        else
        {
            if (isDefusing) { isDefusing = false; defuseProgress = 0f; }
        }
    }
}
