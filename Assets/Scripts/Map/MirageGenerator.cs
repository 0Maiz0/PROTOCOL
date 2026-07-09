using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation;

/// <summary>
/// MIRAGE GENERATOR v2 — de_mirage layout as a low-poly blockout (shapes, no textures).
///
/// Real Mirage geography, T spawn south / CT spawn north:
///   • A site (east): reached via A Ramp (south), Palace (south-east), Connector
///     (from mid) and CT/Sandwich (north).
///   • B site (west): reached via Apartments (south), Short/Catwalk (from mid),
///     Market (north-west) and Underpass (T mid-to-B).
///   • Mid (center): Top Mid → Mid → Window, with Connector peeling east to A
///     and Catwalk/Short peeling west to B.
///
/// Callouts map onto the FIXED waypoint graph so every StrategyData works:
///   WP_NodeA=A  WP_NodeB=B  WP_Axis=Mid  WP_TopAxis=TopMid/Window  WP_Bridge=Mid
///   WP_Catwalk=Connector(→A)  WP_Garden=Palace(→A)  WP_ACorr=A Ramp
///   WP_Bazaar=Market(→B)  WP_Tunnel=Underpass(→B)  WP_BAlley=Apartments(→B)
///   WP_Overlook=A cover   WP_TBase/WP_CTBase=spawns
///
/// SETUP: empty GameObject in the match scene + this component; disable the
/// hand-made map. It builds geometry/spawns/sites/cover, bakes the NavMesh and
/// wires RoundManager/BombManager/StrategyExecutor/ObserverCamera in Awake.
/// </summary>
public class MirageGenerator : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private float wallHeight = 3.5f;

    [Header("Wiring (auto-found if empty)")]
    [SerializeField] private RoundManager roundManager;
    [SerializeField] private BombManager bombManager;
    [SerializeField] private StrategyExecutor strategyExecutor;
    [SerializeField] private ObserverCamera observerCamera;

    private GameObject root;
    private float h;
    private readonly List<Transform> tSpawns = new List<Transform>();
    private readonly List<Transform> ctSpawns = new List<Transform>();

    private static readonly Color FloorColor = new Color(0.60f, 0.53f, 0.40f); // Mirage sandstone
    private static readonly Color WallColor  = new Color(0.74f, 0.68f, 0.56f);
    private static readonly Color CrateColor = new Color(0.48f, 0.36f, 0.20f);
    private static readonly Color SiteColor  = new Color(0.80f, 0.68f, 0.30f);

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

        // ===== GROUND + OUTER SHELL (X = west..east, Z = south(T)..north(CT)) =====
        Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(120, 1, 100), FloorColor);
        Seg(-55, -45, 55, -45); // south (T back)
        Seg(-55,  45, 55,  45); // north (CT back)
        Seg(-55, -45, -55, 45); // west
        Seg( 55, -45,  55, 45); // east

        // ===== MID — central corridor, west wall x=-2, east wall x=12 =====
        // West mid wall: gap z[-20..-13] = Underpass mouth, gap z[9..15] = Catwalk to B
        Seg(-2, -26, -2, -20);
        Seg(-2, -13, -2,   9);
        Seg(-2,  15, -2,  28);
        // East mid wall: gap z[11..17] = Connector to A
        Seg(12, -26, 12,  11);
        Seg(12,  17, 12,  28);

        // ===== A SITE (east) — box x[26..50], z[12..34] =====
        SiteFloor("A", new Vector3(38, 0, 23), 24);
        Seg(26, 20, 26, 34);   // west wall (Connector enters below z20)
        Seg(26, 12, 26, 15);   // west stub south of connector
        Seg(26, 34, 43, 34);   // north wall, gap x[43..50] = CT/Sandwich entry
        Seg(50, 12, 50, 34);   // east wall
        Seg(26, 12, 30, 12);   // south wall, gap x[30..34] = A Ramp
        Seg(34, 12, 44, 12);   // south wall, gap x[44..50] = Palace (SE)
        // Connector shaping (mid → A), open lane x[12..26] z[14..17]
        Seg(12, 11, 26, 12);   // connector south guide

        // ===== A RAMP + PALACE (T's south-east routes to A) =====
        Seg(24, -30, 24, 10);          // Ramp west wall (splits ramp from mid/underpass)
        Seg(40, -28, 40,  8);          // divider: Ramp (west) vs Palace (east)
        Seg(40,  8, 50, 12);           // Palace diagonal into A's SE gap

        // ===== B SITE (west) — box x[-50..-26], z[-4..18] =====
        SiteFloor("B", new Vector3(-38, 0, 7), 22);
        Seg(-26,   8, -26, 18);        // east wall (upper)
        Seg(-26,  -4, -26,  2);        // east wall (lower), gap z[2..8] = Short/Catwalk
        Seg(-46,  18, -26, 18);        // north wall, gap x[-50..-46] = Market entry
        Seg(-50,  -4, -50, 18);        // west wall
        Seg(-50,  -4, -32, -4);        // south wall, gap x[-32..-26] = Apartments

        // ===== APARTMENTS + UNDERPASS (T's south-west / mid routes to B) =====
        Seg(-24, -30, -24, -4);        // Apartments east wall (splits apps from mid)
        Seg(-24, -20, -2, -20);        // Underpass south wall (mid → B, x[-24..-2])
        Seg(-26,  15, -2, 15);         // Catwalk north guide (mid → B short)

        // ===== MARKET (CT's north-west route to B) =====
        Seg(-30, 20, -30, 40);         // Market east wall (splits market from CT/mid)
        Seg(-50, 20, -50, 40);         // Market west wall
        Seg(-50, 40, -30, 40);         // Market north wall

        // ===== CT SPAWN / TOP MID / WINDOW (north) =====
        Seg(-2, 28, 12, 28);           // Window front (mid top), gap open to CT at sides
        Seg(26, 34, 26, 40);           // wall guiding CT → A (Sandwich)

        // ===== COVER (crates + CoverPoints facing the entrances) =====
        // A: triple box / firebox / default
        SiteCrate(new Vector3(37, 0, 22), new Vector3(30, 0, 4));
        SiteCrate(new Vector3(41, 0, 25), new Vector3(30, 0, 4));
        SiteCrate(new Vector3(34, 0, 26), new Vector3(26, 0, 20));
        SiteCrate(new Vector3(45, 0, 20), new Vector3(48, 0, 6));
        // B: bench / van / kitchen
        SiteCrate(new Vector3(-40, 0, 6), new Vector3(-30, 0, -4));
        SiteCrate(new Vector3(-36, 0, 11), new Vector3(-26, 0, 4));
        SiteCrate(new Vector3(-44, 0, 12), new Vector3(-48, 0, 20));
        // Mid window + connector cover
        Crate(new Vector3(5, 0, 24), 2f, 2f);
        Crate(new Vector3(18, 0, 9), 2f, 2f);
        Crate(new Vector3(-14, 0, -18), 2f, 2f); // underpass
        Crate(new Vector3(46, 0, 0), 2.5f, 2f);  // palace steps

        // ===== BOMB SITES =====
        BombSite siteA = MakeSite("A", new Vector3(38, 0, 23), 7f);
        BombSite siteB = MakeSite("B", new Vector3(-38, 0, 7), 6.5f);

        // ===== SPAWNS =====
        MakeSpawns(tSpawns, "TSpawn", new Vector3(2, 0, -40));   // T bottom (slightly A-side)
        MakeSpawns(ctSpawns, "CTSpawn", new Vector3(6, 0, 40));  // CT top-center

        // ===== WAYPOINTS — Mirage callouts on the fixed graph =====
        WP("WP_NodeA",   new Vector3(38, 0, 23));  // A site
        WP("WP_NodeB",   new Vector3(-38, 0, 7));  // B site
        WP("WP_Axis",    new Vector3(5, 0, -2));   // Mid
        WP("WP_Bridge",  new Vector3(5, 0, 9));    // Mid (connector junction)
        WP("WP_TopAxis", new Vector3(6, 0, 24));   // Top Mid / Window
        WP("WP_Catwalk", new Vector3(19, 0, 15));  // Connector (mid → A)
        WP("WP_ACorr",   new Vector3(31, 0, -6));  // A Ramp choke
        WP("WP_Garden",  new Vector3(46, 0, -4));  // Palace
        WP("WP_Overlook",new Vector3(30, 0, 28));  // A overlook / CT cover
        WP("WP_BAlley",  new Vector3(-22, 0, -14));// Apartments choke
        WP("WP_Tunnel",  new Vector3(-14, 0, -17));// Underpass
        WP("WP_Bazaar",  new Vector3(-40, 0, 30)); // Market
        WP("WP_TBase",   new Vector3(2, 0, -40));
        WP("WP_CTBase",  new Vector3(6, 0, 40));

        PostPlant("A", new Vector3(38, 0, 23), 6f);
        PostPlant("B", new Vector3(-38, 0, 7), 5.5f);

        // ===== NAVMESH =====
        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();

        // ===== WIRE THE MATCH SYSTEMS =====
        if (roundManager != null) roundManager.SetSpawnPoints(tSpawns.ToArray(), ctSpawns.ToArray());
        if (bombManager != null) { bombManager.siteA = siteA; bombManager.siteB = siteB; }
        if (strategyExecutor != null) strategyExecutor.CacheWaypoints();
        if (observerCamera != null) observerCamera.SetOverview(new Vector3(0, 96, -26), new Vector3(67, 0, 0));

        Debug.Log("MIRAGE v2: A(ramp/palace/connector) • B(apps/short/market) • mid(window/connector/underpass)");
    }

    // ===================== BUILD HELPERS =====================

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

    /// <summary>Wall segment between two floor points (auto length + rotation).</summary>
    private void Seg(float x1, float z1, float x2, float z2, float thick = 1f)
    {
        Vector3 a = new Vector3(x1, 0, z1), b = new Vector3(x2, 0, z2);
        Vector3 mid = (a + b) * 0.5f; mid.y = h / 2f;
        float len = Vector3.Distance(a, b);
        float ang = Mathf.Atan2(x2 - x1, z2 - z1) * Mathf.Rad2Deg;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Wall";
        go.transform.SetParent(root.transform);
        go.transform.position = mid;
        go.transform.rotation = Quaternion.Euler(0, ang, 0);
        go.transform.localScale = new Vector3(thick, h, len);
        var r = go.GetComponent<Renderer>();
        if (r != null) { var m = new Material(r.sharedMaterial); m.color = WallColor; r.material = m; }
    }

    private void SiteFloor(string name, Vector3 c, float size)
        => Box($"Site_{name}", new Vector3(c.x, 0.02f, c.z), new Vector3(size, 0.05f, size), SiteColor);

    private void Crate(Vector3 pos, float sx, float sz)
        => Box("Crate", new Vector3(pos.x, 1f, pos.z), new Vector3(sx, 2f, sz), CrateColor);

    private void SiteCrate(Vector3 pos, Vector3 threatFrom)
    {
        var crate = Box("Crate", new Vector3(pos.x, 1f, pos.z), new Vector3(2.2f, 2f, 2.2f), CrateColor);
        Vector3 away = (pos - threatFrom); away.y = 0; away = away.normalized;
        var cpGO = new GameObject("CoverPoint");
        cpGO.transform.SetParent(crate.transform.parent);
        cpGO.transform.position = pos + away * 1.7f;
        var cp = cpGO.AddComponent<CoverPoint>();
        cp.peekDirection = -away;
        cp.peekDistance = 1.7f;
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
