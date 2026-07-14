using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Persistent career state across scenes: budget, week, roster, scouting pool, equipment.
/// Created in the Home scene (auto-spawned by HomeHub), survives via DontDestroyOnLoad.
/// Auto-loads the JSON save on startup; saves after every week advance.
/// The match engine reads from it through RoundManager's career hook — if no career
/// is running (standalone match testing), RoundManager falls back to inspector data.
/// </summary>
public class CareerManager : MonoBehaviour
{
    public static CareerManager Instance { get; private set; }

    [Header("Career State")]
    public string teamName = ""; // empty → HomeHub shows the naming screen
    public int budget;
    public int currentWeek = 1;
    public List<CareerPlayer> roster = new List<CareerPlayer>();
    public List<CareerPlayer> scoutingPool = new List<CareerPlayer>();
    public List<MatchRecord> matchHistory = new List<MatchRecord>();
    public TeamProtocols protocols = new TeamProtocols();
    public QualifierBracket bracket; // null = no qualifier entered

    public EquipmentBonuses TeamEquipment { get; private set; }

    /// <summary>The player's org name everywhere it's shown — bracket, intro, history.</summary>
    public static string TeamDisplayName =>
        Instance != null && !string.IsNullOrEmpty(Instance.teamName)
            ? Instance.teamName : Constants.PLAYER_TEAM_NAME;

    public void SetTeamName(string name)
    {
        teamName = name.Trim();
        Save();
        Debug.Log($"CAREER: org founded — {teamName}");
    }

    [Header("Next Match")]
    public string nextOpponentName;
    private List<CareerPlayer> nextOpponentPlayers;

    [Header("Last Match Result (session only)")]
    public bool hasLastResult;
    public bool lastMatchWon;
    public int lastPlayerScore;
    public int lastEnemyScore;
    public int lastPrize;
    public string lastOpponentName;

    /// <summary>
    /// Weekly burn: player salaries (monthly / 4) + rent.
    /// </summary>
    public int WeeklyExpenses
    {
        get
        {
            int salaries = 0;
            foreach (var p in roster) salaries += Mathf.RoundToInt(p.salaryPerMonth / 4f);
            return salaries + Constants.WEEKLY_RENT;
        }
    }

    public int WeeklyIncome => Constants.SPONSOR_WEEKLY;
    public int WeeklyNet => WeeklyIncome - WeeklyExpenses;

    public static void EnsureExists()
    {
        if (Instance == null)
            new GameObject("CareerManager").AddComponent<CareerManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        var saved = SaveSystem.Load();
        if (saved != null) LoadCareer(saved);
        else InitNewCareer();
    }

    private void InitNewCareer()
    {
        teamName = ""; // fresh org — HomeHub asks for the name
        budget = Constants.STARTING_BUDGET;
        currentWeek = 1;
        // You BUILD the team — the career starts with an empty roster and a
        // scouting pool. Sign your own five before the first qualifier.
        roster = new List<CareerPlayer>();
        scoutingPool = PlayerGenerator.GeneratePool(Constants.SCOUTING_POOL_SIZE);
        matchHistory = new List<MatchRecord>();
        bracket = null;
        hasLastResult = false;
        TeamEquipment = ScriptableObject.CreateInstance<EquipmentBonuses>();
        Debug.Log($"CAREER: new career started — budget ${budget}, roster {roster.Count}, pool {scoutingPool.Count}");
    }

    private void LoadCareer(CareerSaveData data)
    {
        budget = data.budget;
        currentWeek = Mathf.Max(1, data.currentWeek);
        roster = data.roster ?? new List<CareerPlayer>();
        scoutingPool = data.scoutingPool ?? new List<CareerPlayer>();
        matchHistory = data.matchHistory ?? new List<MatchRecord>();

        TeamEquipment = ScriptableObject.CreateInstance<EquipmentBonuses>();
        TeamEquipment.pcTier = data.pcTier;
        TeamEquipment.monitorTier = data.monitorTier;
        TeamEquipment.mouseBonus = data.mouseBonus;
        TeamEquipment.headsetBonus = data.headsetBonus;
        TeamEquipment.chairBonus = data.chairBonus;

        // Migration: saves from before Phase C have no assignedRole — default to preference
        if (data.saveVersion < 2)
        {
            foreach (var p in roster) p.assignedRole = p.preferredRole;
            foreach (var p in scoutingPool) p.assignedRole = p.preferredRole;
        }

        // Migration: pre-protocols saves deserialize protocols zeroed — reset to defaults
        protocols = data.saveVersion < 3 || data.protocols == null ? new TeamProtocols() : data.protocols;

        // Migration: pre-bracket saves have no qualifier in progress
        bracket = data.saveVersion >= 4 && data.hasBracket ? data.bracket : null;

        // Migration: pre-v5 saves have no org name — the naming screen appears once
        teamName = data.saveVersion >= 5 && !string.IsNullOrEmpty(data.teamName) ? data.teamName : "";

        // Keep generated enemies/free agents from colliding with saved nicknames
        PlayerGenerator.RegisterExistingNicknames(roster);
        PlayerGenerator.RegisterExistingNicknames(scoutingPool);
        if (bracket != null)
        {
            PlayerGenerator.RegisterExistingNicknames(bracket.sfOpponent?.players);
            PlayerGenerator.RegisterExistingNicknames(bracket.teamB?.players);
            PlayerGenerator.RegisterExistingNicknames(bracket.teamC?.players);
        }

        // Empty roster is now a VALID state (careers start rosterless) —
        // only a save with no players anywhere is treated as corrupt
        if (roster.Count == 0 && scoutingPool.Count == 0)
        {
            Debug.LogWarning("CAREER: save had no players at all — starting a new career");
            InitNewCareer();
            return;
        }
        Debug.Log($"CAREER: save loaded — week {currentWeek}, budget ${budget}, roster {roster.Count}");
    }

