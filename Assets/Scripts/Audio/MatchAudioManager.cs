using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// MATCH AUDIO MANAGER — All match sound effects and music.
/// 
/// Handles:
/// - Gunshot sounds (per weapon type, 3D positioned)
/// - Kill confirmation sound
/// - Grenade sounds (smoke pop, flash bang, molotov whoosh)
/// - Bomb plant/defuse/explosion
/// - Round start/end jingles
/// - Footstep sounds (3D, different on surfaces)
/// - Background music (low tension → high tension as round progresses)
/// - Announcer voice (round start, bomb planted, clutch, etc.)
/// 
/// SETUP:
/// 1. Add to MatchManager
/// 2. Assign AudioClips in inspector (or leave null — system handles missing clips gracefully)
/// 3. System auto-hooks into EngagementSystem, BombManager, etc. events
/// </summary>
public class MatchAudioManager : MonoBehaviour
{
    [Header("=== WEAPON SOUNDS ===")]
    [SerializeField] private AudioClip rifleShot;
    [SerializeField] private AudioClip awpShot;
    [SerializeField] private AudioClip smgShot;
    [SerializeField] private AudioClip pistolShot;
    [SerializeField] private AudioClip killConfirm;         // "dink" on headshot/kill

    [Header("=== GRENADE SOUNDS ===")]
    [SerializeField] private AudioClip smokePopSound;
    [SerializeField] private AudioClip flashBangSound;
    [SerializeField] private AudioClip molotovSound;
    [SerializeField] private AudioClip grenadeThrowSound;

    [Header("=== BOMB SOUNDS ===")]
    [SerializeField] private AudioClip bombPlantSound;
    [SerializeField] private AudioClip bombPlantedBeep;      // Repeating beep after plant
    [SerializeField] private AudioClip bombDefuseSound;
    [SerializeField] private AudioClip bombExplosionSound;
    [SerializeField] private AudioClip bombDefusedSound;     // Success jingle

    [Header("=== ROUND SOUNDS ===")]
    [SerializeField] private AudioClip roundStartSound;
    [SerializeField] private AudioClip freezeTimeSound;
    [SerializeField] private AudioClip tWinSound;
    [SerializeField] private AudioClip ctWinSound;
    [SerializeField] private AudioClip tenSecondWarning;     // 10 seconds left

    [Header("=== ANNOUNCER ===")]
    [SerializeField] private AudioClip announcerRoundStart;
    [SerializeField] private AudioClip announcerBombPlanted;
    [SerializeField] private AudioClip announcerClutch;      // "It's a 1vX!"
    [SerializeField] private AudioClip announcerAce;         // Player got 5 kills
    [SerializeField] private AudioClip announcerMatchPoint;

    [Header("=== MUSIC ===")]
    [SerializeField] private AudioClip bgMusicCalm;          // Early round / freeze time
    [SerializeField] private AudioClip bgMusicTense;         // Late round / bomb planted
    [SerializeField] private AudioClip bgMusicClutch;        // 1vX situations
    [SerializeField] private AudioClip mvpMusic;             // Post-round MVP

    [Header("=== FOOTSTEPS ===")]
    [SerializeField] private AudioClip[] footstepSounds;     // Array of random step sounds
    [SerializeField] private float footstepInterval = 0.35f;

    [Header("=== SETTINGS ===")]
    [SerializeField] private float masterVolume = 1f;
    [SerializeField] private float musicVolume = 0.3f;
    [SerializeField] private float sfxVolume = 0.7f;
    [SerializeField] private float gunshotVolume = 0.5f;
    [SerializeField] private float maxSoundDistance = 50f;   // 3D sound range

    // Audio sources
    private AudioSource musicSource;
    private AudioSource announcerSource;
    private AudioSource sfxSource;

    // State
    private BombManager bombManager;
    private float bombBeepTimer;
    private bool bombPlanted;
    private Coroutine bombBeepRoutine;

    // ===================== LIFECYCLE =====================

    private void Awake()
    {
        // Create persistent audio sources
        musicSource = CreateAudioSource("Music", true, musicVolume);
        announcerSource = CreateAudioSource("Announcer", false, sfxVolume);
        sfxSource = CreateAudioSource("SFX", false, sfxVolume);
    }

