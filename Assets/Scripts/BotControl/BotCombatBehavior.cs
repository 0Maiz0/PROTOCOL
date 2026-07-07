using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// BOT COMBAT BEHAVIOR v2 — Cover-seeking during firefights.
/// 
/// KEY RULES:
/// - Only activated by EngagementSystem when ShouldSeekCover() == true
/// - CT holding site NEVER uses this (they stay in position)
/// - AWP NEVER uses this (needs clean sightline)
/// - SMG/Pistol NEVER uses this (they push, not hide)
/// - T with rifle in open = uses cover (peek/shoot cycle)
/// 
/// Cycle: find cover → move to cover → peek out → shoot → retreat → repeat
/// </summary>
[RequireComponent(typeof(BotController))]
[RequireComponent(typeof(NavMeshAgent))]
public class BotCombatBehavior : MonoBehaviour
{
    [Header("Cover Search")]
    [SerializeField] private float coverSearchRadius = 12f;
    [SerializeField] private LayerMask wallMask;

    [Header("Peek Timing")]
    [SerializeField] private float peekDuration = 1.2f;
    [SerializeField] private float coverDuration = 0.8f;
    [SerializeField] private float moveToCoverSpeed = 6f;

    private BotController myBot;
    private NavMeshAgent agent;
    private BotStateMachine stateMachine;

    private CoverPoint currentCover;
    private CombatPhase combatPhase = CombatPhase.None;
    private float phaseTimer;
    private Vector3 coverPosition;
    private Vector3 peekPosition;
    private bool hasCover = false;
    private Transform combatTarget;

    private enum CombatPhase { None, MovingToCover, InCover, Peeking, Retreating }

    private void Awake()
    {
        myBot = GetComponent<BotController>();
        agent = GetComponent<NavMeshAgent>();
        stateMachine = GetComponent<BotStateMachine>();
    }

    private void Update()
    {
        if (!myBot.Profile.IsAlive) return;
        if (myBot.IsFrozen) return;
        if (combatPhase == CombatPhase.None) return;

        switch (combatPhase)
        {
            case CombatPhase.MovingToCover: UpdateMovingToCover(); break;
            case CombatPhase.InCover: UpdateInCover(); break;
            case CombatPhase.Peeking: UpdatePeeking(); break;
            case CombatPhase.Retreating: UpdateRetreating(); break;
        }
    }

    // ===================== PUBLIC API =====================

    public bool StartCombatCover(Transform target)
    {
        combatTarget = target;
        CoverPoint cover = FindBestCover(target);
        if (cover == null) { hasCover = false; combatPhase = CombatPhase.None; return false; }

        if (currentCover != null) currentCover.Release();
        currentCover = cover;
        cover.occupiedBy = myBot;
        hasCover = true;

        Vector3 dirToTarget = (target.position - cover.transform.position).normalized;
        dirToTarget.y = 0;
        peekPosition = cover.transform.position + dirToTarget * cover.peekDistance;
        coverPosition = cover.transform.position;

        combatPhase = CombatPhase.MovingToCover;
        agent.isStopped = false;
        agent.speed = moveToCoverSpeed;
        agent.SetDestination(coverPosition);
        return true;
    }

    public void StopCombatCover()
    {
        combatPhase = CombatPhase.None;
        hasCover = false;
        if (currentCover != null) { currentCover.Release(); currentCover = null; }
    }

    public bool IsPeeking => combatPhase == CombatPhase.Peeking;
    public bool IsUsingCover => hasCover && combatPhase != CombatPhase.None;

    // ===================== PHASE UPDATES =====================

    private void UpdateMovingToCover()
    {
        if (agent.pathPending) return;
        if (agent.remainingDistance < 0.5f)
        {
            agent.isStopped = true;
            combatPhase = CombatPhase.InCover;
            phaseTimer = coverDuration * Random.Range(0.3f, 0.7f);
            RotateToward(combatTarget);
        }
    }

    private void UpdateInCover()
    {
        phaseTimer -= Time.deltaTime;
        if (combatTarget != null) RotateToward(combatTarget);

        if (phaseTimer <= 0f)
        {
            combatPhase = CombatPhase.Peeking;
            float gsFactor = myBot.Profile.Data.gameSense / 100f;
            phaseTimer = peekDuration * Mathf.Lerp(1.2f, 0.7f, gsFactor);
            agent.isStopped = false;
            agent.speed = moveToCoverSpeed * 1.5f;
            agent.SetDestination(peekPosition);
        }
    }

    private void UpdatePeeking()
    {
        phaseTimer -= Time.deltaTime;
        if (combatTarget != null) RotateToward(combatTarget);

        bool targetDead = combatTarget == null ||
            combatTarget.GetComponent<BotController>()?.Profile?.IsAlive != true;

        if (phaseTimer <= 0f || targetDead)
        {
            combatPhase = CombatPhase.Retreating;
            agent.isStopped = false;
            agent.speed = moveToCoverSpeed * 1.5f;
            agent.SetDestination(coverPosition);
        }
    }

    private void UpdateRetreating()
    {
        if (agent.pathPending) return;
        if (agent.remainingDistance < 0.5f)
        {
            agent.isStopped = true;
            combatPhase = CombatPhase.InCover;
            float conFactor = myBot.Profile.Data.consistency / 100f;
            phaseTimer = coverDuration * Mathf.Lerp(1.3f, 0.6f, conFactor);
            myBot.Profile.SprayCounter = Mathf.Max(0f, myBot.Profile.SprayCounter - 0.3f);
        }
    }

    // ===================== COVER FINDING =====================

    private CoverPoint FindBestCover(Transform threat)
    {
        CoverPoint[] all = FindObjectsByType<CoverPoint>(FindObjectsSortMode.None);
        CoverPoint best = null;
        float bestScore = float.MinValue;
        Vector3 myPos = transform.position;
        Vector3 threatPos = threat.position;

        foreach (var cover in all)
        {
            if (cover.IsOccupied) continue;
            float dist = Vector3.Distance(myPos, cover.transform.position);
            if (dist > coverSearchRadius || dist < 1f) continue;

            Vector3 coverPos = cover.transform.position + Vector3.up * 0.5f;
            Vector3 threatEye = threatPos + Vector3.up * 0.5f;

            bool blocksLOS = Physics.Raycast(coverPos, (threatEye - coverPos).normalized,
                Vector3.Distance(coverPos, threatEye), wallMask);

            Vector3 peekPos = cover.PeekPosition + Vector3.up * 0.5f;
            bool peekHasLOS = !Physics.Raycast(peekPos, (threatEye - peekPos).normalized,
                Vector3.Distance(peekPos, threatEye), wallMask);

            float score = 0f;
            if (blocksLOS) score += 50f;
            if (peekHasLOS) score += 30f;
            score -= dist * 3f;

            float gsFactor = myBot.Profile.Data.gameSense / 100f;
            if (blocksLOS && peekHasLOS) score += gsFactor * 20f;

            if (score > bestScore) { bestScore = score; best = cover; }
        }
        return best;
    }

    private void RotateToward(Transform target)
    {
        if (target == null) return;
        Vector3 dir = target.position - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), 10f * Time.deltaTime);
    }

    private void OnDestroy()
    {
        if (currentCover != null) currentCover.Release();
    }
}
