using UnityEngine;

/// <summary>
/// Weapon stats tied to BuyType. Each buy level = different weapon tier.
/// Eco = Pistol, Half = SMG, Force = Galil/Famas, Full = AK/M4, AWPer role gets AWP on Full buy.
/// 
/// Used by EngagementSystem to determine range, damage, fire rate, spray.
/// </summary>
public static class WeaponData
{
    public struct WeaponStats
    {
        public string weaponName;      // class id — gameplay checks key off this ("AWP", "Pistol", "SMG")
        public string displayName;     // side-correct label set at loadout time (empty = weaponName)
        public float range;           // Max effective engagement range (units)
        public float damage;           // Base damage per hit
        public float fireInterval;     // Seconds between shots
        public int magazineSize;       // Shots before reload
        public float reloadTime;       // Seconds to reload
        public float baseAccuracy;     // 0-1, base hit chance at close range
        public float sprayGrowth;      // How fast accuracy degrades per shot (spray)
        public float maxSpray;         // Worst accuracy from spray (lower = worse)
        public float rangeFalloff;     // Damage multiplier at max range (0-1)
    }

    // ===================== WEAPON TIERS =====================

    public static WeaponStats Pistol => new WeaponStats
    {
        weaponName = "Pistol",
        range = 18f,
        damage = 24f,
        fireInterval = 0.35f,
        magazineSize = 13,
        reloadTime = 2.0f,
        baseAccuracy = 0.68f,
        sprayGrowth = 0.03f,
        maxSpray = 0.30f,
        rangeFalloff = 0.5f
    };

    public static WeaponStats SMG => new WeaponStats
    {
        weaponName = "SMG",
        range = 25f,
        damage = 22f,
        fireInterval = 0.12f,
        magazineSize = 30,
        reloadTime = 2.5f,
        baseAccuracy = 0.62f,
        sprayGrowth = 0.04f,
        maxSpray = 0.20f,
        rangeFalloff = 0.45f
    };

    public static WeaponStats Rifle_Light => new WeaponStats
    {
        weaponName = "Galil/Famas",
        range = 35f,
        damage = 28f,
        fireInterval = 0.11f,
        magazineSize = 25,
        reloadTime = 2.8f,
        baseAccuracy = 0.66f,
        sprayGrowth = 0.045f,
        maxSpray = 0.18f,
        rangeFalloff = 0.6f
    };

    public static WeaponStats Rifle => new WeaponStats
    {
        weaponName = "AK-47/M4",
        range = 42f,
        damage = 33f,
        fireInterval = 0.10f,
        magazineSize = 30,
        reloadTime = 2.5f,
        baseAccuracy = 0.66f,
        sprayGrowth = 0.05f,
        maxSpray = 0.15f,
        rangeFalloff = 0.7f
    };

    public static WeaponStats AWP => new WeaponStats
    {
        weaponName = "AWP",
        range = 70f, // 55 caused 60m sniper stare-downs — out of range = invisible to the scan
        damage = 110f,
        fireInterval = 1.9f, // bolt cycle + re-scope (log: AWP duo = 49 of 97 kills — trimmed again)
        magazineSize = 5,
        reloadTime = 3.5f,
        baseAccuracy = 0.85f, // playtest #6: re-peek nerf
        sprayGrowth = 0f,      // No spray — single shots
        maxSpray = 0.90f,
        rangeFalloff = 0.95f    // Almost no falloff — long range beast
    };

    // ===================== LOOKUP =====================

    /// <summary>
    /// Get weapon stats based on buy type and player role.
    /// AWPer on Full buy gets AWP, everyone else gets AK/M4.
    /// </summary>
    public static WeaponStats GetWeapon(BuyType buyType, Role role = Role.Support)
    {
        return buyType switch
        {
            BuyType.Eco => Pistol,
            BuyType.Half => SMG,
            BuyType.Force => Rifle_Light,
            BuyType.Full => (role == Role.AWPer) ? AWP : Rifle,
            _ => Pistol
        };
    }

    /// <summary>
    /// Get effective range for engagement detection (used by EngagementSystem).
    /// Returns the max sight range for this buy type.
    /// </summary>
    public static float GetEngagementRange(BuyType buyType, Role role = Role.Support)
    {
        return GetWeapon(buyType, role).range;
    }
}
