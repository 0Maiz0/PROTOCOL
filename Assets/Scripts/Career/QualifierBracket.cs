using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// One team slot in a qualifier bracket — name + generated roster.
/// Plain serializable class, part of the JSON save.
/// </summary>
[System.Serializable]
public class BracketTeam
{
    public string teamName;
    public List<CareerPlayer> players = new List<CareerPlayer>();

    /// <summary>Average visible overall — drives off-screen match simulation.</summary>
    public float Strength
    {
        get
        {
            if (players == null || players.Count == 0) return 50f;
            float sum = 0f;
            foreach (var p in players) sum += p.Overall;
            return sum / players.Count;
        }
    }
}

/// <summary>
/// A 4-team single-elimination qualifier: you + 3 generated teams.
/// SF1 is your match; SF2 is simulated off-screen from team strength.
/// The whole bracket runs "in one evening" — between matches you're back
/// in the hub (protocols/roles adjustable), and the week only advances
/// when the bracket closes: final played, or you're out in the semi.
/// </summary>
[System.Serializable]
public class QualifierBracket
{
    public BracketStage stage = BracketStage.Semifinal;
    public int week;

    public BracketTeam sfOpponent; // SEMIFINAL 1: you vs this team
    public BracketTeam teamB;      // SEMIFINAL 2
    public BracketTeam teamC;

    // Semifinal 1 (yours)
    public bool sf1Played;
    public bool sf1Won;
    public int sf1YourScore;
    public int sf1OppScore;

    // Semifinal 2 (simulated)
    public bool sf2Played;
    public bool sf2WinnerIsB;
    public int sf2WinnerScore;
    public int sf2LoserScore;

    // Final
    public bool finalPlayed;
    public string finalWinnerName;
    public string finalLoserName;
    public int finalWinnerScore;
    public int finalLoserScore;
    public bool championIsYou;

    public BracketTeam Sf2Winner => sf2WinnerIsB ? teamB : teamC;

    /// <summary>Your final opponent — null until the other semi is decided.</summary>
    public BracketTeam FinalOpponent => sf2Played ? Sf2Winner : null;

    /// <summary>
    /// Fresh bracket: 3 generated teams with distinct names. Rosters are rolled
    /// here so the finalist you meet is the same squad that "won" its semi.
    /// </summary>
    public static QualifierBracket Create(int currentWeek)
    {
        var used = new HashSet<string>();
        string UniqueName()
        {
            for (int i = 0; i < 20; i++)
            {
                string n = PlayerGenerator.GenerateTeamName();
                if (used.Add(n)) return n;
            }
            string numbered = PlayerGenerator.GenerateTeamName() + " II";
            used.Add(numbered);
            return numbered;
        }

        BracketTeam Gen() => new BracketTeam
        {
            teamName = UniqueName(),
            players = PlayerGenerator.GenerateEnemyTeam(Constants.QUALIFIER_TIER_MIN, Constants.QUALIFIER_TIER_MAX)
        };

        return new QualifierBracket
        {
            week = currentWeek,
            sfOpponent = Gen(),
            teamB = Gen(),
            teamC = Gen()
        };
    }

    public void RecordYourSemifinal(bool won, int yourScore, int oppScore)
    {
        sf1Played = true;
        sf1Won = won;
        sf1YourScore = yourScore;
        sf1OppScore = oppScore;
    }

    /// <summary>SF2 ran off-screen while you played — strength-weighted roll.</summary>
    public void SimulateOtherSemifinal()
    {
        if (sf2Played) return;
        sf2Played = true;
        sf2WinnerIsB = RollWin(teamB.Strength, teamC.Strength);
        sf2WinnerScore = Constants.ROUNDS_TO_WIN;
        sf2LoserScore = SimLoserScore(Mathf.Abs(teamB.Strength - teamC.Strength));
    }

    public void RecordFinal(bool won, int yourScore, int oppScore)
    {
        var opp = FinalOpponent;
        finalPlayed = true;
        championIsYou = won;
        if (won)
        {
            finalWinnerName = Constants.PLAYER_TEAM_NAME;
            finalWinnerScore = yourScore;
            finalLoserName = opp.teamName;
            finalLoserScore = oppScore;
        }
        else
        {
            finalWinnerName = opp.teamName;
            finalWinnerScore = oppScore;
            finalLoserName = Constants.PLAYER_TEAM_NAME;
            finalLoserScore = yourScore;
        }
    }

    /// <summary>You're out in the semi — the other two still crown a champion.</summary>
    public void SimulateFinalWithoutYou()
    {
        var a = sfOpponent;
        var b = Sf2Winner;
        bool aWins = RollWin(a.Strength, b.Strength);
        var winner = aWins ? a : b;
        var loser = aWins ? b : a;
        finalPlayed = true;
        championIsYou = false;
        finalWinnerName = winner.teamName;
        finalLoserName = loser.teamName;
        finalWinnerScore = Constants.ROUNDS_TO_WIN;
        finalLoserScore = SimLoserScore(Mathf.Abs(a.Strength - b.Strength));
    }

    private static bool RollWin(float strengthA, float strengthB)
    {
        float p = 0.5f + (strengthA - strengthB) * Constants.SIM_WINPROB_PER_OVR;
        return Random.value < Mathf.Clamp(p, Constants.SIM_WINPROB_MIN, Constants.SIM_WINPROB_MAX);
    }

    private static int SimLoserScore(float strengthGap)
    {
        float baseScore = Constants.SIM_LOSER_SCORE_BASE - strengthGap * Constants.SIM_LOSER_SCORE_PER_OVR;
        float jitter = Random.Range(-Constants.SIM_LOSER_SCORE_JITTER, Constants.SIM_LOSER_SCORE_JITTER);
        return Mathf.Clamp(Mathf.RoundToInt(baseScore + jitter), 1, Constants.ROUNDS_TO_WIN - 2);
    }
}
