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
    public event System.Action OnBombExploded;
    public event System.Action OnBombDefused;
    public event System.Action<float> OnPlantProgress;
    public event System.Action<float> OnDefuseProgress;

    private float plantProgress = 0f;
    private float defuseProgress = 0f;
    private bool isPlanting = false;
    private bool isDefusing = false;
    private BotController currentPlanter = null;

    public void ResetForRound()
    {
        bombState = BombState.NotPlanted;
        currentTimer = 0f; plantedSite = "";
        plantProgress = 0f; defuseProgress = 0f;
        isPlanting = false; isDefusing = false;

        // Release planter lock
        if (currentPlanter != null)
        { currentPlanter.IsPlanting = false; currentPlanter = null; }
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

    public void TryAutoPlant(List<BotController> attackerBots)
    {
        if (bombState != BombState.NotPlanted) return;

        BotController planter = null;
        BombSite targetSite = null;

        foreach (var bot in attackerBots)
        {
            if (bot == null || bot.Profile == null || !bot.Profile.IsAlive) continue;

            if (siteA != null && siteA.IsPlayerInSite(bot.transform.position))
            { planter = bot; targetSite = siteA; break; }

            if (siteB != null && siteB.IsPlayerInSite(bot.transform.position))
            { planter = bot; targetSite = siteB; break; }
        }

        if (planter != null && targetSite != null)
        {
            ProcessPlant(planter, targetSite);
        }
        else
        {
            // Nobody on site — reset
            if (isPlanting)
            {
                if (currentPlanter != null)
                { currentPlanter.IsPlanting = false; currentPlanter = null; }
                isPlanting = false;
                plantProgress = 0f;
            }
        }
    }

    private void ProcessPlant(BotController planter, BombSite site)
    {
        if (!isPlanting)
        {
            isPlanting = true;
            plantProgress = 0f;
            currentPlanter = planter;

            // >>> STOP THE BOT — they must stand still to plant <<<
            planter.IsPlanting = true;
            var agent = planter.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null) { agent.isStopped = true; agent.ResetPath(); }

            Debug.Log($"PLANTING on {site.siteName}... (bot stopped)");
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
