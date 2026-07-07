using UnityEngine;

[CreateAssetMenu(fileName = "Equipment", menuName = "ETS/Equipment Setup")]
public class EquipmentBonuses : ScriptableObject
{
    [Header("PC")]
    public PCTier pcTier = PCTier.Budget;

    [Header("Monitor")]
    public MonitorTier monitorTier = MonitorTier.Hz60;

    [Header("Peripherals")]
    [Range(0, 5)] public int mouseBonus = 0;
    [Range(0, 5)] public int headsetBonus = 0;
    [Range(0, 3)] public int chairBonus = 0;

    public int GetAimBonus()
    {
        int bonus = mouseBonus;
        bonus += pcTier switch
        {
            PCTier.Budget => 0,
            PCTier.Mid => 5,
            PCTier.HighEnd => 10,
            _ => 0
        };
        return bonus;
    }

    public int GetReflexBonus()
    {
        return monitorTier switch
        {
            MonitorTier.Hz60 => 0,
            MonitorTier.Hz144 => 5,
            MonitorTier.Hz240 => 10,
            _ => 0
        };
    }

    public int GetCommunicationBonus() => headsetBonus;
    public int GetConsistencyBonus() => chairBonus;
}

