using UnityEngine;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// MATCH TELEMETRY — the post-playtest analysis backlog.
///
/// Everything that happens in a match (buys, calls from BOTH teams, kills
/// with weapon/headshot/flash context, utility, bomb events, round results)
/// is written to MatchLogs/match_&lt;timestamp&gt;.log next to Assets/, plus a
/// latest.log copy. Workflow: Maiz plays, Claude reads latest.log and
/// diagnoses the round like a demo review.
/// </summary>
public static class MatchLogger
{
    private static readonly List<string> buffer = new List<string>();
    private static string filePath;
    private static string dirPath;
    private static int round;
    private static float roundStart;
    private static bool active;

    public static void StartMatch(string opponent)
    {
        try
        {
            dirPath = Path.Combine(Application.dataPath, "..", "MatchLogs");
            Directory.CreateDirectory(dirPath);
            filePath = Path.Combine(dirPath, $"match_{System.DateTime.Now:yyyyMMdd_HHmmss}.log");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"MatchLogger: can't create log dir — {e.Message}");
            filePath = null;
        }
        buffer.Clear();
        round = 0;
        roundStart = Time.time;
        active = true;
        Log($"=== MATCH START vs {opponent} ===");
    }

    public static void StartRound(int r, string scoreline)
    {
        round = r;
        roundStart = Time.time;
        Log($"--- ROUND {r} START | {scoreline} ---");
    }

    public static void Log(string message)
    {
        if (!active) return;
        buffer.Add($"[R{round:00} {Time.time - roundStart,5:0.0}s] {message}");
    }

    /// <summary>Write buffered lines to disk — called at round end and match end.</summary>
    public static void Flush()
    {
        if (!active || buffer.Count == 0 || string.IsNullOrEmpty(filePath)) return;
        try
        {
            File.AppendAllLines(filePath, buffer);
            File.Copy(filePath, Path.Combine(dirPath, "latest.log"), true);
            buffer.Clear();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"MatchLogger: write failed — {e.Message}");
        }
    }

    public static void EndMatch(string result)
    {
        Log($"=== MATCH END: {result} ===");
        Flush();
        active = false;
    }
}
