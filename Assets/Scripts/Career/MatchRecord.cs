/// <summary>
/// One played match in career history. Plain serializable class — part of the JSON save.
/// </summary>
[System.Serializable]
public class MatchRecord
{
    public int week;
    public string opponentName;
    public string stage; // "SF" / "FINAL" — empty for pre-bracket records
    public bool won;
    public int playerScore;
    public int enemyScore;
    public int prize;
}
