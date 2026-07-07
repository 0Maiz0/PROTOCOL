using UnityEngine;

[CreateAssetMenu(fileName = "NewPlayer", menuName = "ETS/Player Data")]
public class PlayerData : ScriptableObject
{
    [Header("Identity")]
    public string playerName;
    public string nickname;
    public string nationality;
    public Sprite portrait;

    [Header("Visible Stats (FPL)")]
    [Range(1, 100)] public int aim = 50;
    [Range(1, 100)] public int reflexes = 50;
    [Range(1, 100)] public int gameSense = 50;

    [Header("Hidden Stats (revealed after signing)")]
    [Range(1, 100)] public int clutch = 50;
    [Range(1, 100)] public int consistency = 50;
    [Range(1, 100)] public int communication = 50;

    [Header("Personality (hidden until signed)")]
    [Range(1, 100)] public int ego = 30;
    [Range(1, 100)] public int workEthic = 50;
    [Range(1, 100)] public int toxicity = 10;
    [Range(1, 100)] public int leadership = 30;

    [Header("Contract")]
    public int salaryPerMonth = 500;
    public Role preferredRole = Role.Entry;
}

