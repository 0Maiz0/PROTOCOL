using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// STRATEGY ASSIGNMENT PANEL
/// 
/// Shows during freeze time AFTER strategy is selected.
/// Displays 5 slots with:
/// - Player name + role
/// - Path description (where this slot goes)
/// - Swap buttons to reorder who goes where
/// 
/// Example: "Slot 1: ACorr → Overlook → NodeA" assigned to "ropz (AWPer)"
/// Coach can swap ropz to Slot 3 which goes through Axis → Catwalk if they want.
/// 
/// Add to MatchManager alongside CoachPanel.
/// </summary>
public class StrategyAssignment : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StrategyExecutor strategyExecutor;

    private bool showingPanel = false;
    private StrategyData currentStrategy;
    private List<BotController> currentBots;
    private int[] slotAssignment = { 0, 1, 2, 3, 4 }; // Which bot goes to which path
    private int selectedSlot = -1;
    private System.Action onConfirmed;

    // ===================== PUBLIC API =====================

    /// <summary>
    /// Show the assignment panel after strategy is selected.
    /// Bots are the team's bot list. Strategy defines the paths.
    /// </summary>
    public void ShowPanel(StrategyData strategy, List<BotController> bots, System.Action onConfirm)
    {
        currentStrategy = strategy;
        currentBots = bots;
        onConfirmed = onConfirm;

        // Default assignment: 0→0, 1→1, etc.
        // But smart defaults: put Entry in slot 0, AWPer in highest slot, etc.
        AutoAssignByRole(bots);

        showingPanel = true;
        selectedSlot = -1;
    }

    public void HidePanel() { showingPanel = false; }

    /// <summary>
    /// Apply the assignment — reorder bots in the list so StrategyExecutor
    /// gives the right path to the right player.
    /// </summary>
    public void ApplyAssignment(List<BotController> bots)
    {
        if (currentStrategy == null) return;

        // Reorder the bot list so bot[i] matches slotAssignment[i]
        List<BotController> original = new List<BotController>(bots);
        for (int i = 0; i < 5 && i < bots.Count; i++)
        {
            int assignedBot = slotAssignment[i];
            if (assignedBot < original.Count)
                bots[i] = original[assignedBot];
        }
    }

    // ===================== AUTO ASSIGNMENT =====================

    /// <summary>
    /// Automatically assign players to slots based on their role.
    /// Entry → first slot (leads the push). AWPer → slot that has long sightlines.
    /// Support → middle slots. Lurker → lurker slot if available.
    /// </summary>
    private void AutoAssignByRole(List<BotController> bots)
    {
        // Reset
        for (int i = 0; i < 5; i++) slotAssignment[i] = i;

        if (bots.Count < 5) return;

        // Find players by role
        int entryIdx = -1, awpIdx = -1, lurkerIdx = -1;
        for (int i = 0; i < bots.Count && i < 5; i++)
        {
            if (bots[i].Profile == null) continue;
            switch (bots[i].Profile.AssignedRole)
            {
                case Role.Entry: entryIdx = i; break;
                case Role.AWPer: awpIdx = i; break;
                case Role.Lurker: lurkerIdx = i; break;
            }
        }

        // Entry → slot 0 (leads the charge)
        if (entryIdx >= 0 && entryIdx != 0)
            SwapSlot(0, FindSlotWithBot(entryIdx));

        // AWPer → slot 4 (usually the lurker/back position, or overlook)
        if (awpIdx >= 0)
        {
            int bestSlot = FindBestAWPSlot();
            if (bestSlot >= 0)
                SwapSlot(bestSlot, FindSlotWithBot(awpIdx));
        }

        // Lurker → lurker slot if strategy supports it
        if (lurkerIdx >= 0 && currentStrategy != null && currentStrategy.supportsLurker)
            SwapSlot(currentStrategy.lurkerBotIndex, FindSlotWithBot(lurkerIdx));
    }

    private int FindBestAWPSlot()
    {
        // AWP prefers slots that go to elevated/long-range positions
        if (currentStrategy == null) return 4;

        string[][] paths = currentStrategy.GetAllPaths();
        for (int i = 0; i < paths.Length; i++)
        {
            if (paths[i] == null) continue;
            foreach (string wp in paths[i])
            {
                // Overlook, Garden, Bazaar = good AWP spots
                if (wp.Contains("Overlook") || wp.Contains("Garden") || wp.Contains("Bazaar"))
                    return i;
            }
        }
        return 4; // Default: last slot
    }

    private int FindSlotWithBot(int botIndex)
    {
        for (int i = 0; i < 5; i++)
            if (slotAssignment[i] == botIndex) return i;
        return botIndex;
    }

    private void SwapSlot(int slotA, int slotB)
    {
        if (slotA < 0 || slotA >= 5 || slotB < 0 || slotB >= 5) return;
        int temp = slotAssignment[slotA];
        slotAssignment[slotA] = slotAssignment[slotB];
        slotAssignment[slotB] = temp;
    }

    // ===================== PATH DESCRIPTION =====================

    private string GetPathDescription(int slotIndex)
    {
        if (currentStrategy == null) return "—";
        string[][] paths = currentStrategy.GetAllPaths();
        if (slotIndex >= paths.Length || paths[slotIndex] == null) return "—";

        // Join waypoint names, remove "WP_" prefix for readability
        List<string> clean = new List<string>();
        foreach (string wp in paths[slotIndex])
        {
            string name = wp.Replace("WP_", "").Trim();
            clean.Add(name);
        }
        return string.Join(" → ", clean);
    }

    // ===================== GUI =====================

    private void OnGUI()
    {
        if (!showingPanel || currentStrategy == null || currentBots == null) return;

        float panelW = 420, panelH = 310;
        float px = Screen.width / 2f - panelW / 2f;
        float py = Screen.height / 2f - panelH / 2f + 120; // Below strategy panel

        GUI.Box(new Rect(px - 5, py - 5, panelW + 10, panelH + 10), "");
        GUI.Box(new Rect(px, py, panelW, panelH), "");

        // Title
        GUIStyle title = new GUIStyle(GUI.skin.label);
        title.fontSize = 16; title.fontStyle = FontStyle.Bold;
        title.alignment = TextAnchor.MiddleCenter;
        title.normal.textColor = Color.cyan;
        GUI.Label(new Rect(px, py + 5, panelW, 24), $"PLAYER ASSIGNMENT — {currentStrategy.strategyName}", title);

        // Slot rows
        GUIStyle nameStyle = new GUIStyle(GUI.skin.label);
        nameStyle.fontSize = 13; nameStyle.fontStyle = FontStyle.Bold;

        GUIStyle roleStyle = new GUIStyle(GUI.skin.label);
        roleStyle.fontSize = 11; roleStyle.normal.textColor = Color.gray;

        GUIStyle pathStyle = new GUIStyle(GUI.skin.label);
        pathStyle.fontSize = 11; pathStyle.normal.textColor = new Color(0.6f, 0.9f, 0.6f);

        GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
        btnStyle.fontSize = 11;

        float rowY = py + 35;
        for (int slot = 0; slot < 5 && slot < currentBots.Count; slot++)
        {
            int botIdx = slotAssignment[slot];
            if (botIdx >= currentBots.Count) continue;

            var bot = currentBots[botIdx];
            if (bot.Profile == null) continue;

            bool isSelected = selectedSlot == slot;
            Color rowBg = isSelected ? new Color(0.2f, 0.3f, 0.4f, 0.7f) : new Color(0.15f, 0.15f, 0.18f, 0.6f);
            GUI.DrawTexture(new Rect(px + 5, rowY, panelW - 10, 44), MakeTex(rowBg));

            // Slot number
            nameStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(px + 10, rowY + 2, 20, 20), $"{slot + 1}.", nameStyle);

            // Player name + role
            nameStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(px + 30, rowY + 2, 120, 20), bot.Profile.Data.nickname, nameStyle);
            GUI.Label(new Rect(px + 30, rowY + 22, 120, 16), bot.Profile.AssignedRole.ToString(), roleStyle);

            // Path description
            GUI.Label(new Rect(px + 155, rowY + 2, 190, 20), GetPathDescription(slot), pathStyle);

            // Swap button
            if (GUI.Button(new Rect(px + panelW - 70, rowY + 8, 55, 26),
                isSelected ? "CANCEL" : "SWAP", btnStyle))
            {
                if (isSelected)
                {
                    selectedSlot = -1;
                }
                else if (selectedSlot >= 0)
                {
                    // Swap selected with this slot
                    SwapSlot(selectedSlot, slot);
                    selectedSlot = -1;
                }
                else
                {
                    selectedSlot = slot;
                }
            }

            rowY += 48;
        }

        // Confirm button
        GUIStyle confirmStyle = new GUIStyle(GUI.skin.button);
        confirmStyle.fontSize = 16; confirmStyle.fontStyle = FontStyle.Bold;

        if (GUI.Button(new Rect(px + panelW / 2 - 80, rowY + 10, 160, 35), "CONFIRM LINEUP", confirmStyle))
        {
            ApplyAssignment(currentBots);
            showingPanel = false;
            onConfirmed?.Invoke();
        }
    }

    private static Dictionary<Color, Texture2D> _tc = new Dictionary<Color, Texture2D>();
    private static Texture2D MakeTex(Color c)
    {
        c = new Color(Mathf.Round(c.r * 10f) / 10f, Mathf.Round(c.g * 10f) / 10f,
            Mathf.Round(c.b * 10f) / 10f, Mathf.Round(c.a * 10f) / 10f);
        if (_tc.TryGetValue(c, out Texture2D t)) return t;
        t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); _tc[c] = t; return t;
    }
}
