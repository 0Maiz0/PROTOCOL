using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MAIN MENU — animated IMGUI start screen (same UI stack as the rest of the game).
///
/// Dark gradient backdrop, drifting particles, pulsing PROTOCOL title sliding in,
/// staggered buttons: CONTINUE CAREER (when a save exists) / NEW CAREER / QUIT.
///
/// SETUP: empty scene "MainMenu" (keep the default camera), one GameObject with
/// this component, add the scene to Build Settings (first in the list).
/// </summary>
public class MainMenu : MonoBehaviour
{
    private float t;
    private Texture2D gradientTex;
    private Texture2D whiteTex;
    private Particle[] particles;
    private bool confirmNewCareer;

    private struct Particle
    {
        public float x, y, speed, size, alpha;
    }

    private static readonly Color Accent = new Color(0.25f, 0.85f, 1f);
    private static readonly Color AccentWarm = new Color(1f, 0.6f, 0.2f);

    private void Awake()
    {
        // Vertical gradient: deep navy → near black
        gradientTex = new Texture2D(1, 128);
        for (int i = 0; i < 128; i++)
        {
            float f = i / 127f;
            gradientTex.SetPixel(0, i, Color.Lerp(new Color(0.02f, 0.03f, 0.06f), new Color(0.07f, 0.1f, 0.18f), f));
        }
        gradientTex.Apply();

        whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();

        var rng = new System.Random(1337);
        particles = new Particle[42];
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i] = new Particle
            {
                x = (float)rng.NextDouble(),
                y = (float)rng.NextDouble(),
                speed = 0.01f + (float)rng.NextDouble() * 0.03f,
                size = 1.5f + (float)rng.NextDouble() * 3f,
                alpha = 0.06f + (float)rng.NextDouble() * 0.2f
            };
        }
    }

    private void Update() => t += Time.deltaTime;

    private static float Ease(float x) => 1f - Mathf.Pow(1f - Mathf.Clamp01(x), 3f);

    private void OnGUI()
    {
        float sw = Screen.width, sh = Screen.height;

        // Backdrop + drifting particles
        GUI.DrawTexture(new Rect(0, 0, sw, sh), gradientTex, ScaleMode.StretchToFill);
        for (int i = 0; i < particles.Length; i++)
        {
            var p = particles[i];
            float py = Mathf.Repeat(p.y - t * p.speed, 1f);
            float px = p.x + Mathf.Sin(t * 0.3f + i) * 0.008f;
            GUI.color = new Color(Accent.r, Accent.g, Accent.b, p.alpha * Mathf.Clamp01(t));
            GUI.DrawTexture(new Rect(px * sw, py * sh, p.size, p.size), whiteTex);
        }
        GUI.color = Color.white;

        // Accent line sweeping under the title
        float lineIn = Ease((t - 0.6f) / 0.8f);
        GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.85f * lineIn);
        GUI.DrawTexture(new Rect(sw / 2f - 260f * lineIn, sh * 0.34f, 520f * lineIn, 2f), whiteTex);
        GUI.color = Color.white;

        // Title slides down + pulses between accent and white
        float titleIn = Ease(t / 1.0f);
        float pulse = (Mathf.Sin(t * 1.6f) + 1f) * 0.5f;
        GUIStyle title = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(sh * 0.11f),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        float titleY = Mathf.Lerp(-120f, sh * 0.18f, titleIn);
        title.normal.textColor = new Color(0f, 0f, 0f, 0.6f * titleIn);
        GUI.Label(new Rect(4, titleY + 4, sw, sh * 0.14f), "P R O T O C O L", title); // shadow
        title.normal.textColor = Color.Lerp(Accent, Color.white, pulse * 0.6f) * new Color(1, 1, 1, titleIn);
        GUI.Label(new Rect(0, titleY, sw, sh * 0.14f), "P R O T O C O L", title);

        // Subtitle fade
        GUIStyle sub = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(sh * 0.022f),
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        sub.normal.textColor = new Color(0.8f, 0.85f, 0.9f, Ease((t - 0.8f) / 0.8f));
        GUI.Label(new Rect(0, sh * 0.35f, sw, 30), "ESPORTS TEAM SIMULATOR — YOU BUILD IT, THEY PLAY IT", sub);

        // Buttons stagger in from the left
        bool hasSave = SaveSystem.SaveExists();
        float bw = 340f, bh = 54f, bx = sw / 2f - bw / 2f, by = sh * 0.48f;
        int slot = 0;

        if (hasSave && MenuButton("CONTINUE CAREER", bx, by + slot * 70f, bw, bh, 1.2f + slot * 0.15f, Accent))
        {
            SceneManager.LoadScene(Constants.HOME_SCENE_NAME);
        }
        if (hasSave) slot++;

        string newLabel = confirmNewCareer ? "CONFIRM — WIPE OLD SAVE" : "NEW CAREER";
        if (MenuButton(newLabel, bx, by + slot * 70f, bw, bh, 1.2f + slot * 0.15f,
                confirmNewCareer ? AccentWarm : Accent))
        {
            if (!hasSave || confirmNewCareer)
            {
                SaveSystem.Delete();
                SceneManager.LoadScene(Constants.HOME_SCENE_NAME);
            }
            else confirmNewCareer = true;
        }
        slot++;

        if (MenuButton("QUIT", bx, by + slot * 70f, bw, bh, 1.2f + slot * 0.15f, new Color(0.7f, 0.7f, 0.75f)))
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // Footer
        GUIStyle foot = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        foot.normal.textColor = new Color(1f, 1f, 1f, 0.25f * Mathf.Clamp01(t - 2f));
        GUI.Label(new Rect(0, sh - 34, sw, 20), "3 tactical timeouts. The rest is on the team you built.", foot);
    }

    private bool MenuButton(string label, float x, float y, float w, float h, float delay, Color accent)
    {
        float slide = Ease((t - delay) / 0.6f);
        if (slide <= 0f) return false;

        float bx = Mathf.Lerp(-w - 40f, x, slide);
        Rect r = new Rect(bx, y, w, h);
        bool hover = r.Contains(Event.current.mousePosition);

        // Accent edge marker
        GUI.color = new Color(accent.r, accent.g, accent.b, slide * (hover ? 1f : 0.55f));
        GUI.DrawTexture(new Rect(bx - 10f, y + 6f, 4f, h - 12f), whiteTex);
        GUI.color = Color.white;

        GUIStyle b = new GUIStyle(GUI.skin.button)
        {
            fontSize = hover ? 22 : 20,
            fontStyle = FontStyle.Bold
        };
        b.normal.textColor = hover ? Color.white : new Color(0.85f, 0.9f, 0.95f, 0.9f);

        return GUI.Button(r, label, b);
    }
}
