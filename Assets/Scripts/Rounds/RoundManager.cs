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
	private float defuseOrderTimer;
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
		intro.Show("NAVI",
			CareerManager.Instance != null ? CareerManager.Instance.nextOpponentName : "FAZE");

		MatchLogger.StartMatch(CareerManager.Instance != null ? CareerManager.Instance.nextOpponentName : "standalone");

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
		if (bombManager != null) { bombManager.TryAutoPlant(attackerBots, defenderBots); bombManager.TryAutoDefuse(defenderBots); }

		// All Ts dead + bomb down → CTs must GO to the bomb (they stood around
		// while it exploded in R12); reissued periodically in case fights distract
		if (bombManager != null && bombManager.IsPlanted && attackersAlive <= 0)
		{
			defuseOrderTimer -= Time.deltaTime;
			if (defuseOrderTimer <= 0f)
			{
				defuseOrderTimer = 2.5f;
				OrderDefuseRush();
			}
		}
		if (roundTimer <= 0f)
		{
			roundTimer = 0f;
			if (bombManager == null || !bombManager.IsPlanted) { roundActive = false; Debug.Log("TIME EXPIRED — CT wins"); }
		}
	}

	private void OrderDefuseRush()
	{
		GameObject wp = GameObject.Find(bombManager.PlantedSiteName == "A" ? "WP_NodeA" : "WP_NodeB");
		if (wp == null) return;
		foreach (var b in defenderBots)
		{
			if (b?.Profile == null || !b.Profile.IsAlive) continue;
			b.HazardBypass = true;
			b.AssignWaypoints(new Transform[] { wp.transform });
			b.StartExecuting();
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
		LogScoreboard();
		MatchLogger.EndMatch($"{State.matchWinner} wins {State.attackerScore}-{State.defenderScore} " +
			$"({(State.matchWinner == State.playerTeamSide ? "PLAYER VICTORY" : "OPPONENT VICTORY")})");

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

		// Weapon carryover (CS rule): survivors keep their guns — capture BEFORE reset.
		// NEVER on pistol rounds: round 1 would capture the spawn-default rifles
		// (bots initialize with Full-buy profiles), round 13 would carry across halftime.
		carriedWeapons.Clear();
		if (State.currentRound > 1 && State.currentRound != Constants.HALFTIME_ROUND + 1)
		{
			CaptureCarriedWeapons(attackerBots);
			CaptureCarriedWeapons(defenderBots);
		}

		ResetAllBots();
		FreezeAllBots();
		EngagementSystem.ClearAllEngagements();
		if (bombManager != null) bombManager.ResetForRound();
		if (postPlantManager != null) postPlantManager.ResetForRound();
		if (grenadeManager != null) grenadeManager.ResetForRound();
		if (commsManager != null) commsManager.ResetForRound();
		CommsFeed.Clear();
		MatchLogger.StartRound(State.currentRound,
			$"T {State.attackerScore}-{State.defenderScore} CT | player is {State.playerTeamSide}");
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
		var enemyRoster = (State.playerTeamSide == TeamSide.Attacker) ? defenderBots : attackerBots;
		if (iglBrain != null) iglBrain.SetTeam(playerRoster, enemyRoster, State.playerTeamSide);

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
			TeamSide enemySide = State.playerTeamSide == TeamSide.Attacker ? TeamSide.Defender : TeamSide.Attacker;
			if (iglBrain != null)
				selectedStrategy = iglBrain.PickOpeningStrategy(State.playerTeamSide, GetProtocols(),
					coachPanel, economy.GetEconomy(State.playerTeamSide).currentBuyType,
					economy.GetEconomy(enemySide).currentBuyType);
			yield return new WaitForSeconds(Constants.AUTO_FREEZE_TIME);
		}

		AssignBuyTypes();
		if (grenadeManager != null)
			grenadeManager.AllocateUtility(economy.attackerEconomy.currentBuyType, economy.defenderEconomy.currentBuyType);
		MatchLogger.Log($"BUY: T {economy.attackerEconomy.currentBuyType} (${economy.attackerEconomy.credits}) | " +
			$"CT {economy.defenderEconomy.currentBuyType} (${economy.defenderEconomy.credits})" +
			(timeoutRound ? " | COACH TIMEOUT ROUND" : ""));

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
			MatchLogger.Log($"STRAT[player/{State.playerTeamSide}]: {selectedStrategy.strategyName.Trim()} ({selectedStrategy.type})");
		}

		StrategyData aiStrategy = null;
		if (aiCoach != null)
		{
			TeamSide aiSide = (State.playerTeamSide == TeamSide.Attacker) ? TeamSide.Defender : TeamSide.Attacker;
			var aiBots = (aiSide == TeamSide.Defender) ? defenderBots : attackerBots;
			aiStrategy = aiCoach.PickAndExecute(aiSide, aiBots, playerRoster,
				economy.GetEconomy(State.playerTeamSide).currentBuyType);
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
		if (iglBrain != null)
			iglBrain.OnRoundResult(winner == State.playerTeamSide, selectedStrategy);

		Debug.Log($"ROUND WINNER: {winner} | {State.attackerScore}-{State.defenderScore}");
		MatchLogger.Log($"ROUND END: {winner} wins ({(winner == State.playerTeamSide ? "PLAYER" : "OPPONENT")}) | " +
			$"score T {State.attackerScore}-{State.defenderScore} CT | alive T:{attackersAlive} CT:{defendersAlive} | " +
			$"bomb: {(bombManager != null ? bombManager.CurrentState.ToString() : "n/a")}");
		MatchLogger.Flush();
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

	private readonly Dictionary<BotController, WeaponData.WeaponStats> carriedWeapons
		= new Dictionary<BotController, WeaponData.WeaponStats>();

	private void CaptureCarriedWeapons(List<BotController> bots)
	{
		foreach (var b in bots)
			if (b?.Profile != null && b.Profile.IsAlive && b.Profile.CurrentWeapon.weaponName != "Pistol")
				carriedWeapons[b] = b.Profile.CurrentWeapon;
	}

	private void AssignBuyTypes()
	{
		AssignTeamLoadouts(attackerBots, economy.attackerEconomy, TeamSide.Attacker);
		AssignTeamLoadouts(defenderBots, economy.defenderEconomy, TeamSide.Defender);
	}

	private void AssignTeamLoadouts(List<BotController> bots, TeamEconomy econ, TeamSide side)
	{
		// Survivors keep last round's gun for free — no purchase needed
		int kept = 0;
		foreach (var b in bots)
			if (b?.Profile != null && carriedWeapons.TryGetValue(b, out var carried))
			{
				b.SetLoadout(carried, econ.currentBuyType);
				kept++;
			}
		if (kept > 0) MatchLogger.Log($"CARRY[{side}]: {kept} kept their guns");

		// AWPer buys first so the pool can afford the big green
		foreach (var b in bots)
			if (b?.Profile != null && !carriedWeapons.ContainsKey(b) && b.Profile.AssignedRole == Role.AWPer)
				b.SetLoadout(Localize(econ.BuyWeaponFor(Role.AWPer), side), econ.currentBuyType);
		foreach (var b in bots)
			if (b?.Profile != null && !carriedWeapons.ContainsKey(b) && b.Profile.AssignedRole != Role.AWPer)
				b.SetLoadout(Localize(econ.BuyWeaponFor(b.Profile.AssignedRole), side), econ.currentBuyType);
	}

	/// <summary>Side-correct display names — CTs don't run Galils.</summary>
	private static WeaponData.WeaponStats Localize(WeaponData.WeaponStats w, TeamSide side)
	{
		bool t = side == TeamSide.Attacker;
		w.displayName = w.weaponName switch
		{
			"AK-47/M4" => t ? "AK-47" : "M4A4",
			"Galil/Famas" => t ? "Galil AR" : "FAMAS",
			"SMG" => t ? "MAC-10" : "MP9",
			"Pistol" => t ? "Glock-18" : "USP-S",
			_ => w.weaponName
		};
		return w;
	}

	// ===================== HALFTIME =====================

	private void SwapSidesAtHalftime()
	{
		carriedWeapons.Clear(); // fresh pistol round after the swap — nothing carries over
		(attackerBots, defenderBots) = (defenderBots, attackerBots);
		// Spawns DON'T swap: attackers always spawn at the T side of the map.
		// Swapping them made teams keep their physical positions and only change color.
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

	private void LogScoreboard()
	{
		MatchLogger.Log("=== SCOREBOARD (who actually carried) ===");
		foreach (var b in attackerBots)
			if (b?.Profile != null)
				MatchLogger.Log($"  {b.Profile.Data.nickname}: {b.Profile.MatchKills}K / {b.Profile.MatchDeaths}D");
		foreach (var b in defenderBots)
			if (b?.Profile != null)
				MatchLogger.Log($"  {b.Profile.Data.nickname}: {b.Profile.MatchKills}K / {b.Profile.MatchDeaths}D");
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
		// Player-perspective score — "T 2-13 CT" after halftime reads like a loss when you're the 13
		GUI.Label(new Rect(15, 10, 500, 30),
			$"Round {State.currentRound}  |  YOU {State.PlayerScore} — {State.EnemyScore} THEM  (you are {(State.playerTeamSide == TeamSide.Attacker ? "T" : "CT")})", s);
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
