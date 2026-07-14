using UnityEngine;

/// <summary>
/// World-scaled texture tiling for blockout geometry: shared materials,
/// per-object UV tiling from world bounds via MaterialPropertyBlock — no
/// stretched textures on scaled cubes and no per-object material assets.
/// Added automatically by the PROTOCOL → Texture Hand-Made Map editor tool.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class WorldTiledSurface : MonoBehaviour
{
    [Tooltip("World units covered by one texture tile")]
    public float unitsPerTile = 2f;

    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST"); // URP Lit
    private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST"); // Standard fallback

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    public void Apply()
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;

        Vector3 s = r.bounds.size;
        float u, v;
        bool flat = s.y < Mathf.Min(s.x, s.z) * 0.5f || s.y < 0.35f;
        if (flat)
        {
            // Floor: texture spans the horizontal plane
            u = s.x / unitsPerTile;
            v = s.z / unitsPerTile;
        }
        else
        {
            // Wall/crate: long side drives U, height drives V
            u = Mathf.Max(s.x, s.z) / unitsPerTile;
            v = s.y / unitsPerTile;
        }

        var st = new Vector4(Mathf.Max(0.25f, u), Mathf.Max(0.25f, v), 0f, 0f);
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetVector(BaseMapST, st);
        mpb.SetVector(MainTexST, st);
        r.SetPropertyBlock(mpb);
    }
}
