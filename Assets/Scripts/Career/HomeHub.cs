using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Management hub in the Home scene — IMGUI, same stack as the rest of the game UI
/// (CoachPanel, MatchHUD etc). Tabs: Calendar / Roster / Scouting / Shop / Budget.
/// Phase C: scouting offers with negotiation, role assignment, releases.
/// Shop unlocks purchases in Phase D.
/// Auto-creates CareerManager so the Home scene needs no manual wiring.
/// </summary>
public class HomeHub : MonoBehaviour
{
    private int selectedTab;
    private static readonly string[] TabNames = { "CALENDAR", "ROSTER", "SCOUTING", "SHOP", "BUDGET" };
    private static readonly string[] RoleLabels = { "ENTRY", "SUPPORT", "AWP", "LURKER", "IGL" };

    private Vector2 scoutScroll;
    private Vector2 rosterScroll;
    private List<CareerPlayer> sortedPool;
    private bool confirmingReset;
    private CareerPlayer confirmRelease;

    // Negotiation state (UI-side only — thresholds live in CareerManager)
    private CareerPlayer negotiating;
    private int offerValue;
    private OfferResult? offerResult;
    private int counterValue;
    private bool signedDeal;
    private int signedSalary;

    // Cached styles (built once inside OnGUI — GUI.skin is only valid there)
    private GUIStyle h1, h2, text, dim, btn, smallBtn, tabStyle;

    private void Awake()
    {
        CareerManager.EnsureExists();
    }

    private void EnsureStyles()
    {
        if (h1 != null) return;
        h1 = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
        h1.normal.textColor = Color.white;
        h2 = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        h2.normal.textColor = Color.white;
        text = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        text.normal.textColor = Color.white;
        dim = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        dim.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
        tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
    }

    private GUIStyle Tint(GUIStyle baseStyle, Color color)
    {
        var s = new GUIStyle(baseStyle);
        s.normal.textColor = color;
        return s;
    }

    private static readonly Color WinColor = Color.green;
    private static readonly Color LossColor = new Color(1f, 0.4f, 0.4f);
    private static readonly Color WarnColor = new Color(1f, 0.85f, 0.3f);

    private void OnGUI()
    {
        var cm = CareerManager.Instance;
        if (cm == null) return;
        EnsureStyles();

        float x = 30f, y = 20f, w = 880f;
        GUI.Box(new Rect(x - 15, y - 10, w + 30, 660), "");

        // ---------- Header (always visible) ----------
        string header = string.IsNullOrEmpty(cm.teamName) ? "PROTOCOL — MANAGEMENT" : $"PROTOCOL — {cm.teamName.ToUpper()}";
        GUI.Label(new Rect(x, y, w, 34), header, h1);
        if (GUI.Button(new Rect(x + w - 110, y + 2, 100, 30), "MENU", smallBtn))
            UnityEngine.SceneManagement.SceneManager.LoadScene(Constants.MENU_SCENE_NAME);
        y += 40;

        // ---------- Org naming gate (new career / pre-v5 save) ----------
        if (string.IsNullOrEmpty(cm.teamName))
        {
            DrawTeamNaming(cm, x, y + 20, w);
            return;
        }
        GUI.Label(new Rect(x, y, w, 24), $"Week {cm.currentWeek}   |   Budget: ${cm.budget:N0}", h2); y += 26;

        if (cm.hasLastResult)
        {
            // SF wins pay nothing (the money is in the final) — don't show "+$0"
            string prizeNote = cm.lastPrize > 0 ? $"  (+${cm.lastPrize:N0})"
                : cm.bracket != null && cm.bracket.stage == BracketStage.Final ? "  — FINAL tonight" : "";
            GUI.Label(new Rect(x, y, w, 24),
                $"LAST MATCH vs {cm.lastOpponentName}: {(cm.lastMatchWon ? "WIN" : "LOSS")} {cm.lastPlayerScore}-{cm.lastEnemyScore}{prizeNote}",
                Tint(h2, cm.lastMatchWon ? WinColor : LossColor));
        }
        y += 32;

        int newTab = GUI.Toolbar(new Rect(x, y, w - 20, 34), selectedTab, TabNames, tabStyle);
        if (newTab != selectedTab)
        {
            selectedTab = newTab;
            confirmingReset = false;
            confirmRelease = null;
            CloseNegotiation();
        }
        y += 46;

        switch (selectedTab)
        {
            case 0: DrawCalendar(cm, x, y, w); break;
            case 1: DrawRoster(cm, x, y, w); break;
            case 2: DrawScouting(cm, x, y, w); break;
            case 3: DrawShop(cm, x, y, w); break;
            case 4: DrawBudget(cm, x, y, w); break;
        }
    }

