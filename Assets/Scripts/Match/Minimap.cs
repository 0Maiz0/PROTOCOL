using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// MINIMAP / RADAR — Top-down tactical overview in corner of screen.
/// 
/// Shows:
/// - Friendly bots (always visible, team color dots with direction arrows)
/// - Enemy bots (only when spotted by friendlies — fog of war)
/// - Last known enemy positions (fading yellow "?" marker)
/// - Bomb sites (labeled A/B markers)
/// - Bomb indicator (blinking red when planted)
/// - Grenade effect zones (smoke clouds, molly fire)
/// 
/// SETUP:
/// 1. Add to MatchManager or any persistent object
/// 2. Set mapCenter + mapSize to match your Nexus map world bounds
/// 3. playerTeamSide is auto-updated by RoundManager
/// </summary>
public class Minimap : MonoBehaviour
{
    [Header("Map Bounds")]
    [SerializeField] private Vector3 mapCenter = new Vector3(40f, 0f, 25f);
    [SerializeField] private Vector2 mapSize = new Vector2(80f, 60f);

    [Header("Radar Display")]
    [SerializeField] private float radarSize = 200f;
    [SerializeField] private float radarMargin = 10f;
    [SerializeField] private float dotSize = 8f;

    [Header("Visibility")]
    [SerializeField] private LayerMask wallMask;
    [SerializeField] private float spotRange = 50f;

    [Header("References")]
    [SerializeField] private BombManager bombManager;

    public TeamSide playerTeamSide = TeamSide.Attacker;

    // Bomb blink
    private float bombBlinkTimer;
    private bool bombBlinkOn = true;

    // Spotted enemy tracking
    private Dictionary<int, Vector3> lastSpottedPos = new Dictionary<int, Vector3>();
    private Dictionary<int, float> lastSpottedTime = new Dictionary<int, float>();
    private const float SPOT_FADE = 3f;

    // Site refs
    private BombSite siteA, siteB;

    private void Start()
    {
        if (bombManager == null) bombManager = FindObjectOfType<BombManager>();
        BombSite[] sites = FindObjectsByType<BombSite>(FindObjectsSortMode.None);
        foreach (var s in sites)
        {
            if (s.siteName == "A") siteA = s;
            else if (s.siteName == "B") siteB = s;
        }
    }

    private void Update()
    {
        if (bombManager != null && bombManager.IsPlanted)
        {
            bombBlinkTimer += Time.deltaTime;
            if (bombBlinkTimer > 0.4f) { bombBlinkTimer = 0f; bombBlinkOn = !bombBlinkOn; }
        }
    }

    private void OnGUI()
    {
        float rx = Screen.width - radarSize - radarMargin;
        float ry = Screen.height - radarSize - radarMargin;
        Rect radar = new Rect(rx, ry, radarSize, radarSize);

        // Background
        GUI.DrawTexture(radar, SolidTex(new Color(0.05f, 0.08f, 0.05f, 0.85f)));
        DrawBorder(radar, new Color(0.3f, 0.4f, 0.3f, 0.9f));

        // Title
        GUIStyle ts = new GUIStyle(GUI.skin.label);
        ts.fontSize = 11; ts.fontStyle = FontStyle.Bold;
        ts.normal.textColor = new Color(0.6f, 0.8f, 0.6f, 0.9f);
        ts.alignment = TextAnchor.UpperCenter;
        GUI.Label(new Rect(rx, ry + 2, radarSize, 16), "RADAR", ts);

        DrawSites(radar);
        DrawGrenadeZones(radar);
        DrawBots(radar);
        DrawBomb(radar);
    }

    // ===================== COORDINATE CONVERSION =====================

    private Vector2 W2R(Vector3 w, Rect r)
    {
        float pad = 15f, usable = radarSize - pad * 2;
        float nx = Mathf.Clamp01((w.x - (mapCenter.x - mapSize.x / 2f)) / mapSize.x);
        float nz = Mathf.Clamp01((w.z - (mapCenter.z - mapSize.y / 2f)) / mapSize.y);
        return new Vector2(r.x + pad + nx * usable, r.y + pad + (1f - nz) * usable);
    }

