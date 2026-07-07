using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// On-screen team comms feed — makes bot decision-making AUDIBLE so it can be
/// tuned: "contact — s1mple spotted!", "need help!", "planting!", IGL reads.
/// Only the player's team is shown (you hear YOUR team); PostSystem lines
/// (bomb events) are visible to everyone. Auto-created by RoundManager.
/// </summary>
public class CommsFeed : MonoBehaviour
{
    private struct Line
    {
        public string text;
        public float time;
        public Color color;
    }

    private static readonly List<Line> lines = new List<Line>();
    private static readonly List<Line> opponentLines = new List<Line>(); // enemy AI decision log
    private static TeamSide playerSide = TeamSide.Attacker;

    private const int MAX_LINES = 7;
    private const float LINE_TTL = 6f;
    private const float OPPONENT_TTL = 8f;

    public static void SetPlayerSide(TeamSide side) => playerSide = side;

    /// <summary>Team voice line — only heard if it's the player's team.</summary>
    public static void Post(string nickname, string message, TeamSide side)
    {
        if (side != playerSide) return;
        Push($"[{nickname}] {message}", Color.white);
    }

    /// <summary>Global event line (bomb down/planted) — everyone sees it.</summary>
    public static void PostSystem(string message)
    {
        Push(message, new Color(1f, 0.85f, 0.3f));
    }

    /// <summary>Opponent AI decision log — the observer window into the enemy coach.</summary>
    public static void PostOpponent(string message)
    {
        opponentLines.Add(new Line
        {
            text = message,
            time = Time.time,
            color = new Color(1f, 0.55f, 0.45f)
        });
        if (opponentLines.Count > MAX_LINES) opponentLines.RemoveAt(0);
    }

    private static void Push(string text, Color color)
    {
        lines.Add(new Line { text = text, time = Time.time, color = color });
        if (lines.Count > MAX_LINES) lines.RemoveAt(0);
    }

    public static void Clear()
    {
        lines.Clear();
        opponentLines.Clear();
    }

    private void OnGUI()
    {
        GUIStyle s = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };

        // Your team — right side
        float y = Screen.height * 0.32f;
        for (int i = 0; i < lines.Count; i++)
        {
            float age = Time.time - lines[i].time;
            if (age > LINE_TTL) continue;
            Color c = lines[i].color;
            c.a = Mathf.Clamp01(1.2f - age / LINE_TTL);
            s.normal.textColor = c;
            GUI.Label(new Rect(Screen.width - 430, y, 420, 20), lines[i].text, s);
            y += 20;
        }

        // Opponent AI log — top center, observer style
        bool anyOpp = false;
        float oy = 14f;
        for (int i = 0; i < opponentLines.Count; i++)
        {
            float age = Time.time - opponentLines[i].time;
            if (age > OPPONENT_TTL) continue;
            if (!anyOpp)
            {
                anyOpp = true;
                GUIStyle head = new GUIStyle(s) { fontSize = 12 };
                head.normal.textColor = new Color(1f, 0.55f, 0.45f, 0.8f);
                GUI.Label(new Rect(545, oy, 300, 16), "— OPPONENT AI —", head);
                oy += 18;
            }
            Color c = opponentLines[i].color;
            c.a = Mathf.Clamp01(1.2f - age / OPPONENT_TTL);
            s.normal.textColor = c;
            GUI.Label(new Rect(545, oy, 380, 20), opponentLines[i].text, s);
            oy += 19;
        }
    }
}
