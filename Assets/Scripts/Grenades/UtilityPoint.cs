using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Scene-authored grenade target — place empty GameObjects to tag the map's
/// utility spots (same workflow as CoverPoint): WHERE smokes cut rotations,
/// WHERE entry flashes pop, WHERE mollies flush campers, WHERE the CT anchor
/// denies the push. GrenadeManager builds its throw plans from these; a zone
/// with no points placed falls back to the legacy hardcoded waypoints.
///
/// Purpose guide:
///   Entry  — flash for the site take (canonical type: Flash)
///   Cut    — smoke that cuts the rotation/sightline (Smoke)
///   Hold   — molly on a known camp/hold spot (Molotov)
///   Anchor — CT denial on the approach; re-arms while nades remain
/// </summary>
public class UtilityPoint : MonoBehaviour
{
    [Tooltip("What lands here")]
    public GrenadeType type = GrenadeType.Smoke;
    [Tooltip("Which site/zone this point serves")]
    public StrategyZone zone = StrategyZone.NodeA;
    [Tooltip("Entry = flash the take, Cut = smoke the rotation, Hold = molly the camp spot, Anchor = CT denial on the approach")]
    public UtilityPurpose purpose = UtilityPurpose.Cut;
    [Tooltip("Higher = thrown first when utility is scarce")]
    public int priority;

    public static readonly List<UtilityPoint> All = new List<UtilityPoint>();
    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void OnDrawGizmos()
    {
        Gizmos.color = type switch
        {
            GrenadeType.Flash => new Color(1f, 0.9f, 0.3f, 0.9f),
            GrenadeType.Molotov => new Color(1f, 0.45f, 0.15f, 0.9f),
            _ => new Color(0.75f, 0.75f, 0.75f, 0.9f)
        };
        Gizmos.DrawWireSphere(transform.position, 0.6f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.5f);
    }
}