    public void Save()
    {
        if (TeamEquipment == null) TeamEquipment = ScriptableObject.CreateInstance<EquipmentBonuses>();
        SaveSystem.Save(new CareerSaveData
        {
            saveVersion = Constants.SAVE_VERSION,
            teamName = teamName,
            budget = budget,
            protocols = protocols,
            currentWeek = currentWeek,
            roster = roster,
            scoutingPool = scoutingPool,
            matchHistory = matchHistory,
            hasBracket = bracket != null,
            bracket = bracket,
            pcTier = TeamEquipment.pcTier,
            monitorTier = TeamEquipment.monitorTier,
            mouseBonus = TeamEquipment.mouseBonus,
            headsetBonus = TeamEquipment.headsetBonus,
            chairBonus = TeamEquipment.chairBonus
        });
    }

    /// <summary>
    /// Wipe the save and roll a fresh career.
    /// </summary>
    public void NewCareer()
    {
        SaveSystem.Delete();
        InitNewCareer();
        Save();
    }

    // ===================== ROSTER & NEGOTIATION (Phase C) =====================

    public bool RosterFull => roster.Count >= Constants.ROSTER_SIZE;

    /// <summary>
    /// Hidden accept floor: low-ego players go down to ~85% of their ask,
    /// high-ego players want (nearly) full price. Deterministic — no RNG fishing.
    /// </summary>
    private int AcceptThreshold(CareerPlayer p)
    {
        float floor = Constants.NEGOTIATE_BASE_FLOOR + (1f - Constants.NEGOTIATE_BASE_FLOOR) * (p.ego / 100f);
        return RoundTo50(p.salaryPerMonth * floor);
    }

    /// <summary>
    /// Offer >= hidden floor → accepted at YOUR number. Close miss → one counter
    /// (never below their floor). Insulting lowball → rejected AND their ask goes up.
    /// </summary>
    public OfferResult MakeOffer(CareerPlayer p, int offer, out int counterOffer)
    {
        counterOffer = 0;
        if (RosterFull) return OfferResult.Rejected;

        int threshold = AcceptThreshold(p);
        if (offer >= threshold)
        {
            SignPlayer(p, offer);
            return OfferResult.Accepted;
        }
        if (offer >= Mathf.RoundToInt(threshold * Constants.NEGOTIATE_COUNTER_WINDOW))
        {
            counterOffer = Mathf.Max(threshold, RoundTo50((offer + p.salaryPerMonth) / 2f));
            return OfferResult.Countered;
        }
        p.salaryPerMonth = RoundTo50(p.salaryPerMonth * Constants.NEGOTIATE_REJECT_ASK_RAISE);
        Debug.Log($"CAREER: {p.nickname} rejected ${offer}/mo, new ask ${p.salaryPerMonth}/mo");
        return OfferResult.Rejected;
    }

    public void AcceptCounter(CareerPlayer p, int counterOffer) => SignPlayer(p, counterOffer);

    private void SignPlayer(CareerPlayer p, int salary)
    {
        scoutingPool.Remove(p);
        p.salaryPerMonth = salary;
        p.isSigned = true;
        p.assignedRole = p.preferredRole;
        roster.Add(p);
        Save();
        Debug.Log($"CAREER: signed {p.nickname} at ${salary}/mo");
    }

    /// <summary>
    /// Release back to the free agent pool. Their ask = current salary.
    /// </summary>
    public void ReleasePlayer(CareerPlayer p)
    {
        if (!roster.Remove(p)) return;
        p.isSigned = false;
        scoutingPool.Add(p);
        Save();
        Debug.Log($"CAREER: released {p.nickname} (saves ${p.salaryPerMonth}/mo)");
    }

    public void SetAssignedRole(CareerPlayer p, Role role)
    {
        p.assignedRole = role;
        Save();
    }

    private static int RoundTo50(float value) => Mathf.RoundToInt(value / 50f) * 50;

    // ===================== MATCH FLOW =====================