    private void Start()
    {
        // Hook into game events
        EngagementSystem.OnDuelResolved += OnKill;
        EngagementSystem.OnKillFeedEvent += OnKillFeed;

        bombManager = FindObjectOfType<BombManager>();
        if (bombManager != null)
        {
            bombManager.OnBombPlanted += OnBombPlanted;
            bombManager.OnBombExploded += OnBombExploded;
            bombManager.OnBombDefused += OnBombDefused;
        }

        // Start calm music
        PlayMusic(bgMusicCalm);
    }

    private void OnDestroy()
    {
        EngagementSystem.OnDuelResolved -= OnKill;
        EngagementSystem.OnKillFeedEvent -= OnKillFeed;

        if (bombManager != null)
        {
            bombManager.OnBombPlanted -= OnBombPlanted;
            bombManager.OnBombExploded -= OnBombExploded;
            bombManager.OnBombDefused -= OnBombDefused;
        }
    }

    // ===================== PUBLIC API =====================

    /// <summary>Called by RoundManager at freeze time start.</summary>
    public void OnFreezeTime()
    {
        PlaySFX(freezeTimeSound);
        PlayMusic(bgMusicCalm);
        bombPlanted = false;
        if (bombBeepRoutine != null) StopCoroutine(bombBeepRoutine);
    }

    /// <summary>Called by RoundManager when round goes live.</summary>
    public void OnRoundStart()
    {
        PlaySFX(roundStartSound);
        PlayAnnouncer(announcerRoundStart);
    }

    /// <summary>Called by RoundManager at round end.</summary>
    public void OnRoundEnd(TeamSide winner)
    {
        if (bombBeepRoutine != null) StopCoroutine(bombBeepRoutine);
        PlaySFX(winner == TeamSide.Attacker ? tWinSound : ctWinSound);
        PlayMusic(mvpMusic);
    }

    /// <summary>Called when match point is reached.</summary>
    public void OnMatchPoint()
    {
        PlayAnnouncer(announcerMatchPoint);
    }

    /// <summary>Called when a clutch situation (1vX) is detected.</summary>
    public void OnClutchSituation()
    {
        PlayAnnouncer(announcerClutch);
        PlayMusic(bgMusicClutch);
    }

    /// <summary>Called when 10 seconds remain in the round.</summary>
    public void OnTenSecondWarning()
    {
        PlaySFX(tenSecondWarning);
        if (!bombPlanted)
            PlayMusic(bgMusicTense);
    }

    // ===================== GUNSHOT SOUNDS =====================

    /// <summary>
    /// Play a weapon fire sound at a world position.
    /// Called by EngagementSystem when a bot fires.
    /// </summary>
    public void PlayGunshotAt(Vector3 position, string weaponName)
    {
        AudioClip clip = weaponName switch
        {
            "AK-47/M4" => rifleShot,
            "AWP" => awpShot,
            "SMG" => smgShot,
            "Pistol" => pistolShot,
            "Galil/Famas" => rifleShot, // Same as rifle
            _ => rifleShot
        };

        PlaySFXAt(clip, position, gunshotVolume);
    }

    /// <summary>Play footstep sound at position.</summary>
    public void PlayFootstepAt(Vector3 position)
    {
        if (footstepSounds == null || footstepSounds.Length == 0) return;
        AudioClip clip = footstepSounds[Random.Range(0, footstepSounds.Length)];
        PlaySFXAt(clip, position, sfxVolume * 0.3f);
    }

    // ===================== EVENT HANDLERS =====================

    private void OnKill(DuelResolver.DuelOutcome outcome)
    {
        PlaySFX(killConfirm);

        // Check for ace (5 kills in a round)
        if (outcome.winner.RoundKills >= 5)
            PlayAnnouncer(announcerAce);
    }

    private void OnKillFeed(string killer, string victim, TeamSide killerSide, TeamSide victimSide)
    {
        // Kill sound already played in OnKill
    }

