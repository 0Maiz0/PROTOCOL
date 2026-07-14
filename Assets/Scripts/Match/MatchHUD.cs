using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Match HUD:
/// 1. Nicknames above bots (LOS-based for enemies)
/// 2. Killfeed: T always red, CT always blue (not killer/victim based)
/// 3. HP bars with real HP
/// </summary>
public class MatchHUD : MonoBehaviour
{
    [Header("Nickname Settings")]
    [SerializeField] private float nicknameHeight = 2.2f;
    [SerializeField] private int nicknameFontSize = 14;

    [Header("Visibility")]
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private float visibilityRange = 50f;
    public TeamSide playerTeamSide = TeamSide.Attacker;

    [Header("Killfeed")]
    [SerializeField] private int killfeedMax = 5;
    [SerializeField] private float killfeedDuration = 4f;

    private struct KillEntry
    {
        public string killer;
        public string victim;
        public TeamSide killerSide;
        public TeamSide victimSide;
        public float timeAdded;
    }

    private List<KillEntry> killfeed = new List<KillEntry>();
    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;
        // Subscribe to new event signature with sides
        EngagementSystem.OnKillFeedEvent += OnKill;
    }

    private void OnDestroy()
    {
        EngagementSystem.OnKillFeedEvent -= OnKill;
    }

    private void OnKill(string killer, string victim, TeamSide killerSide, TeamSide victimSide)
    {
        killfeed.Insert(0, new KillEntry
        {
            killer = killer,
            victim = victim,
            killerSide = killerSide,
            victimSide = victimSide,
            timeAdded = Time.time
        });
        while (killfeed.Count > killfeedMax) killfeed.RemoveAt(killfeed.Count - 1);
    }

    private void OnGUI()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;
        DrawNicknames();
        DrawKillfeed();
    }

    // ===================== NICKNAMES (with LOS check) =====================

    private void DrawNicknames()
    {
        BotController[] allBots = FindObjectsByType<BotController>(FindObjectsSortMode.None);

        List<BotController> friendlies = new List<BotController>();
        foreach (var bot in allBots)
            if (bot.Profile != null && bot.Profile.IsAlive && bot.Side == playerTeamSide)
                friendlies.Add(bot);

        foreach (var bot in allBots)
        {
            if (bot.Profile == null || !bot.Profile.IsAlive) continue;

            if (bot.Side != playerTeamSide && !IsSpottedByFriendly(bot, friendlies))
                continue;

            Vector3 worldPos = bot.transform.position + Vector3.up * nicknameHeight;
            Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0) continue;

            float guiY = Screen.height - screenPos.y;

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = nicknameFontSize;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;

            // T = ALWAYS red, CT = ALWAYS blue
            style.normal.textColor = (bot.Side == TeamSide.Attacker)
                ? new Color(1f, 0.4f, 0.4f)
                : new Color(0.4f, 0.6f, 1f);

            GUIStyle shadow = new GUIStyle(style);
            shadow.normal.textColor = Color.black;

            string nick = bot.Profile.Data.nickname;
            float width = 120f;
            float x = screenPos.x - width / 2f;

            GUI.Label(new Rect(x + 1, guiY + 1, width, 25), nick, shadow);
            GUI.Label(new Rect(x, guiY, width, 25), nick, style);

            // HP bar
            float barWidth = 50f, barHeight = 4f;
            float barX = screenPos.x - barWidth / 2f;
            float barY = guiY + 20f;

            float hpPct = Mathf.Clamp01(bot.Profile.HP / 100f);
            Color barColor;
            if (hpPct > 0.5f)
                barColor = (bot.Side == TeamSide.Attacker)
                    ? new Color(1f, 0.3f, 0.3f, 0.8f)
                    : new Color(0.3f, 0.5f, 1f, 0.8f);
            else if (hpPct > 0.25f)
                barColor = new Color(1f, 0.8f, 0.2f, 0.8f);
            else
                barColor = new Color(1f, 0.2f, 0.1f, 0.9f);

            GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), MakeTexture(new Color(0, 0, 0, 0.6f)));
            if (hpPct > 0f)
                GUI.DrawTexture(new Rect(barX, barY, barWidth * hpPct, barHeight), MakeTexture(barColor));
        }
    }

    private bool IsSpottedByFriendly(BotController enemy, List<BotController> friendlies)
    {
        Vector3 ep = enemy.transform.position + Vector3.up * 0.5f;
        foreach (var f in friendlies)
        {
            Vector3 fp = f.transform.position + Vector3.up * 0.5f;
            float d = (ep - fp).magnitude;
            if (d > visibilityRange) continue;
            if (!Physics.Raycast(fp, (ep - fp).normalized, d, wallMask)) return true;
        }
        return false;
    }

    // ===================== KILLFEED — T=Red CT=Blue ALWAYS =====================

    private void DrawKillfeed()
    {
        killfeed.RemoveAll(k => Time.time - k.timeAdded > killfeedDuration);

        float startX = Screen.width - 340;
        float startY = 175; // below the CT comms panel flanking the score bar
        float entryH = 28f;

        for (int i = 0; i < killfeed.Count; i++)
        {
            var e = killfeed[i];
            float y = startY + i * entryH;
            float age = Time.time - e.timeAdded;
            float alpha = Mathf.Clamp01(1f - (age / killfeedDuration) * 0.5f);

            // Background
            GUI.DrawTexture(new Rect(startX - 5, y - 2, 330, entryH), MakeTexture(new Color(0, 0, 0, 0.5f * alpha)));

            // Killer name — color by THEIR team (T=red, CT=blue)
            GUIStyle ks = new GUIStyle(GUI.skin.label);
            ks.fontSize = 15; ks.fontStyle = FontStyle.Bold; ks.alignment = TextAnchor.MiddleRight;
            ks.normal.textColor = (e.killerSide == TeamSide.Attacker)
                ? new Color(1f, 0.4f, 0.4f, alpha)
                : new Color(0.4f, 0.6f, 1f, alpha);
            GUI.Label(new Rect(startX, y, 130, entryH), e.killer, ks);

            // Arrow
            GUIStyle arrow = new GUIStyle(GUI.skin.label);
            arrow.fontSize = 14; arrow.alignment = TextAnchor.MiddleCenter;
            arrow.normal.textColor = new Color(1, 1, 1, alpha);
            GUI.Label(new Rect(startX + 135, y, 40, entryH), ">>", arrow);

            // Victim name — color by THEIR team
            GUIStyle vs = new GUIStyle(GUI.skin.label);
            vs.fontSize = 15; vs.fontStyle = FontStyle.Bold; vs.alignment = TextAnchor.MiddleLeft;
            vs.normal.textColor = (e.victimSide == TeamSide.Attacker)
                ? new Color(1f, 0.4f, 0.4f, alpha)
                : new Color(0.4f, 0.6f, 1f, alpha);
            GUI.Label(new Rect(startX + 180, y, 130, entryH), e.victim, vs);
        }
    }

    // ===================== TEXTURE CACHE =====================

    private static Dictionary<Color, Texture2D> textureCache = new Dictionary<Color, Texture2D>();
    private static Texture2D MakeTexture(Color color)
    {
        color = new Color(color.r, color.g, color.b, Mathf.Round(color.a * 10f) / 10f);
        if (textureCache.TryGetValue(color, out Texture2D cached)) return cached;
        Texture2D tex = new Texture2D(1, 1); tex.SetPixel(0, 0, color); tex.Apply();
        textureCache[color] = tex; return tex;
    }
}
