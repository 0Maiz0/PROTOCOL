using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Place this on each bomb site GameObject (with a trigger collider).
/// </summary>
public class BombSite : MonoBehaviour
{
	public string siteName = "A"; // "A" or "B"
	public float plantRadius = 5f;

	private void OnDrawGizmos()
	{
		Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
		Gizmos.DrawWireSphere(transform.position, plantRadius);
		Gizmos.color = Color.green;
		Gizmos.DrawSphere(transform.position, 0.3f);

#if UNITY_EDITOR
		UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, $"SITE {siteName}");
#endif
	}

	public bool IsPlayerInSite(Vector3 position)
	{
		return Vector3.Distance(transform.position, position) <= plantRadius;
	}
}