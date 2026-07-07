using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Coach Strategy Selection UI.
/// Shows during freeze time — coach picks the BUY TIER, then zone/execution type.
/// Buy tiers show affordability from the team pool (economy v3: per-player
/// loadouts are bought from the pool after the call).
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
    private BuyType selectedBuy = BuyType.Eco;
    private TeamEconomy teamEconomy;
    private System.Action<StrategyData, BuyType> onStrategySelected;

    private static readonly BuyType[] BuyTiers = { BuyType.Full, BuyType.Force, BuyType.Half, BuyType.Eco };

    // Filtered lists for UI
    private StrategyData[] currentStrategies;

    /// <summary>
    /// Show the strategy panel. Called by RoundManager during freeze time.
    /// Callback fires when coach picks a strategy (buy tier rides along).
    /// </summary>
    public void ShowPanel(TeamSide side, TeamEconomy economy, System.Action<StrategyData, BuyType> callback)
    {
        currentSide = side;
        teamEconomy = economy;
        onStrategySelected = callback;
        currentStrategies = (side == TeamSide.Attacker) ? tStrategies : ctStrategies;
        selectedStrategy = null;
        selectedBuy = economy != null ? economy.recommendedBuy : BuyType.Eco;
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
        float panelHeight = 60 + 58 + currentStrategies.Length * 45 + 50;
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
        GUI.Label(new Rect(panelX, panelY + 5, panelWidth, 30), $"SELECT BUY + STRATEGY ({sideLabel})", titleStyle);

        GUIStyle descStyle = new GUIStyle(GUI.skin.label);
        descStyle.fontSize = 11;
        descStyle.normal.textColor = Color.gray;
        descStyle.alignment = TextAnchor.MiddleCenter;

        // ===== Buy tier row =====
        float y = panelY + 36;
        if (teamEconomy != null)
        {
            GUI.Label(new Rect(panelX + 10, y, panelWidth - 20, 16),
                $"Team bank: ${teamEconomy.credits:N0}  |  suggested: {teamEconomy.recommendedBuy}", descStyle);
            y += 18;

            GUIStyle buyStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            float bw = (panelWidth - 20 - 12) / 4f;
            for (int i = 0; i < BuyTiers.Length; i++)
            {
                BuyType tier = BuyTiers[i];
                bool affordable = tier == BuyType.Eco || teamEconomy.CanAffordTier(tier);
                GUI.enabled = affordable;
                string label = (selectedBuy == tier ? ">> " : "") + tier.ToString().ToUpper();
                if (GUI.Button(new Rect(panelX + 10 + i * (bw + 4), y, bw, 28), label, buyStyle))
                    selectedBuy = tier;
                GUI.enabled = true;
            }
            y += 34;
        }

        // ===== Strategy buttons =====
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 14;
        buttonStyle.fontStyle = FontStyle.Bold;

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

                onStrategySelected?.Invoke(selectedStrategy, selectedBuy);

                Debug.Log($"COACH CALL: {strat.strategyName} | Buy: {selectedBuy} | Lurker: {lurkerEnabled}");
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
