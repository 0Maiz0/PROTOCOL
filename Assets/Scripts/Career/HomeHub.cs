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
    private bool helpOpen;
    private Vector2 helpScroll;
    private static readonly string[] TabNamesEn = { "CALENDAR", "ROSTER", "SCOUTING", "SHOP", "BUDGET" };
    private static readonly string[] TabNamesRu = { "КАЛЕНДАРЬ", "СОСТАВ", "СКАУТИНГ", "МАГАЗИН", "БЮДЖЕТ" };
    // Role names stay English everywhere — CS terms are universal
    private static readonly string[] RoleLabels = { "ENTRY", "SUPPORT", "AWP", "LURKER", "IGL" };

    /// <summary>Shorthand for the bilingual helper.</summary>
    private static string L(string en, string ru) => Loc.L(en, ru);

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
        string header = string.IsNullOrEmpty(cm.teamName)
            ? L("PROTOCOL — MANAGEMENT", "PROTOCOL — МЕНЕДЖМЕНТ")
            : $"PROTOCOL — {cm.teamName.ToUpper()}";
        GUI.Label(new Rect(x, y, w, 34), header, h1);
        if (GUI.Button(new Rect(x + w - 110, y + 2, 100, 30), L("MENU", "МЕНЮ"), smallBtn))
            UnityEngine.SceneManagement.SceneManager.LoadScene(Constants.MENU_SCENE_NAME);
        if (GUI.Button(new Rect(x + w - 170, y + 2, 54, 30), Loc.Russian ? "EN" : "РУС", smallBtn))
            Loc.Toggle();
        if (GUI.Button(new Rect(x + w - 280, y + 2, 104, 30),
            helpOpen ? L("CLOSE HELP", "ЗАКРЫТЬ") : L("? HOW TO PLAY", "? КАК ИГРАТЬ"), smallBtn))
            helpOpen = !helpOpen;
        y += 40;

        // ---------- Org naming gate (new career / pre-v5 save) ----------
        if (string.IsNullOrEmpty(cm.teamName))
        {
            if (helpOpen) { DrawHelp(x, y, w); return; }
            DrawTeamNaming(cm, x, y + 20, w);
            return;
        }
        GUI.Label(new Rect(x, y, w, 24),
            $"{L("Week", "Неделя")} {cm.currentWeek}   |   {L("Budget", "Бюджет")}: ${cm.budget:N0}", h2); y += 26;

        if (cm.hasLastResult)
        {
            // SF wins pay nothing (the money is in the final) — don't show "+$0"
            string prizeNote = cm.lastPrize > 0 ? $"  (+${cm.lastPrize:N0})"
                : cm.bracket != null && cm.bracket.stage == BracketStage.Final ? L("  — FINAL tonight", "  — вечером ФИНАЛ") : "";
            GUI.Label(new Rect(x, y, w, 24),
                $"{L("LAST MATCH vs", "ПРОШЛЫЙ МАТЧ против")} {cm.lastOpponentName}: {(cm.lastMatchWon ? L("WIN", "ПОБЕДА") : L("LOSS", "ПОРАЖЕНИЕ"))} {cm.lastPlayerScore}-{cm.lastEnemyScore}{prizeNote}",
                Tint(h2, cm.lastMatchWon ? WinColor : LossColor));
        }
        y += 32;

        if (helpOpen)
        {
            DrawHelp(x, y, w);
            return;
        }

        int newTab = GUI.Toolbar(new Rect(x, y, w - 20, 34), selectedTab,
            Loc.Russian ? TabNamesRu : TabNamesEn, tabStyle);
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

    // ===================== HELP / HOW TO PLAY =====================

    /// <summary>
    /// The friend-proof explainer: what the game is, what the stats mean, how
    /// to build a roster and what the coach actually controls.
    /// </summary>
    private void DrawHelp(float x, float y, float w)
    {
        float listH = 640f - (y - 20f);
        var content = new List<(string title, string body)>
        {
            (L("THE GAME", "СУТЬ ИГРЫ"),
             L("You are the COACH, not the players. Sign five, assign roles, set the protocols, enter the qualifier. The team plays the match itself — you watch like a broadcast, see what's broken and fix the ROSTER, not the round.",
               "Ты — ТРЕНЕР, а не игроки. Подпиши пятерых, раздай роли, настрой протоколы и войди в квалифаер. Матч команда играет сама — ты смотришь как трансляцию, видишь, что не работает, и чинишь СОСТАВ, а не раунд.")),
            (L("STATS (visible)", "СТАТЫ (видимые)"),
             L("AIM — shooting accuracy. REF — reaction speed (who fires first). GS — game sense: positioning, holding angles, reads. CLU — clutch factor in 1vX. CON — consistency, how stable their level is. OVR — average of AIM/REF/GS.",
               "AIM — точность стрельбы. REF — реакция (кто выстрелит первым). GS — гейм-сенс: позиции, углы, чтение игры. CLU — клатч-фактор в 1vX. CON — стабильность уровня от раунда к раунду. OVR — среднее AIM/REF/GS.")),
            (L("PERSONALITY (hidden until signed)", "ЛИЧНОЕ (скрыто до подписания)"),
             L("EGO — negotiation greed and attitude. WORK — work ethic (training, later). TOX — toxicity (locker room, later). LEAD — leadership: THE stat for your IGL, it scales every call the team makes. COM — how well they share info.",
               "EGO — жадность в переговорах и характер. WORK — трудолюбие (тренировки, позже). TOX — токсичность (позже). LEAD — лидерство: ГЛАВНЫЙ стат для IGL, от него зависит качество всех коллов команды. COM — качество передачи информации.")),
            (L("ROLES — who does what", "РОЛИ — кто что делает"),
             L("ENTRY goes in first (wants AIM+REF). SUPPORT carries the bomb and throws utility. AWP is your sniper (expensive buys, needs AIM+REF). LURKER flanks alone (wants GS). IGL calls every round: strategy, rotations, saves — give it to your highest LEAD or the team plays dumb. Click a role button on the ROSTER tab to reassign.",
               "ENTRY входит на сайт первым (нужны AIM+REF). SUPPORT носит бомбу и кидает ютиль. AWP — снайпер (дорогая закупка, нужны AIM+REF). LURKER ходит фланги один (нужен GS). IGL коллит каждый раунд: страты, ротации, сейвы — отдай роль игроку с максимальным LEAD, иначе команда тупит. Роль меняется кнопками на вкладке СОСТАВ.")),
            (L("PROTOCOLS — your fingerprint", "ПРОТОКОЛЫ — твой почерк"),
             L("Aggression: pace of attacks; high = entry flashes first and rush-heavy calls. Economy discipline: when the team forces vs saves. Lurker: split one flanker by default. The IGL executes THESE every round — you never call rounds yourself.",
               "Агрессия: темп атак; высокая = энтри-флеши в приоритете и раши. Эко-дисциплина: когда команда форсит, а когда сейвит. Люркер: выделять ли фланг по умолчанию. IGL исполняет ИМЕННО ЭТО каждый раунд — сам ты раунды не коллишь.")),
            (L("QUALIFIER & MONEY", "КВАЛИФАЕР И ДЕНЬГИ"),
             L("4 teams, single elimination, one evening: semifinal, then final. Between matches you can change roles and protocols. Champion $6,000 / runner-up $2,000 / semifinal exit $500. When the bracket closes the week ticks: salaries and rent go out, sponsor money comes in. Empty roster? Sign free agents on SCOUTING — offer near their asking price, big EGO means hard bargaining.",
               "4 команды, олимпийка за один вечер: полуфинал, затем финал. Между матчами можно менять роли и протоколы. Чемпион $6,000 / финалист $2,000 / вылет в полуфинале $500. Когда сетка закрыта, тикает неделя: списываются зарплаты и аренда, капает спонсор. Нет состава? Подписывай фри-агентов на СКАУТИНГЕ — предлагай около их запроса, высокое EGO = жёсткий торг.")),
            (L("DURING THE MATCH", "ВО ВРЕМЯ МАТЧА"),
             L("You are the observer: 1-5 follow T players, 6-0 CT, TAB next alive, SPACE auto-director, V POV camera. Your ONLY lever is 3 tactical timeouts per match (T key): override the buy and the strategy for one round. Everything else — trust the team you built.",
               "Ты — обсервер: 1-5 камера на T-игроков, 6-0 на CT, TAB следующий живой, SPACE авторежиссёр, V вид от первого лица. Единственный рычаг — 3 тактических таймаута за матч (клавиша T): перехватить закупку и страту на один раунд. Всё остальное — доверься команде, которую собрал.")),
        };

        helpScroll = GUI.BeginScrollView(new Rect(x, y, w - 10, listH), helpScroll,
            new Rect(0, 0, w - 40, content.Count * 118f + 20f));
        float hy = 0f;
        foreach (var (title, body) in content)
        {
            GUI.Label(new Rect(0, hy, w - 40, 22), title, Tint(h2, WarnColor)); hy += 24;
            var bodyStyle = new GUIStyle(text) { wordWrap = true, fontSize = 14 };
            GUI.Label(new Rect(0, hy, w - 50, 86), body, bodyStyle); hy += 94;
        }
        GUI.EndScrollView();
    }

    // ===================== ORG NAMING =====================

    private string teamNameInput = "";

    /// <summary>One-time founding screen — the tag shown in brackets, intros, history.</summary>
    private void DrawTeamNaming(CareerManager cm, float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 28), L("FOUND YOUR ORG", "ОСНУЙ СВОЮ ОРГАНИЗАЦИЮ"), h2); y += 34;
        GUI.Label(new Rect(x, y, w, 22),
            L("Pick the tag the scene will know you by — brackets, broadcasts, match history.",
              "Придумай тег, под которым тебя узнает сцена — сетки, трансляции, история матчей."), text); y += 40;

        var field = new GUIStyle(GUI.skin.textField) { fontSize = 20, fontStyle = FontStyle.Bold };
        teamNameInput = GUI.TextField(new Rect(x, y, 340, 40), teamNameInput, 18, field);

        string trimmed = teamNameInput.Trim();
        GUI.enabled = trimmed.Length >= 2;
        if (GUI.Button(new Rect(x + 360, y, 200, 40), L("FOUND TEAM", "ОСНОВАТЬ"), btn))
            cm.SetTeamName(trimmed);
        GUI.enabled = true;
        y += 52;
        GUI.Label(new Rect(x, y, w, 22),
            L("2–18 characters. You can't rename later — orgs live with their tag.",
              "2–18 символов. Переименовать нельзя — орги живут со своим тегом."), dim);
    }

    // ===================== CALENDAR =====================

    private void DrawCalendar(CareerManager cm, float x, float y, float w)
    {
        var br = cm.bracket;
        bool canPlay = cm.roster.Count >= Constants.ROSTER_SIZE;

        if (br == null)
        {
            GUI.Label(new Rect(x, y, w, 24), L("THIS WEEK", "ЭТА НЕДЕЛЯ"), h2); y += 28;
            GUI.Label(new Rect(x, y, w, 22),
                L("Online qualifier open — 4 teams, single elimination, played in one evening.",
                  "Открыт онлайн-квалифаер — 4 команды, олимпийка, играется за один вечер."), text); y += 24;
            GUI.Label(new Rect(x, y, w, 22),
                $"{L("Champion", "Чемпион")} ${Constants.QUALIFIER_PRIZE_CHAMPION:N0} / {L("runner-up", "финалист")} ${Constants.QUALIFIER_PRIZE_FINAL_LOSS:N0} / {L("semifinal exit", "вылет в полуфинале")} ${Constants.QUALIFIER_PRIZE_LOSS:N0}.   {L("Weekly net without a match", "Недельный баланс без матча")}: {FormatSigned(cm.WeeklyNet)}", dim);
            y += 30;

            if (!canPlay)
            {
                GUI.Label(new Rect(x, y, w, 22),
                    $"{L("Roster incomplete", "Состав неполный")} ({cm.roster.Count}/{Constants.ROSTER_SIZE}) — {L("sign free agents in SCOUTING before playing.", "подпиши фри-агентов на вкладке СКАУТИНГ.")}", Tint(text, WarnColor));
                y += 26;
            }

            GUI.enabled = canPlay;
            if (GUI.Button(new Rect(x, y, 270, 46), L("ENTER QUALIFIER", "ВОЙТИ В КВАЛИФАЕР"), btn))
                cm.EnterQualifier();
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + 290, y, 270, 46), L("SKIP WEEK", "ПРОПУСТИТЬ НЕДЕЛЮ"), btn))
                cm.AdvanceWeek();
            y += 62;
        }
        else
        {
            y = DrawBracket(cm, br, x, y, w, canPlay);
        }

        GUI.Label(new Rect(x, y, w, 24), L("RECENT RESULTS", "ПОСЛЕДНИЕ РЕЗУЛЬТАТЫ"), h2); y += 28;
        if (cm.matchHistory.Count == 0)
        {
            GUI.Label(new Rect(x, y, w, 22), L("No matches played yet.", "Матчей пока не было."), dim);
            return;
        }
        int shown = Mathf.Min(cm.matchHistory.Count, br == null ? 10 : 5);
        for (int i = 0; i < shown; i++)
        {
            var r = cm.matchHistory[i];
            string st = string.IsNullOrEmpty(r.stage) ? "" : $"[{r.stage}] ";
            GUI.Label(new Rect(x, y, w, 22),
                $"{L("Week", "Неделя")} {r.week} — {st}{(r.won ? L("WIN ", "ПОБЕДА ") : L("LOSS", "ПОРАЖ."))} {r.playerScore}-{r.enemyScore} vs {r.opponentName}  (+${r.prize:N0})",
                Tint(text, r.won ? WinColor : LossColor));
            y += 24;
        }
    }

    // ===================== QUALIFIER BRACKET =====================

    /// <summary>The 4-team bracket: two semis feeding the final. Returns the next y.</summary>
    private float DrawBracket(CareerManager cm, QualifierBracket br, float x, float y, float w, bool canPlay)
    {
        GUI.Label(new Rect(x, y, w, 24), $"{L("QUALIFIER BRACKET — week", "СЕТКА КВАЛИФАЕРА — неделя")} {br.week}", h2); y += 30;

        string you = CareerManager.TeamDisplayName;

        DrawBracketMatch(x, y, L("SEMIFINAL 1", "ПОЛУФИНАЛ 1"), you, br.sfOpponent.teamName,
            br.sf1Played, br.sf1YourScore, br.sf1OppScore, br.sf1Won);

        int bScore = br.sf2WinnerIsB ? br.sf2WinnerScore : br.sf2LoserScore;
        int cScore = br.sf2WinnerIsB ? br.sf2LoserScore : br.sf2WinnerScore;
        DrawBracketMatch(x, y + 76, L("SEMIFINAL 2", "ПОЛУФИНАЛ 2"), br.teamB.teamName, br.teamC.teamName,
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
            fTop = L("SF1 winner", "Победитель ПФ1"); fBot = L("SF2 winner", "Победитель ПФ2");
        }
        DrawBracketMatch(x + 330, y + 38, L("FINAL", "ФИНАЛ"), fTop, fBot, br.finalPlayed, fTopScore, fBotScore, true);

        if (br.finalPlayed)
        {
            GUI.Label(new Rect(x + 660, y + 48, 220, 20), L("CHAMPION", "ЧЕМПИОН"), dim);
            GUI.Label(new Rect(x + 660, y + 68, 220, 26), br.finalWinnerName,
                Tint(h2, br.championIsYou ? WinColor : WarnColor));
        }
        y += 160;

        if (br.stage == BracketStage.Done)
        {
            GUI.enabled = canPlay;
            if (GUI.Button(new Rect(x, y, 270, 46), L("ENTER QUALIFIER", "ВОЙТИ В КВАЛИФАЕР"), btn))
                cm.EnterQualifier();
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + 290, y, 270, 46), L("SKIP WEEK", "ПРОПУСТИТЬ НЕДЕЛЮ"), btn))
                cm.AdvanceWeek();
            y += 58;
        }
        else
        {
            if (!canPlay)
            {
                GUI.Label(new Rect(x, y, w, 22),
                    $"{L("Roster incomplete", "Состав неполный")} ({cm.roster.Count}/{Constants.ROSTER_SIZE}) — {L("sign free agents in SCOUTING first.", "сначала подпиши игроков на СКАУТИНГЕ.")}", Tint(text, WarnColor));
                y += 26;
            }
            GUI.enabled = canPlay;
            string label = br.stage == BracketStage.Semifinal ? L("PLAY SEMIFINAL", "ИГРАТЬ ПОЛУФИНАЛ") : L("PLAY FINAL", "ИГРАТЬ ФИНАЛ");
            if (GUI.Button(new Rect(x, y, 270, 46), label, btn))
                cm.PlayBracketMatch();
            GUI.enabled = true;
            GUI.Label(new Rect(x + 290, y + 4, w - 290, 40),
                br.stage == BracketStage.Final
                    ? L("Same evening — tune protocols and roles before the final.",
                        "Тот же вечер — покрути роли и протоколы перед финалом.")
                    : $"{L("Champion", "Чемпион")} ${Constants.QUALIFIER_PRIZE_CHAMPION:N0} / {L("runner-up", "финалист")} ${Constants.QUALIFIER_PRIZE_FINAL_LOSS:N0} / {L("SF exit", "вылет в ПФ")} ${Constants.QUALIFIER_PRIZE_LOSS:N0}", dim);
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
        GUI.Label(new Rect(x, y, w, 24), $"{L("ACTIVE ROSTER", "ОСНОВНОЙ СОСТАВ")} ({cm.roster.Count}/{Constants.ROSTER_SIZE})", h2); y += 26;
        GUI.Label(new Rect(x, y, w, 22),
            L("Click a role to reassign — IGL wants your highest LEAD. Releasing returns a player to the pool.",
              "Клик по роли меняет её — IGL отдай игроку с максимальным LEAD. Релиз возвращает игрока в пул."), dim); y += 26;

        float rowH = 112f;
        float listH = 310f;
        Rect view = new Rect(0, 0, w - 60, cm.roster.Count * rowH);
        rosterScroll = GUI.BeginScrollView(new Rect(x, y, w - 20, listH), rosterScroll, view);
        for (int i = 0; i < cm.roster.Count; i++)
        {
            var p = cm.roster[i];
            float ry = i * rowH;

            GUI.Label(new Rect(0, ry, 640, 24), $"{p.nickname}  —  {p.playerName}, {p.age}, {p.nationality}", h2);
            string relLabel = confirmRelease == p ? L("SURE?", "ТОЧНО?") : L("RELEASE", "РЕЛИЗ");
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
                $"AIM {p.aim}   REF {p.reflexes}   GS {p.gameSense}   CLU {p.clutch}   CON {p.consistency}", text);
            ry += 23;

            string prefNote = p.assignedRole != p.preferredRole
                ? $"      ({L("prefers", "предпочитает")} {p.preferredRole})" : "";
            GUI.Label(new Rect(0, ry, 800, 22),
                $"EGO {p.ego}   WORK {p.workEthic}   TOX {p.toxicity}   LEAD {p.leadership}   COM {p.communication}      ${p.salaryPerMonth:N0}/{L("mo", "мес")}{prefNote}", dim);
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
        GUI.Label(new Rect(x, y, w, 22),
            L("TEAM PROTOCOLS — how they play without you", "ПРОТОКОЛЫ КОМАНДЫ — как они играют без тебя"), h2); y += 28;

        GUI.Label(new Rect(x, y, 220, 20), $"{L("Aggression", "Агрессия")}: {p.aggression}", text);
        int aggr = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 230, y + 6, 240, 18), p.aggression, 0f, 100f));
        GUI.Label(new Rect(x + 485, y, 220, 20),
            aggr < 35 ? L("slow, map control", "медленно, контроль карты")
            : aggr > 65 ? L("fast, rush-heavy", "быстро, раши и флеши") : L("balanced", "сбалансированно"), dim);
        y += 26;

        GUI.Label(new Rect(x, y, 220, 20), $"{L("Economy discipline", "Эко-дисциплина")}: {p.economyDiscipline}", text);
        int disc = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 230, y + 6, 240, 18), p.economyDiscipline, 0f, 100f));
        GUI.Label(new Rect(x + 485, y, 220, 20),
            disc < 40 ? L("force-happy", "любят форсить") : disc >= 70 ? L("strict saves", "строгие сейвы") : L("standard", "стандартно"), dim);
        y += 26;

        bool lurk = GUI.Toggle(new Rect(x, y, 400, 22), p.preferLurker,
            L("  Default lurker on T-side", "  Люркер на T-стороне по умолчанию"));

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

        GUI.Label(new Rect(x, y, w, 24), L("FPL LEADERBOARD — FREE AGENTS", "FPL-ТАБЛИЦА — СВОБОДНЫЕ АГЕНТЫ"), h2); y += 26;
        GUI.Label(new Rect(x, y, w, 22),
            L("AIM accuracy • REF reaction • GS game sense • CLU clutch • CON consistency. Personality reveals after signing.",
              "AIM точность • REF реакция • GS гейм-сенс • CLU клатч • CON стабильность. Личное (EGO/LEAD/COM...) откроется после подписания."), dim); y += 24;
        if (cm.RosterFull)
        {
            GUI.Label(new Rect(x, y, w, 22),
                L("Roster full — release a player before signing someone new.",
                  "Состав полон — сначала релизни кого-то из ростера."), Tint(text, WarnColor));
        }
        y += 26;

        if (sortedPool == null || sortedPool.Count != cm.scoutingPool.Count)
        {
            sortedPool = new List<CareerPlayer>(cm.scoutingPool);
            sortedPool.Sort((a, b) => b.Overall.CompareTo(a.Overall));
        }

        DrawScoutRow(x, y, "#", L("NICKNAME", "НИК"), L("AGE", "ВОЗР"), L("NATION", "СТРАНА"), L("ROLE", "РОЛЬ"),
            "AIM", "REF", "GS", "CLU", "CON", "OVR", L("ASKING", "ЗАПРОС"), dim);
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
                p.clutch.ToString(), p.consistency.ToString(),
                p.Overall.ToString(), $"${p.salaryPerMonth:N0}");

            GUI.enabled = !cm.RosterFull;
            if (GUI.Button(new Rect(706, ry, 84, 22), L("OFFER", "ОФФЕР"), smallBtn))
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
        string role, string aim, string reflexes, string gs, string clu, string con,
        string ovr, string asking, GUIStyle style = null)
    {
        GUIStyle s = style ?? text;
        GUI.Label(new Rect(x, y, 28, 22), rank, s);
        GUI.Label(new Rect(x + 32, y, 120, 22), nick, s);
        GUI.Label(new Rect(x + 156, y, 32, 22), age, s);
        GUI.Label(new Rect(x + 192, y, 74, 22), nation, s);
        GUI.Label(new Rect(x + 270, y, 64, 22), role, s);
        GUI.Label(new Rect(x + 338, y, 38, 22), aim, s);
        GUI.Label(new Rect(x + 378, y, 38, 22), reflexes, s);
        GUI.Label(new Rect(x + 418, y, 38, 22), gs, s);
        GUI.Label(new Rect(x + 458, y, 38, 22), clu, s);
        GUI.Label(new Rect(x + 498, y, 38, 22), con, s);
        GUI.Label(new Rect(x + 540, y, 44, 22), ovr, s);
        GUI.Label(new Rect(x + 588, y, 100, 22), asking, s);
    }

    // ===================== NEGOTIATION =====================

    private void DrawNegotiation(CareerManager cm, float x, float y, float w)
    {
        var p = negotiating;
        GUI.Label(new Rect(x, y, w, 26), $"{L("CONTRACT OFFER", "КОНТРАКТ")} — {p.nickname}", h2); y += 30;
        GUI.Label(new Rect(x, y, w, 22),
            $"{p.playerName}, {p.age}, {p.nationality}   [{p.preferredRole}]   AIM {p.aim} / REF {p.reflexes} / GS {p.gameSense} / CLU {p.clutch} / CON {p.consistency}   (OVR {p.Overall})", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Asking", "Запрос")}: ${p.salaryPerMonth:N0}/{L("mo", "мес")}", text); y += 34;

        if (signedDeal)
        {
            GUI.Label(new Rect(x, y, w, 26),
                $"{L("SIGNED at", "ПОДПИСАН за")} ${signedSalary:N0}/{L("mo — welcome aboard!", "мес — добро пожаловать!")}", Tint(h2, WinColor)); y += 38;
            if (GUI.Button(new Rect(x, y, 160, 42), L("DONE", "ГОТОВО"), btn)) CloseNegotiation();
            return;
        }

        if (offerResult == OfferResult.Countered)
        {
            GUI.Label(new Rect(x, y, w, 26),
                $"{L("Counter-offer", "Контр-оффер")}: ${counterValue:N0}/{L("mo — final. Take it or leave it.", "мес — финальный. Бери или уходи.")}", Tint(h2, WarnColor)); y += 38;
            if (GUI.Button(new Rect(x, y, 240, 42), $"{L("ACCEPT", "ПРИНЯТЬ")} ${counterValue:N0}", btn))
            {
                cm.AcceptCounter(p, counterValue);
                signedDeal = true;
                signedSalary = counterValue;
                sortedPool = null;
            }
            if (GUI.Button(new Rect(x + 260, y, 180, 42), L("WALK AWAY", "УЙТИ"), btn)) CloseNegotiation();
            return;
        }

        if (offerResult == OfferResult.Rejected)
        {
            GUI.Label(new Rect(x, y, w, 24),
                L("Rejected — insultingly low. Their asking price just went up.",
                  "Отказ — оскорбительно мало. Его запрос только что вырос."), Tint(h2, LossColor)); y += 32;
        }

        GUI.Label(new Rect(x, y + 6, 240, 30), $"{L("Your offer", "Твой оффер")}: ${offerValue:N0}/{L("mo", "мес")}", h2);
        float bx = x + 260;
        if (GUI.Button(new Rect(bx, y, 70, 36), "-100", smallBtn)) offerValue = Mathf.Max(100, offerValue - 100);
        if (GUI.Button(new Rect(bx + 78, y, 70, 36), "-25", smallBtn)) offerValue = Mathf.Max(100, offerValue - 25);
        if (GUI.Button(new Rect(bx + 156, y, 70, 36), "+25", smallBtn)) offerValue += 25;
        if (GUI.Button(new Rect(bx + 234, y, 70, 36), "+100", smallBtn)) offerValue += 100;
        y += 48;

        if (GUI.Button(new Rect(x, y, 220, 44), L("SEND OFFER", "ОТПРАВИТЬ ОФФЕР"), btn))
        {
            offerResult = cm.MakeOffer(p, offerValue, out counterValue);
            if (offerResult == OfferResult.Accepted)
            {
                signedDeal = true;
                signedSalary = offerValue;
                sortedPool = null;
            }
        }
        if (GUI.Button(new Rect(x + 240, y, 160, 44), L("CANCEL", "ОТМЕНА"), btn)) CloseNegotiation();
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
        GUI.Label(new Rect(x, y, w, 24), L("TEAM EQUIPMENT", "ОБОРУДОВАНИЕ КОМАНДЫ"), h2); y += 30;

        int pcAim = eq.GetAimBonus() - eq.mouseBonus;
        GUI.Label(new Rect(x, y, w, 22), $"{L("PC tier", "Уровень ПК")}:   {eq.pcTier}   ({L("team", "команде")} AIM +{pcAim})", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Monitor", "Монитор")}:   {eq.monitorTier}   ({L("team", "команде")} REF +{eq.GetReflexBonus()})", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Mouse", "Мышь")}:     AIM +{eq.mouseBonus}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Headset", "Гарнитура")}:   COM +{eq.headsetBonus}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Chair", "Кресло")}:     CON +{eq.chairBonus}", text); y += 34;

        GUI.Label(new Rect(x, y, w, 22),
            $"{L("Total team bonuses", "Суммарные бонусы команды")}:  AIM +{eq.GetAimBonus()}   REF +{eq.GetReflexBonus()}   COM +{eq.GetCommunicationBonus()}   CON +{eq.GetConsistencyBonus()}", h2);
        y += 34;
        GUI.Label(new Rect(x, y, w, 22),
            L("Bonuses already apply to your players in matches. Purchases come in a later update.",
              "Бонусы уже действуют на игроков в матчах. Покупки появятся в следующем обновлении."), dim);
    }

    // ===================== BUDGET =====================

    private void DrawBudget(CareerManager cm, float x, float y, float w)
    {
        GUI.Label(new Rect(x, y, w, 30), $"{L("BALANCE", "БАЛАНС")}: ${cm.budget:N0}", h1); y += 44;

        GUI.Label(new Rect(x, y, w, 24), L("WEEKLY CASH FLOW", "НЕДЕЛЬНЫЙ ДЕНЕЖНЫЙ ПОТОК"), h2); y += 28;
        int salaries = cm.WeeklyExpenses - Constants.WEEKLY_RENT;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Salaries", "Зарплаты")} ({cm.roster.Count} {L("players", "игроков")}):   -${salaries:N0}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Apartment rent", "Аренда квартиры")}:            -${Constants.WEEKLY_RENT:N0}", text); y += 24;
        GUI.Label(new Rect(x, y, w, 22), $"{L("Sponsor deal", "Спонсорский контракт")}:              +${Constants.SPONSOR_WEEKLY:N0}", text); y += 26;
        GUI.Label(new Rect(x, y, w, 24), $"{L("Net per week", "Итог за неделю")}: {FormatSigned(cm.WeeklyNet)}",
            Tint(h2, cm.WeeklyNet >= 0 ? WinColor : LossColor)); y += 38;

        GUI.Label(new Rect(x, y, w, 24), L("PRIZE INCOME", "ПРИЗОВЫЕ"), h2); y += 28;
        int totalPrizes = 0;
        foreach (var r in cm.matchHistory) totalPrizes += r.prize;
        GUI.Label(new Rect(x, y, w, 22),
            $"{L("Matches played", "Сыграно матчей")}: {cm.matchHistory.Count}    {L("Total prize money", "Всего призовых")}: ${totalPrizes:N0}", text); y += 34;

        GUI.Label(new Rect(x, y, w, 22),
            $"{L("Auto-save after every week", "Автосейв каждую неделю")}: {Constants.SAVE_FILE_NAME}", dim); y += 40;

        string resetLabel = confirmingReset
            ? L("CONFIRM — WIPE SAVE & RESTART", "ТОЧНО? — СТЕРЕТЬ СЕЙВ И НАЧАТЬ ЗАНОВО")
            : L("NEW CAREER (wipe save)", "НОВАЯ КАРЬЕРА (стереть сейв)");
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
