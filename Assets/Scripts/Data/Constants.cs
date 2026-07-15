using UnityEngine;

public static class Constants
{
    // Duel Formula
    public const float BASE_DUEL_CHANCE = 50f;
    public const float AIM_WEIGHT = 0.30f;
    public const float REFLEX_WEIGHT = 0.15f;
    public const float MAX_VARIANCE = 15f;
    public const float CONSISTENCY_REDUCTION = 0.1f;
    public const float CLUTCH_WEIGHT = 0.2f;
    public const float CLUTCH_BASELINE = 50f;
    public const float STRATEGY_COUNTER_BONUS = 10f;

    // Buy Type Modifiers
    public const float FULL_BUY_MODIFIER = 15f;
    public const float FORCE_BUY_MODIFIER = 5f;
    public const float HALF_BUY_MODIFIER = 0f;
    public const float ECO_MODIFIER = -10f;

    // Economy — CS2-style, team pool = 5 players' wallets combined
    public const int TEAM_START_CREDITS = 4000;   // $800 × 5, pistol round
    public const int WIN_REWARD = 16250;          // $3250 × 5 elimination win
    public const int LOSS_BONUS_START = 7000;     // $1400 × 5
    public const int LOSS_BONUS_INCREMENT = 2500; // $500 × 5 per consecutive loss
    public const int LOSS_BONUS_MAX = 17000;      // $3400 × 5 (CS2 cap)
    public const int KILL_BONUS_MIN = 200;        // pooled, per kill
    public const int KILL_BONUS_MAX = 300;
    public const int PLANT_BONUS = 4000;          // $800 × 5 — paid even if the round is lost
    public const int TEAM_CREDITS_CAP = 80000;    // $16000 × 5

    // Per-player buy packages (weapon + armor + utility, approximated)
    public const int BUY_COST_FULL = 4400;
    public const int BUY_COST_FULL_AWP = 6500;    // AWPer slot on a full buy
    public const int BUY_COST_FORCE = 2900;
    public const int BUY_COST_HALF = 2000;
    public const int BUY_COST_ECO = 500;

    // Match
    public const int ROUNDS_TO_WIN = 13;
    public const int HALFTIME_ROUND = 12;
    public const int MAX_ROUNDS = 24; // OT not in demo, but placeholder

    // Round Timing (seconds, adjustable)
    public const float FREEZE_TIME = 5f;
    public const float ROUND_TIME = 90f;
    public const float MID_ROUND_TRIGGER_TIME = 20f; // seconds after round start
    public const float LATE_ROUND_TRIGGER_TIME = 60f;
    public const float POST_ROUND_DELAY = 3f;

    // Bot
    public const float BOT_MOVE_SPEED = 5f;
    public const float BOT_PEEK_SPEED = 2.5f;
    public const float RAYCAST_CHECK_INTERVAL = 0.2f; // LoS check frequency
    public const float DUEL_RESOLVE_TIME = 0.5f; // time for duel animation

    // Bot AI (engine fix pass)
    public const float CLUTCH_HITCHANCE_SCALE = 0.3f; // hit-chance multiplier scale in 1vX
    public const float AI_COMMIT_DELAY_MIN = 10f;     // AI coach commit window after strategies run
    public const float AI_COMMIT_DELAY_MAX = 18f;

    // Hold-angle scanning (playtest #17): look at a spot, HOLD it, flick to the
    // next — the old continuous ping-pong read as bots spinning in place
    public const float SWEEP_TURN_SPEED = 120f;  // deliberate re-aim flick, deg/sec
    public const float SWEEP_DWELL_MIN = 1.6f;   // seconds an angle is held
    public const float SWEEP_DWELL_MAX = 3.4f;

    // Bot AI (playtest #1 pass)
    public const float BURST_SPRAY_LIMIT = 0.45f;   // stop spraying above this, re-aim
    public const float BURST_RESUME_SPRAY = 0.15f;  // resume firing when spray recovers to this
    public const float HAZARD_LOOKAHEAD = 5f;       // how far ahead bots check for smokes/mollies
    public const float RETAKE_GROUP_RADIUS = 8f;    // "gathered at approach" radius
    public const float RETAKE_GROUP_TIMEOUT = 8f;   // max seconds to wait for the group
    public const float DEFUSE_ALERT_RANGE = 35f;    // Ts inside this range snap to the defuse sound

    // Combat feel (playtest #2, CS-referenced)
    public const float PROXIMITY_AWARENESS_RANGE = 7f; // this close = heard, FOV ignored
    public const float FIRST_SHOT_BONUS = 1.25f;       // first-bullet accuracy
    public const float HEADSHOT_BASE_CHANCE = 0.04f;   // playtest #8: was a 70% HS-rate coin flip
    public const float HEADSHOT_AIM_SCALE = 0.10f;     // aim 100 → +10% headshot chance
    public const float HEADSHOT_MULTIPLIER = 3.2f;     // rifle body 33 → ~105 head (one-tap like AK)

