using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Observer Camera v2 — Manual control + auto-director.
/// 
/// Controls:
///   1-5: Follow attacker bots (your team)
///   6-0: Follow defender bots (enemy team)
///   TAB: Cycle to next alive player
///   Space: Toggle auto-director on/off
///   F: Free camera mode (WASD to fly, mouse to look)
///   Right Mouse Hold: Freelook around current target
///   Scroll Wheel: Zoom in/out
///   
/// Auto-director still works when enabled — switches on kills and clutches.
/// But manual input always overrides.
/// </summary>
public class ObserverCamera : MonoBehaviour
{
	[Header("Overview")]
	[SerializeField] private Vector3 overviewPosition = new Vector3(40f, 45f, 25f);
	[SerializeField] private Vector3 overviewRotation = new Vector3(65f, 0f, 0f);

	[Header("Follow")]
	[SerializeField] private bool povMode = true; // player-POV follow (V toggles third person)
	[SerializeField] private float followHeight = 10f;
	[SerializeField] private float followDistance = 6f;
	[SerializeField] private float smoothSpeed = 8f;
	[SerializeField] private float minZoom = 3f;
	[SerializeField] private float maxZoom = 20f;
	[SerializeField] private float zoomSpeed = 3f;

	[Header("Free Camera")]
	[SerializeField] private float freeMoveSpeed = 20f;
	[SerializeField] private float freeLookSpeed = 3f;

	[Header("Auto-Director")]
	[SerializeField] private bool autoDirector = true;
	[SerializeField] private float autoSwitchInterval = 3f;
	[SerializeField] private float killCamDuration = 1.5f;

	// State
	private CameraMode currentMode = CameraMode.Overview;
	private Transform currentTarget;
	private int currentTargetIndex = -1;
	private float autoSwitchTimer;
	private float killCamTimer;
	private float currentZoom;
	private float freeLookX, freeLookY;
	private float orbitX, orbitY;
	private bool isOrbiting = false;

	// Cached bots
	private List<BotController> attackerBots = new List<BotController>();
	private List<BotController> defenderBots = new List<BotController>();
	private List<BotController> allAliveBots = new List<BotController>();
	private float cacheTimer = 0f;

	private void OnEnable()
	{
		// EngagementSystem is the live combat system (DuelTrigger was legacy and never fired)
		EngagementSystem.OnDuelResolved += OnDuelResolved;
		currentZoom = followDistance;
	}

	private void OnDisable()
	{
		EngagementSystem.OnDuelResolved -= OnDuelResolved;
	}

	private void Update()
	{
		RefreshBotCache();
		HandleInput();
	}

	private void LateUpdate()
	{
		switch (currentMode)
		{
			case CameraMode.Overview:
				ApplyOverview();
				break;
			case CameraMode.Follow:
			case CameraMode.KillCam:
			case CameraMode.Clutch:
				ApplyFollow();
				break;
			case CameraMode.Free:
				ApplyFreeCamera();
				break;
		}
	}

	// ===================== INPUT =====================

