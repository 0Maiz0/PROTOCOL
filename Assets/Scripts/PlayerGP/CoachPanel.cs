using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Coach Strategy Selection UI.
/// Shows during freeze time — coach picks zone then execution type.
/// Two-step picker as described in GDD.
/// 
/// Add to MatchManager. References StrategyExecutor and RoundManager.
/// </summary>
public class CoachPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StrategyExecutor strategyExecutor;

    [Header("T-Side Strategies (assign in inspector)")]
    [SerializeField] private StrategyData[] tStrategies;

    [Header("CT-Side Strategies (assign in inspector)")]
    [SerializeField] private StrategyData[] ctStrategies;

    [Header("Settings")]
    [SerializeField] private bool lurkerEnabled = false;

    // Current selection state
    private bool showingPanel = false;
    private TeamSide currentSide = TeamSide.Attacker;
    private StrategyData selectedStrategy = null;
    private System.Action<StrategyData> onStrategySelected;

    // Filtered lists for UI
    private StrategyData[] currentStrategies;

    /// <summary>
    /// Show the strategy panel. Called by RoundManager during freeze time.
    /// Callback fires when coach picks a strategy.
    /// </summary>
    public void ShowPanel(TeamSide side, System.Action<StrategyData> callback)
    {
        currentSide = side;
        onStrategySelected = callback;
        currentStrategies = (side == TeamSide.Attacker) ? tStrategies : ctStrategies;
        selectedStrategy = null;
        showingPanel = true;
    }

    public void HidePanel()
    {
        showingPanel = false;
    }

    public void SetLurker(bool enabled)
    {
        lurkerEnabled = enabled;
        if (strategyExecutor != null)
            strategyExecutor.SetLurkerEnabled(enabled);
    }

    private void OnGUI()
    {
        if (!showingPanel) return;
        if (currentStrategies == null || currentStrategies.Length == 0) return;

        // Panel background
        float panelWidth = 350;
        float panelHeight = 60 + currentStrategies.Length * 45 + 50;
        float panelX = Screen.width / 2f - panelWidth / 2f;
        float panelY = Screen.height - panelHeight - 20;

        GUI.Box(new Rect(panelX - 5, panelY - 5, panelWidth + 10, panelHeight + 10), "");
        GUI.Box(new Rect(panelX, panelY, panelWidth, panelHeight), "");

        // Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 20;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;

        string sideLabel = currentSide == TeamSide.Attacker ? "T-SIDE" : "CT-SIDE";
        GUI.Label(new Rect(panelX, panelY + 5, panelWidth, 30), $"SELECT STRATEGY ({sideLabel})", titleStyle);

        // Strategy buttons
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 14;
        buttonStyle.fontStyle = FontStyle.Bold;

        GUIStyle descStyle = new GUIStyle(GUI.skin.label);
        descStyle.fontSize = 11;
        descStyle.normal.textColor = Color.gray;
        descStyle.alignment = TextAnchor.MiddleCenter;

        float y = panelY + 40;

        for (int i = 0; i < currentStrategies.Length; i++)
        {
            var strat = currentStrategies[i];
            if (strat == null) continue;

            if (GUI.Button(new Rect(panelX + 10, y, panelWidth - 20, 30), strat.strategyName, buttonStyle))
            {
                selectedStrategy = strat;
                showingPanel = false;

                if (strategyExecutor != null)
                    strategyExecutor.SetLurkerEnabled(lurkerEnabled);

                onStrategySelected?.Invoke(selectedStrategy);

                Debug.Log($"COACH CALL: {strat.strategyName} | Lurker: {lurkerEnabled}");
            }

            // Description below button
            GUI.Label(new Rect(panelX + 10, y + 28, panelWidth - 20, 15), strat.description, descStyle);

            y += 45;
        }

        // Lurker toggle
        y += 5;
        lurkerEnabled = GUI.Toggle(new Rect(panelX + 10, y, panelWidth - 20, 25), lurkerEnabled, "  Enable Lurker");
    }
}
