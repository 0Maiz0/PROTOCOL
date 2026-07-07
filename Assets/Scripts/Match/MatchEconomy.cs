using UnityEngine;

/// <summary>
/// Team Economy v2 — Proper CS-style buy logic.
/// 
/// KEY RULES:
/// - Can afford Full ($4100)? → Full buy
/// - Can't afford Full? → ECO ($500) to save for next round
/// - EXCEPTION: Force buy ($2500) only when:
///   - 2+ consecutive ecos already (can't keep saving forever)
///   - Enemy at match point (must win or lose match)
///   - After pistol round win (round 2 = force up with bonus)
/// - Round 1 = always Eco (pistol round, only $800)
/// - After winning = Full buy (winner gets $3250 + kills)
/// 
/// This creates the CS rhythm: Win → Full, Lose → Eco, Eco, Full (with loss bonus)
/// </summary>
[System.Serializable]
public class TeamEconomy
{
    public int credits = 800;
    public int consecutiveLosses = 0;
    public BuyType currentBuyType = BuyType.Eco;

    // Track eco history for force-buy decisions
    public int consecutiveEcos = 0;
    public bool wonLastRound = false;
    public bool isFirstRound = true;

    public void AddWinReward()
    {
        credits += Constants.WIN_REWARD;
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
        credits += lossBonus;
    }

    public void AddKillBonus(int kills)
    {
        credits += kills * Random.Range(Constants.KILL_BONUS_MIN, Constants.KILL_BONUS_MAX + 1);
    }

    public void AddPlantBonus()
    {
        credits += Constants.PLANT_BONUS;
    }

    /// <summary>
    /// Smart buy decision — mimics real CS economy flow.
    /// </summary>
    public void ApplyBuy(int enemyScore = 0)
    {
        // Round 1 = pistol round, always eco (only $800)
        if (isFirstRound)
        {
            currentBuyType = BuyType.Eco;
            isFirstRound = false;
            credits = Mathf.Max(credits - GetBuyCost(BuyType.Eco), 0);
            consecutiveEcos = 0; // Pistol doesn't count as eco
            Debug.Log($"  BUY: Pistol round → Eco (${credits} left)");
            return;
        }

        // === CAN AFFORD FULL? → FULL BUY ===
        if (CanAfford(BuyType.Full))
        {
            currentBuyType = BuyType.Full;
            consecutiveEcos = 0;
            credits = Mathf.Max(credits - GetBuyCost(BuyType.Full), 0);
            Debug.Log($"  BUY: Full buy (${credits} left)");
            return;
        }

        // === CAN'T AFFORD FULL — Should we eco or force? ===

        bool shouldForce = false;

        // Force if: enemy at match point (12 rounds) — must win or lose match
        if (enemyScore >= Constants.ROUNDS_TO_WIN - 1)
            shouldForce = true;

        // Force if: already eco'd 2+ rounds in a row (can't keep saving)
        if (consecutiveEcos >= 2)
            shouldForce = true;

        // Force if: won last round (round 2 after pistol win = force with bonus money)
        if (wonLastRound && credits >= GetBuyCost(BuyType.Force))
            shouldForce = true;

        // Force if: have enough that even after force, next round could full buy with win reward
        // (credits - forceCost + winReward >= fullCost)
        if (CanAfford(BuyType.Force))
        {
            int afterForce = credits - GetBuyCost(BuyType.Force);
            if (afterForce + Constants.WIN_REWARD >= GetBuyCost(BuyType.Full))
                shouldForce = true;
        }

        if (shouldForce && CanAfford(BuyType.Force))
        {
            currentBuyType = BuyType.Force;
            consecutiveEcos = 0;
            credits = Mathf.Max(credits - GetBuyCost(BuyType.Force), 0);
            Debug.Log($"  BUY: Force buy! (${credits} left) [reason: {(enemyScore >= 12 ? "match point" : consecutiveEcos >= 2 ? "too many ecos" : "can afford")}]");
            return;
        }

        // === ECO — Save for next round ===
        currentBuyType = BuyType.Eco;
        consecutiveEcos++;
        credits = Mathf.Max(credits - GetBuyCost(BuyType.Eco), 0);
        Debug.Log($"  BUY: ECO save (${credits} left, eco streak: {consecutiveEcos})");
    }

    public bool CanAfford(BuyType type)
    {
        return credits >= GetBuyCost(type);
    }

    public static int GetBuyCost(BuyType type)
    {
        return type switch
        {
            BuyType.Full => 4100,
            BuyType.Force => 2500,
            BuyType.Half => 1800,
            BuyType.Eco => 500,
            _ => 0
        };
    }

    public void Reset()
    {
        credits = 800;
        consecutiveLosses = 0;
        consecutiveEcos = 0;
        wonLastRound = false;
        isFirstRound = true;
        currentBuyType = BuyType.Eco;
    }
}

public class MatchEconomy : MonoBehaviour
{
    public TeamEconomy attackerEconomy = new TeamEconomy();
    public TeamEconomy defenderEconomy = new TeamEconomy();

    // Track scores for match-point force buy decisions
    private int attackerScore = 0;
    private int defenderScore = 0;

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

    public void BuyPhase()
    {
        // Pass enemy score so teams know if it's match point
        attackerEconomy.ApplyBuy(defenderScore);
        defenderEconomy.ApplyBuy(attackerScore);

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
