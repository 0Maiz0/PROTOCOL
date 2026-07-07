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
	[SerializeField] private IGLBrain iglBrain;

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
	private StrategyData selectedStrategy = null;
	private BuyType selectedBuy = BuyType.Eco;
	private string lastPlayerStrategy = "";

	// The coach's only in-match lever — tactical timeouts, like real CS
	private int timeoutsLeft = Constants.TIMEOUTS_PER_MATCH;
	private bool timeoutPending = false;
	private TeamProtocols fallbackProtocols = new TeamProtocols();

	private TeamProtocols GetProtocols() =>
		CareerManager.Instance != null ? CareerManager.Instance.protocols : fallbackProtocols;

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
		if (iglBrain == null) iglBrain = GetComponent<IGLBrain>();
		if (iglBrain == null) iglBrain = gameObject.AddComponent<IGLBrain>();
		if (FindObjectOfType<CommsFeed>() == null) gameObject.AddComponent<CommsFeed>();
		CommsFeed.SetPlayerSide(State.playerTeamSide);

		// Broadcast-style intro splash
		var intro = FindObjectOfType<MatchIntro>();
		if (intro == null) intro = gameObject.AddComponent<MatchIntro>();
		intro.Show("YOUR SQUAD",
			CareerManager.Instance != null ? CareerManager.Instance.nextOpponentName : "OPPONENT");

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
		if (Input.GetKeyDown(KeyCode.T) && timeoutsLeft > 0 && !timeoutPending)
		{
			timeoutPending = true;
			Debug.Log("COACH: tactical timeout requested — takes effect next freeze");
		}

		if (!roundActive || currentPhase != RoundPhase.Live) return;
		roundTimer -= Time.deltaTime;
		if (bombManager != null) { bombManager.TryAutoPlant(attackerBots); bombManager.TryAutoDefuse(defenderBots); }
		if (roundTimer <= 0f)
		{
			roundTimer = 0f;
			if (bombManager == null || !bombManager.IsPlanted) { roundActive = false; Debug.Log("TIME EXPIRED — CT wins"); }
		}
	}

	/// <summary>Called by MapGenerator — generated maps bring their own spawns.</summary>
	public void SetSpawnPoints(Transform[] attackers, Transform[] defenders)
	{
		attackerSpawns = attackers;
		defenderSpawns = defenders;
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
		CommsFeed.Clear();
		if (bombManager != null) bombManager.AssignCarrier(attackerBots);
		// THE TEAM PLAYS, THE COACH WATCHES: the IGL calls buy + strategy.
		// A tactical timeout hands this one round back to the coach.
		bool timeoutRound = timeoutPending && timeoutsLeft > 0;
		if (timeoutRound)
		{
			timeoutPending = false;
			timeoutsLeft--;
			CommsFeed.PostSystem($"TACTICAL TIMEOUT — coach takes over ({timeoutsLeft} left)");
		}

		economy.DecideBuys(State.playerTeamSide, GetProtocols().economyDiscipline, !timeoutRound);
		CommsFeed.PostOpponent($"buy: {economy.GetEconomy(State.playerTeamSide == TeamSide.Attacker ? TeamSide.Defender : TeamSide.Attacker).currentBuyType}");

		if (observerCamera != null) observerCamera.SetOverviewMode();

		var playerRoster = (State.playerTeamSide == TeamSide.Attacker) ? attackerBots : defenderBots;
		if (iglBrain != null) iglBrain.SetTeam(playerRoster, State.playerTeamSide);

		selectedStrategy = null;
		if (timeoutRound && coachPanel != null)
		{
			coachCallMade = false;
			selectedBuy = economy.GetEconomy(State.playerTeamSide).recommendedBuy;
			coachPanel.ShowPanel(State.playerTeamSide, economy.GetEconomy(State.playerTeamSide),
				(strat, buy) => { selectedStrategy = strat; selectedBuy = buy; coachCallMade = true; });

			float elapsed = 0f;
			while (!coachCallMade || elapsed < 2f)
			{ elapsed += Time.deltaTime; yield return null; if (elapsed > 30f) { coachCallMade = true; break; } }
			coachPanel.HidePanel();
			economy.ApplyPlayerBuy(State.playerTeamSide, selectedBuy);
		}
		else
		{
			// The IGL calls the round
			if (strategyExecutor != null) strategyExecutor.SetLurkerEnabled(GetProtocols().preferLurker);
			if (iglBrain != null)
				selectedStrategy = iglBrain.PickOpeningStrategy(State.playerTeamSide, GetProtocols(),
					coachPanel, economy.GetEconomy(State.playerTeamSide).currentBuyType);
			yield return new WaitForSeconds(Constants.AUTO_FREEZE_TIME);
		}

		AssignBuyTypes();
		if (grenadeManager != null)
			grenadeManager.AllocateUtility(economy.attackerEconomy.currentBuyType, economy.defenderEconomy.currentBuyType);

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
			strategyExecutor.ExecuteStrategy(selectedStrategy, playerBots, State.playerTeamSide);
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

		// ============ LIVE UNTIL RESOLVED ============
		// Mid-round decisions belong to the IGL now — the coach watches
		// (and spends a timeout if the plan needs to change next round)
		yield return new WaitUntil(() => !roundActive);

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
		AssignTeamLoadouts(attackerBots, economy.attackerEconomy);
		AssignTeamLoadouts(defenderBots, economy.defenderEconomy);
	}

	private void AssignTeamLoadouts(List<BotController> bots, TeamEconomy econ)
	{
		// AWPer buys first so the pool can afford the big green
		foreach (var b in bots)
			if (b?.Profile != null && b.Profile.AssignedRole == Role.AWPer)
				b.SetLoadout(econ.BuyWeaponFor(Role.AWPer), econ.currentBuyType);
		foreach (var b in bots)
			if (b?.Profile != null && b.Profile.AssignedRole != Role.AWPer)
				b.SetLoadout(econ.BuyWeaponFor(b.Profile.AssignedRole), econ.currentBuyType);
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
		CommsFeed.SetPlayerSide(State.playerTeamSide);
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
		// Tactical timeout — the coach's only in-match lever
		if (!State.isMatchOver)
		{
			if (timeoutPending)
			{
				GUIStyle ts = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
				ts.normal.textColor = Color.yellow;
				GUI.Label(new Rect(Screen.width - 240, 12, 230, 30), "TIMEOUT next freeze...", ts);
			}
			else
			{
				GUIStyle tb = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
				GUI.enabled = timeoutsLeft > 0;
				if (GUI.Button(new Rect(Screen.width - 240, 10, 230, 34), $"TIMEOUT [T] — {timeoutsLeft} left", tb))
					timeoutPending = true;
				GUI.enabled = true;
			}
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