	private void HandleInput()
	{
		// Number keys 1-5: select attacker
		for (int i = 0; i < 5; i++)
		{
			if (Input.GetKeyDown(KeyCode.Alpha1 + i))
			{
				SelectBot(attackerBots, i);
				return;
			}
		}

		// Number keys 6-0: select defender
		for (int i = 0; i < 5; i++)
		{
			KeyCode key = i < 4 ? (KeyCode.Alpha6 + i) : KeyCode.Alpha0;
			if (Input.GetKeyDown(key))
			{
				SelectBot(defenderBots, i);
				return;
			}
		}

		// TAB: cycle next alive player
		if (Input.GetKeyDown(KeyCode.Tab))
		{
			CycleNextAlive();
		}

		// Space: toggle auto-director
		if (Input.GetKeyDown(KeyCode.Space))
		{
			autoDirector = !autoDirector;
			Debug.Log($"Auto-Director: {(autoDirector ? "ON" : "OFF")}");
		}

		// V: toggle POV / third-person follow
		if (Input.GetKeyDown(KeyCode.V))
		{
			povMode = !povMode;
			Debug.Log($"Camera: {(povMode ? "POV" : "third person")}");
		}

		// F: toggle free camera
		if (Input.GetKeyDown(KeyCode.F))
		{
			if (currentMode == CameraMode.Free)
			{
				currentMode = CameraMode.Follow;
				Cursor.lockState = CursorLockMode.None;
			}
			else
			{
				currentMode = CameraMode.Free;
				freeLookX = transform.eulerAngles.y;
				freeLookY = transform.eulerAngles.x;
				Cursor.lockState = CursorLockMode.Locked;
			}
		}

		// Scroll: zoom
		float scroll = Input.GetAxis("Mouse ScrollWheel");
		if (Mathf.Abs(scroll) > 0.01f)
		{
			currentZoom -= scroll * zoomSpeed;
			currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
		}

		// Right mouse: orbit around target
		isOrbiting = Input.GetMouseButton(1) && currentMode != CameraMode.Free;
		if (isOrbiting)
		{
			orbitX += Input.GetAxis("Mouse X") * 3f;
			orbitY -= Input.GetAxis("Mouse Y") * 2f;
			orbitY = Mathf.Clamp(orbitY, 10f, 80f);
		}
		else
		{
			// Reset orbit slowly
			orbitX = Mathf.Lerp(orbitX, 0f, Time.deltaTime * 2f);
			orbitY = Mathf.Lerp(orbitY, followHeight > 5f ? 45f : 30f, Time.deltaTime * 2f);
		}

		// Auto-director logic
		if (autoDirector && currentMode == CameraMode.Follow)
		{
			autoSwitchTimer -= Time.deltaTime;
			if (autoSwitchTimer <= 0f)
			{
				currentTarget = FindMostInterestingBot();
				autoSwitchTimer = autoSwitchInterval;
			}
		}

		// Kill cam timer
		if (currentMode == CameraMode.KillCam)
		{
			killCamTimer -= Time.deltaTime;
			if (killCamTimer <= 0f)
				currentMode = CameraMode.Follow;
		}

		// Validate target
		if (currentTarget != null && !IsAlive(currentTarget))
		{
			if (autoDirector)
				currentTarget = FindMostInterestingBot();
			else
				CycleNextAlive();
		}
	}

	// ===================== SELECT / CYCLE =====================

	private void SelectBot(List<BotController> team, int index)
	{
		if (index >= team.Count) return;
		var bot = team[index];
		if (bot == null || bot.Profile == null || !bot.Profile.IsAlive)
		{
			Debug.Log($"Bot {index + 1} is dead");
			return;
		}

		currentTarget = bot.transform;
		currentMode = CameraMode.Follow;
		autoDirector = false;
		autoSwitchTimer = autoSwitchInterval;
		Debug.Log($"Following: {bot.Profile.Data.nickname}");
	}

	private void CycleNextAlive()
	{
		if (allAliveBots.Count == 0) return;

		currentTargetIndex++;
		if (currentTargetIndex >= allAliveBots.Count) currentTargetIndex = 0;

		currentTarget = allAliveBots[currentTargetIndex].transform;
		currentMode = CameraMode.Follow;
		autoSwitchTimer = autoSwitchInterval;

		var bot = allAliveBots[currentTargetIndex];
		Debug.Log($"Following: {bot.Profile.Data.nickname} ({bot.Side})");
	}

	// ===================== CAMERA MODES =====================

