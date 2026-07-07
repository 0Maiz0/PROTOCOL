using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class DuelTrigger : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float maxSightRange = 50f;
    [SerializeField] private float sightAngle = 90f;
    [SerializeField] private LayerMask wallMask;

    [Header("Tracer Visual")]
    [SerializeField] private float tracerDuration = 0.3f;

    private BotController myBot;
    private BotStateMachine myStateMachine;
    private float losCheckTimer;

    private static List<DuelTrigger> allTriggers = new List<DuelTrigger>();
    private static HashSet<DuelTrigger> inDuel = new HashSet<DuelTrigger>();

    public static event System.Action<DuelResolver.DuelOutcome> OnDuelResolved;

    // For killfeed
    public static event System.Action<string, string> OnKillFeedEvent; // winner nickname, loser nickname

    private void OnEnable()
    {
        myBot = GetComponent<BotController>();
        myStateMachine = GetComponent<BotStateMachine>();
        allTriggers.Add(this);
    }

    private void OnDisable()
    {
        allTriggers.Remove(this);
    }

    private void Update()
    {
        if (myBot == null || myBot.Profile == null) return;
        if (!myBot.Profile.IsAlive) return;
        if (inDuel.Contains(this)) return;

        losCheckTimer -= Time.deltaTime;
        if (losCheckTimer <= 0f)
        {
            losCheckTimer = Constants.RAYCAST_CHECK_INTERVAL;
            CheckForEngagements();
        }
    }

    private void CheckForEngagements()
    {
        foreach (var other in allTriggers)
        {
            if (other == this) continue;
            if (other.myBot.Side == myBot.Side) continue;
            if (!other.myBot.Profile.IsAlive) continue;
            if (inDuel.Contains(other)) continue;

            // Both need to see each other — OR at least one sees the other
            bool iSeeEnemy = HasLineOfSight(other.transform);
            bool enemySeesMe = other.HasLineOfSight(transform);

            if (iSeeEnemy || enemySeesMe)
            {
                ResolveDuel(this, other);
                return;
            }
        }
    }

    private bool HasLineOfSight(Transform target)
    {
        Vector3 myPos = transform.position + Vector3.up * 0.5f;
        Vector3 targetPos = target.position + Vector3.up * 0.5f;
        Vector3 direction = targetPos - myPos;
        float distance = direction.magnitude;

        if (distance > maxSightRange) return false;

        // FOV check — wider when stationary (peeking/idle = looking around)
        float currentFOV = sightAngle;
        if (myStateMachine != null &&
            (myStateMachine.CurrentState == BotState.Peeking || myStateMachine.CurrentState == BotState.Idle))
        {
            currentFOV = 270f; // Almost full vision when holding position
        }

        float angle = Vector3.Angle(transform.forward, direction);
        if (angle > currentFOV * 0.5f) return false;

        // Wall check
        if (Physics.Raycast(myPos, direction.normalized, out RaycastHit hit, distance, wallMask))
        {
            return false;
        }

        return true;
    }

    private static void ResolveDuel(DuelTrigger a, DuelTrigger b)
    {
        inDuel.Add(a);
        inDuel.Add(b);

        a.myStateMachine.TransitionTo(BotState.Engaged);
        b.myStateMachine.TransitionTo(BotState.Engaged);

        var ctx = new DuelResolver.DuelContext
        {
            attacker = a.myBot.Profile,
            defender = b.myBot.Profile,
            attackerBuy = BuyType.Full,
            defenderBuy = BuyType.Full,
            strategyCounterBonus = 0f,
            isClutch = false,
            clutchPlayersRemaining = 0
        };

        var outcome = DuelResolver.Resolve(ctx);

        outcome.winner.RoundKills++;
        outcome.winner.MatchKills++;
        outcome.loser.MatchDeaths++;

        BotController loserBot = (outcome.loser == a.myBot.Profile) ? a.myBot : b.myBot;
        BotController winnerBot = (outcome.winner == a.myBot.Profile) ? a.myBot : b.myBot;

        // === TRACER VFX ===
        DrawTracer(winnerBot.transform, loserBot.transform);

        loserBot.Die();
        winnerBot.GetComponent<BotStateMachine>().TransitionTo(BotState.Moving);

        inDuel.Remove(a);
        inDuel.Remove(b);

        Debug.Log($"DUEL: {outcome.winner.Data.nickname} killed {outcome.loser.Data.nickname} " +
                  $"(score: {outcome.winnerScore:F1}, upset: {outcome.wasUpset})");

        // Fire events
        OnDuelResolved?.Invoke(outcome);
        OnKillFeedEvent?.Invoke(outcome.winner.Data.nickname, outcome.loser.Data.nickname);
    }

    // === TRACER LINE ===
    private static void DrawTracer(Transform shooter, Transform target)
    {
        Vector3 start = shooter.position + Vector3.up * 0.5f;
        Vector3 end = target.position + Vector3.up * 0.5f;

        // Create a temporary line renderer
        GameObject tracerObj = new GameObject("Tracer");
        LineRenderer lr = tracerObj.AddComponent<LineRenderer>();

        lr.startWidth = 0.05f;
        lr.endWidth = 0.02f;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        // Yellow tracer
        lr.startColor = Color.yellow;
        lr.endColor = new Color(1f, 0.5f, 0f, 0.5f); // orange fade
        lr.material = new Material(Shader.Find("Sprites/Default"));

        // Auto destroy
        Object.Destroy(tracerObj, 0.3f);
    }

    public static void ClearAllDuels()
    {
        inDuel.Clear();
    }

    private void OnDrawGizmos()
    {
        if (myBot == null || myBot.Profile == null || !myBot.Profile.IsAlive) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, transform.forward * 3f);
    }
}