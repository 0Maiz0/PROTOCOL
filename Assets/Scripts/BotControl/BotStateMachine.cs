using UnityEngine;

public class BotStateMachine : MonoBehaviour
{
    public BotState CurrentState { get; private set; } = BotState.Idle;

    public event System.Action<BotState, BotState> OnStateChanged;

    public void TransitionTo(BotState newState)
    {
        if (CurrentState == newState) return;
        if (CurrentState == BotState.Dead) return; // Can't leave dead state

        BotState oldState = CurrentState;
        CurrentState = newState;
        OnStateChanged?.Invoke(oldState, newState);
    }

    public void ResetForRound()
    {
        CurrentState = BotState.Idle;
    }

    /// <summary>
    /// Check if bot is in a combat state (shooting or engaged).
    /// </summary>
    public bool IsInCombat => CurrentState == BotState.Shooting ||
                              CurrentState == BotState.Engaged ||
                              CurrentState == BotState.Reloading;
}