    /// <summary>
    /// Open a fresh 4-team bracket — all opponents revealed in the Calendar tab.
    /// Replaces a finished bracket; does NOT load the match scene.
    /// </summary>
    public void EnterQualifier()
    {
        bracket = QualifierBracket.Create(currentWeek);
        Save();
        Debug.Log($"CAREER: entered qualifier — SF vs {bracket.sfOpponent.teamName} " +
            $"(also in: {bracket.teamB.teamName}, {bracket.teamC.teamName})");
    }

    /// <summary>Load the match scene for the current bracket stage (semifinal or final).</summary>
    public void PlayBracketMatch()
    {
        if (bracket == null || bracket.stage == BracketStage.Done) return;
        BracketTeam opp = bracket.stage == BracketStage.Semifinal ? bracket.sfOpponent : bracket.FinalOpponent;
        if (opp == null) return;
        nextOpponentName = opp.teamName;
        nextOpponentPlayers = opp.players;
        Debug.Log($"CAREER: playing qualifier {bracket.stage} vs {opp.teamName}");
        SceneManager.LoadScene(Constants.MATCH_SCENE_NAME);
    }

    /// <summary>
    /// Player team for RoundManager (player side = attackers first half).
    /// </summary>
    public PlayerData[] BuildPlayerTeamData()
    {
        var arr = new PlayerData[5];
        for (int i = 0; i < arr.Length && i < roster.Count; i++)
            arr[i] = roster[i].ToPlayerData();
        return arr;
    }

    public PlayerData[] BuildEnemyTeamData()
    {
        // Guard for a match scene loaded without PlayBracketMatch()
        if (nextOpponentPlayers == null)
            nextOpponentPlayers = PlayerGenerator.GenerateEnemyTeam(Constants.QUALIFIER_TIER_MIN, Constants.QUALIFIER_TIER_MAX);

        var arr = new PlayerData[5];
        for (int i = 0; i < arr.Length && i < nextOpponentPlayers.Count; i++)
            arr[i] = nextOpponentPlayers[i].ToPlayerData();
        return arr;
    }

    /// <summary>
    /// Called by RoundManager when the match ends. Advances the bracket, applies prize
    /// money and records history. The week is consumed only when the bracket closes
    /// (bracket runs "in one evening") — or immediately for standalone matches.
    /// </summary>
    public void OnMatchFinished(bool playerWon, int playerScore, int enemyScore)
    {
        int prize;
        string stageLabel = null;
        bool bracketStillRunning = false;

        if (bracket != null && bracket.stage == BracketStage.Semifinal)
        {
            stageLabel = "SF";
            bracket.RecordYourSemifinal(playerWon, playerScore, enemyScore);
            bracket.SimulateOtherSemifinal();
            if (playerWon)
            {
                prize = 0; // the money is in the final
                bracket.stage = BracketStage.Final;
                bracketStillRunning = true;
            }
            else
            {
                prize = Constants.QUALIFIER_PRIZE_LOSS;
                bracket.SimulateFinalWithoutYou();
                bracket.stage = BracketStage.Done;
            }
        }
        else if (bracket != null && bracket.stage == BracketStage.Final)
        {
            stageLabel = "FINAL";
            bracket.RecordFinal(playerWon, playerScore, enemyScore);
            prize = playerWon ? Constants.QUALIFIER_PRIZE_CHAMPION : Constants.QUALIFIER_PRIZE_FINAL_LOSS;
            bracket.stage = BracketStage.Done;
        }
        else
        {
            // Match scene played without a bracket (direct scene test / legacy save)
            prize = playerWon ? Constants.QUALIFIER_PRIZE_WIN : Constants.QUALIFIER_PRIZE_LOSS;
        }

        budget += prize;

        matchHistory.Insert(0, new MatchRecord
        {
            week = currentWeek,
            opponentName = nextOpponentName,
            stage = stageLabel,
            won = playerWon,
            playerScore = playerScore,
            enemyScore = enemyScore,
            prize = prize
        });

        hasLastResult = true;
        lastMatchWon = playerWon;
        lastPlayerScore = playerScore;
        lastEnemyScore = enemyScore;
        lastPrize = prize;
        lastOpponentName = nextOpponentName;
        nextOpponentPlayers = null;

        Debug.Log($"CAREER: {stageLabel ?? "match"} {(playerWon ? "WON" : "LOST")} {playerScore}-{enemyScore} vs {lastOpponentName}, prize ${prize}");

        if (bracketStillRunning)
            Save(); // same evening — the final is next, no week tick between matches
        else
            AdvanceWeek();

        SceneManager.LoadScene(Constants.HOME_SCENE_NAME);
    }

    /// <summary>
    /// Advance one week: pay salaries + rent, collect sponsor income, save.
    /// Playing a match also consumes the week.
    /// </summary>
    public void AdvanceWeek()
    {
        int expenses = WeeklyExpenses;
        budget += Constants.SPONSOR_WEEKLY - expenses;
        currentWeek++;
        Debug.Log($"CAREER: week {currentWeek} — expenses ${expenses}, sponsor +${Constants.SPONSOR_WEEKLY}, budget ${budget}");
        Save();
    }
}
