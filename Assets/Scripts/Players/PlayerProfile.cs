using UnityEngine;

[System.Serializable]
public class PlayerProfile
{
    public PlayerData Data { get; private set; }
    public EquipmentBonuses Equipment { get; private set; }
    public Role AssignedRole { get; set; }
    public bool IsAlive { get; set; } = true;
    public int RoundKills { get; set; }
    public int MatchKills { get; set; }
    public int MatchDeaths { get; set; }

    // ===================== COMBAT STATE =====================

    public float HP { get; set; } = 100f;
    public int CurrentAmmo { get; set; }
    public float SprayCounter { get; set; }     // Accumulates per shot, degrades accuracy
    public bool IsReloading { get; set; }
    public float ReloadTimer { get; set; }

    // Weapon assigned at buy phase
    public WeaponData.WeaponStats CurrentWeapon { get; set; }

    public PlayerProfile(PlayerData data, EquipmentBonuses equipment)
    {
        Data = data;
        Equipment = equipment;
        ResetCombatState(BuyType.Full);
    }

    // Effective stats = base + equipment
    public int EffectiveAim => Data.aim + Equipment.GetAimBonus();
    public int EffectiveReflexes => Data.reflexes + Equipment.GetReflexBonus();
    public int EffectiveCommunication => Data.communication + Equipment.GetCommunicationBonus();
    public int EffectiveConsistency => Data.consistency + Equipment.GetConsistencyBonus();

    // ===================== COMBAT METHODS =====================

    /// <summary>
    /// Reset HP, ammo, spray for a new round. Weapon assigned by buy type.
    /// </summary>
    public void ResetCombatState(BuyType buyType)
    {
        ResetCombatState(WeaponData.GetWeapon(buyType, AssignedRole));
    }

    /// <summary>
    /// Reset with an explicit weapon — economy buys per player now (v3),
    /// the team tier alone no longer decides the loadout.
    /// </summary>
    public void ResetCombatState(WeaponData.WeaponStats weapon)
    {
        HP = 100f;
        SprayCounter = 0f;
        IsReloading = false;
        ReloadTimer = 0f;
        CurrentWeapon = weapon;
        CurrentAmmo = weapon.magazineSize;
    }

    /// <summary>
    /// Take damage. Returns true if this kills the player.
    /// </summary>
    public bool TakeDamage(float damage)
    {
        if (!IsAlive) return false;
        HP -= damage;
        if (HP <= 0f)
        {
            HP = 0f;
            return true; // Dead
        }
        return false;
    }

    /// <summary>
    /// Fire a shot. Increases spray, decreases ammo.
    /// Returns true if actually fired (has ammo and not reloading).
    /// </summary>
    public bool FireShot()
    {
        if (IsReloading || CurrentAmmo <= 0) return false;

        CurrentAmmo--;
        SprayCounter += CurrentWeapon.sprayGrowth;

        if (CurrentAmmo <= 0)
        {
            StartReload();
        }

        return true;
    }

    /// <summary>
    /// Start a reload cycle.
    /// </summary>
    public void StartReload()
    {
        IsReloading = true;
        ReloadTimer = CurrentWeapon.reloadTime;
    }

    /// <summary>
    /// Tick reload timer. Returns true when reload is complete.
    /// </summary>
    public bool TickReload(float deltaTime)
    {
        if (!IsReloading) return false;

        ReloadTimer -= deltaTime;
        if (ReloadTimer <= 0f)
        {
            IsReloading = false;
            ReloadTimer = 0f;
            CurrentAmmo = CurrentWeapon.magazineSize;
            SprayCounter = 0f; // Reset spray on reload
            return true;
        }
        return false;
    }

    /// <summary>
    /// Calculate hit chance for a single shot.
    /// Based on: aim stat, weapon accuracy, distance, spray, consistency.
    /// </summary>
    public float CalculateHitChance(float distance)
    {
        var weapon = CurrentWeapon;

        // Base: weapon accuracy * (aim/100)
        float aimFactor = EffectiveAim / 100f;
        float hitChance = weapon.baseAccuracy * (0.5f + aimFactor * 0.5f);

        // Spray penalty — accuracy degrades the more you fire continuously
        float sprayPenalty = Mathf.Clamp01(1f - SprayCounter);
        float minAccuracy = weapon.maxSpray;
        hitChance *= Mathf.Max(sprayPenalty, minAccuracy);

        // Distance falloff — closer = more accurate
        float distRatio = Mathf.Clamp01(distance / weapon.range);
        float distMultiplier = Mathf.Lerp(1f, weapon.rangeFalloff, distRatio);
        hitChance *= distMultiplier;

        // Consistency — high consistency = more reliable (Effective = base + chair bonus)
        float consistencyFactor = 1f - (Constants.MAX_VARIANCE - EffectiveConsistency * Constants.CONSISTENCY_REDUCTION) / 100f;
        hitChance *= Mathf.Lerp(0.85f, 1.1f, consistencyFactor);

        // First-bullet accuracy — a settled first shot is laser-like in CS
        if (SprayCounter <= 0.01f)
            hitChance *= Constants.FIRST_SHOT_BONUS;

        return Mathf.Clamp(hitChance, 0.05f, 0.95f);
    }

    /// <summary>
    /// Calculate damage for a hit at given distance. Rolls a headshot —
    /// aim-scaled chance, rifles one-tap to the head like an AK.
    /// </summary>
    public float CalculateDamage(float distance, out bool headshot)
    {
        var weapon = CurrentWeapon;

        float distRatio = Mathf.Clamp01(distance / weapon.range);
        float distMultiplier = Mathf.Lerp(1f, weapon.rangeFalloff, distRatio);

        // Some random variance in damage (± 15%)
        float variance = Random.Range(0.85f, 1.15f);
        float damage = weapon.damage * distMultiplier * variance;

        float hsChance = Constants.HEADSHOT_BASE_CHANCE + (EffectiveAim / 100f) * Constants.HEADSHOT_AIM_SCALE;
        headshot = Random.value < hsChance;
        if (headshot) damage *= Constants.HEADSHOT_MULTIPLIER;

        return damage;
    }

    /// <summary>
    /// Decay spray when NOT actively shooting (between shots / idle).
    /// </summary>
    public void DecaySpray(float deltaTime)
    {
        if (SprayCounter > 0f)
        {
            SprayCounter -= deltaTime * 0.5f; // Slow spray recovery
            SprayCounter = Mathf.Max(0f, SprayCounter);
        }
    }
}
