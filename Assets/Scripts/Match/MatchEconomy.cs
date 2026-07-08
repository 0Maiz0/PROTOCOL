using UnityEngine;

/// <summary>
/// Team Economy v3 — CS2-style with per-player loadouts.
///
/// The team pool = 5 players' wallets combined. The coach (player or AI)
/// picks a buy TIER (Full/Force/Half/Eco); at loadout time each player
/// BUYS INDIVIDUALLY from the pool with role priority (AWPer buys the AWP
/// first on a full buy) and automatic downgrades when the pool runs dry.
///
/// CS rhythm this reproduces:
/// - Round 1 pistol ($800 × 5) → winner forces round 2 (SMG/light rifles),
///   loser ecos round 2 and buys round 3 on the loss bonus
/// - AWP appears when the pool actually affords the $6500 slot — round 3+
/// - Loss bonus ladder $1400→$3400 (×5 pooled), resets on a win
/// - Plant bonus paid even on a lost round
/// </summary>
[System.Serializable]
public class TeamEconomy
{
    public int credits = Constants.TEAM_START_CREDITS;
    public int consecutiveLosses = 0;
    public BuyType currentBuyType = BuyType.Eco;
    public BuyType recommendedBuy = BuyType.Eco; // shown to the coach as the default

    public int consecutiveEcos = 0;
    public bool wonLastRound = false;
    public bool isFirstRound = true;

    public void AddWinReward()
    {
        credits = Mathf.Min(credits + Constants.WIN_REWARD, Constants.TEAM_CREDITS_CAP);
        consecutiveLosses = 0;
        wonLastRound = true;
    }

    public void AddLossReward()
    {
        consecutiveLosses++;
        wonLastRound = false;
        int lossBonus = Mathf.Min(
            Constants.LOSS_BONUS_START + (consecutiveLosses - 1) * Constants.LOSS_BONUS_INCREMENT,
            Constants.LOSS_BONUS_MAX);
        credits = Mathf.Min(credits + lossBonus, Constants.TEAM_CREDITS_CAP);
    }

    public void AddKillBonus(int kills)
    {
        credits = Mathf.Min(
            credits + kills * Random.Range(Constants.KILL_BONUS_MIN, Constants.KILL_BONUS_MAX + 1),
            Constants.TEAM_CREDITS_CAP);
    }

    public void AddPlantBonus()
    {
        credits = Mathf.Min(credits + Constants.PLANT_BONUS, Constants.TEAM_CREDITS_CAP);
    }

    // ===================== BUY DECISION =====================

    public int EstimateTierCost(BuyType t) => t switch
    {
        BuyType.Full => Constants.BUY_COST_FULL * 5,
        BuyType.Force => Constants.BUY_COST_FORCE * 5,
        BuyType.Half => Constants.BUY_COST_HALF * 5,
        _ => Constants.BUY_COST_ECO * 5
    };

    public bool CanAffordTier(BuyType t) => credits >= EstimateTierCost(t);

    /// <summary>
    /// The IGL's buy call. Discipline comes from team protocols:
    /// strict teams save properly, loose cannons force whenever they can.
    /// </summary>
    public BuyType ChooseBuy(int enemyScore, int discipline = 50)
    {
        if (isFirstRound) return BuyType.Eco; // pistol round

        if (CanAffordTier(BuyType.Full)) return BuyType.Full;

        bool mustWin = enemyScore >= Constants.ROUNDS_TO_WIN - 1; // match point — no point saving
        int ecoStreakNeeded = discipline >= 70 ? 2 : 1;
        // Desperation: 3+ losses in a row — stop the bleed, buy SOMETHING
        // (log evidence: CT sat on $10331 at 0-3 and picked Eco)
        bool shouldForce = mustWin || consecutiveEcos >= ecoStreakNeeded || wonLastRound
                        || consecutiveLosses >= 3;
        if (discipline < 40) shouldForce |= CanAffordTier(BuyType.Force); // force-happy squad
        if (shouldForce && CanAffordTier(BuyType.Force)) return BuyType.Force;
        if (shouldForce && CanAffordTier(BuyType.Half)) return BuyType.Half;

        return BuyType.Eco;
    }

    public void SetBuy(BuyType t)
    {
        currentBuyType = t;
        if (isFirstRound) { isFirstRound = false; consecutiveEcos = 0; return; } // pistol doesn't count as eco
        if (t == BuyType.Eco) consecutiveEcos++;
        else consecutiveEcos = 0;
    }

    // ===================== PER-PLAYER LOADOUT =====================

