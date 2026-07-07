using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation;

/// <summary>
/// MAP GENERATOR v1 — seeded arenas with the NEXUS TOPOLOGY.
///
/// The callout graph is FIXED (Node A / Node B / Axis / Tunnel / Bridge /
/// A Corridor / B Alley / Bazaar / Garden / Catwalk / Top Axis) so every
/// existing StrategyData asset keeps working — but sizes, choke widths,
/// gap positions and cover layouts are seeded. Same protocol, new ground.
///
/// SETUP: empty GameObject in the match scene + this component. It builds
/// geometry, waypoints, spawns, bomb sites and CoverPoints in Awake, bakes
/// the NavMesh (com.unity.ai.navigation) and wires RoundManager/BombManager/
/// StrategyExecutor/ObserverCamera. Disable or delete the hand-made map first.
/// Seed 0 = new map every play; a fixed seed reproduces the same arena.
/// </summary>
public class MapGenerator : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private int seed = 0; // 0 = random every play session
    [SerializeField] private bool generateOnAwake = true;
    [SerializeField] private float wallHeight = 3f;

    [Header("Wiring (auto-found if empty)")]
    [SerializeField] private RoundManager roundManager;
    [SerializeField] private BombManager bombManager;
    [SerializeField] private StrategyExecutor strategyExecutor;
    [SerializeField] private ObserverCamera observerCamera;

    private System.Random rng;
    private GameObject root;
    private readonly List<Transform> tSpawns = new List<Transform>();
    private readonly List<Transform> ctSpawns = new List<Transform>();

    private static readonly Color FloorColor = new Color(0.32f, 0.34f, 0.38f);
    private static readonly Color WallColor = new Color(0.55f, 0.53f, 0.48f);
    private static readonly Color CrateColor = new Color(0.5f, 0.38f, 0.22f);

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
        int usedSeed = seed != 0 ? seed : Random.Range(1, 999999);
        rng = new System.Random(usedSeed);
        root = new GameObject($"GeneratedMap_{usedSeed}");
        tSpawns.Clear();
        ctSpawns.Clear();
        Debug.Log($"MAPGEN: building arena, seed {usedSeed}");

        // ===== Seeded dimensions (T spawns south, CT north) =====
        float W = Range(70f, 88f);        // east-west
        float D = Range(54f, 66f);        // south-north
        float mid = Range(8f, 12f);       // Axis corridor width
        float tGap = Range(4f, 6f);       // Tunnel choke (south mid entrance)
        float ctGap = Range(5f, 8f);      // Top Axis opening (north)
        float catZ = Range(-4f, 8f);      // Catwalk gap position (west mid wall)
        float bazZ = Range(2f, 12f);      // Bazaar gap position (east mid wall)
        float catW = Range(3.5f, 5.5f);
        float bazW = Range(3.5f, 5.5f);
        float h = wallHeight;

        // ===== Floor + border =====
        Box("Floor", new Vector3(0, -0.5f, 0), new Vector3(W, 1f, D), FloorColor);
        Box("Wall_N", new Vector3(0, h / 2, D / 2), new Vector3(W, h, 1), WallColor);
        Box("Wall_S", new Vector3(0, h / 2, -D / 2), new Vector3(W, h, 1), WallColor);
        Box("Wall_W", new Vector3(-W / 2, h / 2, 0), new Vector3(1, h, D), WallColor);
        Box("Wall_E", new Vector3(W / 2, h / 2, 0), new Vector3(1, h, D), WallColor);

        // ===== Mid (Axis) walls with gaps: Tunnel (S), Top Axis (N), Catwalk (W), Bazaar (E) =====
        WallAlongZ(-mid / 2, -D / 2 + tGap, catZ - catW / 2, h);
        WallAlongZ(-mid / 2, catZ + catW / 2, D / 2 - ctGap, h);
        WallAlongZ(mid / 2, -D / 2 + tGap, bazZ - bazW / 2, h);
        WallAlongZ(mid / 2, bazZ + bazW / 2, D / 2 - ctGap, h);

        // ===== Sites =====
        float sA = Range(12f, 16f);
        float sB = Range(12f, 16f);
        Vector3 aCenter = new Vector3(-W / 2 + sA / 2 + Range(4f, 8f), 0, D / 2 - sA / 2 - Range(6f, 10f));
        Vector3 bCenter = new Vector3(W / 2 - sB / 2 - Range(4f, 8f), 0, D / 2 - sB / 2 - Range(6f, 10f));

        // Chokes across the outer lanes, south of each site
        float aGateX, aGateZ, bGateX, bGateZ;
        BuildSiteChoke(aCenter, sA, -W / 2, -mid / 2, Range(3.5f, 6f), h, out aGateX, out aGateZ);
        BuildSiteChoke(bCenter, sB, mid / 2, W / 2, Range(3.5f, 6f), h, out bGateX, out bGateZ);

        // ===== Cover crates (with CoverPoints facing the entrances) =====
        ScatterCrates(aCenter, sA * 0.42f, RangeInt(3, 6), new Vector3(aGateX, 0, aGateZ));
        ScatterCrates(bCenter, sB * 0.42f, RangeInt(3, 6), new Vector3(bGateX, 0, bGateZ));
        ScatterCrates(new Vector3(0, 0, catZ), mid * 0.7f, RangeInt(2, 4), new Vector3(0, 0, -D / 2));

        // ===== Bomb sites =====
        BombSite siteA = MakeSite("A", aCenter, sA * 0.4f);
        BombSite siteB = MakeSite("B", bCenter, sB * 0.4f);

        // ===== Spawns =====
        MakeSpawns(tSpawns, "TSpawn", new Vector3(0, 0, -D / 2 + 4f));
        MakeSpawns(ctSpawns, "CTSpawn", new Vector3(0, 0, D / 2 - 4f));

        // ===== Waypoints — the fixed callout graph =====
        WP("WP_NodeA", aCenter);
        WP("WP_NodeB", bCenter);
        WP("WP_Axis", Vector3.zero);
        WP("WP_TopAxis", new Vector3(0, 0, D / 2 - ctGap - 2f));
        WP("WP_Bridge", new Vector3(0, 0, catZ));
        WP("WP_Tunnel", new Vector3(0, 0, -D / 2 + tGap + 2f));
        WP("WP_ACorr", new Vector3(aGateX, 0, aGateZ - 4f));
        WP("WP_BAlley", new Vector3(bGateX, 0, bGateZ - 4f));
        WP("WP_Catwalk", new Vector3(-mid / 2, 0, catZ));
        WP("WP_Bazaar", new Vector3(mid / 2 + 3f, 0, bazZ));
        WP("WP_Garden", new Vector3(aCenter.x + sA / 2 + 4f, 0, D / 2 - 5f));
        WP("WP_TBase", new Vector3(0, 0, -D / 2 + 4f));
        WP("WP_CTBase", new Vector3(0, 0, D / 2 - 4f));

        // Post-plant spots — site corners
        PostPlantSpots("A", aCenter, sA * 0.35f);
        PostPlantSpots("B", bCenter, sB * 0.35f);

        // ===== NavMesh =====
        var surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();

        // ===== Wire the match systems =====
        if (roundManager != null) roundManager.SetSpawnPoints(tSpawns.ToArray(), ctSpawns.ToArray());
        if (bombManager != null) { bombManager.siteA = siteA; bombManager.siteB = siteB; }
        if (strategyExecutor != null) strategyExecutor.CacheWaypoints();
        if (observerCamera != null)
            observerCamera.SetOverview(new Vector3(0, Mathf.Max(W, D) * 0.75f, -D * 0.25f), new Vector3(65f, 0f, 0f));
    }

    // ===================== BUILD HELPERS =====================

    private float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
    private int RangeInt(int min, int maxExclusive) => rng.Next(min, maxExclusive);

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

    private void WallAlongZ(float x, float z0, float z1, float h)
    {
        if (z1 - z0 < 0.5f) return;
        float mid = (z0 + z1) / 2f, len = z1 - z0;
        Box("Wall", new Vector3(x, h / 2, mid), new Vector3(1f, h, len), WallColor);
    }

    private void BuildSiteChoke(Vector3 site, float size, float laneXMin, float laneXMax,
        float gapWidth, float h, out float gateX, out float gateZ)
    {
        gateZ = site.z - size / 2f - 2f;
        gateX = Mathf.Lerp(laneXMin + gapWidth, laneXMax - gapWidth, (float)rng.NextDouble());
        // Two wall segments across the lane, leaving the choke gap
        float leftLen = (gateX - gapWidth / 2f) - laneXMin;
        float rightLen = laneXMax - (gateX + gapWidth / 2f);
        if (leftLen > 0.5f)
            Box("Choke", new Vector3(laneXMin + leftLen / 2f, h / 2, gateZ), new Vector3(leftLen, h, 1f), WallColor);
        if (rightLen > 0.5f)
            Box("Choke", new Vector3(laneXMax - rightLen / 2f, h / 2, gateZ), new Vector3(rightLen, h, 1f), WallColor);
    }

    private void ScatterCrates(Vector3 center, float radius, int count, Vector3 threatFrom)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = center + new Vector3(Range(-radius, radius), 0, Range(-radius, radius));
            pos.y = 1f;
            var crate = Box("Crate", pos, new Vector3(Range(1.6f, 2.4f), 2f, Range(1.6f, 2.4f)), CrateColor);

            // CoverPoint behind the crate relative to the entrance, peeking toward it
            Vector3 away = (center - threatFrom).normalized;
            var cpGO = new GameObject("CoverPoint");
            cpGO.transform.SetParent(crate.transform.parent);
            cpGO.transform.position = pos + away * 1.6f + Vector3.down * 1f;
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
            go.transform.position = center + new Vector3((i - 2) * 2.2f, 0, 0);
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

    private void PostPlantSpots(string site, Vector3 center, float r)
    {
        WP($"WP_PP_{site}_1", center + new Vector3(-r, 0, -r));
        WP($"WP_PP_{site}_2", center + new Vector3(r, 0, -r));
        WP($"WP_PP_{site}_3", center + new Vector3(-r, 0, r));
        WP($"WP_PP_{site}_4", center + new Vector3(r, 0, r));
    }
}
