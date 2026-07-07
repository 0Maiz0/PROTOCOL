using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// IGL BRAIN v1 — the in-game leader of the PLAYER's team reads the round
/// (comms intel per site) and calls it out in the comms feed: "A is stacked,
/// B looks open". The coach (player) still makes the actual calls — the IGL
/// is the info layer between the bots and the coach.
///
/// Leadership stat = how sharp/frequent the reads are. A dead IGL calls
/// nothing — that's why you keep them alive (and why IGL fit matters when
/// scouting: v2 will let IGL quality shape execute sync and role usage).
/// </summary>
public class IGLBrain : MonoBehaviour
{
    private List<BotController> myBots;
    private TeamSide mySide;
    private BotController igl;

    private float evalTimer;
    private float lastCallTime;
    private string lastCall;

    /// <summary>Called by RoundManager each round after strategies execute.</summary>
    public void SetTeam(List<BotController> bots, TeamSide side)
    {
        myBots = bots;
        mySide = side;
        igl = null;
        lastCall = null;
        if (bots == null) return;
        foreach (var b in bots)
            if (b?.Profile != null && b.Profile.AssignedRole == Role.IGL) { igl = b; break; }
        if (igl == null)
            foreach (var b in bots)
                if (b?.Profile != null) { igl = b; break; }
    }

    private void Update()
    {
        if (myBots == null || igl == null || igl.Profile == null) return;
        if (!igl.Profile.IsAlive || igl.IsFrozen) return; // dead IGLs make no reads

        evalTimer -= Time.deltaTime;
        if (evalTimer > 0f) return;
        evalTimer = 3f;
        Evaluate();
    }

    private void Evaluate()
    {
        if (CommsManager.Instance == null) return;
        GameObject aWP = GameObject.Find("WP_NodeA"), bWP = GameObject.Find("WP_NodeB");
        if (aWP == null || bWP == null) return;

        int sigA = CommsManager.Instance.CountKnownEnemiesNear(mySide, aWP.transform.position, Constants.CT_SIGNAL_RANGE);
        int sigB = CommsManager.Instance.CountKnownEnemiesNear(mySide, bWP.transform.position, Constants.CT_SIGNAL_RANGE);

        // Leadership sharpens the read — great IGLs call on thinner info
        float lead = igl.Profile.Data.leadership / 100f;
        int threshold = lead > 0.6f ? 2 : 3;

        string call = null;
        if (mySide == TeamSide.Attacker)
        {
            if (sigA - sigB >= threshold) call = $"A is stacked ({sigA} known) — B looks open";
            else if (sigB - sigA >= threshold) call = $"B is stacked ({sigB} known) — A looks open";
        }
        else
        {
            if (sigA - sigB >= threshold) call = $"they're committing A — {sigA} spotted!";
            else if (sigB - sigA >= threshold) call = $"they're committing B — {sigB} spotted!";
        }

        if (call != null && call != lastCall && Time.time - lastCallTime > 8f)
        {
            lastCall = call;
            lastCallTime = Time.time;
            CommsFeed.Post($"{igl.Profile.Data.nickname} (IGL)", call, mySide);
        }
    }
}
