using UnityEngine;

/// <summary>
/// Tracks current match state: scores, round, side assignments.
/// Used by RoundManager and UI.
/// </summary>
[System.Serializable]
public class MatchState
{
    public int currentRound = 1;
    public int attackerScore = 0;
    public int defenderScore = 0;
    public bool isHalftime = false;
    public bool isMatchOver = false;
    public TeamSide matchWinner;

    // Track which team is "your" team
    public TeamSide playerTeamSide = TeamSide.Attacker;

    public int PlayerScore => playerTeamSide == TeamSide.Attacker ? attackerScore : defenderScore;
    public int EnemyScore => playerTeamSide == TeamSide.Attacker ? defenderScore : attackerScore;

    public void AddRoundWin(TeamSide winner)
    {
        if (winner == TeamSide.Attacker)
            attackerScore++;
        else
            defenderScore++;

        // Check match end
        if (attackerScore >= Constants.ROUNDS_TO_WIN)
        {
            isMatchOver = true;
            matchWinner = TeamSide.Attacker;
        }
        else if (defenderScore >= Constants.ROUNDS_TO_WIN)
        {
            isMatchOver = true;
            matchWinner = TeamSide.Defender;
        }
    }

    public void NextRound()
    {
        currentRound++;

        // Halftime check
        if (currentRound == Constants.HALFTIME_ROUND + 1)
        {
            isHalftime = true;
        }
    }

    /// <summary>
    /// Swap sides at halftime. Scores follow the TEAMS, not the side labels.
    /// Player switches side, and scores swap so PlayerScore stays correct.
    /// </summary>
    public void SwapSides()
    {
        // Swap scores so they follow teams
        int tempScore = attackerScore;
        attackerScore = defenderScore;
        defenderScore = tempScore;

        // Flip player side
        playerTeamSide = (playerTeamSide == TeamSide.Attacker)
            ? TeamSide.Defender
            : TeamSide.Attacker;

        Debug.Log($"HALFTIME SWAP: Player now {playerTeamSide} | Score: {PlayerScore}-{EnemyScore}");
    }

    public string GetScoreDisplay()
    {
        return $"Round {currentRound} | Attackers {attackerScore} - {defenderScore} Defenders";
    }
}
