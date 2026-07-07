using UnityEngine;

/// <summary>
/// Runtime career data for one player — lives in the roster or the scouting pool.
/// Plain serializable class (JSON save in Phase B), NOT a ScriptableObject.
/// </summary>
[System.Serializable]
public class CareerPlayer
{
    [Header("Identity")]
    public string playerName;
    public string nickname;
    public string nationality;
    public int age;

    [Header("Visible Stats (FPL)")]
    public int aim;
    public int reflexes;
    public int gameSense;

    [Header("Hidden Stats (revealed after signing)")]
    public int clutch;
    public int consistency;
    public int communication;

    [Header("Personality (hidden until signed)")]
    public int ego;
    public int workEthic;
    public int toxicity;
    public int leadership;

    [Header("Contract")]
    public int salaryPerMonth;
    public Role preferredRole;
    public Role assignedRole; // role they actually play in matches (coach can override preference)
    public bool isSigned;

    public int Overall => Mathf.RoundToInt((aim + reflexes + gameSense) / 3f);

    /// <summary>
    /// Bridge to the match engine — builds a runtime PlayerData instance.
    /// The engine keeps consuming PlayerData and never knows career players exist.
    /// </summary>
    public PlayerData ToPlayerData()
    {
        var data = ScriptableObject.CreateInstance<PlayerData>();
        data.playerName = playerName;
        data.nickname = nickname;
        data.nationality = nationality;
        data.aim = aim;
        data.reflexes = reflexes;
        data.gameSense = gameSense;
        data.clutch = clutch;
        data.consistency = consistency;
        data.communication = communication;
        data.ego = ego;
        data.workEthic = workEthic;
        data.toxicity = toxicity;
        data.leadership = leadership;
        data.salaryPerMonth = salaryPerMonth;
        data.preferredRole = assignedRole; // engine picks weapons/behavior by this
        return data;
    }
}
