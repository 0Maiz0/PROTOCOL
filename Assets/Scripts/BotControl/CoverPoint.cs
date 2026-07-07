using UnityEngine;

/// <summary>
/// Marks a position as a cover point for bot AI.
/// Place on boxes, walls, pillars, or any object bots should hide behind.
/// 
/// SETUP:
/// 1. Add this component to cover objects in the scene
/// 2. OR tag objects with "Cover" tag for auto-detection (CoverFinder)
/// 3. Set peekDirection = direction the bot peeks FROM this cover
///    (usually toward the most common engagement angle)
/// 
/// The bot will stand at this transform's position when "in cover"
/// and peek by stepping out in peekDirection.
/// </summary>
public class CoverPoint : MonoBehaviour
{
    [Header("Cover Properties")]
    [Tooltip("Direction bot peeks from this cover (local forward is default)")]
    public Vector3 peekDirection = Vector3.forward;

    [Tooltip("How far bot steps out to peek (units)")]
    public float peekDistance = 1.5f;

    [Tooltip("Can this cover be used by defenders holding site?")]
    public bool suitableForDefenders = true;

    [Tooltip("Can this cover be used during post-plant?")]
    public bool suitableForPostPlant = true;

    [Tooltip("Associated site — leave empty for general cover")]
    public string associatedSite = "";

    [Header("Runtime")]
    [HideInInspector] public BotController occupiedBy = null;

    public bool IsOccupied => occupiedBy != null && occupiedBy.Profile != null && occupiedBy.Profile.IsAlive;

    public Vector3 PeekPosition => transform.position + transform.TransformDirection(peekDirection.normalized) * peekDistance;

    public void Release()
    {
        occupiedBy = null;
    }

    private void OnDrawGizmos()
    {
        // Cover position
        Gizmos.color = IsOccupied ? Color.red : Color.green;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.4f);

        // Peek direction
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, PeekPosition + Vector3.up * 0.5f);
        Gizmos.DrawWireSphere(PeekPosition + Vector3.up * 0.5f, 0.2f);
    }
}
