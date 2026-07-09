using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation;

/// <summary>
/// MIRAGE GENERATOR — builds a CS-Mirage-style layout (low-poly blockout).
///
/// Real Mirage callouts are mapped onto our FIXED waypoint graph so every
/// existing StrategyData asset keeps working:
///   WP_NodeA  = A site        WP_NodeB   = B site        WP_Axis    = Mid
///   WP_Garden = Palace        WP_ACorr   = A Ramp        WP_Catwalk = Connector
///   WP_BAlley = Apartments    WP_Bazaar  = Market        WP_Tunnel  = Underpass
///   WP_Bridge = Mid Connector WP_TopAxis = Top Mid       WP_TBase/WP_CTBase = spawns
///
/// SETUP: empty GameObject in the match scene + this component. It builds
/// geometry, waypoints, spawns, bomb sites and CoverPoints in Awake, bakes
/// the NavMesh and wires RoundManager / BombManager / StrategyExecutor /
/// ObserverCamera. Disable the hand-made map first. Layout is fixed (Mirage
/// is Mirage) — no seed.
/// </summary>
public class MirageGenerator : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private float wallHeight = 3f;

    [Header("Wiring (auto-found if empty)")]
    [SerializeField] private RoundManager roundManager;
    [SerializeField] private BombManager bombManager;
    [SerializeField] private StrategyExecutor strategyExecutor;
    [SerializeField] private ObserverCamera observerCamera;

    private GameObject root;
    private float h;
    private readonly List<Transform> tSpawns = new List<Transform>();
    private readonly List<Transform> ctSpawns = new List<Transform>();

    private static readonly Color FloorColor = new Color(0.62f, 0.55f, 0.42f); // Mirage sand
    private static readonly Color WallColor = new Color(0.72f, 0.66f, 0.55f);
    private static readonly Color CrateColor = new Color(0.5f, 0.38f, 0.22f);
    private static readonly Color SiteColor = new Color(0.75f, 0.65f, 0.3f);

    private void Awake()
    {
        if (generateOnAwake) Generate();
    }

    public void Generate()
    {
        if (roundManager == null) roundManager = FindObjectOfType<RoundManager>();
        if (bombManager == null) bombManager = FindObjectOfType<BombManager>();
        if (strategyExecutor == null) strategyExecutor = FindObjectOfType<StrategyExecutor>();
        if (observerCamera == null) observerCamera = Camera.main != null ? Camera.main.GetComponent<ObserverCamera>() : null;

        if (root != null) Destroy(root);
        root = new GameObject("GeneratedMirage");
        h = wallHeight;
        tSpawns.Clear();
        ctSpawns.Clear();

        // ===== Ground + outer walls (X = west..east, Z = south(T)..north(CT)) =====
        // ~100 x 80 arena
        Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(104, 1, 84), FloorColor);
        Wall(0, 41.5f, 104, 1);   // north (CT)
        Wall(0, -41.5f, 104, 1);  // south (T)
        Wall(-51.5f, 0, 1, 84);   // west
        Wall(51.5f, 0, 1, 84);    // east

        // ===== MID — central vertical lane, x in [-6, 6] =====
        // East mid wall: gap 6..13 = Connector to A ; West mid wall: gap 4..11 = to B
        Wall(6, -12f, 1, 36);          // east mid wall lower (z -30..6)
        Wall(6, 22.5f, 1, 17);         // east mid wall upper (z 14..31)
        Wall(-6, -13f, 1, 34);         // west mid wall lower (z -30..4)
        Wall(-6, 21f, 1, 20);          // west mid wall upper (z 11..31)
        // Mid doors / window ledge (partial cover crate cluster mid)
        Crate(new Vector3(0, 0, 2), 2.2f, 2.5f);
        Crate(new Vector3(2, 0, -6), 2f, 2f);

        // ===== A SITE (north-east) =====
        Vector3 aCenter = new Vector3(30, 24, 0);
        SiteFloor("A", aCenter, 20);
        // A west wall (separates from mid/connector), gap at south = connector entry
        Wall(20, 24, 1, 20);           // z 14..34
        // A south wall with RAMP gap around x=30 (width 6)
        Wall(25.5f, 14, 9, 1);         // x 21..30
        Wall(37, 14, 8, 1);            // x 33..41
        // Palace lane (SE): a raised chunk east of ramp
        Wall(44, 2, 1, 26);            // x 44 wall z -11..15 (palace/ramp divider)

        // ===== B SITE (north-west) =====
        Vector3 bCenter = new Vector3(-32, 22, 0);
        SiteFloor("B", bCenter, 18);
        // B east wall, gap at south = apartments entry
        Wall(-20, 23, 1, 18);          // z 14..32
        // B south wall with APARTMENTS gap around x=-30 (width 6)
        Wall(-25.5f, 13, 9, 1);        // x -30..-21
        Wall(-40, 13, 12, 1);          // x -46..-34
        // Market structure (far west, outside B)
        Wall(-46, 20, 1, 14);          // market back wall

        // ===== Cover crates + CoverPoints =====
        ScatterSiteCover(aCenter, 7f, 4, new Vector3(30, 0, -10)); // threats from ramp/mid south
        ScatterSiteCover(bCenter, 6f, 4, new Vector3(-30, 0, -10));

        // ===== Bomb sites =====
        BombSite siteA = MakeSite("A", aCenter, 7f);
        BombSite siteB = MakeSite("B", bCenter, 6.5f);

        // ===== Spawns =====
        MakeSpawns(tSpawns, "TSpawn", new Vector3(0, 0, -37));
        MakeSpawns(ctSpawns, "CTSpawn", new Vector3(0, 0, 37));

        // ===== Waypoints — Mirage callouts on the fixed graph =====
        WP("WP_NodeA", aCenter);                       // A site
        WP("WP_NodeB", bCenter);                       // B site
        WP("WP_Axis", new Vector3(0, 0, 0));           // Mid
        WP("WP_TopAxis", new Vector3(0, 0, 26));       // Top Mid (CT mid)
        WP("WP_Bridge", new Vector3(0, 0, -14));       // Mid / underpass mouth
        WP("WP_Tunnel", new Vector3(-2, 0, -24));      // Underpass (T under mid)
        WP("WP_Catwalk", new Vector3(13, 0, 10));      // Connector (mid → A)
        WP("WP_ACorr", new Vector3(30, 0, 2));         // A Ramp choke
        WP("WP_Garden", new Vector3(44, 0, -6));       // Palace
        WP("WP_BAlley", new Vector3(-30, 0, 2));       // Apartments choke
        WP("WP_Bazaar", new Vector3(-45, 0, 14));      // Market
        WP("WP_TBase", new Vector3(0, 0, -37));
        WP("WP_CTBase", new Vector3(0, 0, 37));

        // Post-plant spots (site corners)
        PostPlant("A", aCenter, 6f);
        PostPlant("B", bCenter, 5.5f);

        // ===== NavMesh =====
        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();

        // ===== Wire the match systems =====
        if (roundManager != null) roundManager.SetSpawnPoints(tSpawns.ToArray(), ctSpawns.ToArray());
        if (bombManager != null) { bombManager.siteA = siteA; bombManager.siteB = siteB; }
        if (strategyExecutor != null) strategyExecutor.CacheWaypoints();
        if (observerCamera != null) observerCamera.SetOverview(new Vector3(0, 78, -20), new Vector3(66, 0, 0));

        Debug.Log("MIRAGE: generated — A(ramp/palace/connector), B(apartments/market), mid");
    }

    // ===================== HELPERS =====================

    private GameObject Box(string name, Vector3 pos, Vector3 size, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root.transform);
        go.transform.position = pos;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        if (r != null) { var m = new Material(r.sharedMaterial); m.color = color; r.material = m; }
        return go;
    }

    private void Wall(float x, float z, float sx, float sz)
        => Box("Wall", new Vector3(x, h / 2f, z), new Vector3(sx, h, sz), WallColor);

    private void SiteFloor(string name, Vector3 c, float size)
        => Box($"Site_{name}", new Vector3(c.x, 0.02f, c.z), new Vector3(size, 0.05f, size), SiteColor);

    private void Crate(Vector3 pos, float sx, float sz)
    {
        pos.y = 1f;
        Box("Crate", pos, new Vector3(sx, 2f, sz), CrateColor);
    }

    private void ScatterSiteCover(Vector3 center, float radius, int count, Vector3 threatFrom)
    {
        for (int i = 0; i < count; i++)
        {
            float ang = (i / (float)count) * Mathf.PI * 2f;
            Vector3 pos = center + new Vector3(Mathf.Cos(ang) * radius, 0, Mathf.Sin(ang) * radius);
            var crate = Box("Crate", new Vector3(pos.x, 1f, pos.z), new Vector3(2f, 2f, 2f), CrateColor);

            Vector3 away = (center - threatFrom).normalized;
            var cpGO = new GameObject("CoverPoint");
            cpGO.transform.SetParent(crate.transform.parent);
            cpGO.transform.position = pos + away * 1.6f;
            var cp = cpGO.AddComponent<CoverPoint>();
            cp.peekDirection = -away;
            cp.peekDistance = 1.6f;
        }
    }

    private BombSite MakeSite(string name, Vector3 center, float plantRadius)
    {
        var go = new GameObject($"BombSite_{name}");
        go.transform.SetParent(root.transform);
        go.transform.position = center;
        var site = go.AddComponent<BombSite>();
        site.siteName = name;
        site.plantRadius = plantRadius;
        return site;
    }

    private void MakeSpawns(List<Transform> list, string prefix, Vector3 center)
    {
        for (int i = 0; i < 5; i++)
        {
            var go = new GameObject($"{prefix}_{i + 1}");
            go.transform.SetParent(root.transform);
            go.transform.position = center + new Vector3((i - 2) * 2.4f, 0, 0);
            go.transform.rotation = Quaternion.LookRotation(center.z < 0 ? Vector3.forward : Vector3.back);
            list.Add(go.transform);
        }
    }

    private void WP(string name, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform);
        go.transform.position = pos;
    }

    private void PostPlant(string site, Vector3 c, float r)
    {
        WP($"WP_PP_{site}_1", c + new Vector3(-r, 0, -r));
        WP($"WP_PP_{site}_2", c + new Vector3(r, 0, -r));
        WP($"WP_PP_{site}_3", c + new Vector3(-r, 0, r));
        WP($"WP_PP_{site}_4", c + new Vector3(r, 0, r));
    }
}
