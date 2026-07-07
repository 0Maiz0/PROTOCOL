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
        GUI.Label(new Rect(x, y, w, 34), "PROTOCOL — MANAGEMENT", h1); y += 40;
        GUI.Label(new Rect(x, y, w, 24), $"Week {cm.currentWeek}   |   Budget: ${cm.budget:N0}", h2); y += 26;

        if (cm.hasLastResult)
        {
            GUI.Label(new Rect(x, y, w, 24),
                $"LAST MATCH vs {cm.lastOpponentName}: {(cm.lastMatchWon ? "WIN" : "LOSS")} {cm.lastPlayerScore}-{cm.lastEnemyScore}  (+${cm.lastPrize:N0})",
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

    // ===================== CALENDAR =====================

    private void DrawCalendar(CareerManager cm, float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), "THIS WEEK", h2); y += 28;
        GUI.Label(new Rect(x, y, w, 22), "Online qualifier open — opponent revealed at match start.", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22),
            $"Prize: ${Constants.QUALIFIER_PRIZE_WIN:N0} win / ${Constants.QUALIFIER_PRIZE_LOSS:N0} loss.   Weekly net without a match: {FormatSigned(cm.WeeklyNet)}", dim);
        y += 30;

        bool canPlay = cm.roster.Count >= Constants.ROSTER_SIZE;
        if (!canPlay)
        {
            GUI.Label(new Rect(x, y, w, 22),
                $"Roster incomplete ({cm.roster.Count}/{Constants.ROSTER_SIZE}) — sign free agents in SCOUTING before playing.", Tint(text, WarnColor));
            y += 26;
        }

        GUI.enabled = canPlay;
        if (GUI.Button(new Rect(x, y, 270, 46), "PLAY QUALIFIER", btn))
            cm.StartQualifierMatch();
        GUI.enabled = true;
        if (GUI.Button(new Rect(x + 290, y, 270, 46), "SKIP WEEK", btn))
            cm.AdvanceWeek();
        y += 62;

        GUI.Label(new Rect(x, y, w, 24), "RECENT RESULTS", h2); y += 28;
        if (cm.matchHistory.Count == 0)
        {
            GUI.Label(new Rect(x, y, w, 22), "No matches played yet.", dim);
            return;
        }
        int shown = Mathf.Min(cm.matchHistory.Count, 10);
        for (int i = 0; i < shown; i++)
        {
            var r = cm.matchHistory[i];
            GUI.Label(new Rect(x, y, w, 22),
                $"Week {r.week} — {(r.won ? "WIN " : "LOSS")} {r.playerScore}-{r.enemyScore} vs {r.opponentName}  (+${r.prize:N0})",
                Tint(text, r.won ? WinColor : LossColor));
            y += 24;
        }
    }

    // ===================== ROSTER =====================

    private void DrawRoster(CareerManager cm, float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 24), $"ACTIVE ROSTER ({cm.roster.Count}/{Constants.ROSTER_SIZE})", h2); y += 26;
        GUI.Label(new Rect(x, y, w, 22), "Click a role to reassign. Releasing a player returns them to the free agent pool.", dim); y += 26;

        float rowH = 112f;
        float listH = 440f;
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
        GUI.Label(new Rect(x, y, w, 22), "Purchases arrive in Phase D. Bonuses already apply to your bots in matches.", dim);
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
