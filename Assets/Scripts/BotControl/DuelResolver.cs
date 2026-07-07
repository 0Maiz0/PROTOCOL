using UnityEngine;

public static class DuelResolver
{
    public struct DuelContext
    {
        public PlayerProfile attacker;
        public PlayerProfile defender;
        public BuyType attackerBuy;
        public BuyType defenderBuy;
        public float strategyCounterBonus; // 0 or STRATEGY_COUNTER_BONUS
        public bool isClutch; // 1vX situation
        public int clutchPlayersRemaining; // X in 1vX
    }

    public struct DuelOutcome
    {
        public PlayerProfile winner;
        public PlayerProfile loser;
        public float winnerScore;
        public bool wasUpset; // lower-rated player won
    }

    public static DuelOutcome Resolve(DuelContext ctx)
    {
        float attackerScore = CalculateScore(ctx.attacker, ctx.defender,
            ctx.attackerBuy, ctx.strategyCounterBonus, ctx.isClutch);

        float defenderScore = CalculateScore(ctx.defender, ctx.attacker,
            ctx.defenderBuy, 0f, false); // defender doesn't get counter bonus

        // Normalize to win probability
        float attackerWinChance = attackerScore / (attackerScore + defenderScore);

        bool attackerWins = Random.value < attackerWinChance;

        return new DuelOutcome
        {
            winner = attackerWins ? ctx.attacker : ctx.defender,
            loser = attackerWins ? ctx.defender : ctx.attacker,
            winnerScore = attackerWins ? attackerScore : defenderScore,
            wasUpset = attackerWins
                ? ctx.attacker.EffectiveAim < ctx.defender.EffectiveAim
                : ctx.defender.EffectiveAim < ctx.attacker.EffectiveAim
        };
    }

    private static float CalculateScore(PlayerProfile player, PlayerProfile enemy,
        BuyType buyType, float counterBonus, bool isClutch)
    {
        float score = Constants.BASE_DUEL_CHANCE;

        // Stat differentials
        score += (player.EffectiveAim - enemy.EffectiveAim) * Constants.AIM_WEIGHT;
        score += (player.EffectiveReflexes - enemy.EffectiveReflexes) * Constants.REFLEX_WEIGHT;

        // Buy type
        score += GetBuyModifier(buyType);

        // Strategy counter
        score += counterBonus;

        // Clutch bonus
        if (isClutch)
        {
            score += (player.Data.clutch - Constants.CLUTCH_BASELINE) * Constants.CLUTCH_WEIGHT;
        }

        // Variance (consistency reduces it)
        float variance = Constants.MAX_VARIANCE
            - (player.Data.consistency * Constants.CONSISTENCY_REDUCTION);
        score += Random.Range(-variance, variance);

        return Mathf.Max(score, 5f); // Floor to prevent negative/zero
    }

    private static float GetBuyModifier(BuyType buy)
    {
        return buy switch
        {
            BuyType.Full => Constants.FULL_BUY_MODIFIER,
            BuyType.Force => Constants.FORCE_BUY_MODIFIER,
            BuyType.Half => Constants.HALF_BUY_MODIFIER,
            BuyType.Eco => Constants.ECO_MODIFIER,
            _ => 0f
        };
    }
}
