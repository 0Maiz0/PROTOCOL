using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Procedural generation of career players: starting roster, scouting pool, enemy teams.
/// Visible stats roll inside a tier range; salary derives from visible overall.
/// </summary>
public static class PlayerGenerator
{
    private static readonly string[] Nicknames =
    {
        "frostbyte", "zeni", "kair0", "w1nter", "drozd", "stormz", "neonx", "quartz",
        "rev0lt", "sable", "tw1st", "echoz", "glitch", "raz0r", "phant0m", "onyx",
        "krypto", "havoc", "zephyr", "bl1tz", "cinder", "talon", "specter", "jinx",
        "volt", "ember", "shadez", "pyro", "kaiser", "orbit", "pulse", "reaper",
        "saber", "toxin", "vertex", "wisp", "xenon", "zealot", "aero", "cypher",
        "dredge", "enigma", "flux", "gambit", "hexa", "jolt", "karma", "lynx",
        "nitro", "omen", "quill", "rift", "sonar", "tundra", "umbra", "vandal",
        "warden", "yeti", "zircon", "apexo"
    };

    private static readonly string[] FirstNames =
    {
        "Alexey", "Marcus", "Jonas", "Erik", "Mateusz", "Viktor", "Daniel", "Lucas",
        "Emil", "Nikita", "Oscar", "Felix", "Adam", "Timur", "Jakub", "Elias",
        "Robin", "Sergey", "Kacper", "Anton", "Milan", "David", "Artur", "Leon"
    };

    private static readonly string[] LastNames =
    {
        "Volkov", "Berg", "Novak", "Kowalski", "Lindqvist", "Petrov", "Jensen", "Marek",
        "Sokolov", "Andersen", "Kovacs", "Lehtinen", "Weber", "Moreau", "Silva", "Rask",
        "Horvat", "Bondar", "Nilsen", "Zima", "Krause", "Dvorak", "Melnik", "Stark"
    };

    private static readonly string[] Nationalities =
    {
        "Sweden", "Denmark", "Russia", "Ukraine", "Poland", "France", "Germany", "Czechia",
        "Finland", "Norway", "Kazakhstan", "Serbia", "Bosnia", "Lithuania", "Estonia", "Slovakia"
    };

    private static readonly string[] TeamNames =
    {
        "Crimson Hex", "Void Sector", "Iron Pulse", "Night Raid", "Static Zero", "Obsidian Five",
        "Rift Kings", "Neon Fangs", "Grey Protocol", "Last Sync", "Bunker Nine", "Silent Frag",
        "Red Latency", "Phase Shift", "Core Dump", "Wolfpack EU"
    };

    private static readonly Role[] AllRoles = { Role.Entry, Role.Support, Role.AWPer, Role.Lurker, Role.IGL };

    // Prevents duplicate nicknames within one play session (roster + pool + enemies)
    private static readonly HashSet<string> usedNicknames = new HashSet<string>();

    /// <summary>
    /// Starting roster: 5 mid-tier players, one per role.
    /// </summary>
    public static List<CareerPlayer> GenerateStartingRoster()
    {
        var roster = new List<CareerPlayer>();
        foreach (var role in AllRoles)
            roster.Add(Generate(Constants.STARTING_ROSTER_TIER_MIN, Constants.STARTING_ROSTER_TIER_MAX, role));
        return roster;
    }

    /// <summary>
    /// Scouting pool with mixed quality: 40% low tier, 40% mid, 20% high.
    /// </summary>
    public static List<CareerPlayer> GeneratePool(int count)
    {
        var pool = new List<CareerPlayer>();
        for (int i = 0; i < count; i++)
        {
            float roll = Random.value;
            if (roll < 0.4f) pool.Add(Generate(35, 55));
            else if (roll < 0.8f) pool.Add(Generate(50, 70));
            else pool.Add(Generate(65, 85));
        }
        return pool;
    }

    /// <summary>
    /// Enemy team for a match: 5 players, one per role, inside the given tier range.
    /// </summary>
    public static List<CareerPlayer> GenerateEnemyTeam(int tierMin, int tierMax)
    {
        var team = new List<CareerPlayer>();
        foreach (var role in AllRoles)
            team.Add(Generate(tierMin, tierMax, role));
        return team;
    }

    public static CareerPlayer Generate(int tierMin, int tierMax, Role? role = null)
    {
        var p = new CareerPlayer
        {
            nickname = UniqueNickname(),
            playerName = Pick(FirstNames) + " " + Pick(LastNames),
            nationality = Pick(Nationalities),
            age = Random.Range(17, 29),
            aim = Random.Range(tierMin, tierMax + 1),
            reflexes = Random.Range(tierMin, tierMax + 1),
            gameSense = Random.Range(tierMin, tierMax + 1),
            clutch = Random.Range(20, 96),
            consistency = Random.Range(20, 96),
            communication = Random.Range(20, 96),
            ego = Random.Range(5, 91),
            workEthic = Random.Range(20, 96),
            toxicity = Random.Range(0, 71),
            leadership = Random.Range(5, 91),
            preferredRole = role ?? AllRoles[Random.Range(0, AllRoles.Length)]
        };
        p.assignedRole = p.preferredRole;
        p.salaryPerMonth = SalaryFor(p);
        return p;
    }

    public static string GenerateTeamName() => Pick(TeamNames);

    /// <summary>
    /// Re-register nicknames from a loaded save so newly generated players don't duplicate them.
    /// </summary>
    public static void RegisterExistingNicknames(IEnumerable<CareerPlayer> players)
    {
        if (players == null) return;
        foreach (var p in players)
            if (p != null && !string.IsNullOrEmpty(p.nickname))
                usedNicknames.Add(p.nickname);
    }

    private static string Pick(string[] arr) => arr[Random.Range(0, arr.Length)];

    private static string UniqueNickname()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string nick = Pick(Nicknames);
            if (usedNicknames.Add(nick)) return nick;
        }
        // Pool exhausted — suffix a number to keep names unique
        string numbered = Pick(Nicknames) + Random.Range(2, 100);
        usedNicknames.Add(numbered);
        return numbered;
    }

    private static int SalaryFor(CareerPlayer p)
    {
        float salary = 200f + Mathf.Max(0, p.Overall - 40) * 30f;
        salary *= Random.Range(0.9f, 1.1f);
        int rounded = Mathf.RoundToInt(salary / 50f) * 50;
        return Mathf.Clamp(rounded, 300, 5000);
    }
}