    // ===================== ORG NAMING =====================

    private string teamNameInput = "";

    /// <summary>One-time founding screen — the tag shown in brackets, intros, history.</summary>
    private void DrawTeamNaming(CareerManager cm, float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 28), "FOUND YOUR ORG", h2); y += 34;
        GUI.Label(new Rect(x, y, w, 22), "Pick the tag the scene will know you by — brackets, broadcasts, match history.", text); y += 40;

        var field = new GUIStyle(GUI.skin.textField) { fontSize = 20, fontStyle = FontStyle.Bold };
        teamNameInput = GUI.TextField(new Rect(x, y, 340, 40), teamNameInput, 18, field);

        string trimmed = teamNameInput.Trim();
        GUI.enabled = trimmed.Length >= 2;
        if (GUI.Button(new Rect(x + 360, y, 200, 40), "FOUND TEAM", btn))
            cm.SetTeamName(trimmed);
        GUI.enabled = true;
        y += 52;
        GUI.Label(new Rect(x, y, w, 22), "2–18 characters. You can't rename later — orgs live with their tag.", dim);
    }

    // ===================== CALENDAR =====================

    private void DrawCalendar(CareerManager cm, float x, float y, float w)
    {
        var br = cm.bracket;
        bool canPlay = cm.roster.Count >= Constants.ROSTER_SIZE;

        if (br == null)
        {
            GUI.Label(new Rect(x, y, w, 24), "THIS WEEK", h2); y += 28;
            GUI.Label(new Rect(x, y, w, 22), "Online qualifier open — 4 teams, single elimination, played in one evening.", text); y += 24;
            GUI.Label(new Rect(x, y, w, 22),
                $"Champion ${Constants.QUALIFIER_PRIZE_CHAMPION:N0} / runner-up ${Constants.QUALIFIER_PRIZE_FINAL_LOSS:N0} / semifinal exit ${Constants.QUALIFIER_PRIZE_LOSS:N0}.   Weekly net without a match: {FormatSigned(cm.WeeklyNet)}", dim);
            y += 30;

            if (!canPlay)
            {
                GUI.Label(new Rect(x, y, w, 22),
                    $"Roster incomplete ({cm.roster.Count}/{Constants.ROSTER_SIZE}) — sign free agents in SCOUTING before playing.", Tint(text, WarnColor));
                y += 26;
            }

            GUI.enabled = canPlay;
            if (GUI.Button(new Rect(x, y, 270, 46), "ENTER QUALIFIER", btn))
                cm.EnterQualifier();
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + 290, y, 270, 46), "SKIP WEEK", btn))
                cm.AdvanceWeek();
            y += 62;
        }
        else
        {
            y = DrawBracket(cm, br, x, y, w, canPlay);
        }

        GUI.Label(new Rect(x, y, w, 24), "RECENT RESULTS", h2); y += 28;
        if (cm.matchHistory.Count == 0)
        {
            GUI.Label(new Rect(x, y, w, 22), "No matches played yet.", dim);
            return;
        }
        int shown = Mathf.Min(cm.matchHistory.Count, br == null ? 10 : 5);
        for (int i = 0; i < shown; i++)
        {
            var r = cm.matchHistory[i];
            string st = string.IsNullOrEmpty(r.stage) ? "" : $"[{r.stage}] ";
            GUI.Label(new Rect(x, y, w, 22),
                $"Week {r.week} — {st}{(r.won ? "WIN " : "LOSS")} {r.playerScore}-{r.enemyScore} vs {r.opponentName}  (+${r.prize:N0})",
                Tint(text, r.won ? WinColor : LossColor));
            y += 24;
        }
    }

    // ===================== QUALIFIER BRACKET =====================

    /// <summary>The 4-team bracket: two semis feeding the final. Returns the next y.</summary>
    private float DrawBracket(CareerManager cm, QualifierBracket br, float x, float y, float w, bool canPlay)
    {
        GUI.Label(new Rect(x, y, w, 24), $"QUALIFIER BRACKET — week {br.week}", h2); y += 30;

        string you = CareerManager.TeamDisplayName;

        DrawBracketMatch(x, y, "SEMIFINAL 1", you, br.sfOpponent.teamName,
            br.sf1Played, br.sf1YourScore, br.sf1OppScore, br.sf1Won);

        int bScore = br.sf2WinnerIsB ? br.sf2WinnerScore : br.sf2LoserScore;
        int cScore = br.sf2WinnerIsB ? br.sf2LoserScore : br.sf2WinnerScore;
        DrawBracketMatch(x, y + 76, "SEMIFINAL 2", br.teamB.teamName, br.teamC.teamName,
            br.sf2Played, bScore, cScore, br.sf2WinnerIsB);

        string fTop, fBot;
        int fTopScore = 0, fBotScore = 0;
        if (br.finalPlayed)
        {
            fTop = br.finalWinnerName; fBot = br.finalLoserName;
            fTopScore = br.finalWinnerScore; fBotScore = br.finalLoserScore;
        }
        else if (br.stage == BracketStage.Final && br.FinalOpponent != null)
        {
            fTop = you; fBot = br.FinalOpponent.teamName;
        }
        else
        {
            fTop = "SF1 winner"; fBot = "SF2 winner";
        }
        DrawBracketMatch(x + 330, y + 38, "FINAL", fTop, fBot, br.finalPlayed, fTopScore, fBotScore, true);

        if (br.finalPlayed)
        {
            GUI.Label(new Rect(x + 660, y + 48, 220, 20), "CHAMPION", dim);
            GUI.Label(new Rect(x + 660, y + 68, 220, 26), br.finalWinnerName,
                Tint(h2, br.championIsYou ? WinColor : WarnColor));
        }
        y += 160;

        if (br.stage == BracketStage.Done)
        {
            GUI.enabled = canPlay;
            if (GUI.Button(new Rect(x, y, 270, 46), "ENTER QUALIFIER", btn))
                cm.EnterQualifier();
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + 290, y, 270, 46), "SKIP WEEK", btn))
                cm.AdvanceWeek();
            y += 58;
        }
        else
        {
            if (!canPlay)
            {
                GUI.Label(new Rect(x, y, w, 22),
                    $"Roster incomplete ({cm.roster.Count}/{Constants.ROSTER_SIZE}) — sign free agents in SCOUTING first.", Tint(text, WarnColor));
                y += 26;
            }
            GUI.enabled = canPlay;
            string label = br.stage == BracketStage.Semifinal ? "PLAY SEMIFINAL" : "PLAY FINAL";
            if (GUI.Button(new Rect(x, y, 270, 46), label, btn))
                cm.PlayBracketMatch();
            GUI.enabled = true;
            GUI.Label(new Rect(x + 290, y + 4, w - 290, 40),
                br.stage == BracketStage.Final
                    ? "Same evening — tune protocols and roles before the final."
                    : $"Champion ${Constants.QUALIFIER_PRIZE_CHAMPION:N0} / runner-up ${Constants.QUALIFIER_PRIZE_FINAL_LOSS:N0} / SF exit ${Constants.QUALIFIER_PRIZE_LOSS:N0}", dim);
            y += 58;
        }
        return y + 6;
    }

    /// <summary>One bracket box: title + two teams, winner green / loser red once played.</summary>
    private void DrawBracketMatch(float x, float y, string title, string top, string bot,
        bool played, int topScore, int botScore, bool topWon)
    {
        GUI.Box(new Rect(x, y, 310, 70), "");
        GUI.Label(new Rect(x + 10, y + 3, 290, 16), title, dim);
        var topStyle = Tint(text, !played ? Color.white : (topWon ? WinColor : LossColor));
        var botStyle = Tint(text, !played ? Color.white : (topWon ? LossColor : WinColor));
        GUI.Label(new Rect(x + 10, y + 22, 235, 20), top, topStyle);
        GUI.Label(new Rect(x + 255, y + 22, 45, 20), played ? topScore.ToString() : "—", topStyle);
        GUI.Label(new Rect(x + 10, y + 44, 235, 20), bot, botStyle);
        GUI.Label(new Rect(x + 255, y + 44, 45, 20), played ? botScore.ToString() : "—", botStyle);
    }

    // ===================== ROSTER =====================

    private void DrawRoster(CareerManager cm, float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), $"ACTIVE ROSTER ({cm.roster.Count}/{Constants.ROSTER_SIZE})", h2); y += 26;
        GUI.Label(new Rect(x, y, w, 22), "Click a role to reassign. Releasing a player returns them to the free agent pool.", dim); y += 26;

        float rowH = 112f;
        float listH = 310f;
        Rect view = new Rect(0, 0, w - 60, cm.roster.Count * rowH);
        rosterScroll = GUI.BeginScrollView(new Rect(x, y, w - 20, listH), rosterScroll, view);
        for (int i = 0; i < cm.roster.Count; i++)
        {
            var p = cm.roster[i];
            float ry = i * rowH;

            GUI.Label(new Rect(0, ry, 640, 24), $"{p.nickname}  —  {p.playerName}, {p.age}, {p.nationality}", h2);
            string relLabel = confirmRelease == p ? "SURE?" : "RELEASE";
            if (GUI.Button(new Rect(690, ry, 110, 26), relLabel, smallBtn))
            {
                if (confirmRelease == p)
                {
                    cm.ReleasePlayer(p);
                    confirmRelease = null;
                    sortedPool = null;
                    GUI.EndScrollView();
                    return; // collection changed — redraw next frame
                }
                confirmRelease = p;
            }
            ry += 27;

            GUI.Label(new Rect(0, ry, 800, 22),
                $"AIM {p.aim}   REF {p.reflexes}   GS {p.gameSense}    |    CLU {p.clutch}   CON {p.consistency}   COM {p.communication}", text);
            ry += 23;

            string prefNote = p.assignedRole != p.preferredRole ? $"      (prefers {p.preferredRole})" : "";
            GUI.Label(new Rect(0, ry, 800, 22),
                $"EGO {p.ego}   WORK {p.workEthic}   TOX {p.toxicity}   LEAD {p.leadership}      ${p.salaryPerMonth:N0}/mo{prefNote}", dim);
            ry += 25;

            int cur = (int)p.assignedRole;
            int sel = GUI.Toolbar(new Rect(0, ry, 420, 26), cur, RoleLabels, smallBtn);
            if (sel != cur) cm.SetAssignedRole(p, (Role)sel);
        }
        GUI.EndScrollView();

        DrawProtocols(cm, x, y + listH + 12, w);
    }

    // ===================== TEAM PROTOCOLS =====================

    private bool protocolsDirty;

    /// <summary>
    /// The coach's real job: HOW the team plays without you. The IGL calls
    /// every round from these — in-match you only get 3 tactical timeouts.
    /// </summary>
    private void DrawProtocols(CareerManager cm, float x, float y, float w)
    {
        var p = cm.protocols;
        GUI.Label(new Rect(x, y, w, 22), "TEAM PROTOCOLS — how they play without you", h2); y += 28;

        GUI.Label(new Rect(x, y, 220, 20), $"Aggression: {p.aggression}", text);
        int aggr = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 230, y + 6, 240, 18), p.aggression, 0f, 100f));
        GUI.Label(new Rect(x + 485, y, 220, 20),
            aggr < 35 ? "slow, map control" : aggr > 65 ? "fast, rush-heavy" : "balanced", dim);
        y += 26;

        GUI.Label(new Rect(x, y, 220, 20), $"Economy discipline: {p.economyDiscipline}", text);
        int disc = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 230, y + 6, 240, 18), p.economyDiscipline, 0f, 100f));
        GUI.Label(new Rect(x + 485, y, 220, 20),
            disc < 40 ? "force-happy" : disc >= 70 ? "strict saves" : "standard", dim);
        y += 26;

        bool lurk = GUI.Toggle(new Rect(x, y, 320, 22), p.preferLurker, "  Default lurker on T-side");

        if (aggr != p.aggression || disc != p.economyDiscipline || lurk != p.preferLurker)
        {
            p.aggression = aggr;
            p.economyDiscipline = disc;
            p.preferLurker = lurk;
            protocolsDirty = true;
        }
    }

    private void Update()
    {
        // Save protocols when the drag is released, not every slider frame
        if (protocolsDirty && !Input.GetMouseButton(0))
        {
            protocolsDirty = false;
            CareerManager.Instance?.Save();
        }
    }

    // ===================== SCOUTING =====================

    private void DrawScouting(CareerManager cm, float x, float y, float w)
    {
        if (negotiating != null)
        {
            DrawNegotiation(cm, x, y, w);
            return;
        }

        GUI.Label(new Rect(x, y, w, 24), "FPL LEADERBOARD — FREE AGENTS", h2); y += 26;
        GUI.Label(new Rect(x, y, w, 22), "Hidden stats (CLU/CON/COM) and personality reveal after signing.", dim); y += 24;
        if (cm.RosterFull)
        {
            GUI.Label(new Rect(x, y, w, 22), "Roster full — release a player before signing someone new.", Tint(text, WarnColor));
        }
        y += 26;

        if (sortedPool == null || sortedPool.Count != cm.scoutingPool.Count)
        {
            sortedPool = new List<CareerPlayer>(cm.scoutingPool);
            sortedPool.Sort((a, b) => b.Overall.CompareTo(a.Overall));
        }

        DrawScoutRow(x, y, "#", "NICKNAME", "AGE", "NATION", "ROLE", "AIM", "REF", "GS", "OVR", "ASKING", dim);
        y += 26;

        float rowH = 26f;
        float listH = 400f;
        Rect view = new Rect(0, 0, w - 60, sortedPool.Count * rowH);
        scoutScroll = GUI.BeginScrollView(new Rect(x, y, w - 20, listH), scoutScroll, view);
        for (int i = 0; i < sortedPool.Count; i++)
        {
            var p = sortedPool[i];
            float ry = i * rowH;
            DrawScoutRow(0, ry, (i + 1).ToString(), p.nickname, p.age.ToString(), p.nationality,
                p.preferredRole.ToString(), p.aim.ToString(), p.reflexes.ToString(), p.gameSense.ToString(),
                p.Overall.ToString(), $"${p.salaryPerMonth:N0}");

            GUI.enabled = !cm.RosterFull;
            if (GUI.Button(new Rect(706, ry, 84, 22), "OFFER", smallBtn))
            {
                negotiating = p;
                offerValue = p.salaryPerMonth;
                offerResult = null;
                signedDeal = false;
            }
            GUI.enabled = true;
        }
        GUI.EndScrollView();
    }

    private void DrawScoutRow(float x, float y, string rank, string nick, string age, string nation,
        string role, string aim, string reflexes, string gs, string ovr, string asking, GUIStyle style = null)
    {
        GUIStyle s = style ?? text;
        GUI.Label(new Rect(x, y, 32, 22), rank, s);
        GUI.Label(new Rect(x + 36, y, 140, 22), nick, s);
        GUI.Label(new Rect(x + 180, y, 36, 22), age, s);
        GUI.Label(new Rect(x + 220, y, 100, 22), nation, s);
        GUI.Label(new Rect(x + 324, y, 80, 22), role, s);
        GUI.Label(new Rect(x + 408, y, 44, 22), aim, s);
        GUI.Label(new Rect(x + 452, y, 44, 22), reflexes, s);
        GUI.Label(new Rect(x + 496, y, 44, 22), gs, s);
        GUI.Label(new Rect(x + 540, y, 48, 22), ovr, s);
        GUI.Label(new Rect(x + 592, y, 110, 22), asking, s);
    }

    // ===================== NEGOTIATION =====================

    private void DrawNegotiation(CareerManager cm, float x, float y, float w)
    {
        var p = negotiating;
        GUI.Label(new Rect(x, y, w, 26), $"CONTRACT OFFER — {p.nickname}", h2); y += 30;
        GUI.Label(new Rect(x, y, w, 22),
            $"{p.playerName}, {p.age}, {p.nationality}   [{p.preferredRole}]   AIM {p.aim} / REF {p.reflexes} / GS {p.gameSense}   (OVR {p.Overall})", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"Asking: ${p.salaryPerMonth:N0}/mo", text); y += 34;

        if (signedDeal)
        {
            GUI.Label(new Rect(x, y, w, 26), $"SIGNED at ${signedSalary:N0}/mo — welcome aboard!", Tint(h2, WinColor)); y += 38;
            if (GUI.Button(new Rect(x, y, 160, 42), "DONE", btn)) CloseNegotiation();
            return;
        }

        if (offerResult == OfferResult.Countered)
        {
            GUI.Label(new Rect(x, y, w, 26), $"Counter-offer: ${counterValue:N0}/mo — final. Take it or leave it.", Tint(h2, WarnColor)); y += 38;
            if (GUI.Button(new Rect(x, y, 240, 42), $"ACCEPT ${counterValue:N0}/mo", btn))
            {
                cm.AcceptCounter(p, counterValue);
                signedDeal = true;
                signedSalary = counterValue;
                sortedPool = null;
            }
            if (GUI.Button(new Rect(x + 260, y, 180, 42), "WALK AWAY", btn)) CloseNegotiation();
            return;
        }

        if (offerResult == OfferResult.Rejected)
        {
            GUI.Label(new Rect(x, y, w, 24), "Rejected — insultingly low. Their asking price just went up.", Tint(h2, LossColor)); y += 32;
        }

        GUI.Label(new Rect(x, y + 6, 240, 30), $"Your offer: ${offerValue:N0}/mo", h2);
        float bx = x + 260;
        if (GUI.Button(new Rect(bx, y, 70, 36), "-100", smallBtn)) offerValue = Mathf.Max(100, offerValue - 100);
        if (GUI.Button(new Rect(bx + 78, y, 70, 36), "-25", smallBtn)) offerValue = Mathf.Max(100, offerValue - 25);
        if (GUI.Button(new Rect(bx + 156, y, 70, 36), "+25", smallBtn)) offerValue += 25;
        if (GUI.Button(new Rect(bx + 234, y, 70, 36), "+100", smallBtn)) offerValue += 100;
        y += 48;

        if (GUI.Button(new Rect(x, y, 220, 44), "SEND OFFER", btn))
        {
            offerResult = cm.MakeOffer(p, offerValue, out counterValue);
            if (offerResult == OfferResult.Accepted)
            {
                signedDeal = true;
                signedSalary = offerValue;
                sortedPool = null;
            }
        }
        if (GUI.Button(new Rect(x + 240, y, 160, 44), "CANCEL", btn)) CloseNegotiation();
    }

    private void CloseNegotiation()
    {
        negotiating = null;
        offerResult = null;
        signedDeal = false;
    }

    // ===================== SHOP =====================

    private void DrawShop(CareerManager cm, float x, float y, float w)
    {
        var eq = cm.TeamEquipment;
        GUI.Label(new Rect(x, y, w, 24), "TEAM EQUIPMENT", h2); y += 30;

        int pcAim = eq.GetAimBonus() - eq.mouseBonus;
        GUI.Label(new Rect(x, y, w, 22), $"PC tier:   {eq.pcTier}   (team AIM +{pcAim})", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"Monitor:   {eq.monitorTier}   (team REF +{eq.GetReflexBonus()})", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"Mouse:     AIM +{eq.mouseBonus}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"Headset:   COM +{eq.headsetBonus}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"Chair:     CON +{eq.chairBonus}", text); y += 34;

        GUI.Label(new Rect(x, y, w, 22),
            $"Total team bonuses:  AIM +{eq.GetAimBonus()}   REF +{eq.GetReflexBonus()}   COM +{eq.GetCommunicationBonus()}   CON +{eq.GetConsistencyBonus()}", h2);
        y += 34;
        
    }

    // ===================== BUDGET =====================

    private void DrawBudget(CareerManager cm, float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 30), $"BALANCE: ${cm.budget:N0}", h1); y += 44;

        GUI.Label(new Rect(x, y, w, 24), "WEEKLY CASH FLOW", h2); y += 28;
        int salaries = cm.WeeklyExpenses - Constants.WEEKLY_RENT;
        GUI.Label(new Rect(x, y, w, 22), $"Salaries ({cm.roster.Count} players):   -${salaries:N0}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"Apartment rent:            -${Constants.WEEKLY_RENT:N0}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"Sponsor deal:              +${Constants.SPONSOR_WEEKLY:N0}", text); y += 26;
        GUI.Label(new Rect(x, y, w, 24), $"Net per week: {FormatSigned(cm.WeeklyNet)}",
            Tint(h2, cm.WeeklyNet >= 0 ? WinColor : LossColor)); y += 38;

        GUI.Label(new Rect(x, y, w, 24), "PRIZE INCOME", h2); y += 28;
        int totalPrizes = 0;
        foreach (var r in cm.matchHistory) totalPrizes += r.prize;
        GUI.Label(new Rect(x, y, w, 22), $"Matches played: {cm.matchHistory.Count}    Total prize money: ${totalPrizes:N0}", text); y += 34;

        GUI.Label(new Rect(x, y, w, 22), $"Auto-save after every week: {Constants.SAVE_FILE_NAME} (persistentDataPath)", dim); y += 40;

        string resetLabel = confirmingReset ? "CONFIRM — WIPE SAVE & RESTART" : "NEW CAREER (wipe save)";
        if (GUI.Button(new Rect(x, y, 340, 42), resetLabel, btn))
        {
            if (confirmingReset)
            {
                cm.NewCareer();
                sortedPool = null;
                confirmingReset = false;
            }
            else confirmingReset = true;
        }
    }

    private string FormatSigned(int value) => value >= 0 ? $"+${value:N0}" : $"-${-value:N0}";
}
