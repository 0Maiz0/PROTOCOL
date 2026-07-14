using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// PROTOCOL → Texture Hand-Made Map
///
/// One-click texturing for the hand-built blockout: generates procedural
/// textures (sand floor / plaster walls / wood crates, Mirage-ish palette),
/// creates shared URP Lit materials and assigns them to the scene's
/// primitive geometry by shape — flat &amp; wide = floor, tall = wall, small
/// box = crate. Each object gets a WorldTiledSurface so tiling follows
/// world size instead of stretching. Bots, waypoints, spawns and bomb-site
/// zones are left untouched. Undo-able; tweak the palette below and re-run.
/// </summary>
public static class MapTexturer
{
    // Palette — edit and re-run to re-tint the whole map
    private static readonly Color FloorBase = new Color(0.72f, 0.64f, 0.50f); // warm sand
    private static readonly Color WallBase  = new Color(0.84f, 0.79f, 0.69f); // cream plaster
    private static readonly Color CrateBase = new Color(0.55f, 0.42f, 0.28f); // crate wood

    private const string TexDir = "Assets/Textures/Generated";
    private const string MatDir = "Assets/Materials/Generated";

    [MenuItem("PROTOCOL/Texture Hand-Made Map")]
    public static void Run()
    {
        Directory.CreateDirectory(TexDir);
        Directory.CreateDirectory(MatDir);

        Material floorMat = MakeMaterial("MapFloor", MakeTexture("map_floor", FloorBase, grid: true,  noise: 0.07f,  planks: false));
        Material wallMat  = MakeMaterial("MapWall",  MakeTexture("map_wall",  WallBase,  grid: true,  noise: 0.04f,  planks: false));
        Material crateMat = MakeMaterial("MapCrate", MakeTexture("map_crate", CrateBase, grid: false, noise: 0.06f,  planks: true));

        int floors = 0, walls = 0, crates = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!ShouldTexture(r)) continue;

            Vector3 s = r.bounds.size;
            Material mat;
            float unitsPerTile;
            bool flat = s.y < Mathf.Min(s.x, s.z) * 0.35f || s.y < 0.35f;

            if (flat && Mathf.Max(s.x, s.z) > 2.5f) { mat = floorMat; unitsPerTile = 2f; floors++; }
            else if (s.y >= 1.8f)                   { mat = wallMat;  unitsPerTile = 2f; walls++; }
            else                                    { mat = crateMat; unitsPerTile = 1f; crates++; }

            Undo.RecordObject(r, "Texture Map");
            r.sharedMaterial = mat;

            var tiled = r.GetComponent<WorldTiledSurface>();
            if (tiled == null) tiled = Undo.AddComponent<WorldTiledSurface>(r.gameObject);
            else Undo.RecordObject(tiled, "Texture Map");
            tiled.unitsPerTile = unitsPerTile;
            tiled.Apply();
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"MAP TEXTURER: floors {floors}, walls {walls}, crates {crates}. " +
                  "Save the scene to keep it; Edit → Undo to revert.");
    }

    /// <summary>Static blockout primitives only — never bots, markers or zones.</summary>
    private static bool ShouldTexture(MeshRenderer r)
    {
        GameObject go = r.gameObject;
        if (go.GetComponentInParent<BotController>() != null) return false;
        if (go.GetComponentInParent<BombSite>() != null) return false; // keep the zone tint readable

        for (Transform t = go.transform; t != null; t = t.parent)
        {
            string n = t.name;
            if (n.StartsWith("WP_") || n.Contains("SPAWN") || n.Contains("Waypoint") ||
                n.Contains("Bot") || n.Contains("Grenade") || n.Contains("Bomb") ||
                n.Contains("Tracer") || n.Contains("Mirage"))
                return false;
        }

        var mf = go.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;
        string mesh = mf.sharedMesh.name;
        return mesh == "Cube" || mesh == "Plane" || mesh == "Quad";
    }

    private static Material MakeMaterial(string name, Texture2D tex)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 512px procedural tile: per-cell value variation + fine grain, optional
    /// panel grid lines (floors/walls) or vertical planks (crates).
    /// </summary>
    private static Texture2D MakeTexture(string name, Color baseCol, bool grid, float noise, bool planks)
    {
        const int size = 512, cell = 128, plank = 64;
        float seed = Mathf.Abs(name.GetHashCode() % 97);

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cellNoise = (Mathf.PerlinNoise(seed + (x / cell) * 12.9f, seed + (y / cell) * 78.2f) - 0.5f) * 2f * noise;
                float grain = (Mathf.PerlinNoise(seed + x * 0.11f, seed + y * 0.11f) - 0.5f) * noise;
                float v = 1f + cellNoise + grain;

                if (planks)
                {
                    if (x % plank < 2) v *= 0.72f; // plank seams
                    v *= 1f + (((x / plank) * 37) % 7 - 3) * 0.02f; // per-plank tint
                }
                if (grid && (x % cell < 2 || y % cell < 2)) v *= 0.82f; // panel lines

                pixels[y * size + x] = new Color(baseCol.r * v, baseCol.g * v, baseCol.b * v, 1f);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
