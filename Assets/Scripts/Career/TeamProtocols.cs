using UnityEngine;

/// <summary>
/// The coach's PRE-MATCH instructions — how the team plays without per-round
/// micromanagement. Set in the management hub, saved with the career,
/// read by IGLBrain (strategy picks) and MatchEconomy (buy discipline).
///
/// Design philosophy: you built the team — the match TESTS it. The coach's
/// in-match lever is a tactical timeout (3 per match), nothing more.
/// </summary>
[System.Serializable]
public class TeamProtocols
{
    [Range(0, 100)] public int aggression = 50;        // Rush/Execute ↔ Control/Default weights
    [Range(0, 100)] public int economyDiscipline = 50; // high = proper saves, low = force-happy
    public bool preferLurker = false;                  // default lurker on T-side strats
}