	private void ApplyOverview()
	{
		transform.position = Vector3.Lerp(transform.position, overviewPosition, smoothSpeed * Time.deltaTime);
		transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(overviewRotation), smoothSpeed * Time.deltaTime);
	}

	private void ApplyFollow()
	{
		if (currentTarget == null)
		{
			ApplyOverview();
			return;
		}

		// POV: camera at the bot's eyes, looking where they look.
		// Right-click orbit temporarily overrides for a look-around.
		if (povMode && !isOrbiting && currentMode != CameraMode.Clutch)
		{
			Vector3 eye = currentTarget.position + Vector3.up * 1.55f + currentTarget.forward * 0.35f;
			transform.position = Vector3.Lerp(transform.position, eye, smoothSpeed * 1.5f * Time.deltaTime);
			Quaternion povRot = Quaternion.LookRotation(currentTarget.forward, Vector3.up);
			transform.rotation = Quaternion.Slerp(transform.rotation, povRot, smoothSpeed * Time.deltaTime);
			return;
		}

		// Calculate orbit position
		float rad = orbitX * Mathf.Deg2Rad;
		float pitchRad = orbitY * Mathf.Deg2Rad;

		Vector3 offset = new Vector3(
			Mathf.Sin(rad) * currentZoom * Mathf.Cos(pitchRad),
			Mathf.Sin(pitchRad) * currentZoom,
			-Mathf.Cos(rad) * currentZoom * Mathf.Cos(pitchRad)
		);

		// Default: behind the target
		if (!isOrbiting)
		{
			Vector3 behindOffset = -currentTarget.forward * 3.5f + Vector3.up * 1.8f + currentTarget.right * 0.5f;
			offset = behindOffset;
		}

		Vector3 targetPos = currentTarget.position + offset;
		Vector3 lookAt = currentTarget.position + Vector3.up * 1.5f;

		// Clutch mode: tighter
		if (currentMode == CameraMode.Clutch)
		{
			targetPos = currentTarget.position + offset.normalized * (currentZoom * 0.5f);
		}

		transform.position = Vector3.Lerp(transform.position, targetPos, smoothSpeed * Time.deltaTime);
		Quaternion lookRot = Quaternion.LookRotation(lookAt - transform.position);
		transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, smoothSpeed * Time.deltaTime);
	}

	private void ApplyFreeCamera()
	{
		// Mouse look
		freeLookX += Input.GetAxis("Mouse X") * freeLookSpeed;
		freeLookY -= Input.GetAxis("Mouse Y") * freeLookSpeed;
		freeLookY = Mathf.Clamp(freeLookY, -89f, 89f);

		transform.rotation = Quaternion.Euler(freeLookY, freeLookX, 0f);

		// WASD movement
		float speed = freeMoveSpeed * Time.deltaTime;
		if (Input.GetKey(KeyCode.LeftShift)) speed *= 2.5f;

		if (Input.GetKey(KeyCode.W)) transform.position += transform.forward * speed;
		if (Input.GetKey(KeyCode.S)) transform.position -= transform.forward * speed;
		if (Input.GetKey(KeyCode.A)) transform.position -= transform.right * speed;
		if (Input.GetKey(KeyCode.D)) transform.position += transform.right * speed;
		if (Input.GetKey(KeyCode.Q)) transform.position -= Vector3.up * speed;
		if (Input.GetKey(KeyCode.E)) transform.position += Vector3.up * speed;
	}

	// ===================== EVENTS =====================

	private void OnDuelResolved(DuelResolver.DuelOutcome outcome)
	{
		// Find winner bot
		foreach (var bot in allAliveBots)
		{
			if (bot != null && bot.Profile == outcome.winner)
			{
				if (autoDirector || currentMode == CameraMode.KillCam)
				{
					currentTarget = bot.transform;
					currentMode = CameraMode.KillCam;
					killCamTimer = killCamDuration;
				}
				break;
			}
		}

		// Check clutch
		if (IsClutchSituation())
		{
			currentMode = CameraMode.Clutch;
			currentTarget = FindClutchPlayer();
		}
	}

	// ===================== PUBLIC =====================

	public void SetOverviewMode()
	{
		currentMode = CameraMode.Overview;
	}

	/// <summary>Called by MapGenerator so the overview matches the generated layout.</summary>
	public void SetOverview(Vector3 position, Vector3 eulerRotation)
	{
		overviewPosition = position;
		overviewRotation = eulerRotation;
	}

	public void SetFollowMode()
	{
		currentMode = CameraMode.Follow;
		if (currentTarget == null)
			currentTarget = FindMostInterestingBot();
		autoSwitchTimer = autoSwitchInterval;
	}

	// ===================== HELPERS =====================

	private void RefreshBotCache()
	{
		cacheTimer -= Time.deltaTime;
		if (cacheTimer > 0f) return;
		cacheTimer = 0.5f;

		attackerBots.Clear();
		defenderBots.Clear();
		allAliveBots.Clear();

		BotController[] all = FindObjectsByType<BotController>(FindObjectsSortMode.None);
		foreach (var bot in all)
		{
			if (bot == null || bot.Profile == null) continue;
			if (bot.Side == TeamSide.Attacker) attackerBots.Add(bot);
			else defenderBots.Add(bot);

			if (bot.Profile.IsAlive) allAliveBots.Add(bot);
		}

		// Sort by name for consistent numbering
		attackerBots.Sort((a, b) => string.Compare(a.name, b.name));
		defenderBots.Sort((a, b) => string.Compare(a.name, b.name));
	}

	private Transform FindMostInterestingBot()
	{
		Transform best = null;
		float bestScore = float.MinValue;

		foreach (var bot in allAliveBots)
		{
			float score = 0f;

			float closestEnemy = float.MaxValue;
			foreach (var other in allAliveBots)
			{
				if (other.Side == bot.Side) continue;
				float d = Vector3.Distance(bot.transform.position, other.transform.position);
				if (d < closestEnemy) closestEnemy = d;
			}

			if (closestEnemy < float.MaxValue)
				score += 50f - Mathf.Min(closestEnemy, 50f);

			score += bot.Profile.RoundKills * 10f;
			if (bot.Side == TeamSide.Attacker) score += 5f;

			if (score > bestScore) { bestScore = score; best = bot.transform; }
		}
		return best;
	}

	private Transform FindClutchPlayer()
	{
		int t = 0, ct = 0;
		Transform lastT = null, lastCT = null;
		foreach (var bot in allAliveBots)
		{
			if (bot.Side == TeamSide.Attacker) { t++; lastT = bot.transform; }
			else { ct++; lastCT = bot.transform; }
		}
		if (t == 1 && ct > 1) return lastT;
		if (ct == 1 && t > 1) return lastCT;
		return null;
	}

	private bool IsClutchSituation()
	{
		int t = 0, ct = 0;
		foreach (var bot in allAliveBots)
		{
			if (bot.Side == TeamSide.Attacker) t++; else ct++;
		}
		return (t == 1 && ct >= 2) || (ct == 1 && t >= 2);
	}

	private bool IsAlive(Transform target)
	{
		var bot = target?.GetComponent<BotController>();
		return bot != null && bot.Profile != null && bot.Profile.IsAlive;
	}

	// ===================== HUD =====================

	private void OnGUI()
	{
		GUIStyle s = new GUIStyle(GUI.skin.label);
		s.fontSize = 12;
		s.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

		// Controls hint
		float y = Screen.height - 120;
		GUI.Box(new Rect(5, y - 5, 280, 120), "");
		GUI.Label(new Rect(10, y, 270, 18), "1-5: T players | 6-0: CT players", s);
		GUI.Label(new Rect(10, y + 18, 270, 18), "TAB: Next alive | SPACE: Auto-director", s);
		GUI.Label(new Rect(10, y + 36, 270, 18), "F: Free cam (WASD+Mouse) | Scroll: Zoom", s);
		GUI.Label(new Rect(10, y + 54, 270, 18), "V: POV/3rd person | Right Click: Orbit", s);

		// Current state
		string targetName = "";
		if (currentTarget != null)
		{
			var bot = currentTarget.GetComponent<BotController>();
			if (bot?.Profile?.Data != null)
				targetName = $"{bot.Profile.Data.nickname} ({bot.Side})";
		}

		s.fontStyle = FontStyle.Bold;
		s.normal.textColor = Color.yellow;
		GUI.Label(new Rect(10, y + 78, 270, 18), $"{currentMode} | {targetName}", s);

		s.normal.textColor = autoDirector ? Color.green : Color.red;
		GUI.Label(new Rect(10, y + 96, 270, 18), $"Auto-Director: {(autoDirector ? "ON" : "OFF")}", s);

		// Player list on left side
		DrawPlayerList();
	}

	private void DrawPlayerList()
	{
		GUIStyle alive = new GUIStyle(GUI.skin.label);
		alive.fontSize = 13;

		GUIStyle dead = new GUIStyle(alive);
		dead.normal.textColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

		float x = 10, y = 130;

		// T side
		alive.normal.textColor = new Color(1f, 0.4f, 0.4f);
		GUI.Label(new Rect(x, y, 200, 18), "— T SIDE —", alive);
		y += 18;

		for (int i = 0; i < attackerBots.Count; i++)
		{
			var bot = attackerBots[i];
			bool isAlive = bot.Profile != null && bot.Profile.IsAlive;
			bool isFollowed = currentTarget == bot.transform;

			GUIStyle st = isAlive ? alive : dead;
			string prefix = isFollowed ? ">> " : $"  {i + 1}. ";
			string kills = isAlive ? $" [{bot.Profile.RoundKills}K]" : " [DEAD]";

			GUI.Label(new Rect(x, y, 200, 18), $"{prefix}{bot.Profile?.Data?.nickname ?? "?"}{kills}", st);
			y += 16;
		}

		y += 8;

		// CT side
		alive.normal.textColor = new Color(0.4f, 0.6f, 1f);
		GUI.Label(new Rect(x, y, 200, 18), "— CT SIDE —", alive);
		y += 18;

		for (int i = 0; i < defenderBots.Count; i++)
		{
			var bot = defenderBots[i];
			bool isAlive = bot.Profile != null && bot.Profile.IsAlive;
			bool isFollowed = currentTarget == bot.transform;

			GUIStyle st = isAlive ? alive : dead;
			int keyNum = i < 4 ? i + 6 : 0;
			string prefix = isFollowed ? ">> " : $"  {keyNum}. ";
			string kills = isAlive ? $" [{bot.Profile.RoundKills}K]" : " [DEAD]";

			GUI.Label(new Rect(x, y, 200, 18), $"{prefix}{bot.Profile?.Data?.nickname ?? "?"}{kills}", st);
			y += 16;
		}
	}
}