    // Crosshair placement model (playtest #3) — the duel is decided before the trigger
    public const float ACQUIRE_REACTION_MAX = 0.40f;      // reflexes 0
    public const float ACQUIRE_REACTION_MIN = 0.10f;      // reflexes 100
    public const float ACQUIRE_ANGLE_MIN_FACTOR = 0.5f;   // enemy walks INTO the crosshair — boom
    public const float ACQUIRE_ANGLE_MAX_FACTOR = 1.6f;   // caught 90°+ off-angle — long swing
    public const float ACQUIRE_INTEL_FACTOR = 0.65f;      // prefire/pre-aim: the team knew they were there
    public const float INTEL_PREAIM_RADIUS = 8f;          // intel counts if within this of the actual position
    public const float AWP_ACQUIRE_PENALTY = 0.35f;       // scoping in (AWPers took 50% of all kills — trim)

    // Counter-strafe discipline — rifles do not fire on the move
    public const float MOVE_ACCURACY_RIFLE = 0.08f;       // AK sprinting hits nothing
    public const float MOVE_ACCURACY_AWP = 0.02f;
    public const float MOVE_ACCURACY_LIGHT = 0.7f;        // SMG/pistol run-and-gun is legit
    public const float COUNTERSTRAFE_SPEED_THRESHOLD = 1.2f; // above this speed a rifle holds fire

    // Hold-angle sweep by GameSense (pros keep the crosshair ON the angle)
    public const float SWEEP_ARC_LOW_GS = 80f;
    public const float SWEEP_ARC_HIGH_GS = 30f;

    // Coach control rework: the team plays, the coach watches
    public const int TIMEOUTS_PER_MATCH = 3;
    public const float AUTO_FREEZE_TIME = 3f; // freeze length when the IGL calls the round

    // Flash blinding (playtest #5) — flashes previously only nudged spray
    public const float FLASH_MAX_BLIND = 2.8f;       // full face-flash blind duration
    public const float FLASH_BLIND_THRESHOLD = 0.35f; // min strength that actually blinds

    // Playtest #4: bomb carrier, reload retreat, one-taps, panic utility
    public const float BOMB_PICKUP_RANGE = 2.5f;
    public const float RELOAD_RETREAT_RANGE = 14f;  // enemy this close while reloading → back off
    public const float FIRST_BULLET_HS_MULT = 1.6f; // opening shot is aimed head-level
    public const float MOLOTOV_DPS = 55f; // real fire — crossing costs ~60HP (scene had stale 30)
    public const float TARGET_MOVE_PENALTY = 0.85f;     // runners are harder to hit
    public const float TARGET_MOVE_PENALTY_AWP = 0.6f;  // and nearly unsnipable (no more 45m runner picks)
    public const float SITE_EXECUTE_RADIUS = 14f;   // bots inside this of a site = already executing

    // Panic self-smoke (playtest #17: 10 in 6 rounds — a desperation play, not a habit).
    // Fires only when badly hurt, locally swarmed AND alone — once per team per round.
    public const float PANIC_SMOKE_HP = 32f;
    public const float PANIC_SMOKE_ENEMY_RANGE = 14f;   // 2+ known enemies this close
    public const float PANIC_SMOKE_SUPPORT_RANGE = 12f; // a teammate this close = no panic

    // Utility is bought with leftover money, not granted by buy tier alone —
    // "5 AKs round 2" must mean a THIN util pouch (playtest #17). $500/nade at
    // CS scale; the team floor still guarantees a few flashes on any budget.
    public const int UTIL_BANK_PER_NADE = 500;

    // Playtest #18: guns beat grenades at contact + evade sanity
    public const float UTIL_ABORT_ENEMY_RANGE = 12f; // known enemy this close → shoot, don't nade
    public const float CLOSE_RANGE_NO_COVER = 9f;    // point-blank: no cover cycling, stand and fight
    public const float EVADE_COOLDOWN = 1.5f;        // one dodge, then turn and face the shooter

    // Playtest #20: rotations don't yank site-adjacent holders through the open
    public const float ROTATE_ALREADY_THERE_RANGE = 20f;

    // Playtest #20: two bots trading misses face to face at 2m looked absurd —
    // point-blank shots get a hit-chance floor no matter the spray/movement
    public const float POINTBLANK_RANGE = 5f;
    public const float POINTBLANK_HITCHANCE_FLOOR = 0.4f;

