using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MID-ROUND PANEL v3
/// 
/// COMMIT now:
/// 1. Calls StrategyExecutor.CommitStagedBots() — sends staged bots to site
/// 2. Calls GrenadeManager.OnCommitCalled() — throws execute utility
/// 
/// SLOW PLAY → 10s hold → follow-up: HIT A / HIT B / SAVE
/// 
/// If no bots are staged (Rush strategy), COMMIT just pushes nearest site.
/// </summary>
public class MidRoundPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StrategyExecutor strategyExecutor;
    [SerializeField] private GrenadeManager grenadeManager;

    private bool showingPanel = false;
    private TeamSide coachSide;
    private List<BotController> coachBots;
    private List<MidRoundOption> options = new List<MidRoundOption>();
    private System.Action<MidRoundCall> onCallMade;
    private int aliveFriendly, aliveEnemy;
    private bool waitingForFollowUp = false;
    private float followUpTimer;

    private struct MidRoundOption
    {
        public MidRoundCall call;
        public string label;
        public string description;
    }

    private void Start()
    {
        if (strategyExecutor == null) strategyExecutor = FindObjectOfType<StrategyExecutor>();
        if (grenadeManager == null) grenadeManager = FindObjectOfType<GrenadeManager>();
    }

    private void Update()
    {
        if (waitingForFollowUp)
        {
            followUpTimer -= Time.deltaTime;
            if (followUpTimer <= 0f)
            { waitingForFollowUp = false; ShowFollowUpPanel(); }
        }
    }

    public void ShowPanel(TeamSide side, List<BotController> bots,
        int friendlyAlive, int enemyAlive, bool bombPlanted, string plantedSite,
        System.Action<MidRoundCall> callback)
    {
        coachSide = side; coachBots = bots;
        aliveFriendly = friendlyAlive; aliveEnemy = enemyAlive;
        onCallMade = callback;
        BuildOptions(side, friendlyAlive, enemyAlive, bombPlanted);
        showingPanel = true;
    }

    public void HidePanel() { showingPanel = false; }

    private void BuildOptions(TeamSide side, int friendly, int enemy, bool bombPlanted)
    {
        options.Clear();

        if (side == TeamSide.Attacker)
        {
            // Show EXECUTE if bots are staged, otherwise COMMIT (push nearest)
            bool hasStaged = strategyExecutor != null && strategyExecutor.HasStagedBots();

            options.Add(new MidRoundOption
            {
                call = MidRoundCall.Commit,
                label = hasStaged ? "EXECUTE" : "COMMIT",
                description = hasStaged
                    ? "Throw utility and push site — GO GO GO!"
                    : "Push into nearest site with all players"
            });

            options.Add(new MidRoundOption
            {
                call = MidRoundCall.Rotate,
                label = "ROTATE",
                description = "Redirect all players to the other site"
            });

            if (!bombPlanted)
            {
                options.Add(new MidRoundOption
                {
                    call = MidRoundCall.SlowPlay,
                    label = "SLOW PLAY",
                    description = "Hold positions, burn clock — then choose"
                });
            }

            if (friendly <= 2 && enemy >= friendly + 2)
                options.Add(new MidRoundOption { call = MidRoundCall.Save, label = "SAVE", description = "Retreat and save" });
        }
        else // CT
        {
            options.Add(new MidRoundOption { call = MidRoundCall.HoldPositions, label = "HOLD", description = "Stay in positions" });
            options.Add(new MidRoundOption { call = MidRoundCall.RotateA, label = "ROTATE A", description = "Shift toward A" });
            options.Add(new MidRoundOption { call = MidRoundCall.RotateB, label = "ROTATE B", description = "Shift toward B" });
            if (friendly > enemy)
                options.Add(new MidRoundOption { call = MidRoundCall.AggressivePush, label = "PUSH", description = "Push for picks" });
            if (friendly <= 2 && enemy >= friendly + 2 && !bombPlanted)
                options.Add(new MidRoundOption { call = MidRoundCall.CTSave, label = "SAVE", description = "Save weapons" });
        }
    }

    private void ShowFollowUpPanel()
    {
        options.Clear();
        options.Add(new MidRoundOption { call = MidRoundCall.Commit, label = "HIT A", description = "Utility + execute A site" });
        options.Add(new MidRoundOption { call = MidRoundCall.Rotate, label = "HIT B", description = "Utility + execute B site" });
        options.Add(new MidRoundOption { call = MidRoundCall.Save, label = "SAVE", description = "Time running out — save" });
        showingPanel = true;
    }

    public void ExecuteCall(MidRoundCall call, List<BotController> bots, TeamSide side)
    {
        switch (call)
        {
            case MidRoundCall.Commit:
                // >>> COMMIT: send staged bots + utility <<<
                if (strategyExecutor != null && strategyExecutor.HasStagedBots())
                {
                    StrategyZone zone = strategyExecutor.GetLastStrategyZone();

                    // Throw utility FIRST
                    if (grenadeManager != null)
                        grenadeManager.OnCommitCalled(zone, side);

                    // Then send bots (slight delay for utility to land)
                    StartCoroutine(DelayedCommit(0.8f));
                }
                else
                {
                    // No staged bots (Rush) — just push nearest site
                    string nearest = FindNearestSiteWP(bots);
                    SendBotsToWP(bots, nearest);

                    if (grenadeManager != null)
                    {
                        StrategyZone z = nearest.Contains("NodeA") ? StrategyZone.NodeA : StrategyZone.NodeB;
                        grenadeManager.OnCommitCalled(z, side);
                    }
                }
                break;

            case MidRoundCall.Rotate:
                string far = FindFarSiteWP(bots);
                SendBotsToWP(bots, far);
                if (grenadeManager != null)
                {
                    StrategyZone rz = far.Contains("NodeA") ? StrategyZone.NodeA : StrategyZone.NodeB;
                    grenadeManager.OnCommitCalled(rz, side);
                }
                break;

            case MidRoundCall.SlowPlay:
                StopBots(bots);
                waitingForFollowUp = true;
                followUpTimer = 10f;
                break;

            case MidRoundCall.Save:
            case MidRoundCall.CTSave:
                SendBotsToWP(bots, side == TeamSide.Attacker ? "WP_TopAxis" : "WP_Garden");
                break;

            case MidRoundCall.HoldPositions:
                break;

            case MidRoundCall.RotateA:
                SendBotsToWP(bots, "WP_NodeA");
                if (grenadeManager != null) grenadeManager.TriggerCTReactive(StrategyZone.NodeA);
                break;

            case MidRoundCall.RotateB:
                SendBotsToWP(bots, "WP_NodeB");
                if (grenadeManager != null) grenadeManager.TriggerCTReactive(StrategyZone.NodeB);
                break;

            case MidRoundCall.AggressivePush:
                SendBotsToWP(bots, "WP_Axis");
                break;
        }
    }

    private System.Collections.IEnumerator DelayedCommit(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (strategyExecutor != null) strategyExecutor.CommitStagedBots();
    }

    private void SendBotsToWP(List<BotController> bots, string wp)
    {
        if (string.IsNullOrEmpty(wp)) return;
        GameObject obj = GameObject.Find(wp);
        if (obj == null) return;
        foreach (var b in bots)
        {
            if (b == null || b.Profile == null || !b.Profile.IsAlive) continue;
            b.AssignWaypoints(new Transform[] { obj.transform });
            b.StartExecuting();
        }
    }

    private void StopBots(List<BotController> bots)
    {
        foreach (var b in bots)
        {
            if (b == null || b.Profile == null || !b.Profile.IsAlive) continue;
            b.GetComponent<BotStateMachine>()?.TransitionTo(BotState.Peeking);
        }
    }

    private string FindNearestSiteWP(List<BotController> bots)
    {
        Vector3 avg = Vector3.zero; int c = 0;
        foreach (var b in bots) { if (b == null || !b.Profile.IsAlive) continue; avg += b.transform.position; c++; }
        if (c == 0) return "WP_NodeA";
        avg /= c;
        GameObject a = GameObject.Find("WP_NodeA"), b2 = GameObject.Find("WP_NodeB");
        if (a == null) return "WP_NodeB"; if (b2 == null) return "WP_NodeA";
        return Vector3.Distance(avg, a.transform.position) < Vector3.Distance(avg, b2.transform.position) ? "WP_NodeA" : "WP_NodeB";
    }

    private string FindFarSiteWP(List<BotController> bots)
    { string n = FindNearestSiteWP(bots); return n == "WP_NodeA" ? "WP_NodeB" : "WP_NodeA"; }

    // ===================== GUI =====================

    private void OnGUI()
    {
        if (!showingPanel) return;

        float pw = 360, ph = 50 + options.Count * 50;
        float px = Screen.width / 2f - pw / 2f, py = Screen.height / 2f - ph / 2f;

        GUI.Box(new Rect(px - 5, py - 5, pw + 10, ph + 10), "");
        GUI.Box(new Rect(px, py, pw, ph), "");

        GUIStyle ts = new GUIStyle(GUI.skin.label);
        ts.fontSize = 18; ts.fontStyle = FontStyle.Bold;
        ts.alignment = TextAnchor.MiddleCenter; ts.normal.textColor = Color.yellow;

        string title = waitingForFollowUp ? "SLOW PLAY — NOW WHAT?" : $"MID-ROUND ({aliveFriendly}v{aliveEnemy})";
        GUI.Label(new Rect(px, py + 5, pw, 28), title, ts);

        GUIStyle bs = new GUIStyle(GUI.skin.button); bs.fontSize = 14; bs.fontStyle = FontStyle.Bold;
        GUIStyle ds = new GUIStyle(GUI.skin.label); ds.fontSize = 10; ds.normal.textColor = Color.gray; ds.alignment = TextAnchor.MiddleCenter;

        float y = py + 38;
        for (int i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            if (GUI.Button(new Rect(px + 10, y, pw - 20, 28), opt.label, bs))
            {
                showingPanel = false; waitingForFollowUp = false;
                ExecuteCall(opt.call, coachBots, coachSide);
                onCallMade?.Invoke(opt.call);
            }
            GUI.Label(new Rect(px + 10, y + 26, pw - 20, 16), opt.description, ds);
            y += 50;
        }
    }
}
