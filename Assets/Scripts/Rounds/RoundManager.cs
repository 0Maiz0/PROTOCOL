using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RoundManager : MonoBehaviour
{
	[Header("Core References")]
	[SerializeField] private MatchEconomy economy;
	[SerializeField] private StrategyExecutor strategyExecutor;
	[SerializeField] private CoachPanel coachPanel;
	[SerializeField] private BombManager bombManager;
	[SerializeField] private AICoach aiCoach;
	[SerializeField] private ObserverCamera observerCamera;
	[SerializeField] private MidRoundPanel midRoundPanel;
	[SerializeField] private MatchHUD matchHUD;
	[SerializeField] private PostPlantManager postPlantManager;
	[SerializeField] private GrenadeManager grenadeManager;
	[SerializeField] private CommsManager commsManager;
	[SerializeField] private PlayerHUD playerHUD;

	[Header("Spawn Points")]
	[SerializeField] private Transform[] attackerSpawns = new Transform[5];
	[SerializeField] private Transform[] defenderSpawns = new Transform[5];

	[Header("Bot Prefab")]
	[SerializeField] private GameObject botPrefab;

	[Header("Player Data")]
	[SerializeField] private PlayerData[] attackerPlayerData = new PlayerData[5];
	[SerializeField] private PlayerData[] defenderPlayerData = new PlayerData[5];

	[Header("Equipment")]
	[SerializeField] private EquipmentBonuses attackerEquipment;
	[SerializeField] private EquipmentBonuses defenderEquipment;

	[Header("Runtime")]
	[SerializeField] private RoundPhase currentPhase;
	[SerializeField] private float roundTimer;
	[SerializeField] private int attackersAlive;
	[SerializeField] private int defendersAlive;

	public MatchState State { get; private set; }
	private List<BotController> attackerBots = new List<BotController>();
	private List<BotController> defenderBots = new List<BotController>();
	private bool roundActive = false;
	private bool coachCallMade = false;
	private bool midRoundCallMade = false;
	private StrategyData selectedStrategy = null;
	private string lastPlayerStrategy = "";

	private void Start()
	{
		State = new MatchState();

		// Career hook: if a career is running, roster + equipment come from CareerManager
		// (player team = attackers first half). Inspector data stays as standalone fallback.
		if (CareerManager.Instance != null)
		{
			attackerPlayerData = CareerManager.Instance.BuildPlayerTeamData();
			defenderPlayerData = CareerManager.Instance.BuildEnemyTeamData();
			attackerEquipment = CareerManager.Instance.TeamEquipment;
		}

		if (economy == null) economy = GetComponent<MatchEconomy>();
		if (economy == null) economy = gameObject.AddComponent<MatchEconomy>();
		if (strategyExecutor == null) strategyExecutor = GetComponent<StrategyExecutor>();
		if (strategyExecutor == null) strategyExecutor = gameObject.AddComponent<StrategyExecutor>();
		if (coachPanel == null) coachPanel = GetComponent<CoachPanel>();
		if (bombManager == null) bombManager = GetComponent<BombManager>();
		if (aiCoach == null) aiCoach = GetComponent<AICoach>();
		if (midRoundPanel == null) midRoundPanel = GetComponent<MidRoundPanel>();
		if (observerCamera == null) observerCamera = Camera.main?.GetComponent<ObserverCamera>();
		if (matchHUD == null) matchHUD = FindObjectOfType<MatchHUD>();
		if (postPlantManager == null) postPlantManager = GetComponent<PostPlantManager>();
		if (postPlantManager == null) postPlantManager = FindObjectOfType<PostPlantManager>();
		if (grenadeManager == null) grenadeManager = GetComponent<GrenadeManager>();
		if (grenadeManager == null) grenadeManager = FindObjectOfType<GrenadeManager>();
		if (commsManager == null) commsManager = GetComponent<CommsManager>();
		if (commsManager == null) commsManager = FindObjectOfType<CommsManager>();
		if (playerHUD == null) playerHUD = FindObjectOfType<PlayerHUD>();

		if (attackerEquipment == null) attackerEquipment = ScriptableObject.CreateInstance<EquipmentBonuses>();
		if (defenderEquipment == null) defenderEquipment = ScriptableObject.CreateInstance<EquipmentBonuses>();

		if (bombManager != null)
		{
			bombManager.OnBombExploded += () => roundActive = false;
			bombManager.OnBombDefused += () => roundActive = false;
			bombManager.OnBombPlanted += (site) => FootstepSystem.OnBombPlantedStatic(true);
		}

		SpawnAllBots();
		if (postPlantManager != null) postPlantManager.SetBotReferences(attackerBots, defenderBots);
		if (grenadeManager != null) grenadeManager.SetBotReferences(attackerBots, defenderBots);
		EngagementSystem.OnDuelResolved += OnDuelResolved;
		StartCoroutine(MatchLoop());
	}

	private void OnDestroy() => EngagementSystem.OnDuelResolved -= OnDuelResolved;

	private void Update()
	{
		if (!roundActive || currentPhase != RoundPhase.Live) return;
		roundTimer -= Time.deltaTime;
		if (bombManager != null) { bombManager.TryAutoPlant(attackerBots); bombManager.TryAutoDefuse(defenderBots); }
		if (roundTimer <= 0f)
		{
			roundTimer = 0f;
			if (bombManager == null || !bombManager.IsPlanted) { roundActive = false; Debug.Log("TIME EXPIRED — CT wins"); }
		}
	}

	private void FreezeAllBots()
	{ foreach (var b in attackerBots) if (b != null) b.IsFrozen = true;
	  foreach (var b in defenderBots) if (b != null) b.IsFrozen = true; }

	private void UnfreezeAllBots()
	{ foreach (var b in attackerBots) if (b != null) b.IsFrozen = false;
	  foreach (var b in defenderBots) if (b != null) b.IsFrozen = false; }

	// ===================== MATCH LOOP =====================

	private IEnumerator MatchLoop()
	{
		Debug.Log("========== MATCH START ==========");
		while (!State.isMatchOver)
		{
			yield return StartCoroutine(PlayRound());
			if (State.isMatchOver) break;
			State.NextRound();
			if (State.isHalftime)
			{
				Debug.Log("========== HALFTIME ==========");
				SwapSidesAtHalftime();
				State.isHalftime = false;
				yield return new WaitForSeconds(3f);
			}
		}
		Debug.Log($"========== {State.matchWinner} WINS {State.attackerScore}-{State.defenderScore} ==========");

		// Career hook: report result and return home after the win screen
		if (CareerManager.Instance != null)
		{
			yield return new WaitForSeconds(Constants.MATCH_END_RETURN_DELAY);
			CareerManager.Instance.OnMatchFinished(
				State.matchWinner == State.playerTeamSide, State.PlayerScore, State.EnemyScore);
		}
	}

	private IEnumerator PlayRound()
	{
		Debug.Log($"\n--- Round {State.currentRound} | {State.attackerScore}-{State.defenderScore} ---");

		// ============ FREEZE TIME ============
		currentPhase = RoundPhase.FreezeTime;
		ResetAllBots();
		FreezeAllBots();
		EngagementSystem.ClearAllEngagements();
		if (bombManager != null) bombManager.ResetForRound();
		if (postPlantManager != null) postPlantManager.ResetForRound();
		if (grenadeManager != null) grenadeManager.ResetForRound();
		if (commsManager != null) commsManager.ResetForRound();
		economy.BuyPhase();
		AssignBuyTypes();

		if (grenadeManager != null)
			grenadeManager.AllocateUtility(economy.attackerEconomy.currentBuyType, economy.defenderEconomy.currentBuyType);

		if (observerCamera != null) observerCamera.SetOverviewMode();

		coachCallMade = false;
		selectedStrategy = null;
		if (coachPanel != null)
			coachPanel.ShowPanel(State.playerTeamSide, (strat) => { selectedStrategy = strat; coachCallMade = true; });
		else coachCallMade = true;

		float elapsed = 0f;
		while (!coachCallMade || elapsed < 2f)
		{ elapsed += Time.deltaTime; yield return null; if (elapsed > 15f) { coachCallMade = true; break; } }
		if (coachPanel != null) coachPanel.HidePanel();

		// ============ LIVE ============
		currentPhase = RoundPhase.Live;
		roundTimer = Constants.ROUND_TIME;
		roundActive = true;
		UnfreezeAllBots();
		FootstepSystem.OnRoundStarted();
		FootstepSystem.OnBombPlantedStatic(false);

		if (observerCamera != null) observerCamera.SetFollowMode();

		if (selectedStrategy != null && strategyExecutor != null)
		{
			lastPlayerStrategy = selectedStrategy.strategyName;
			var playerBots = (State.playerTeamSide == TeamSide.Attacker) ? attackerBots : defenderBots;
			strategyExecutor.ExecuteStrategy(selectedStrategy, playerBots);
			if (grenadeManager != null) grenadeManager.OnStrategyExecuted(selectedStrategy, State.playerTeamSide);
		}

		StrategyData aiStrategy = null;
		if (aiCoach != null)
		{
			TeamSide aiSide = (State.playerTeamSide == TeamSide.Attacker) ? TeamSide.Defender : TeamSide.Attacker;
			var aiBots = (aiSide == TeamSide.Defender) ? defenderBots : attackerBots;
			aiStrategy = aiCoach.PickAndExecute(aiSide, aiBots);
			if (grenadeManager != null && aiStrategy != null)
				grenadeManager.OnStrategyExecuted(aiStrategy, aiSide);
		}

		UpdateAliveCounts();

		// ============ MID-ROUND ============
		float midWait = Constants.MID_ROUND_TRIGGER_TIME;
		while (midWait > 0f && roundActive) { midWait -= Time.deltaTime; yield return null; }
		if (!roundActive) goto PostRound;

		midRoundCallMade = false;
		UpdateAliveCounts();
		bool bombPlanted = bombManager != null && bombManager.IsPlanted;

		if (midRoundPanel != null && !bombPlanted)
		{
			var playerBots = (State.playerTeamSide == TeamSide.Attacker) ? attackerBots : defenderBots;
			int friendly = (State.playerTeamSide == TeamSide.Attacker) ? attackersAlive : defendersAlive;
			int enemy = (State.playerTeamSide == TeamSide.Attacker) ? defendersAlive : attackersAlive;
			midRoundPanel.ShowPanel(State.playerTeamSide, playerBots, friendly, enemy, false, "", (call) => { midRoundCallMade = true; });
			float mw = 0f;
			while (!midRoundCallMade && roundActive && mw < 10f) { mw += Time.deltaTime; yield return null; }
			if (midRoundPanel != null) midRoundPanel.HidePanel();
		}

		yield return new WaitUntil(() => !roundActive);

	PostRound:
		currentPhase = RoundPhase.PostRound;
		FreezeAllBots();
		if (observerCamera != null) observerCamera.SetOverviewMode();

		TeamSide winner = DetermineRoundWinner();
		int aK = CountKills(attackerBots), dK = CountKills(defenderBots);
		bool planted = bombManager != null && (bombManager.IsPlanted || bombManager.IsExploded);
		State.AddRoundWin(winner);
		economy.ProcessRoundEnd(winner, aK, dK, planted);

		if (aiCoach != null)
		{
			TeamSide aiSide = (State.playerTeamSide == TeamSide.Attacker) ? TeamSide.Defender : TeamSide.Attacker;
			aiCoach.OnRoundResult(winner, aiSide, lastPlayerStrategy);
		}

		Debug.Log($"ROUND WINNER: {winner} | {State.attackerScore}-{State.defenderScore}");
		ResetRoundKills();
		yield return new WaitForSeconds(Constants.POST_ROUND_DELAY);
	}

	// ===================== BOTS =====================

	private void SpawnAllBots()
	{
		for (int i = 0; i < attackerPlayerData.Length; i++)
			if (attackerPlayerData[i] != null && i < attackerSpawns.Length && attackerSpawns[i] != null)
				attackerBots.Add(SpawnBot(attackerPlayerData[i], attackerEquipment, attackerSpawns[i], TeamSide.Attacker, Color.red));
		for (int i = 0; i < defenderPlayerData.Length; i++)
			if (defenderPlayerData[i] != null && i < defenderSpawns.Length && defenderSpawns[i] != null)
				defenderBots.Add(SpawnBot(defenderPlayerData[i], defenderEquipment, defenderSpawns[i], TeamSide.Defender, Color.blue));
	}

	private BotController SpawnBot(PlayerData data, EquipmentBonuses equip, Transform spawn, TeamSide side, Color color)
	{
		GameObject obj = Instantiate(botPrefab, spawn.position, spawn.rotation);
		obj.name = $"Bot_{side}_{data.nickname}";
		BotController bot = obj.GetComponent<BotController>();
		PlayerProfile profile = new PlayerProfile(data, equip);
		profile.AssignedRole = data.preferredRole;
		bot.Initialize(profile, side);
		Renderer r = obj.GetComponentInChildren<Renderer>();
		if (r != null) { Material m = new Material(r.sharedMaterial); m.color = color; r.material = m; }
		return bot;
	}

	private void ResetAllBots()
	{
		for (int i = 0; i < attackerBots.Count; i++)
			if (i < attackerSpawns.Length && attackerSpawns[i] != null) attackerBots[i].ResetForRound(attackerSpawns[i].position);
		for (int i = 0; i < defenderBots.Count; i++)
			if (i < defenderSpawns.Length && defenderSpawns[i] != null) defenderBots[i].ResetForRound(defenderSpawns[i].position);
	}

	private void AssignBuyTypes()
	{
		BuyType aB = economy.attackerEconomy.currentBuyType, dB = economy.defenderEconomy.currentBuyType;
		foreach (var b in attackerBots) if (b?.Profile != null) b.SetBuyType(aB);
		foreach (var b in defenderBots) if (b?.Profile != null) b.SetBuyType(dB);
	}

	// ===================== HALFTIME =====================

	private void SwapSidesAtHalftime()
	{
		(attackerBots, defenderBots) = (defenderBots, attackerBots);
		(attackerSpawns, defenderSpawns) = (defenderSpawns, attackerSpawns);
		(attackerEquipment, defenderEquipment) = (defenderEquipment, attackerEquipment);
		foreach (var b in attackerBots) { b.SetSide(TeamSide.Attacker); b.SetColor(Color.red); }
		foreach (var b in defenderBots) { b.SetSide(TeamSide.Defender); b.SetColor(Color.blue); }
		State.SwapSides();
		if (economy != null) economy.SwapSides();
		if (matchHUD != null) matchHUD.playerTeamSide = State.playerTeamSide;
		if (playerHUD != null) playerHUD.playerTeamSide = State.playerTeamSide;
		if (postPlantManager != null) postPlantManager.SetBotReferences(attackerBots, defenderBots);
		if (grenadeManager != null) grenadeManager.SetBotReferences(attackerBots, defenderBots);
	}

	// ===================== RESOLUTION =====================

	private void OnDuelResolved(DuelResolver.DuelOutcome outcome)
	{
		UpdateAliveCounts();
		if (attackersAlive <= 0 || defendersAlive <= 0)
		{
			if (bombManager != null && bombManager.IsPlanted && attackersAlive <= 0 && defendersAlive > 0)
				Debug.Log("All T dead but bomb planted — CT must defuse!");
			else roundActive = false;
		}
	}

	private void UpdateAliveCounts()
	{
		attackersAlive = defendersAlive = 0;
		foreach (var b in attackerBots) if (b?.Profile != null && b.Profile.IsAlive) attackersAlive++;
		foreach (var b in defenderBots) if (b?.Profile != null && b.Profile.IsAlive) defendersAlive++;
	}

	private TeamSide DetermineRoundWinner()
	{
		if (bombManager != null) { if (bombManager.IsExploded) return TeamSide.Attacker; if (bombManager.IsDefused) return TeamSide.Defender; }
		UpdateAliveCounts();
		return (attackersAlive <= 0) ? TeamSide.Defender : (defendersAlive <= 0) ? TeamSide.Attacker : TeamSide.Defender;
	}

	private int CountKills(List<BotController> bots)
	{ int t = 0; foreach (var b in bots) if (b?.Profile != null) t += b.Profile.RoundKills; return t; }

	private void ResetRoundKills()
	{ foreach (var b in attackerBots) if (b?.Profile != null) b.Profile.RoundKills = 0;
	  foreach (var b in defenderBots) if (b?.Profile != null) b.Profile.RoundKills = 0; }

	// ===================== HUD =====================

	private void OnGUI()
	{
		if (State == null) return;
		GUIStyle s = new GUIStyle(GUI.skin.label);
		s.fontSize = 22; s.fontStyle = FontStyle.Bold; s.normal.textColor = Color.white;
		GUI.Box(new Rect(5, 5, 520, 115), "");
		GUI.Label(new Rect(15, 10, 500, 30), $"Round {State.currentRound}  |  T {State.attackerScore} - {State.defenderScore} CT", s);
		s.fontSize = 16; s.fontStyle = FontStyle.Normal;
		GUI.Label(new Rect(15, 40, 500, 25), $"{currentPhase}  |  Timer: {roundTimer:F0}s  |  {attackersAlive}v{defendersAlive}", s);
		s.fontSize = 14;
		if (economy != null)
			GUI.Label(new Rect(15, 65, 500, 25),
				$"T: ${economy.attackerEconomy.credits} ({economy.attackerEconomy.currentBuyType}) | CT: ${economy.defenderEconomy.credits} ({economy.defenderEconomy.currentBuyType})", s);
		if (bombManager != null)
		{
			s.normal.textColor = bombManager.IsPlanted || bombManager.IsExploded ? Color.red : (bombManager.IsDefused ? Color.green : Color.gray);
			s.fontStyle = FontStyle.Bold;
			string bt = bombManager.CurrentState switch
			{
				BombState.Planted => $"BOMB PLANTED {bombManager.PlantedSiteName}! ({bombManager.Timer:F0}s)",
				BombState.Exploded => "BOMB EXPLODED!", BombState.Defused => "BOMB DEFUSED!", _ => "Bomb: Not planted"
			};
			GUI.Label(new Rect(15, 90, 500, 25), bt, s);
		}
		if (State.isMatchOver)
		{
			s.fontSize = 36; s.fontStyle = FontStyle.Bold; s.alignment = TextAnchor.MiddleCenter; s.normal.textColor = Color.yellow;
			GUI.Box(new Rect(Screen.width / 2 - 260, Screen.height / 2 - 35, 520, 70), "");
			GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 - 30, 500, 60),
				$"{State.matchWinner} WINS!  {State.attackerScore}-{State.defenderScore}", s);
		}
	}
}
