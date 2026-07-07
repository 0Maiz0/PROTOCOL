using UnityEngine;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// JSON save/load for career state. Single file in Application.persistentDataPath.
/// </summary>
public static class SaveSystem
{
    private static string SavePath => Path.Combine(Application.persistentDataPath, Constants.SAVE_FILE_NAME);

    public static bool SaveExists() => File.Exists(SavePath);

    public static void Save(CareerSaveData data)
    {
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SAVE: failed to write career save — {e.Message}");
        }
    }

    /// <summary>
    /// Returns null when no save exists or the file is unreadable.
    /// </summary>
    public static CareerSaveData Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return null;
            return JsonUtility.FromJson<CareerSaveData>(File.ReadAllText(SavePath));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SAVE: failed to load career save — {e.Message}");
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SAVE: failed to delete career save — {e.Message}");
        }
    }
}

/// <summary>
/// Snapshot of everything the career needs to survive a session restart.
/// Equipment is stored as raw tiers because the runtime EquipmentBonuses SO
/// instance is rebuilt from them on load.
/// </summary>
[System.Serializable]
public class CareerSaveData
{
    public int saveVersion;
    public int budget;
    public TeamProtocols protocols;
    public int currentWeek;
    public List<CareerPlayer> roster;
    public List<CareerPlayer> scoutingPool;
    public List<MatchRecord> matchHistory;

    public PCTier pcTier;
    public MonitorTier monitorTier;
    public int mouseBonus;
    public int headsetBonus;
    public int chairBonus;
}
