using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation;

/// <summary>
/// MIRAGE GENERATOR v3 — de_mirage as a low-poly blockout of ENCLOSED rooms
/// and corridors (shapes only, no textures). Instead of scattered walls on an
/// open plane, each area is its own floor tile fully walled except for
/// door-width connections, so the walkable footprint reads as Mirage from above.
///
/// Layout (T south / CT north):
///   T Spawn ─┬─ Apartments ── B Site ── Market ─┐
///            ├─ Mid ─┬─ Catwalk/Underpass → B    ├─ CT Spawn
///            │       └─ Connector → A            │
///            └─ A Ramp / Palace ──── A Site ─────┘
///
/// Callouts map onto the FIXED waypoint graph so every StrategyData works:
///   WP_NodeA=A WP_NodeB=B WP_Axis/WP_Bridge=Mid WP_TopAxis=Window
///   WP_Catwalk=Connector(→A) WP_Garden=Palace WP_ACorr=A Ramp WP_Overlook=A
///   WP_Bazaar=Market(→B) WP_Tunnel=Underpass(→B) WP_BAlley=Apartments(→B)
///
/// SETUP: empty GameObject + this component in the match scene; disable the
/// hand-made map. Builds geometry/floors/spawns/sites/cover, bakes NavMesh and
/// wires RoundManager/BombManager/StrategyExecutor/ObserverCamera in Awake.
/// </summary>
public class MirageGenerator : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private float wallHeight = 4f;

    [Header("Wiring (auto-found if empty)")]
    [SerializeField] private RoundManager roundManager;
    [SerializeField] private BombManager bombManager;
    [SerializeField] private StrategyExecutor strategyExecutor;
    [SerializeField] private ObserverCamera observerCamera;

    private GameObject root;
    private float h;
    private readonly List<Transform> tSpawns = new List<Transform>();
    private readonly List<Transform> ctSpawns = new List<Transform>();

    private static readonly Color FloorColor = new Color(0.60f, 0.53f, 0.40f); // sandstone
    private static readonly Color WallColor  = new Color(0.76f, 0.70f, 0.58f);
    private static readonly Color CrateColor = new Color(0.46f, 0.34f, 0.19f);
    private static readonly Color SiteColor  = new Color(0.82f, 0.70f, 0.30f);

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

        // ============ FLOOR TILES (the walkable footprint = the Mirage shape) ============
        Floor(-38, -44, 36, -32, FloorColor); // T Spawn (wide, feeds apps/mid/ramp)
        Floor(-38, -32, -26, -8, FloorColor);  // Apartments
        Floor(-6, -32, 10, 20, FloorColor);     // Mid
        Floor(-6, 20, 10, 38, FloorColor);      // Window / Top Mid
        Floor(26, -32, 36, 10, FloorColor);     // A Ramp
        Floor(36, -24, 52, 10, FloorColor);     // Palace
        Floor(10, 10, 26, 18, FloorColor);      // Connector (mid → A)
        Floor(-26, 4, -6, 10, FloorColor);      // Catwalk / Short (mid → B)
        Floor(-26, -8, -6, -2, FloorColor);     // Underpass (mid → B)
        Floor(26, 10, 52, 38, FloorColor);      // A Site
        Floor(-52, -8, -26, 20, FloorColor);    // B Site
        Floor(-52, 20, -26, 38, FloorColor);    // Market
        Floor(-52, 38, 52, 50, FloorColor);     // CT Spawn (wide top)
        SiteMark(new Vector3(39, 0, 24), 20);   // A plant zone
        SiteMark(new Vector3(-39, 0, 6), 20);   // B plant zone

        // ============ WALLS — enclose each area, gaps = doorways/chokes ============
        // T Spawn shell (opens north into apps / mid / ramp)
        Seg(-38, -44, 36, -44); Seg(-38, -44, -38, -32); Seg(36, -44, 36, -32);
        Seg(-26, -32, -6, -32); Seg(10, -32, 26, -32);   // north wall between the three lane mouths

        // Apartments (T ↔ B)
        Seg(-38, -32, -38, -8);   // west
        Seg(-26, -32, -26, -8);   // east (faces void toward mid)

        // Mid corridor — west wall (doors: Underpass z[-8..-2], Catwalk z[4..10])
        Seg(-6, -32, -6, -8); Seg(-6, -2, -6, 4); Seg(-6, 10, -6, 38);
        // Mid corridor — east wall (door: Connector z[10..18])
        Seg(10, -32, 10, 10); Seg(10, 18, 10, 38);

        // Connector (mid → A)
        Seg(10, 18, 26, 18); Seg(10, 10, 26, 10);

        // Catwalk + Underpass north/south guides (mid → B)
        Seg(-26, 10, -6, 10); Seg(-26, 4, -6, 4);
        Seg(-26, -2, -6, -2); Seg(-26, -8, -6, -8);

        // A Ramp (T ↔ A), and Ramp↔Palace divider with a door z[-20..-14]
        Seg(26, -32, 26, 10);                      // ramp west (void toward mid)
        Seg(36, -32, 36, -20); Seg(36, -14, 36, 10); // ramp/palace divider + door

        // Palace shell
        Seg(36, -24, 52, -24);   // south
        Seg(52, -24, 52, 10);    // east

        // A Site shell (doors: Connector west z[10..18], CT/Sandwich north x[42..50])
        Seg(26, 18, 26, 38);     // west (above connector)
        Seg(52, 10, 52, 38);     // east
        Seg(26, 38, 42, 38);     // north wall, gap x[42..50] = CT entry
        // (A south is open to Ramp + Palace)

        // B Site shell (doors: Apps south x[-38..-26], Underpass/Catwalk east, Market north open)
        Seg(-52, -8, -38, -8);   // south, gap x[-38..-26] = Apartments
        Seg(-52, -8, -52, 20);   // west
        Seg(-26, -2, -26, 4); Seg(-26, 10, -26, 20); // east wall between the two lane doors

        // Market (CT ↔ B), open to B south and CT north
        Seg(-52, 20, -52, 38);   // west
        Seg(-26, 20, -26, 38);   // east (void toward mid/window)

        // CT Spawn shell (doors down to A x[42..50], Window x[-6..10], Market x[-52..-26])
        Seg(-52, 50, 52, 50); Seg(-52, 38, -52, 50); Seg(52, 38, 52, 50);
        Seg(-26, 38, -6, 38); Seg(10, 38, 42, 38); Seg(50, 38, 52, 38); // south wall between the mouths

        // ============ COVER (crates + CoverPoints facing likely threats) ============
        SiteCrate(new Vector3(37, 0, 22), new Vector3(30, 0, 6));   // A triple
        SiteCrate(new Vector3(42, 0, 26), new Vector3(30, 0, 6));   // A firebox
        SiteCrate(new Vector3(34, 0, 30), new Vector3(30, 0, 12));  // A default
        SiteCrate(new Vector3(46, 0, 18), new Vector3(50, 0, 6));   // A CT-side
        SiteCrate(new Vector3(-40, 0, 5), new Vector3(-30, 0, -4)); // B bench
        SiteCrate(new Vector3(-35, 0, 10), new Vector3(-26, 0, 4)); // B van
        SiteCrate(new Vector3(-45, 0, 12), new Vector3(-50, 0, 20));// B market-side
        Crate(new Vector3(2, 0, 26), 2f, 2f);    // window
        Crate(new Vector3(18, 0, 14), 2f, 2f);   // connector
        Crate(new Vector3(-16, 0, -5), 2f, 2f);  // underpass
        Crate(new Vector3(45, 0, -4), 2.5f, 2f); // palace steps

        // ============ BOMB SITES ============
        BombSite siteA = MakeSite("A", new Vector3(39, 0, 24), 7f);
        BombSite siteB = MakeSite("B", new Vector3(-39, 0, 6), 6.5f);

        // ============ SPAWNS ============
        MakeSpawns(tSpawns, "TSpawn", new Vector3(-2, 0, -40));
        MakeSpawns(ctSpawns, "CTSpawn", new Vector3(0, 0, 44));

        // ============ WAYPOINTS (Mirage callouts on the fixed graph) ============
        WP("WP_NodeA",   new Vector3(39, 0, 24));  // A site
        WP("WP_NodeB",   new Vector3(-39, 0, 6));  // B site
        WP("WP_Axis",    new Vector3(2, 0, -4));   // Mid
        WP("WP_Bridge",  new Vector3(2, 0, 8));    // Mid (connector junction)
        WP("WP_TopAxis", new Vector3(2, 0, 28));   // Window / Top Mid
        WP("WP_Catwalk", new Vector3(18, 0, 14));  // Connector (mid → A)
        WP("WP_ACorr",   new Vector3(31, 0, -6));  // A Ramp
        WP("WP_Garden",  new Vector3(44, 0, -4));  // Palace
        WP("WP_Overlook",new Vector3(44, 0, 30));  // A CT-side cover
        WP("WP_BAlley",  new Vector3(-32, 0, -20));// Apartments
        WP("WP_Tunnel",  new Vector3(-16, 0, -5)); // Underpass
        WP("WP_Bazaar",  new Vector3(-39, 0, 29)); // Market
        WP("WP_TBase",   new Vector3(-2, 0, -40));
        WP("WP_CTBase",  new Vector3(0, 0, 44));

        PostPlant("A", new Vector3(39, 0, 24), 6f);
        PostPlant("B", new Vector3(-39, 0, 6), 5.5f);

        // ============ NAVMESH ============
        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();

        // ============ WIRE THE MATCH SYSTEMS ============
        if (roundManager != null) roundManager.SetSpawnPoints(tSpawns.ToArray(), ctSpawns.ToArray());
        if (bombManager != null) { bombManager.siteA = siteA; bombManager.siteB = siteB; }
        if (strategyExecutor != null) strategyExecutor.CacheWaypoints();
        if (observerCamera != null) observerCamera.SetOverview(new Vector3(0, 104, -22), new Vector3(66, 0, 0));

        Debug.Log("MIRAGE v3: enclosed rooms — A(ramp/palace/connector) B(apps/short/market) mid(window/underpass)");
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

    private void Floor(float x1, float z1, float x2, float z2, Color c)
        => Box("Floor", new Vector3((x1 + x2) / 2f, -0.5f, (z1 + z2) / 2f),
               new Vector3(Mathf.Abs(x2 - x1), 1f, Mathf.Abs(z2 - z1)), c);

    private void SiteMark(Vector3 c, float size)
        => Box("SiteMark", new Vector3(c.x, 0.03f, c.z), new Vector3(size, 0.06f, size), SiteColor);

    /// <summary>Wall segment between two floor points (auto length + rotation).</summary>
    private void Seg(float x1, float z1, float x2, float z2, float thick = 0.8f)
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
