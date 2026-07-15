using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PLAYER HUD — Bottom-of-screen panels showing player info.
/// 
/// Left side: T-side players (or your team)
/// Right side: CT-side players (or enemy team)
/// Each panel shows: nickname, HP bar, weapon icon, ammo count
/// Dead players are grayed out.
/// 
/// Add to MatchManager or any persistent object.
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private float panelWidth = 160f;
    [SerializeField] private float panelHeight = 42f;
    [SerializeField] private float panelSpacing = 2f;
    [SerializeField] private float bottomMargin = 8f;

    public TeamSide playerTeamSide = TeamSide.Attacker;

    // Cached
    private List<BotController> attackerBots = new List<BotController>();
    private List<BotController> defenderBots = new List<BotController>();
    private float refreshTimer;
    private ObserverCamera observerCam;   // followed-player highlight
    private GrenadeManager grenadeManager; // team util readout above the panels

    private void Update()
    {
        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 0.5f;
            RefreshBotLists();
            if (observerCam == null) observerCam = FindObjectOfType<ObserverCamera>();
            if (grenadeManager == null) grenadeManager = FindObjectOfType<GrenadeManager>();
        }
    }

    private void RefreshBotLists()
    {
        attackerBots.Clear();
        defenderBots.Clear();
        foreach (var bot in FindObjectsByType<BotController>(FindObjectsSortMode.None))
        {
            if (bot.Profile == null) continue;
            if (bot.Side == TeamSide.Attacker) attackerBots.Add(bot);
            else defenderBots.Add(bot);
        }
        attackerBots.Sort((a, b) => string.Compare(a.name, b.name));
        defenderBots.Sort((a, b) => string.Compare(a.name, b.name));
    }

    private void OnGUI()
    {
        // T-side: bottom-left
        DrawTeamPanels(attackerBots, TeamSide.Attacker, true);
        // CT-side: bottom-right
        DrawTeamPanels(defenderBots, TeamSide.Defender, false);
    }

    private void DrawTeamPanels(List<BotController> bots, TeamSide side, bool leftSide)
    {
        if (bots.Count == 0) return;

        // Wider than the serialized default — K/D needs the room (scene keeps
        // the old 160 in panelWidth; floor it in code, stale-scene-value bug)
        float pw = Mathf.Max(panelWidth, 200f);
        float totalHeight = bots.Count * (panelHeight + panelSpacing);
        float startY = Screen.height - totalHeight - bottomMargin;
        float startX = leftSide ? 8f : (Screen.width - pw - 8f);

        // Team header
        GUIStyle header = new GUIStyle(GUI.skin.label);
        header.fontSize = 12;
        header.fontStyle = FontStyle.Bold;
        header.normal.textColor = side == TeamSide.Attacker
            ? new Color(1f, 0.5f, 0.4f, 0.9f)
            : new Color(0.4f, 0.6f, 1f, 0.9f);
        header.alignment = leftSide ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;

        string label = side == TeamSide.Attacker ? "T SIDE" : "CT SIDE";
        if (side == playerTeamSide) label += " (YOU)";
        GUI.Label(new Rect(startX, startY - 18, pw, 16), label, header);

        // Team utility level sits right above its own panel stack
        // (CS scale: full-buy team pouch is ~16 nades, so 8+ reads as FULL)
        if (grenadeManager != null)
        {
            int nades = grenadeManager.GetNadeCount(side);
            string utilLabel = nades <= 0 ? "NONE" : nades <= 3 ? "LOW" : nades <= 7 ? "OK" : "FULL";
            GUIStyle util = new GUIStyle(header);
            util.normal.textColor = nades <= 0 ? new Color(1f, 0.35f, 0.3f)
                : nades <= 3 ? new Color(1f, 0.65f, 0.25f)
                : nades <= 7 ? new Color(1f, 0.9f, 0.4f)
                : new Color(0.5f, 1f, 0.5f);
            GUI.Label(new Rect(startX, startY - 36, pw, 16), $"UTIL: {utilLabel} ({nades})", util);
        }

        for (int i = 0; i < bots.Count; i++)
        {
            var bot = bots[i];
            if (bot.Profile == null) continue;

            float y = startY + i * (panelHeight + panelSpacing);
            Rect panelRect = new Rect(startX, y, pw, panelHeight);

            DrawPlayerPanel(panelRect, bot, side);
        }
    }

    private void DrawPlayerPanel(Rect rect, BotController bot, TeamSide side)
    {
        bool alive = bot.Profile.IsAlive;
        float hp = bot.Profile.HP;
        int ammo = bot.Profile.CurrentAmmo;
        int maxAmmo = bot.Profile.CurrentWeapon.magazineSize;
        string weapon = string.IsNullOrEmpty(bot.Profile.CurrentWeapon.displayName)
            ? bot.Profile.CurrentWeapon.weaponName
            : bot.Profile.CurrentWeapon.displayName;
        string nick = bot.Profile.Data.nickname;
        bool reloading = bot.Profile.IsReloading;

        // Background
        Color bg = alive
            ? new Color(0.1f, 0.1f, 0.12f, 0.85f)
            : new Color(0.08f, 0.08f, 0.08f, 0.6f);
        GUI.DrawTexture(rect, Tex(bg));

        // Left color bar (team color); the camera's followed player gets gold
        bool followed = observerCam != null && observerCam.FollowTarget == bot.transform;
        Color teamColor = followed
            ? new Color(1f, 0.85f, 0.25f, 0.95f)
            : side == TeamSide.Attacker
                ? new Color(1f, 0.35f, 0.3f, alive ? 0.9f : 0.3f)
                : new Color(0.3f, 0.5f, 1f, alive ? 0.9f : 0.3f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), Tex(teamColor));
        if (followed)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), Tex(teamColor));
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2, rect.width, 2), Tex(teamColor));
        }

        float textAlpha = alive ? 1f : 0.4f;

        // Row 1: Nickname + weapon
        GUIStyle nickStyle = new GUIStyle(GUI.skin.label);
        nickStyle.fontSize = 12;
        nickStyle.fontStyle = FontStyle.Bold;
        nickStyle.normal.textColor = new Color(1, 1, 1, textAlpha);
        GUI.Label(new Rect(rect.x + 8, rect.y + 2, 90, 16), nick, nickStyle);

        GUIStyle wpnStyle = new GUIStyle(GUI.skin.label);
        wpnStyle.fontSize = 10;
        wpnStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, textAlpha);
        wpnStyle.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(rect.x + 90, rect.y + 2, rect.width - 98, 16), weapon, wpnStyle);

        // Row 2: HP bar + ammo
        float barX = rect.x + 8;
        float barY = rect.y + 20;
        float barW = rect.width - 58;
        float barH = 6;

        // HP bar background
        GUI.DrawTexture(new Rect(barX, barY, barW, barH), Tex(new Color(0.2f, 0.2f, 0.2f, 0.8f)));

        if (alive)
        {
            float hpPct = Mathf.Clamp01(hp / 100f);
            Color hpColor;
            if (hpPct > 0.5f)
                hpColor = side == TeamSide.Attacker
                    ? new Color(1f, 0.4f, 0.35f, 0.9f)
                    : new Color(0.35f, 0.55f, 1f, 0.9f);
            else if (hpPct > 0.25f)
                hpColor = new Color(1f, 0.8f, 0.2f, 0.9f);
            else
                hpColor = new Color(1f, 0.2f, 0.1f, 0.9f);

            GUI.DrawTexture(new Rect(barX, barY, barW * hpPct, barH), Tex(hpColor));

            // HP number
            GUIStyle hpNum = new GUIStyle(GUI.skin.label);
            hpNum.fontSize = 10;
            hpNum.normal.textColor = new Color(1, 1, 1, 0.8f);
            GUI.Label(new Rect(barX, barY + 7, 30, 14), $"{(int)hp}", hpNum);

            // K/D (match) between the bar and the ammo
            GUIStyle kd = new GUIStyle(GUI.skin.label);
            kd.fontSize = 10;
            kd.alignment = TextAnchor.MiddleRight;
            kd.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 0.9f);
            GUI.Label(new Rect(rect.x + rect.width - 106, barY + 5, 50, 14),
                $"{bot.Profile.MatchKills}/{bot.Profile.MatchDeaths}", kd);

            // Ammo
            GUIStyle ammoStyle = new GUIStyle(GUI.skin.label);
            ammoStyle.fontSize = 11;
            ammoStyle.fontStyle = FontStyle.Bold;
            ammoStyle.alignment = TextAnchor.MiddleRight;
            ammoStyle.normal.textColor = reloading
                ? new Color(1f, 0.6f, 0.2f, 0.9f)
                : (ammo <= 5 ? new Color(1f, 0.3f, 0.3f, 0.9f) : new Color(0.8f, 0.8f, 0.8f, 0.9f));

            string ammoText = reloading ? "RLD" : $"{ammo}/{maxAmmo}";
            GUI.Label(new Rect(rect.x + rect.width - 52, barY - 2, 48, 18), ammoText, ammoStyle);
        }
        else
        {
            // Dead label + final K/D
            GUIStyle deadStyle = new GUIStyle(GUI.skin.label);
            deadStyle.fontSize = 11;
            deadStyle.normal.textColor = new Color(0.5f, 0.2f, 0.2f, 0.6f);
            deadStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(rect.x + 8, barY - 2, rect.width - 16, 18),
                $"{bot.Profile.MatchKills}/{bot.Profile.MatchDeaths}  DEAD", deadStyle);
        }
    }

    // ===================== TEXTURE CACHE =====================

    private static Dictionary<Color, Texture2D> _tc = new Dictionary<Color, Texture2D>();
    private static Texture2D Tex(Color c)
    {
        Color r = new Color(Mathf.Round(c.r * 20f) / 20f, Mathf.Round(c.g * 20f) / 20f,
            Mathf.Round(c.b * 20f) / 20f, Mathf.Round(c.a * 10f) / 10f);
        if (_tc.TryGetValue(r, out Texture2D t)) return t;
        t = new Texture2D(1, 1); t.SetPixel(0, 0, r); t.Apply();
        _tc[r] = t; return t;
    }
}
