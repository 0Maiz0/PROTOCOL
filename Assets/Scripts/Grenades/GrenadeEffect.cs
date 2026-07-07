using UnityEngine;
using System.Collections.Generic;

public enum GrenadeType { Smoke, Flash, Molotov }

/// <summary>
/// Active grenade effect zone on the map.
/// Smoke: blocks LOS. Flash: accuracy debuff. Molotov: area damage.
/// Created by GrenadeManager, auto-destroys after duration.
/// </summary>
public class GrenadeEffect : MonoBehaviour
{
    public GrenadeType Type;
    public TeamSide ThrowerSide; // bots don't push ENEMY smokes; mollies block everyone
    public float Radius = 4f;
    public float Duration = 15f;
    public float DamagePerSecond = 30f;
    public bool IsActive { get; private set; } = true;

    private float timer;
    private GameObject visualObj;
    private float tickTimer;
    private HashSet<int> flashedBots = new HashSet<int>();

    // Registry — IsBlockedBySmoke runs inside every LoS check (hot path),
    // FindObjectsByType there was scanning the scene dozens of times per frame
    public static readonly List<GrenadeEffect> ActiveEffects = new List<GrenadeEffect>();

    private void OnEnable() { ActiveEffects.Add(this); }
    private void OnDisable() { ActiveEffects.Remove(this); }

    private void Start()
    {
        timer = Duration;
        CreateVisual();
        if (Type == GrenadeType.Flash)
        {
            DetonateFlash();
            timer = 0.5f;
        }
    }

    private void Update()
    {
        if (!IsActive) return;
        timer -= Time.deltaTime;
        if (timer <= 0f) { Deactivate(); return; }

        if (Type == GrenadeType.Molotov)
        {
            tickTimer -= Time.deltaTime;
            if (tickTimer <= 0f)
            {
                tickTimer = 0.5f;
                TickMolotovDamage();
            }
        }

        if (timer < 2f && visualObj != null)
        {
            Renderer r = visualObj.GetComponent<Renderer>();
            if (r != null) { Color c = r.material.color; c.a = (timer / 2f) * 0.5f; r.material.color = c; }
        }
    }

    // ===================== SMOKE LOS BLOCKER =====================

    public static bool IsBlockedBySmoke(Vector3 from, Vector3 to)
    {
        for (int i = 0; i < ActiveEffects.Count; i++)
        {
            var fx = ActiveEffects[i];
            if (fx == null || !fx.IsActive || fx.Type != GrenadeType.Smoke) continue;
            if (LineHitsSphere(from, to, fx.transform.position, fx.Radius))
                return true;
        }
        return false;
    }

    private static bool LineHitsSphere(Vector3 a, Vector3 b, Vector3 c, float r)
    {
        Vector3 d = b - a, f = a - c;
        float A = Vector3.Dot(d, d);
        float B = 2f * Vector3.Dot(f, d);
        float C = Vector3.Dot(f, f) - r * r;
        float disc = B * B - 4f * A * C;
        if (disc < 0) return false;
        float sq = Mathf.Sqrt(disc);
        float t1 = (-B - sq) / (2f * A), t2 = (-B + sq) / (2f * A);
        return (t1 >= 0f && t1 <= 1f) || (t2 >= 0f && t2 <= 1f) || (t1 < 0f && t2 > 1f);
    }

    // ===================== FLASH =====================

    private void DetonateFlash()
    {
        BotController[] bots = FindObjectsByType<BotController>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var bot in bots)
        {
            if (bot.Profile == null || !bot.Profile.IsAlive) continue;
            float dist = Vector3.Distance(transform.position, bot.transform.position);
            if (dist > Radius) continue;

            Vector3 dir = (transform.position - bot.transform.position).normalized;
            float dot = Vector3.Dot(bot.transform.forward, dir);
            float reflexFactor = Mathf.Clamp(1f - bot.Profile.EffectiveReflexes / 150f, 0.3f, 1f);
            float strength = ((dot > 0.3f) ? 1f : 0.4f) * reflexFactor * (1f - dist / Radius);

            // Teammates KNOW the pop is coming — they turn away (don't full-blind your own execute)
            if (bot.Side == ThrowerSide) strength *= 0.25f;

            if (strength > 0.1f)
            {
                bot.Profile.SprayCounter += strength * 0.8f;

                // ACTUAL blinding — a flashed player can't scan, fire or turn to fight.
                // (Previously flashes only nudged spray, which is why they "did nothing".)
                if (strength >= Constants.FLASH_BLIND_THRESHOLD)
                {
                    float blind = strength * Constants.FLASH_MAX_BLIND;
                    bot.Profile.BlindUntil = Mathf.Max(bot.Profile.BlindUntil, Time.time + blind);
                    if (blind > 1.2f)
                        CommsFeed.Post(bot.Profile.Data.nickname, "I'm FLASHED — can't see!", bot.Side);
                }
                count++;
            }
        }
        Debug.Log($"FLASH at {transform.position} — blinded {count} bots");
    }

    // ===================== MOLOTOV =====================

    private void TickMolotovDamage()
    {
        BotController[] bots = FindObjectsByType<BotController>(FindObjectsSortMode.None);
        foreach (var bot in bots)
        {
            if (bot.Profile == null || !bot.Profile.IsAlive) continue;
            float dist = Vector3.Distance(transform.position, bot.transform.position);
            if (dist > Radius) continue;

            float dmg = DamagePerSecond * 0.5f * (1f - dist / Radius);
            if (bot.Profile.TakeDamage(dmg))
            {
                bot.Die();
                Debug.Log($"MOLOTOV killed {bot.Profile.Data.nickname}!");
            }
        }
    }

    // ===================== VISUAL =====================

    private void CreateVisual()
    {
        visualObj = GameObject.CreatePrimitive(Type == GrenadeType.Molotov ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
        visualObj.transform.SetParent(transform);
        visualObj.transform.localPosition = Vector3.up * 0.5f;
        float s = Radius * 2f;
        visualObj.transform.localScale = Type == GrenadeType.Molotov
            ? new Vector3(s, 0.1f, s)
            : new Vector3(s, s * 0.6f, s);

        Collider col = visualObj.GetComponent<Collider>();
        if (col) Destroy(col);

        Renderer rend = visualObj.GetComponent<Renderer>();
        if (rend)
        {
            Material mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Type switch
            {
                GrenadeType.Smoke => new Color(0.8f, 0.8f, 0.8f, 0.35f),
                GrenadeType.Flash => new Color(1f, 1f, 0.9f, 0.8f),
                GrenadeType.Molotov => new Color(1f, 0.3f, 0f, 0.4f),
                _ => Color.white
            };
            rend.material = mat;
        }
    }

    private void Deactivate()
    {
        IsActive = false;
        if (visualObj) Destroy(visualObj);
        Destroy(gameObject, 0.1f);
    }

    private void OnDrawGizmos()
    {
        if (!IsActive) return;
        Gizmos.color = Type switch
        {
            GrenadeType.Smoke => new Color(0.8f, 0.8f, 0.8f, 0.3f),
            GrenadeType.Molotov => new Color(1f, 0.3f, 0f, 0.3f),
            _ => new Color(1f, 1f, 0f, 0.3f)
        };
        Gizmos.DrawWireSphere(transform.position, Radius);
    }
}