    // Utility points (playtest #20 follow-up): scene-authored throw targets +
    // IGL doctrine — aggressive teams spend scarce nades on entry flashes,
    // patient teams cut the map with smokes first
    public const float UTIL_DOCTRINE_AGGRO = 55f; // protocols.aggression at/above = flash-first
    public const int CT_ANCHOR_POINT_CAP = 4;     // max simultaneous anchor watches

    // Utility inventory at CS scale (playtest #21: "1 nade on the pistol round"
    // was an order of magnitude off — every CS player carries up to 4 nades,
    // a full-buy team ~16-20, and even pistol rounds run a few flashes).
    // Budgets live HERE, not in scene-serialized fields (stale-value bug class).
    public const int UTIL_BUDGET_FULL = 16;
    public const int UTIL_BUDGET_FORCE = 10;
    public const int UTIL_BUDGET_HALF = 7;
    public const int UTIL_BUDGET_ECO = 4;
    public const int UTIL_TEAM_FLOOR = 4;            // below this basically never happens in CS
    public const float CT_CHOKE_REARM = 12f;         // denial molly re-arms after burning out
    public const float SUPPORT_FLASH_COOLDOWN = 10f; // per side — flash before a contested re-peek
    public const float SUPPORT_FLASH_ENEMIES = 2;    // known enemies at the fight to justify it

    // Utility timing (playtest #2)
    public const float UTIL_THROW_RANGE = 40f;    // rush smoke must LAND before contact (log: smoke 20.0s, first duel 21.5s)
    public const float UTIL_JIT_TIMEOUT = 20f;    // give up waiting and throw anyway
    public const float CT_CHOKE_WATCH_TIME = 40f; // how long CT watches a choke for denial molly
    public const float CT_CHOKE_TRIGGER_RANGE = 24f; // trigger EARLY — flight time made mollies land behind the rush
    public const float UTIL_MAX_THROW_RANGE = 45f; // 32 starved commit utility (log: 0 attacker mollies/match)   // nobody lobs a nade across the whole map

    // CT defensive brain (playtest #2)
    public const float CT_EVAL_INTERVAL = 2f;     // how often the AI CT re-reads comms info
    public const float CT_ROTATE_COOLDOWN = 12f;  // min seconds between info rotations
    public const int CT_ROTATE_SIGNAL_DIFF = 2;   // site signal lead required to rotate
    public const float CT_SIGNAL_RANGE = 30f;     // info within this range of a site counts

    // Career (Sprint 2)
    public const int STARTING_BUDGET = 10000;
    public const int QUALIFIER_PRIZE_WIN = 3000;
    public const int QUALIFIER_PRIZE_LOSS = 500;
    public const int WEEKLY_RENT = 200;
    public const int SPONSOR_WEEKLY = 400;
    public const int SCOUTING_POOL_SIZE = 30;
    public const int STARTING_ROSTER_TIER_MIN = 48;
    public const int STARTING_ROSTER_TIER_MAX = 62;
    public const int QUALIFIER_TIER_MIN = 45;
    public const int QUALIFIER_TIER_MAX = 65;
    public const float MATCH_END_RETURN_DELAY = 6f; // win screen time before returning home

    // Qualifier bracket — 4 teams, single elim, played "in one evening"
    // (the week only ticks when the bracket closes)
    public const string PLAYER_TEAM_NAME = "YOUR SQUAD";
    public const int QUALIFIER_PRIZE_CHAMPION = 6000;
    public const int QUALIFIER_PRIZE_FINAL_LOSS = 2000;
    public const float SIM_WINPROB_PER_OVR = 0.035f;   // win-prob shift per point of avg-OVR gap
    public const float SIM_WINPROB_MIN = 0.12f;        // even a weak team can upset
    public const float SIM_WINPROB_MAX = 0.88f;
    public const float SIM_LOSER_SCORE_BASE = 11f;     // evenly matched sims end 13-11ish
    public const float SIM_LOSER_SCORE_PER_OVR = 0.5f; // each OVR point of gap costs half a round
    public const float SIM_LOSER_SCORE_JITTER = 3f;

    // Scenes
    public const string HOME_SCENE_NAME = "Home";
    public const string MATCH_SCENE_NAME = "SampleScene";
    public const string MENU_SCENE_NAME = "MainMenu";

    // Roster & Negotiation (Phase C)
    public const int ROSTER_SIZE = 5;
    public const float NEGOTIATE_BASE_FLOOR = 0.85f;     // low-ego players accept down to 85% of ask
    public const float NEGOTIATE_COUNTER_WINDOW = 0.85f; // offers within this fraction of the floor get countered
    public const float NEGOTIATE_REJECT_ASK_RAISE = 1.05f; // insulting lowball bumps their asking price

    // Save
    public const string SAVE_FILE_NAME = "career_save.json";
    public const int SAVE_VERSION = 5;
}