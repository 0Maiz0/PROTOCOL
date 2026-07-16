using UnityEngine;

/// <summary>
/// Two-language UI helper (EN/RU) for the management layer. In-match comms
/// and the broadcast HUD intentionally stay English — CS slang is universal
/// and the esports layer reads like an HLTV stream either way.
/// Usage: Loc.L("English", "Русский"). Choice persists via PlayerPrefs.
/// </summary>
public static class Loc
{
    private const string PREF_KEY = "protocol_lang_ru";
    private static bool loaded;
    private static bool russian;

    public static bool Russian
    {
        get
        {
            if (!loaded) { russian = PlayerPrefs.GetInt(PREF_KEY, 0) == 1; loaded = true; }
            return russian;
        }
        set
        {
            russian = value; loaded = true;
            PlayerPrefs.SetInt(PREF_KEY, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static void Toggle() => Russian = !Russian;

    /// <summary>Pick the string for the active language.</summary>
    public static string L(string en, string ru) => Russian ? ru : en;
}
