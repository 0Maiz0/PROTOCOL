using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ENGAGEMENT SYSTEM v4
/// 
/// NEW:
/// - CommsManager integration: report spotted enemies, call for help when shot
/// - Active seeking: if no LOS target but team has info → move toward known position
/// - AWP prefers holding sightline, plays passive, retreats if pushed
/// - Help response: if teammate calls help, rotate toward them
/// - CT actively peeks when enemies known on their site
/// </summary>
public class EngagementSystem : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private float sightAngle = 90f;
    [SerializeField] private float stationaryFOV = 270f;

    private BotController myBot;
    private BotStateMachine myStateMachine;
    private BotCombatBehavior coverBehavior;

    private BotController currentTarget;
    private float fireTimer;
    private float losCheckTimer;
    private float disengageTimer;
    private float seekTimer;         // Timer for checking comms info
    private float helpCallTimer;     // Cooldown between help requests
    private bool hasCalledHelp;
    private float clutchFactor = 1f; // Hit-chance multiplier when last alive vs 2+
    private bool burstHold;          // Pausing mid-spray to re-aim (burst discipline)
    private float weaponCooldownUntil; // Time.time — fire interval persists across target switches

    private static Material tracerMaterial; // Shared — one material for all tracers

    private static List<EngagementSystem> allSystems = new List<EngagementSystem>();

    public static event System.Action<DuelResolver.DuelOutcome> OnDuelResolved;
    public static event System.Action<string, string, TeamSide, TeamSide> OnKillFeedEvent;
    // Changed: now passes killer side + victim side for proper colors

    private void OnEnable()
    {
        myBot = GetComponent<BotController>();
        myStateMachine = GetComponent<BotStateMachine>();
        coverBehavior = GetComponent<BotCombatBehavior>();
        allSystems.Add(this);
    }

    private void OnDisable() { allSystems.Remove(this); }

    private void Update()
    {
        if (myBot == null || myBot.Profile == null) return;
        if (!myBot.Profile.IsAlive || myBot.IsFrozen) return;
        if (myStateMachine.CurrentState == BotState.Dead) return;

        if (myBot.Profile.IsReloading)
        {
            myBot.Profile.TickReload(Time.deltaTime);
            return;
        }

        if (myStateMachine.CurrentState != BotState.Shooting)
            myBot.Profile.DecaySpray(Time.deltaTime);

        if (myStateMachine.CurrentState == BotState.Shooting)
        {
            UpdateShooting();
            return;
        }

        // Scan for direct LOS targets
        losCheckTimer -= Time.deltaTime;
        if (losCheckTimer <= 0f)
        {
            losCheckTimer = Constants.RAYCAST_CHECK_INTERVAL;
            ScanForTarget();
        }

        // If idle/peeking with no target — check comms for known enemy positions
        if (currentTarget == null &&
            (myStateMachine.CurrentState == BotState.Peeking || myStateMachine.CurrentState == BotState.Idle))
        {
            seekTimer -= Time.deltaTime;
            if (seekTimer <= 0f)
            {
                seekTimer = 1.0f;
                CheckCommsForAction();
            }
        }
    }

    // ===================== SCANNING =====================

    private void ScanForTarget()
    {
        BotController bestTarget = null;
        float bestDist = float.MaxValue;
        float myRange = myBot.Profile.CurrentWeapon.range;

        foreach (var other in allSystems)
        {
            if (other == this || other.myBot.Side == myBot.Side) continue;
            if (!other.myBot.Profile.IsAlive) continue;

            float dist = Vector3.Distance(transform.position, other.transform.position);
            if (dist > myRange) continue;
            if (!HasLineOfSight(other.transform, dist)) continue;

            if (dist < bestDist) { bestDist = dist; bestTarget = other.myBot; }
        }

        if (bestTarget != null)
        {
            // Report to team via comms
            if (CommsManager.Instance != null)
                CommsManager.Instance.ReportEnemy(myBot, bestTarget);

            StartEngagement(bestTarget);
        }
    }

    // ===================== COMMS-BASED ACTIONS =====================

    /// <summary>
    /// When idle with no visible enemy, check team comms for info and act on it.
    /// </summary>
    private void CheckCommsForAction()
    {
        if (CommsManager.Instance == null) return;

        // Priority 1: Teammate needs help nearby
        if (CommsManager.Instance.GetNearbyHelpRequest(myBot, out Vector3 helpPos))
        {
            MoveToward(helpPos);
            return;
        }

        // Priority 2: Known enemy position — go investigate
        if (CommsManager.Instance.GetNearestKnownEnemy(myBot, out Vector3 enemyPos))
        {
            float dist = Vector3.Distance(transform.position, enemyPos);

            // CT don't push far from their position (hold site)
            if (myBot.Side == TeamSide.Defender && dist > 15f)
                return;

            // AWP doesn't chase — holds position
            if (myBot.Profile.AssignedRole == Role.AWPer)
                return;

            MoveToward(enemyPos);
        }
    }

    private void MoveToward(Vector3 position)
    {
        var agent = myBot.Agent;
        if (agent == null || !agent.isOnNavMesh) return;

        agent.isStopped = false;
        agent.speed = Constants.BOT_MOVE_SPEED * 0.8f; // Cautious speed
        agent.SetDestination(position);
        myStateMachine.TransitionTo(BotState.Moving);
    }

    // ===================== ENGAGEMENT =====================

    private void StartEngagement(BotController target)
    {
        currentTarget = target;
        disengageTimer = 0f;
        hasCalledHelp = false;

        // Clutch stat only matters in a real clutch: last alive against 2+
        clutchFactor = 1f;
        if (IsInClutch())
            clutchFactor = 1f + ((myBot.Profile.Data.clutch - Constants.CLUTCH_BASELINE) / 100f)
                                * Constants.CLUTCH_HITCHANCE_SCALE;

        float reactionTime = Mathf.Lerp(0.5f, 0.08f, myBot.Profile.EffectiveReflexes / 100f);
        if (myBot.Profile.CurrentWeapon.weaponName == "AWP")
            reactionTime += 0.3f;

        // Respect the weapon's fire interval across target switches —
        // an AWP can't instantly re-fire at a new target (bolt cycle)
        fireTimer = Mathf.Max(reactionTime, weaponCooldownUntil - Time.time);
        myStateMachine.TransitionTo(BotState.Shooting);

        RotateToward(target.transform);

        // Cover: only T-side rifle in open
        if (ShouldSeekCover() && coverBehavior != null)
            coverBehavior.StartCombatCover(target.transform);
    }

    private bool IsInClutch()
    {
        int mates = 0, enemies = 0;
        foreach (var s in allSystems)
        {
            if (s == this || s.myBot == null || s.myBot.Profile == null || !s.myBot.Profile.IsAlive) continue;
            if (s.myBot.Side == myBot.Side) mates++;
            else enemies++;
        }
        return mates == 0 && enemies >= 2;
    }

    /// <summary>
    /// Called by the shooter's EngagementSystem when we take a non-lethal hit.
    /// You know the direction you're shot from — turn and fight back if possible.
    /// </summary>
    public void OnDamagedBy(BotController shooter)
    {
        if (myBot == null || myBot.Profile == null || !myBot.Profile.IsAlive || myBot.IsFrozen) return;
        if (shooter == null || shooter.Profile == null || !shooter.Profile.IsAlive) return;
        if (myStateMachine.CurrentState == BotState.Shooting ||
            myStateMachine.CurrentState == BotState.Dead) return;

        Vector3 dir = shooter.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);

        float dist = Vector3.Distance(transform.position, shooter.transform.position);
        if (dist <= myBot.Profile.CurrentWeapon.range && HasLineOfSight(shooter.transform, dist))
            StartEngagement(shooter);
    }

    /// <summary>
    /// External sound alert (footsteps, defuse): face the position and scan immediately.
    /// Doesn't interrupt an ongoing fight.
    /// </summary>
    public void AlertAt(Vector3 position)
    {
        if (myBot == null || myBot.Profile == null || !myBot.Profile.IsAlive || myBot.IsFrozen) return;
        if (myStateMachine.CurrentState == BotState.Shooting ||
            myStateMachine.CurrentState == BotState.Dead) return;

        Vector3 dir = position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir);
        losCheckTimer = 0f; // scan this frame
    }

    private bool ShouldSeekCover()
    {
        if (myBot.Side == TeamSide.Defender &&
            (myStateMachine.CurrentState == BotState.Peeking || myStateMachine.CurrentState == BotState.Idle))
            return false;
        if (myBot.Profile.AssignedRole == Role.AWPer) return false;
        if (myBot.CanRunAndGun()) return false;
        return true;
    }

    // ===================== SHOOTING =====================

    private void UpdateShooting()
    {
        if (currentTarget == null || !currentTarget.Profile.IsAlive)
        {
            Disengage();
            return;
        }

        float dist = Vector3.Distance(transform.position, currentTarget.transform.position);
        float myRange = myBot.Profile.CurrentWeapon.range;

        if (dist > myRange * 1.1f || !HasLineOfSight(currentTarget.transform, dist))
        {
            disengageTimer += Time.deltaTime;
            if (disengageTimer > 0.6f) Disengage();
            return;
        }

        disengageTimer = 0f;
        RotateToward(currentTarget.transform);

        // Call for help if taking damage and haven't called yet
        if (!hasCalledHelp && myBot.Profile.HP < 70f && CommsManager.Instance != null)
        {
            CommsManager.Instance.RequestHelp(myBot);
            hasCalledHelp = true;
        }

        // Fire check — respect cover cycle
        bool canFire = true;
        if (coverBehavior != null && coverBehavior.IsUsingCover && !coverBehavior.IsPeeking)
            canFire = false;

        if (canFire)
        {
            // Burst discipline — tap/burst like a real player instead of spraying the mag
            if (!burstHold && myBot.Profile.SprayCounter >= Constants.BURST_SPRAY_LIMIT)
                burstHold = true;
            if (burstHold)
            {
                myBot.Profile.DecaySpray(Time.deltaTime * 2f); // re-aiming
                if (myBot.Profile.SprayCounter <= Constants.BURST_RESUME_SPRAY)
                    burstHold = false;
            }

            if (!burstHold)
            {
                fireTimer -= Time.deltaTime;
                if (fireTimer <= 0f)
                {
                    FireAtTarget(dist);
                    fireTimer = myBot.Profile.CurrentWeapon.fireInterval;
                }
            }
        }
    }

    private void FireAtTarget(float distance)
    {
        var profile = myBot.Profile;
        if (!profile.FireShot()) return;
        weaponCooldownUntil = Time.time + profile.CurrentWeapon.fireInterval;

        float movePenalty = 1f;
        if (myBot.Agent != null && !myBot.Agent.isStopped && myBot.Agent.velocity.magnitude > 0.5f)
        {
            movePenalty = myBot.CanRunAndGun() ? 0.7f : 0.25f;
        }

        float hitChance = Mathf.Min(profile.CalculateHitChance(distance) * movePenalty * clutchFactor, 0.98f);
        bool isHit = Random.value < hitChance;

        DrawTracer(transform, currentTarget.transform, isHit);

        if (isHit)
        {
            float damage = profile.CalculateDamage(distance, out bool headshot);
            bool killed = currentTarget.Profile.TakeDamage(damage);
            if (headshot && killed)
                Debug.Log($"HEADSHOT: {profile.Data.nickname} one-taps {currentTarget.Profile.Data.nickname}!");
            if (killed) OnTargetKilled(currentTarget);
            else if (currentTarget.TryGetComponent(out EngagementSystem victim))
                victim.OnDamagedBy(myBot);
        }
    }

    private void OnTargetKilled(BotController victim)
    {
        myBot.Profile.RoundKills++;
        myBot.Profile.MatchKills++;
        victim.Profile.MatchDeaths++;
        victim.Die();

        // Comms: report kill
        if (CommsManager.Instance != null)
            CommsManager.Instance.ReportKill(myBot, victim);

        var outcome = new DuelResolver.DuelOutcome
        {
            winner = myBot.Profile,
            loser = victim.Profile,
            winnerScore = myBot.Profile.EffectiveAim,
            wasUpset = myBot.Profile.EffectiveAim < victim.Profile.EffectiveAim
        };

        OnDuelResolved?.Invoke(outcome);
        OnKillFeedEvent?.Invoke(myBot.Profile.Data.nickname, victim.Profile.Data.nickname, myBot.Side, victim.Side);

        currentTarget = null;
        myBot.Profile.SprayCounter = 0f;
        if (coverBehavior != null) coverBehavior.StopCombatCover();
        myStateMachine.TransitionTo(BotState.Moving);
        losCheckTimer = 0.1f;
    }

    private void Disengage()
    {
        currentTarget = null;
        disengageTimer = 0f;
        if (coverBehavior != null) coverBehavior.StopCombatCover();

        if (myStateMachine.CurrentState == BotState.Shooting)
        {
            if (myBot.ShouldHoldPosition())
                myStateMachine.TransitionTo(BotState.Peeking);
            else
                myStateMachine.TransitionTo(BotState.Moving);
        }
    }

    // ===================== LINE OF SIGHT =====================

    private bool HasLineOfSight(Transform target, float distance)
    {
        Vector3 myPos = transform.position + Vector3.up * 0.5f;
        Vector3 targetPos = target.position + Vector3.up * 0.5f;
        Vector3 direction = targetPos - myPos;

        float currentFOV = sightAngle;
        if (myStateMachine.CurrentState == BotState.Peeking || myStateMachine.CurrentState == BotState.Idle)
            currentFOV = stationaryFOV;
        if (myStateMachine.CurrentState == BotState.Shooting)
            currentFOV = 360f;
        // Point-blank you HEAR them — no more circling each other obliviously
        if (distance < Constants.PROXIMITY_AWARENESS_RANGE)
            currentFOV = 360f;

        if (Vector3.Angle(transform.forward, direction) > currentFOV * 0.5f) return false;
        if (Physics.Raycast(myPos, direction.normalized, out _, distance, wallMask)) return false;
        if (GrenadeEffect.IsBlockedBySmoke(myPos, targetPos)) return false;

        return true;
    }

    // ===================== HELPERS =====================

    private void RotateToward(Transform target)
    {
        Vector3 dir = target.position - transform.position; dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 12f * Time.deltaTime);
    }

    private void DrawTracer(Transform shooter, Transform target, bool isHit)
    {
        Vector3 start = shooter.position + Vector3.up * 0.5f;
        Vector3 end = target.position + Vector3.up * 0.5f;
		end += Vector3.Scale(Random.insideUnitSphere * 1.5f, new Vector3(1f, 0.3f, 1f));

		GameObject obj = new GameObject("Tracer");
        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.startWidth = 0.04f; lr.endWidth = 0.015f; lr.positionCount = 2;
        lr.SetPosition(0, start); lr.SetPosition(1, end);
        lr.startColor = isHit ? Color.yellow : new Color(1, 1, 0.5f, 0.3f);
        lr.endColor = isHit ? new Color(1, 0.5f, 0, 0.6f) : new Color(1, 0.8f, 0.3f, 0.1f);
        // Shared material — a new Material per shot leaked (colors are vertex-based anyway)
        if (tracerMaterial == null) tracerMaterial = new Material(Shader.Find("Sprites/Default"));
        lr.material = tracerMaterial;
        Object.Destroy(obj, 0.15f);
    }

    public static void ClearAllEngagements()
    {
        foreach (var s in allSystems)
        { s.currentTarget = null; s.disengageTimer = 0; s.fireTimer = 0; s.hasCalledHelp = false; s.clutchFactor = 1f; s.burstHold = false; s.weaponCooldownUntil = 0f; }
    }
}