    /// <summary>
    /// Buy one player's weapon from the pool. Deducts credits;
    /// downgrades automatically when the pool can't cover the tier.
    /// AWPer should buy FIRST on a full buy so the pool affords the big green.
    /// </summary>
    public WeaponData.WeaponStats BuyWeaponFor(Role role)
    {
        switch (currentBuyType)
        {
            case BuyType.Eco:
                TrySpend(Constants.BUY_COST_ECO);
                return WeaponData.Pistol;

            case BuyType.Half:
                if (TrySpend(Constants.BUY_COST_HALF)) return WeaponData.SMG;
                TrySpend(Constants.BUY_COST_ECO);
                return WeaponData.Pistol;

            case BuyType.Force:
                if (TrySpend(Constants.BUY_COST_FORCE)) return WeaponData.Rifle_Light;
                if (TrySpend(Constants.BUY_COST_HALF)) return WeaponData.SMG;
                TrySpend(Constants.BUY_COST_ECO);
                return WeaponData.Pistol;

            default: // Full
                if (role == Role.AWPer && TrySpend(Constants.BUY_COST_FULL_AWP)) return WeaponData.AWP;
                if (TrySpend(Constants.BUY_COST_FULL)) return WeaponData.Rifle;
                if (TrySpend(Constants.BUY_COST_FORCE)) return WeaponData.Rifle_Light;
                if (TrySpend(Constants.BUY_COST_HALF)) return WeaponData.SMG;
                TrySpend(Constants.BUY_COST_ECO);
                return WeaponData.Pistol;
        }
    }

    private bool TrySpend(int cost)
    {
        if (credits < cost) return false;
        credits -= cost;
        return true;
    }

    public void Reset()
    {
        credits = Constants.TEAM_START_CREDITS;
        consecutiveLosses = 0;
        consecutiveEcos = 0;
        wonLastRound = false;
        isFirstRound = true;
        currentBuyType = BuyType.Eco;
        recommendedBuy = BuyType.Eco;
    }
}

public class MatchEconomy : MonoBehaviour
{
    public TeamEconomy attackerEconomy = new TeamEconomy();
    public TeamEconomy defenderEconomy = new TeamEconomy();

    // Track scores for match-point force buy decisions
    private int attackerScore = 0;
    private int defenderScore = 0;

    private void Awake()
    {
        // The scene serializes TeamEconomy with STALE v1 values (credits 800!) —
        // field initializers never run for serialized classes. Hard reset.
        // Log evidence: R01 "BUY: T Eco ($300)" — start bank was 800, not 4000.
        attackerEconomy.Reset();
        defenderEconomy.Reset();
        attackerScore = 0;
        defenderScore = 0;
    }

    public TeamEconomy GetEconomy(TeamSide side) =>
        side == TeamSide.Attacker ? attackerEconomy : defenderEconomy;

    public void ProcessRoundEnd(TeamSide winner, int attackerKills, int defenderKills, bool bombPlanted)
    {
        if (winner == TeamSide.Attacker)
        {
            attackerEconomy.AddWinReward();
            defenderEconomy.AddLossReward();
            attackerScore++;
        }
        else
        {
            defenderEconomy.AddWinReward();
            attackerEconomy.AddLossReward();
            defenderScore++;
        }

        attackerEconomy.AddKillBonus(attackerKills);
        defenderEconomy.AddKillBonus(defenderKills);

        if (bombPlanted)
            attackerEconomy.AddPlantBonus();

        Debug.Log($"ECONOMY | T: ${attackerEconomy.credits} | CT: ${defenderEconomy.credits}");
    }

    /// <summary>
    /// Freeze-time buys — both IGLs call their own economy. The player side
    /// follows team protocols (discipline). On a tactical timeout the coach
    /// overrides afterwards via ApplyPlayerBuy (playerAuto = false skips SetBuy
    /// so the eco-streak isn't double counted).
    /// </summary>
    public void DecideBuys(TeamSide playerSide, int playerDiscipline, bool playerAuto)
    {
        TeamEconomy playerEcon = GetEconomy(playerSide);
        TeamEconomy aiEcon = playerSide == TeamSide.Attacker ? defenderEconomy : attackerEconomy;
        int playerScore = playerSide == TeamSide.Attacker ? attackerScore : defenderScore;
        int aiScore = playerSide == TeamSide.Attacker ? defenderScore : attackerScore;

        aiEcon.SetBuy(aiEcon.ChooseBuy(playerScore));
        playerEcon.recommendedBuy = playerEcon.ChooseBuy(aiScore, playerDiscipline);
        if (playerAuto)
        {
            playerEcon.SetBuy(playerEcon.recommendedBuy);
            Debug.Log($"BUY | T: {attackerEconomy.currentBuyType} (${attackerEconomy.credits}) " +
                      $"| CT: {defenderEconomy.currentBuyType} (${defenderEconomy.credits})");
        }
    }

    /// <summary>
    /// Coach confirmed (or timed out with the recommendation).
    /// </summary>
    public void ApplyPlayerBuy(TeamSide playerSide, BuyType chosen)
    {
        GetEconomy(playerSide).SetBuy(chosen);
        Debug.Log($"BUY | T: {attackerEconomy.currentBuyType} (${attackerEconomy.credits}) " +
                  $"| CT: {defenderEconomy.currentBuyType} (${defenderEconomy.credits})");
    }

    public void SwapSides()
    {
        (attackerEconomy, defenderEconomy) = (defenderEconomy, attackerEconomy);
        (attackerScore, defenderScore) = (defenderScore, attackerScore);
        attackerEconomy.Reset();
        defenderEconomy.Reset();
    }
}
