using UnityEngine;

/// <summary>
/// Broadcast-style match intro: black bars slide in, "YOUR SQUAD vs OPPONENT"
/// pops, then everything fades as round 1 freeze begins. Auto-created and
/// triggered by RoundManager — no scene setup.
/// </summary>
public class MatchIntro : MonoBehaviour
{
    private const float DURATION = 4.2f;

    private string teamA = "YOUR SQUAD";
    private string teamB = "OPPONENT";
    private float startTime = -100f;
    private Texture2D blackTex;

    private static readonly Color Accent = new Color(0.25f, 0.85f, 1f);

    public void Show(string a, string b)
    {
        teamA = string.IsNullOrEmpty(a) ? "YOUR SQUAD" : a;
        teamB = string.IsNullOrEmpty(b) ? "OPPONENT" : b;
        startTime = Time.time;
    }

    private void Awake()
    {
        blackTex = new Texture2D(1, 1);
        blackTex.SetPixel(0, 0, Color.black);
        blackTex.Apply();
    }

    private static float Ease(float x) => 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f);

    private void OnGUI()
    {
        float el = Time.time - startTime;
        if (el < 0f || el > DURATION) return;

        float sw = Screen.width, sh = Screen.height;
        float fade = el > DURATION - 1f ? 1f - (el - (DURATION - 1f)) : 1f;
        float slide = Ease(el / 0.7f);

        // Cinematic bars
        GUI.color = new Color(0, 0, 0, 0.85f * fade);
        GUI.DrawTexture(new Rect(0, -sh * 0.22f * (1f - slide), sw, sh * 0.22f), blackTex);
        GUI.DrawTexture(new Rect(0, sh - sh * 0.22f * slide, sw, sh * 0.22f), blackTex);
        // Center strip
        GUI.color = new Color(0, 0, 0, 0.6f * fade * slide);
        GUI.DrawTexture(new Rect(0, sh * 0.36f, sw, sh * 0.28f), blackTex);
        GUI.color = Color.white;

        // Team names slide from the sides
        float namesIn = Ease((el - 0.5f) / 0.7f);
        GUIStyle team = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(sh * 0.055f),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        team.normal.textColor = new Color(1f, 1f, 1f, fade * namesIn);
        GUI.Label(new Rect(Mathf.Lerp(-sw * 0.5f, 0f, namesIn), sh * 0.40f, sw * 0.44f, sh * 0.1f), teamA, team);
        GUI.Label(new Rect(Mathf.Lerp(sw, sw * 0.56f, namesIn), sh * 0.40f, sw * 0.44f, sh * 0.1f), teamB, team);

        // VS pop
        float vsIn = Ease((el - 1.0f) / 0.4f);
        GUIStyle vs = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(sh * 0.07f * (0.5f + 0.5f * vsIn)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        vs.normal.textColor = new Color(Accent.r, Accent.g, Accent.b, fade * vsIn);
        GUI.Label(new Rect(0, sh * 0.40f, sw, sh * 0.1f), "VS", vs);

        // Subtitle
        GUIStyle sub = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(sh * 0.02f),
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        sub.normal.textColor = new Color(0.8f, 0.85f, 0.9f, fade * Ease((el - 1.4f) / 0.6f));
        GUI.Label(new Rect(0, sh * 0.53f, sw, 26), "ONLINE QUALIFIER — BO1 — FIRST TO 13", sub);
    }
}
