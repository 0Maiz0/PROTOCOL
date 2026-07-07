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
    private static TeamSide playerSide = TeamSide.Attacker;

    private const int MAX_LINES = 7;
    private const float LINE_TTL = 6f;

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

    private static void Push(string text, Color color)
    {
        lines.Add(new Line { text = text, time = Time.time, color = color });
        if (lines.Count > MAX_LINES) lines.RemoveAt(0);
    }

    public static void Clear() => lines.Clear();

    private void OnGUI()
    {
        float y = Screen.height * 0.32f;
        GUIStyle s = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
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
    }
}
