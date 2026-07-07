using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(BotStateMachine))]
public class BotController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private BotStateMachine stateMachine;

    public PlayerProfile Profile { get; private set; }
    public TeamSide Side { get; private set; }
    public BuyType CurrentBuyType { get; private set; } = BuyType.Full;
    public NavMeshAgent Agent => agent;
    public bool IsFrozen { get; set; } = true;
    public bool IsPlanting { get; set; } = false; // Set by BombManager

    private Transform[] currentWaypoints;
    private int waypointIndex;
    private float scanTimer;
    private float seekCheckTimer;
    private bool isSeekingTarget;
    private float molotovCheckTimer;

    // Hold-angle scanning: face known enemy intel (comms), otherwise sweep.
    // No direct enemy-position peeking — bots only "know" what the team reported.
    private bool hasScanPoint;
    private Vector3 scanPoint;
    private float scanBaseYaw;
    private float sweepPhaseOffset;

    public void Initialize(PlayerProfile profile, TeamSide side)
    {
        Profile = profile; Side = side;
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (stateMachine == null) stateMachine = GetComponent<BotStateMachine>();
        stateMachine.OnStateChanged += OnStateChanged;
        sweepPhaseOffset = Random.Range(0f, 100f); // desync sweep across bots
    }

    private void OnDestroy() { if (stateMachine != null) stateMachine.OnStateChanged -= OnStateChanged; }

    public void SetSide(TeamSide s) => Side = s;
    public void SetColor(Color c) {
        Renderer r = GetComponentInChildren<Renderer>();
        if (r != null) { Material m = new Material(r.sharedMaterial); m.color = c; r.material = m; }
    }
    public void SetBuyType(BuyType b) { CurrentBuyType = b; if (Profile != null) Profile.ResetCombatState(b); }

    public void AssignWaypoints(Transform[] wp) { currentWaypoints = wp; waypointIndex = 0; isSeekingTarget = false; }

    public void StartExecuting()
    {
        if (IsFrozen) return;
        if (currentWaypoints == null || currentWaypoints.Length == 0) return;
        stateMachine.TransitionTo(BotState.Moving);
        agent.isStopped = false;
        MoveToNextWaypoint();
    }

    private void Update()
    {
        if (stateMachine.CurrentState == BotState.Dead || IsFrozen) return;

        // PLANTING — don't move at all
        if (IsPlanting) { agent.isStopped = true; return; }

        // MOLOTOV AVOIDANCE — check every 0.5s
        molotovCheckTimer -= Time.deltaTime;
        if (molotovCheckTimer <= 0f)
        {
            molotovCheckTimer = 0.5f;
            AvoidMolotov();
        }

        if (stateMachine.IsInCombat) return;

        // MOVING
        if (stateMachine.CurrentState == BotState.Moving && !agent.pathPending)
        {
            if (agent.remainingDistance < 0.5f)
            {
                waypointIndex++;
                if (waypointIndex < (currentWaypoints?.Length ?? 0))
                    MoveToNextWaypoint();
                else { agent.isStopped = true; stateMachine.TransitionTo(BotState.Peeking); }
            }
        }

        // PEEKING/IDLE — scan + seek
        if (stateMachine.CurrentState == BotState.Peeking || stateMachine.CurrentState == BotState.Idle)
        {
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f) { scanTimer = 0.5f; UpdateScanPoint(); }

            if (hasScanPoint)
            {
                // Team intel says an enemy is around there — pre-aim it
                Vector3 dir = scanPoint - transform.position; dir.y = 0;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(dir), 5f * Time.deltaTime);
            }
            else
            {
                // No intel — sweep the held angle to cover the FOV blind spots
                float sweep = Mathf.PingPong((Time.time + sweepPhaseOffset) * Constants.SCAN_SWEEP_SPEED,
                    Constants.SCAN_SWEEP_ARC) - Constants.SCAN_SWEEP_ARC * 0.5f;
                Quaternion target = Quaternion.Euler(0f, scanBaseYaw + sweep, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target,
                    Constants.SCAN_SWEEP_SPEED * Time.deltaTime);
            }

            seekCheckTimer -= Time.deltaTime;
            if (seekCheckTimer <= 0f && !isSeekingTarget)
            { seekCheckTimer = 2f; TrySeekFromComms(); }
        }
    }

    private void UpdateScanPoint()
    {
        hasScanPoint = CommsManager.Instance != null &&
                       CommsManager.Instance.GetNearestKnownEnemy(this, out scanPoint);
    }

    // ===================== MOLOTOV AVOIDANCE =====================

    private void AvoidMolotov()
    {
        // Static registry — FindObjectsByType every 0.5s per bot was a hot-path scan
        var effects = GrenadeEffect.ActiveEffects;
        for (int i = 0; i < effects.Count; i++)
        {
            var fx = effects[i];
            if (fx == null) continue;
            if (!fx.IsActive || fx.Type != GrenadeType.Molotov) continue;

            float dist = Vector3.Distance(transform.position, fx.transform.position);
            if (dist < fx.Radius + 1f) // In or very near molly
            {
                // RUN AWAY from molotov center
                Vector3 away = (transform.position - fx.transform.position).normalized;
                away.y = 0;
                Vector3 escapePos = transform.position + away * (fx.Radius + 3f);

                if (agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                    agent.speed = Constants.BOT_MOVE_SPEED * 1.2f; // Sprint away!
                    agent.SetDestination(escapePos);

                    if (stateMachine.CurrentState != BotState.Moving)
                        stateMachine.TransitionTo(BotState.Moving);
                }
                return;
            }
        }
    }

    // ===================== SEEKING =====================

    private void TrySeekFromComms()
    {
        if (CommsManager.Instance == null) return;
        if (Profile.AssignedRole == Role.AWPer) return;

        if (CommsManager.Instance.GetNearbyHelpRequest(this, out Vector3 helpPos))
        { SeekPosition(helpPos); return; }

        if (Side == TeamSide.Attacker)
        {
            if (CommsManager.Instance.GetNearestKnownEnemy(this, out Vector3 ep))
                SeekPosition(ep);
        }
        else
        {
            if (CommsManager.Instance.GetNearestKnownEnemy(this, out Vector3 ep))
            {
                if (Vector3.Distance(transform.position, ep) < 15f)
                    SeekPosition(ep);
            }
        }
    }

    private void SeekPosition(Vector3 pos)
    {
        if (agent == null || !agent.isOnNavMesh) return;
        isSeekingTarget = true;
        agent.isStopped = false;
        agent.speed = Constants.BOT_MOVE_SPEED * 0.7f;
        agent.SetDestination(pos);
        stateMachine.TransitionTo(BotState.Moving);
    }

    // ===================== STATE CHANGES =====================

    private void OnStateChanged(BotState oldState, BotState newState)
    {
        // Remember the angle we start holding — the idle sweep oscillates around it
        if (newState == BotState.Peeking || newState == BotState.Idle)
            scanBaseYaw = transform.eulerAngles.y;
        if (newState == BotState.Shooting)
        {
            isSeekingTarget = false;
            if (CanRunAndGun()) agent.speed = Constants.BOT_MOVE_SPEED * 0.4f;
            else agent.isStopped = true;
        }
        if ((oldState == BotState.Shooting || oldState == BotState.Engaged) && newState == BotState.Moving)
        {
            agent.isStopped = false; agent.speed = Constants.BOT_MOVE_SPEED;
            if (!isSeekingTarget && waypointIndex < (currentWaypoints?.Length ?? 0))
                MoveToNextWaypoint();
        }
    }

    public bool CanRunAndGun()
    {
        if (Profile == null) return false;
        string w = Profile.CurrentWeapon.weaponName;
        return w == "Pistol" || w == "SMG";
    }

    public bool ShouldHoldPosition()
    {
        if (Profile == null) return false;
        if (Side == TeamSide.Defender) return true;
        if (Profile.AssignedRole == Role.AWPer) return true;
        return false;
    }

    private void MoveToNextWaypoint()
    {
        if (waypointIndex < currentWaypoints.Length)
        { agent.speed = Constants.BOT_MOVE_SPEED; agent.SetDestination(currentWaypoints[waypointIndex].position); }
    }

    public void Die()
    {
        stateMachine.TransitionTo(BotState.Dead);
        Profile.IsAlive = false; Profile.HP = 0f;
        agent.isStopped = true; IsPlanting = false;
        transform.localScale = new Vector3(1f, 0.2f, 1f);
    }

    public void ResetForRound(Vector3 spawnPos)
    {
        agent.isStopped = true; agent.ResetPath(); agent.enabled = false;
        transform.position = spawnPos; transform.localScale = Vector3.one;
        agent.enabled = true; agent.isStopped = true;
        IsFrozen = true; IsPlanting = false;
        Profile.IsAlive = true; Profile.RoundKills = 0;
        Profile.ResetCombatState(CurrentBuyType);
        stateMachine.ResetForRound();
        waypointIndex = 0; currentWaypoints = null; hasScanPoint = false;
        scanBaseYaw = transform.eulerAngles.y;
        isSeekingTarget = false; seekCheckTimer = 3f;
    }
}
