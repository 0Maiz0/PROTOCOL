using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

/// <summary>
/// FOOTSTEP SYSTEM v2
/// 
/// SHIFT-WALK RULES (like real CS):
/// - Round start: EVERYONE RUNS. CT runs to hold positions, T runs to map control.
///   Enemies are far away, no point being silent.
/// - Mid round: Shift only when CLOSE to an actual enemy (within ~12 units LOS).
///   NOT based on comms info from across the map.
/// - Retake: CT RUNS to site (bomb ticking, urgency > stealth).
/// - Post-plant T holding: shift (already in position, stay quiet).
/// - AWP: shift when stationary/peeking. Run when traveling to position.
/// 
/// Running = footstep noise in radius. Enemies hear and turn toward sound.
/// </summary>
[RequireComponent(typeof(BotController))]
public class FootstepSystem : MonoBehaviour
{
    [Header("Sound")]
    [SerializeField] private float runNoiseRadius = 16f;
    [SerializeField] private float noiseInterval = 0.4f;

    [Header("Shift Logic")]
    [SerializeField] private float shiftWalkSpeed = 2.0f;
    [SerializeField] private float enemyProximityForShift = 12f;  // Shift when enemy THIS close
    [SerializeField] private float earlyRoundRunTime = 12f;       // Seconds at round start where everyone runs

    private BotController myBot;
    private NavMeshAgent agent;
    private BotStateMachine stateMachine;
    private float noiseTimer;
    private bool isShiftWalking = false;

    // Round timing — set by external (RoundManager sets roundStartTime)
    private static float roundStartTime = 0f;
    public static void OnRoundStarted() { roundStartTime = Time.time; }
    private static bool bombIsPlanted = false;
    public static void OnBombPlantedStatic(bool planted) { bombIsPlanted = planted; }

    private static List<FootstepSystem> allFootsteps = new List<FootstepSystem>();

    private void OnEnable()
    {
        myBot = GetComponent<BotController>();
        agent = GetComponent<NavMeshAgent>();
        stateMachine = GetComponent<BotStateMachine>();
        allFootsteps.Add(this);
    }

    private void OnDisable() { allFootsteps.Remove(this); }

    private void Update()
    {
        if (!myBot.Profile.IsAlive || myBot.IsFrozen) return;
        if (stateMachine.CurrentState == BotState.Dead) return;

        UpdateShiftWalk();

        // Emit footstep noise when running (not shifting)
        if (stateMachine.CurrentState == BotState.Moving && !isShiftWalking)
        {
            if (agent.velocity.magnitude > 1f)
            {
                noiseTimer -= Time.deltaTime;
                if (noiseTimer <= 0f)
                {
                    noiseTimer = noiseInterval;
                    EmitFootstep();
                }
            }
        }
    }

    // ===================== SHIFT-WALK DECISION =====================

    private void UpdateShiftWalk()
    {
        if (stateMachine.CurrentState != BotState.Moving)
        {
            isShiftWalking = false;
            return;
        }

        bool shouldShift = false;
        float timeSinceRoundStart = Time.time - roundStartTime;

        // === EARLY ROUND: EVERYONE RUNS (first 12 seconds) ===
        // CT needs to reach positions, T needs map control. Nobody shifts.
        if (timeSinceRoundStart < earlyRoundRunTime)
        {
            shouldShift = false;
            isShiftWalking = false;
            if (agent.speed < Constants.BOT_MOVE_SPEED - 0.1f)
                agent.speed = Constants.BOT_MOVE_SPEED;
            return;
        }

        // === RETAKE: CT ALWAYS RUNS (bomb is ticking, urgency!) ===
        if (bombIsPlanted && myBot.Side == TeamSide.Defender)
        {
            shouldShift = false;
            isShiftWalking = false;
            if (agent.speed < Constants.BOT_MOVE_SPEED - 0.1f)
                agent.speed = Constants.BOT_MOVE_SPEED;
            return;
        }

        // === POST-PLANT T HOLDING: Shift (stay quiet in position) ===
        if (bombIsPlanted && myBot.Side == TeamSide.Attacker)
        {
            shouldShift = true;
        }

        // === MID-ROUND: Shift only when ACTUAL enemy is nearby ===
        // Check real proximity via direct distance to alive enemies, not comms
        if (!shouldShift)
        {
            float closestEnemy = GetClosestEnemyDistance();

            if (closestEnemy < enemyProximityForShift)
            {
                // Enemy is close — go silent
                shouldShift = true;
            }
        }

        // === AWP special: shift when in Peeking-like state (stationary area) ===
        // But NOT while traveling long distance to a position
        if (myBot.Profile.AssignedRole == Role.AWPer)
        {
            // AWP shifts when they're near their destination (close to waypoint end)
            if (agent.hasPath && agent.remainingDistance < 5f)
                shouldShift = true;
            else
                shouldShift = false; // AWP running to position = run
        }

        isShiftWalking = shouldShift;

        if (isShiftWalking && agent.speed > shiftWalkSpeed + 0.1f)
            agent.speed = shiftWalkSpeed;
        else if (!isShiftWalking && agent.speed < Constants.BOT_MOVE_SPEED - 0.1f)
            agent.speed = Constants.BOT_MOVE_SPEED;
    }

    /// <summary>Is this bot currently shift-walking?</summary>
    public bool IsShiftWalking => isShiftWalking;

    // ===================== ENEMY PROXIMITY =====================

    /// <summary>Direct distance to closest alive enemy. No comms, no info — raw distance.</summary>
    private float GetClosestEnemyDistance()
    {
        float best = float.MaxValue;
        foreach (var other in allFootsteps)
        {
            if (other == this || other.myBot.Side == myBot.Side) continue;
            if (!other.myBot.Profile.IsAlive) continue;
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d < best) best = d;
        }
        return best;
    }

    // ===================== FOOTSTEP NOISE =====================

    private void EmitFootstep()
    {
        foreach (var other in allFootsteps)
        {
            if (other == this || other.myBot.Side == myBot.Side) continue;
            if (!other.myBot.Profile.IsAlive) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);

            // Hearing range scales with GameSense
            float hearRange = runNoiseRadius * Mathf.Lerp(0.8f, 1.3f, other.myBot.Profile.Data.gameSense / 100f);
            if (dist > hearRange) continue;

            // Heard! Listener rotates toward sound
            Vector3 dir = transform.position - other.transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 1f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                other.transform.rotation = Quaternion.Slerp(other.transform.rotation, look, 3f * Time.deltaTime);
            }

            // Report approximate position to team via comms
            if (CommsManager.Instance != null)
            {
                float inaccuracy = Mathf.Lerp(6f, 1f, other.myBot.Profile.Data.gameSense / 100f);
                Vector3 approxPos = transform.position + new Vector3(
                    Random.Range(-inaccuracy, inaccuracy), 0, Random.Range(-inaccuracy, inaccuracy));

                // Simple report — just rotate, don't broadcast exact coords from footsteps
                // (only visual contact gives precise info to comms)
            }
        }
    }
}