    private void OnBombPlanted(string site)
    {
        bombPlanted = true;
        PlaySFX(bombPlantSound);
        PlayAnnouncer(announcerBombPlanted);
        PlayMusic(bgMusicTense);

        // Start beeping
        bombBeepRoutine = StartCoroutine(BombBeepLoop());
    }

    private void OnBombExploded()
    {
        if (bombBeepRoutine != null) StopCoroutine(bombBeepRoutine);
        PlaySFX(bombExplosionSound);
    }

    private void OnBombDefused()
    {
        if (bombBeepRoutine != null) StopCoroutine(bombBeepRoutine);
        PlaySFX(bombDefusedSound);
    }

    private IEnumerator BombBeepLoop()
    {
        float interval = 1.0f;

        while (bombPlanted && bombManager != null && bombManager.IsPlanted)
        {
            PlaySFX(bombPlantedBeep);

            // Beeps speed up as timer runs low
            float timeLeft = bombManager.Timer;
            if (timeLeft < 10f)
                interval = 0.3f;
            else if (timeLeft < 20f)
                interval = 0.5f;
            else
                interval = 1.0f;

            yield return new WaitForSeconds(interval);
        }
    }

    // ===================== GRENADE SOUNDS =====================

    /// <summary>Called by GrenadeEffect when it activates.</summary>
    public static void PlayGrenadeSound(GrenadeType type, Vector3 position)
    {
        var instance = FindObjectOfType<MatchAudioManager>();
        if (instance == null) return;

        AudioClip clip = type switch
        {
            GrenadeType.Smoke => instance.smokePopSound,
            GrenadeType.Flash => instance.flashBangSound,
            GrenadeType.Molotov => instance.molotovSound,
            _ => null
        };

        instance.PlaySFXAt(clip, position, instance.sfxVolume);
    }

    // ===================== AUDIO PLAYBACK =====================

    private void PlaySFX(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, sfxVolume * masterVolume);
    }

    private void PlaySFXAt(AudioClip clip, Vector3 position, float volume)
    {
        if (clip == null) return;

        // Create temporary 3D audio source at position
        GameObject obj = new GameObject("SFX_Temp");
        obj.transform.position = position;

        AudioSource src = obj.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume * masterVolume;
        src.spatialBlend = 1f;         // Full 3D
        src.minDistance = 2f;
        src.maxDistance = maxSoundDistance;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.Play();

        Destroy(obj, clip.length + 0.1f);
    }

    private void PlayAnnouncer(AudioClip clip)
    {
        if (clip == null || announcerSource == null) return;
        announcerSource.PlayOneShot(clip, sfxVolume * masterVolume);
    }

    private void PlayMusic(AudioClip clip)
    {
        if (clip == null || musicSource == null) return;

        if (musicSource.clip == clip && musicSource.isPlaying) return;

        musicSource.clip = clip;
        musicSource.volume = musicVolume * masterVolume;
        musicSource.Play();
    }

    /// <summary>Crossfade to new music track.</summary>
    public void CrossfadeMusic(AudioClip newClip, float duration = 1f)
    {
        if (newClip == null) return;
        StartCoroutine(DoCrossfade(newClip, duration));
    }

    private IEnumerator DoCrossfade(AudioClip newClip, float duration)
    {
        float startVol = musicSource.volume;
        float t = 0;

        // Fade out
        while (t < duration / 2)
        {
            t += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVol, 0, t / (duration / 2));
            yield return null;
        }

        // Switch track
        musicSource.clip = newClip;
        musicSource.Play();

        // Fade in
        t = 0;
        while (t < duration / 2)
        {
            t += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(0, musicVolume * masterVolume, t / (duration / 2));
            yield return null;
        }
    }

    private AudioSource CreateAudioSource(string name, bool loop, float volume)
    {
        GameObject obj = new GameObject($"Audio_{name}");
        obj.transform.SetParent(transform);
        AudioSource src = obj.AddComponent<AudioSource>();
        src.loop = loop;
        src.volume = volume * masterVolume;
        src.playOnAwake = false;
        src.spatialBlend = 0f; // 2D for music/UI sounds
        return src;
    }
}