    // ===================== SITES =====================

    private void DrawSites(Rect r)
    {
        GUIStyle s = new GUIStyle(GUI.skin.label);
        s.fontSize = 12; s.fontStyle = FontStyle.Bold;
        s.normal.textColor = new Color(0.4f, 0.9f, 0.4f, 0.6f);
        s.alignment = TextAnchor.MiddleCenter;

        if (siteA != null)
        {
            Vector2 p = W2R(siteA.transform.position, r);
            GUI.DrawTexture(new Rect(p.x - 6, p.y - 6, 12, 12), SolidTex(new Color(0.2f, 0.5f, 0.2f, 0.3f)));
            GUI.Label(new Rect(p.x - 10, p.y - 18, 20, 16), "A", s);
        }
        if (siteB != null)
        {
            Vector2 p = W2R(siteB.transform.position, r);
            GUI.DrawTexture(new Rect(p.x - 6, p.y - 6, 12, 12), SolidTex(new Color(0.2f, 0.5f, 0.2f, 0.3f)));
            GUI.Label(new Rect(p.x - 10, p.y - 18, 20, 16), "B", s);
        }
    }

    // ===================== BOTS =====================

    private void DrawBots(Rect radar)
    {
        BotController[] all = FindObjectsByType<BotController>(FindObjectsSortMode.None);

        List<BotController> friendlies = new List<BotController>();
        foreach (var b in all)
            if (b.Profile != null && b.Profile.IsAlive && b.Side == playerTeamSide)
                friendlies.Add(b);

        foreach (var bot in all)
        {
            if (bot.Profile == null) continue;
            bool friendly = bot.Side == playerTeamSide;
            int id = bot.GetInstanceID();

            if (!bot.Profile.IsAlive)
            {
                if (friendly)
                {
                    Vector2 p = W2R(bot.transform.position, radar);
                    GUIStyle xs = new GUIStyle(GUI.skin.label);
                    xs.fontSize = 9; xs.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                    xs.alignment = TextAnchor.MiddleCenter;
                    GUI.Label(new Rect(p.x - 5, p.y - 6, 10, 12), "x", xs);
                }
                continue;
            }

            if (friendly)
            {
                Vector2 p = W2R(bot.transform.position, radar);
                Color c = playerTeamSide == TeamSide.Attacker
                    ? new Color(1f, 0.4f, 0.3f, 0.95f)
                    : new Color(0.3f, 0.5f, 1f, 0.95f);
                Dot(p, c, dotSize);
                Arrow(p, bot.transform.forward, c);
            }
            else
            {
                bool spotted = IsSpotted(bot, friendlies);
                if (spotted)
                {
                    lastSpottedPos[id] = bot.transform.position;
                    lastSpottedTime[id] = Time.time;

                    Vector2 p = W2R(bot.transform.position, radar);
                    Color ec = playerTeamSide == TeamSide.Attacker
                        ? new Color(0.3f, 0.5f, 1f, 0.9f)
                        : new Color(1f, 0.4f, 0.3f, 0.9f);
                    Dot(p, ec, dotSize);
                }
                else if (lastSpottedPos.ContainsKey(id))
                {
                    float age = Time.time - lastSpottedTime[id];
                    if (age < SPOT_FADE)
                    {
                        float a = 1f - age / SPOT_FADE;
                        Vector2 p = W2R(lastSpottedPos[id], radar);
                        Dot(p, new Color(0.8f, 0.8f, 0.2f, a * 0.5f), dotSize * 0.7f);

                        GUIStyle qs = new GUIStyle(GUI.skin.label);
                        qs.fontSize = 9; qs.normal.textColor = new Color(1f, 1f, 0.3f, a * 0.6f);
                        qs.alignment = TextAnchor.MiddleCenter;
                        GUI.Label(new Rect(p.x - 5, p.y - 13, 10, 10), "?", qs);
                    }
                }
            }
        }
    }

    // ===================== BOMB =====================

