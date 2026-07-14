using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// On-screen team comms — makes bot decision-making AUDIBLE so it can be
/// tuned: "contact — s1mple spotted!", "need help!", "planting!", IGL reads.
/// Broadcast layout: comms flank the top-center score bar BY SIDE (T panel
/// left, CT panel right); system lines (bomb events, highlights) sit centered
/// under the bar. The enemy team's panel is the observer window into their
/// brain — C toggles it. Auto-created by RoundManager.
/// </summary>
public class CommsFeed : MonoBehaviour
{
    private struct Line
    {
        public string text;
        public float time;
        public Color color;
    }

    private static readonly List<Line> tLines = new List<Line>();
    private static readonly List<Line> ctLines = new List<Line>();
    private static readonly List<Line> sysLines = new List<Line>();
    private static TeamSide playerSide = TeamSide.Attacker;

    private const int MAX_LINES = 7;
    private const float LINE_TTL = 6f;
    private const float ENEMY_TTL = 8f;

    private static readonly Color OwnColor = Color.white;
    private static readonly Color EnemyColor = new Color(1f, 0.55f, 0.45f);
    private static readonly Color SystemColor = new Color(1f, 0.85f, 0.3f);

    public static void SetPlayerSide(TeamSide side) => playerSide = side;

    private static bool showOpponentComms = true; // C toggles — observer mode for tuning

    /// <summary>
    /// Team voice line, routed to that SIDE's panel (T left / CT right).
    /// EVERYTHING is mirrored into the match log regardless of visibility.
    /// </summary>
    public static void Post(string nickname, string message, TeamSide side)
    {
        MatchLogger.Log($"COMMS[{side}] {nickname}: {message}");
        Push(side, $"[{nickname}] {message}", side == playerSide ? OwnColor : EnemyColor);
    }

    /// <summary>Global event line (bomb down/planted, highlights) — centered under the bar.</summary>
    public static void PostSystem(string message)
    {
        MatchLogger.Log($"EVENT: {message}");
        sysLines.Add(new Line { text = message, time = Time.time, color = SystemColor });
        if (sysLines.Count > MAX_LINES) sysLines.RemoveAt(0);
    }

    /// <summary>Opponent AI coach decision — lands on the enemy side's panel.</summary>
    public static void PostOpponent(string message)
    {
        MatchLogger.Log($"AI-COACH: {message}");
        TeamSide enemySide = playerSide == TeamSide.Attacker ? TeamSide.Defender : TeamSide.Attacker;
        Push(enemySide, message, EnemyColor);
    }

    private static void Push(TeamSide side, string text, Color color)
    {
        var list = side == TeamSide.Attacker ? tLines : ctLines;
        list.Add(new Line { text = text, time = Time.time, color = color });
        if (list.Count > MAX_LINES) list.RemoveAt(0);
    }

    public static void Clear()
    {
        tLines.Clear();
        ctLines.Clear();
        sysLines.Clear();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
            showOpponentComms = !showOpponentComms;
    }

    private void OnGUI()
    {
        GUIStyle s = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        float cx = Screen.width / 2f;

        // Fit the panels between the score bar (cx ± 330) and the screen edge
        float panelW = Mathf.Min(300f, cx - 330f - 16f);
        DrawSidePanel(tLines, TeamSide.Attacker, cx - 338f - panelW, panelW, s);
        DrawSidePanel(ctLines, TeamSide.Defender, cx + 338f, panelW, s);

        // System lines — centered under the score bar's status row
        float sy = 80f;
        GUIStyle sysStyle = new GUIStyle(s) { alignment = TextAnchor.MiddleCenter };
        for (int i = 0; i < sysLines.Count; i++)
        {
            float age = Time.time - sysLines[i].time;
            if (age > LINE_TTL) continue;
            Color c = sysLines[i].color;
            c.a = Mathf.Clamp01(1.2f - age / LINE_TTL);
            sysStyle.normal.textColor = c;
            GUI.Label(new Rect(cx - 260, sy, 520, 20), sysLines[i].text, sysStyle);
            sy += 20;
        }
    }

    private void DrawSidePanel(List<Line> lines, TeamSide side, float x, float width, GUIStyle s)
    {
        bool isEnemy = side != playerSide;
        if (isEnemy && !showOpponentComms) return;

        float y = 10f;
        if (isEnemy && lines.Count > 0)
        {
            GUIStyle head = new GUIStyle(s) { fontSize = 11 };
            head.normal.textColor = new Color(1f, 0.55f, 0.45f, 0.8f);
            GUI.Label(new Rect(x, y, width, 14), "— OPPONENT [C to hide] —", head);
            y += 16;
        }

        float ttl = isEnemy ? ENEMY_TTL : LINE_TTL;
        for (int i = 0; i < lines.Count; i++)
        {
            float age = Time.time - lines[i].time;
            if (age > ttl) continue;
            Color c = lines[i].color;
            c.a = Mathf.Clamp01(1.2f - age / ttl);
            s.normal.textColor = c;
            GUI.Label(new Rect(x, y, width, 20), lines[i].text, s);
            y += 19;
        }
    }
}