    private void DrawBomb(Rect radar)
    {
        if (bombManager == null || !bombManager.IsPlanted) return;
        BombSite site = bombManager.PlantedSiteName == "A" ? siteA : siteB;
        if (site == null) return;

        if (!bombBlinkOn) return;

        Vector2 p = W2R(site.transform.position, radar);
        Dot(p, new Color(1f, 0.1f, 0.1f, 0.9f), 10f);

        GUIStyle bs = new GUIStyle(GUI.skin.label);
        bs.fontSize = 10; bs.fontStyle = FontStyle.Bold;
        bs.normal.textColor = new Color(1f, 0.2f, 0.2f, 1f);
        bs.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(p.x - 8, p.y - 6, 16, 12), "B", bs);
    }

    // ===================== GRENADE ZONES =====================

    private void DrawGrenadeZones(Rect radar)
    {
        GrenadeEffect[] effects = FindObjectsByType<GrenadeEffect>(FindObjectsSortMode.None);
        foreach (var fx in effects)
        {
            if (!fx.IsActive) continue;
            Vector2 p = W2R(fx.transform.position, radar);

            Color col; float sz;
            switch (fx.Type)
            {
                case GrenadeType.Smoke:
                    col = new Color(0.7f, 0.7f, 0.7f, 0.4f);
                    sz = Mathf.Max((fx.Radius / mapSize.x) * (radarSize - 30) * 2f, 14f);
                    break;
                case GrenadeType.Molotov:
                    col = new Color(1f, 0.4f, 0.1f, 0.4f);
                    sz = Mathf.Max((fx.Radius / mapSize.x) * (radarSize - 30) * 2f, 10f);
                    break;
                default: continue;
            }
            GUI.DrawTexture(new Rect(p.x - sz / 2, p.y - sz / 2, sz, sz), SolidTex(col));
        }
    }

    // ===================== SPOTTING =====================

    private bool IsSpotted(BotController enemy, List<BotController> friendlies)
    {
        Vector3 ep = enemy.transform.position + Vector3.up * 0.5f;
        foreach (var f in friendlies)
        {
            Vector3 fp = f.transform.position + Vector3.up * 0.5f;
            float d = Vector3.Distance(fp, ep);
            if (d > spotRange) continue;
            if (!Physics.Raycast(fp, (ep - fp).normalized, d, wallMask))
                return true;
        }
        return false;
    }

    // ===================== DRAW HELPERS =====================

    private void Dot(Vector2 c, Color col, float size)
    {
        GUI.DrawTexture(new Rect(c.x - size / 2, c.y - size / 2, size, size), SolidTex(col));
    }

    private void Arrow(Vector2 c, Vector3 fwd, Color col)
    {
        Vector2 d = new Vector2(fwd.x, -fwd.z).normalized * (dotSize + 3);
        Matrix4x4 saved = GUI.matrix;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(angle, c);
        GUI.DrawTexture(new Rect(c.x, c.y - 0.5f, d.magnitude, 1.5f), SolidTex(col));
        GUI.matrix = saved;
    }

    private void DrawBorder(Rect r, Color c)
    {
        Texture2D t = SolidTex(c);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), t);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), t);
        GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), t);
        GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), t);
    }

    public void ClearSpotData()
    {
        lastSpottedPos.Clear();
        lastSpottedTime.Clear();
        bombBlinkOn = true; bombBlinkTimer = 0f;
    }

    // ===================== TEXTURE CACHE =====================

    private static Dictionary<Color, Texture2D> _texCache = new Dictionary<Color, Texture2D>();

    private static Texture2D SolidTex(Color c)
    {
        Color r = new Color(
            Mathf.Round(c.r * 20f) / 20f, Mathf.Round(c.g * 20f) / 20f,
            Mathf.Round(c.b * 20f) / 20f, Mathf.Round(c.a * 10f) / 10f);
        if (_texCache.TryGetValue(r, out Texture2D cached)) return cached;
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, r); tex.Apply();
        _texCache[r] = tex;
        return tex;
    }
}
