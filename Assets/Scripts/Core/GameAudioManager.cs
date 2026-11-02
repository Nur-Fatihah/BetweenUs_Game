using System;
using System.Threading.Tasks;
using UnityEngine;
using S = Crony.AcousticSynth;

namespace Crony
{
    /// <summary>Menu = lobby waltz, Park = peaceful play, Meeting = gentle minor tension (also used during sabotage).</summary>
    public enum MusicMood { None, Menu, Park, Meeting }

    /// <summary>
    /// Cozy acoustic soundscape, synthesised at runtime (AcousticSynth) so the game ships no audio files: piano,
    /// picked guitar, airy flute and pizzicato strings for music; wood, paper, water and chimes for effects.
    /// Music renders on a worker thread so a mood change never hitches a frame. Music and effects have separate
    /// saved volumes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameAudioManager : MonoBehaviour
    {
        public const string MusicVolumeKey = "Crony.MusicVolume", EffectsVolumeKey = "Crony.EffectsVolume", MuteInBackgroundKey = "Crony.MuteInBackground";
        const float FadeSeconds = .9f;

        static GameAudioManager instance;
        [SerializeField] AudioSource source;
        [SerializeField, Range(0f, 1f)] float effectsVolume = .6f;
        [SerializeField, Range(0f, 1f)] float musicVolume = .4f;

        AudioClip uiClick, uiHover, taskStep, taskMiss, taskComplete, gameTap, klaxon, incapacitate, report, vote, eject,
            sabotage, repaired, recruit, chatPing, roleCrew, roleImpostor, victory, defeat, stumpTravel;
        AudioClip[] notes;
        AudioClip[][] footsteps;
        int footstepFoot;
        readonly AudioClip[] music = new AudioClip[4];
        readonly Task<float[]>[] rendering = new Task<float[]>[4];
        AudioSource musicSource;
        MusicMood mood = MusicMood.None, playingMood = MusicMood.None;
        float musicFade;

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            if (source == null) source = GetComponent<AudioSource>();
            if (source == null) source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.volume = 0f;
            effectsVolume = PlayerPrefs.GetFloat(EffectsVolumeKey, effectsVolume);
            musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, musicVolume);
            BuildEffects();
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browsers have no worker threads, so the browser build plays the tracks baked by Crony > Bake Browser Music.
            for (int i = 1; i < music.Length; i++) music[i] = Resources.Load<AudioClip>(BakedMusicFolder + "/" + (MusicMood)i);
#else
            // Start every track now on worker threads; each is ready long before it's first needed.
            for (int i = 1; i < music.Length; i++) { MusicMood m = (MusicMood)i; rendering[i] = Task.Run(() => RenderMusic(m)); }
#endif
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        static GameAudioManager EnsureInstance()
        {
            if (instance != null) return instance;
            instance = FindAnyObjectByType<GameAudioManager>();
            if (instance != null) return instance;
            GameObject audioObject = new GameObject("Crony Audio");
            DontDestroyOnLoad(audioObject);
            return audioObject.AddComponent<GameAudioManager>();
        }

        void Update()
        {
            float target = mood == playingMood ? musicVolume : 0f;
            musicFade = Mathf.MoveTowards(musicFade, target, Time.unscaledDeltaTime / FadeSeconds * Mathf.Max(.05f, musicVolume));
            if (mood != playingMood && musicFade <= .001f)
            {
                AudioClip next = mood == MusicMood.None ? null : MusicClip(mood);
                // Wait (silently) for a track that is still rendering rather than blocking the frame.
                if (mood == MusicMood.None || next != null)
                {
                    playingMood = mood;
                    musicSource.Stop();
                    if (next != null) { musicSource.clip = next; musicSource.Play(); }
                }
            }
            musicSource.volume = musicFade;
        }

        AudioClip MusicClip(MusicMood next)
        {
            int index = (int)next;
            if (music[index] != null) return music[index];
            Task<float[]> task = rendering[index];
            if (task == null || !task.IsCompleted) return null;
            if (task.IsFaulted) { Debug.LogWarning("Music render failed: " + task.Exception?.GetBaseException().Message); rendering[index] = null; return null; }
            music[index] = Clip("Music " + next, task.Result);
            return music[index];
        }

        void OnApplicationFocus(bool focused)
        {
            if (PlayerPrefs.GetInt(MuteInBackgroundKey, 1) == 1) AudioListener.pause = !focused;
        }

        // ---- Public API ----

        public static float EffectsVolume => EnsureInstance().effectsVolume;
        public static float MusicVolume => EnsureInstance().musicVolume;

        public static void SetEffectsVolume(float value)
        {
            GameAudioManager manager = EnsureInstance();
            manager.effectsVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(EffectsVolumeKey, manager.effectsVolume);
        }

        public static void SetMusicVolume(float value)
        {
            GameAudioManager manager = EnsureInstance();
            manager.musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, manager.musicVolume);
        }

        public static void SetMusic(MusicMood next) => EnsureInstance().mood = next;

        public static void PlayUiHover() => Play(m => m.uiHover, .5f);
        public static void PlayUiClick() => Play(m => m.uiClick, .8f);
        /// <summary>A mini-game round won.</summary>
        public static void PlayTaskInteraction() => Play(m => m.taskStep, .8f);
        public static void PlayTaskMiss() => Play(m => m.taskMiss, .8f);
        public static void PlayTaskComplete() => Play(m => m.taskComplete, 1f);
        /// <summary>Any mini-game action key or tap, before the result is known.</summary>
        public static void PlayGameTap() => Play(m => m.gameTap, .6f);
        public static void PlayNote(int index) => Play(m => m.notes[Mathf.Abs(index) % m.notes.Length], .8f);
        public static void PlayEmergencyKlaxon() => Play(m => m.klaxon, 1f);
        public static void PlayIncapacitate() => Play(m => m.incapacitate, .9f);
        public static void PlayReport() => Play(m => m.report, 1f);
        public static void PlayVote() => Play(m => m.vote, .9f);
        public static void PlayEject() => Play(m => m.eject, .9f);
        public static void PlaySabotage() => Play(m => m.sabotage, .85f);
        public static void PlayRepaired() => Play(m => m.repaired, .9f);
        public static void PlayRecruit() => Play(m => m.recruit, .9f);
        public static void PlayChatPing() => Play(m => m.chatPing, .55f);
        public static void PlayRoleReveal(bool impostorSide) => Play(m => impostorSide ? m.roleImpostor : m.roleCrew, 1f);
        public static void PlayMatchEnd(bool won) => Play(m => won ? m.victory : m.defeat, 1f);
        public static void PlayStumpTravel() => Play(m => m.stumpTravel, .9f);

        /// <summary>Alternating left/right steps per surface: grass rustle, path crunch, hollow bridge clop.</summary>
        public static void PlayFootstep(SurfaceType surface)
        {
            GameAudioManager manager = EnsureInstance();
            AudioClip[] pair = manager.footsteps[Mathf.Clamp((int)surface, 0, manager.footsteps.Length - 1)];
            manager.footstepFoot ^= 1;
            manager.PlayClip(pair[manager.footstepFoot], .5f);
        }

        static void Play(Func<GameAudioManager, AudioClip> pick, float volume)
        {
            GameAudioManager manager = EnsureInstance();
            manager.PlayClip(pick(manager), volume);
        }

        void PlayClip(AudioClip clip, float volume)
        {
            if (source != null && clip != null) source.PlayOneShot(clip, effectsVolume * volume);
        }

        static AudioClip Clip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, S.Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip Effect(string name, float seconds, float peak, Action<float[], System.Random> paint)
        {
            float[] buffer = S.Buffer(seconds);
            paint(buffer, new System.Random(name.GetHashCode()));
            S.Master(buffer, peak);
            return Clip(name, buffer);
        }

        // ---- Effects ----

        void BuildEffects()
        {
            uiClick = Effect("Wooden Tock", .12f, .45f, (b, r) => S.Wood(b, 0f, 760f, 1f, r, 70f));
            uiHover = Effect("Parchment", .08f, .12f, (b, r) => S.Noise(b, 0f, .08f, 1f, r, 5200f, 1800f, t => Mathf.Sin(Mathf.PI * t) * (.6f + .4f * Mathf.Sin(t * 60f))));
            gameTap = Effect("Wood Tap", .08f, .35f, (b, r) => S.Wood(b, 0f, 1180f, 1f, r, 95f));
            chatPing = Effect("Water Drop", .4f, .35f, (b, r) =>
            {
                for (int i = 0; i < b.Length; i++)
                {
                    float t = i / (float)S.Rate;
                    b[i] += Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(650f, 1500f, Mathf.Clamp01(t / .05f)) * t) * Mathf.Exp(-t * 22f);
                }
                S.Chime(b, .03f, S.Hz(91), .35f, .25f);
            });
            taskStep = Effect("Chime Step", .7f, .4f, (b, r) => { S.Chime(b, 0f, S.Hz(88), .6f, 1f); S.Chime(b, .08f, S.Hz(95), .6f, .8f); });
            taskComplete = Effect("Chime String", 1.9f, .5f, (b, r) =>
            {
                int[] run = { 84, 88, 91, 96, 100, 103 };
                for (int i = 0; i < run.Length; i++) S.Chime(b, i * .06f, S.Hz(run[i]), 1.2f, 1f - i * .08f);
                // A loose wind-chime shimmer trailing after the run.
                for (int i = 0; i < 7; i++) S.Chime(b, .45f + (float)r.NextDouble() * .9f, S.Hz(96 + r.Next(12)), .9f, .25f);
            });
            taskMiss = Effect("Soft Thunk", .2f, .4f, (b, r) => S.Wood(b, 0f, 190f, 1f, r, 40f));
            notes = new AudioClip[5];
            int[] pentatonic = { 84, 86, 88, 91, 93 };
            for (int i = 0; i < notes.Length; i++) { int midi = pentatonic[i]; notes[i] = Effect("Glock " + i, .8f, .4f, (b, r) => S.Chime(b, 0f, S.Hz(midi), .8f, 1f)); }
            klaxon = Effect("Hand Bell", 2f, .55f, (b, r) => { for (int i = 0; i < 3; i++) S.Bell(b, i * .38f, S.Hz(81), 1.6f, 1f - i * .15f); });
            report = Effect("Discovery", 1.8f, .55f, (b, r) =>
            {
                foreach (int m in new[] { 45, 48, 52 }) S.Piano(b, 0f, S.Hz(m), 1.8f, .6f, r);
                S.Pluck(b, 0f, S.Hz(33), 1.2f, 1f, r, .99f, 3);
            });
            vote = Effect("Wax Stamp", .2f, .5f, (b, r) => { S.Wood(b, 0f, 420f, 1f, r, 45f); S.Noise(b, 0f, .06f, .4f, r, 900f, 80f, t => 1f - t); });
            eject = Effect("Wind Away", 1.3f, .45f, (b, r) =>
            {
                S.Noise(b, 0f, 1.3f, 1f, r, 1800f, 200f, t => Mathf.Sin(Mathf.PI * t));
                S.Flute(b, .1f, S.Hz(76), .5f, .35f, r); S.Flute(b, .5f, S.Hz(71), .7f, .3f, r);
            });
            sabotage = Effect("Low Bells", 1.6f, .5f, (b, r) =>
            {
                S.Bell(b, 0f, S.Hz(62), 1.4f, 1f); S.Bell(b, .5f, S.Hz(62), 1.1f, .7f);
                foreach (int m in new[] { 38, 41, 45 }) S.Pluck(b, .02f, S.Hz(m), .9f, .5f, r, .985f);
            });
            repaired = Effect("Rising Chime", 1f, .45f, (b, r) => { S.Chime(b, 0f, S.Hz(79), .8f, 1f); S.Chime(b, .1f, S.Hz(84), .8f, 1f); S.Chime(b, .2f, S.Hz(88), .8f, 1f); });
            recruit = Effect("Whisper Flute", 1.2f, .45f, (b, r) => { S.Flute(b, 0f, S.Hz(62), .6f, .6f, r); S.Flute(b, .45f, S.Hz(65), .75f, .6f, r); });
            roleCrew = Effect("Warm Strum", 1.8f, .5f, (b, r) =>
            {
                int[] chord = { 48, 55, 60, 64, 67, 72 };
                for (int i = 0; i < chord.Length; i++) S.Pluck(b, i * .025f, S.Hz(chord[i]), 1.6f, .6f, r);
                S.Chime(b, .25f, S.Hz(91), 1.2f, .4f);
            });
            roleImpostor = Effect("Low Mystery", 1.8f, .5f, (b, r) =>
            {
                foreach (int m in new[] { 45, 48, 51 }) S.Piano(b, 0f, S.Hz(m), 1.8f, .5f, r);
                S.Pluck(b, 0f, S.Hz(33), 1.3f, .9f, r, .992f, 3);
            });
            victory = Effect("Victory", 2.2f, .55f, (b, r) =>
            {
                int[] chord = { 48, 55, 60, 64, 67, 72 };
                for (int i = 0; i < chord.Length; i++) S.Pluck(b, i * .03f, S.Hz(chord[i]), 1.8f, .55f, r);
                int[] run = { 84, 88, 91, 96 };
                for (int i = 0; i < run.Length; i++) S.Chime(b, .3f + i * .1f, S.Hz(run[i]), 1.2f, .7f);
            });
            defeat = Effect("Defeat", 2.2f, .5f, (b, r) => { int[] fall = { 72, 69, 65, 64 }; for (int i = 0; i < fall.Length; i++) S.Piano(b, i * .32f, S.Hz(fall[i]), 1.2f, .7f, r); });
            incapacitate = Effect("Muffled Fall", .45f, .5f, (b, r) =>
            {
                for (int i = 0; i < b.Length; i++) { float t = i / (float)S.Rate; b[i] += Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 18f); }
                S.Noise(b, .02f, .3f, .5f, r, 4000f, 1200f, t => (1f - t) * (.5f + .5f * Mathf.Sin(t * 70f)));
            });

            stumpTravel = Effect("Hollow Stump", .9f, .45f, (b, r) =>
            {
                S.Noise(b, 0f, .5f, 1f, r, 4200f, 900f, t => Mathf.Sin(Mathf.PI * t) * (.6f + .4f * Mathf.Sin(t * 50f)));
                S.Wood(b, .05f, 210f, .9f, r, 22f);
                S.Wood(b, .55f, 240f, .7f, r, 22f);
                S.Noise(b, .5f, .35f, .7f, r, 4200f, 900f, t => 1f - t);
            });

            // Order matches SurfaceType: Grass, Concrete (paths), Wood (bridge), Water, Metal, Tile.
            footsteps = new AudioClip[6][];
            for (int foot = 0; foot < 2; foot++)
            {
                float lift = foot == 0 ? 1f : 1.07f;
                Set(0, foot, Effect("Grass Rustle " + foot, .11f, .22f, (b, r) => S.Noise(b, 0f, .11f, 1f, r, 5000f * lift, 1500f, t => (1f - t) * (.55f + .45f * Mathf.Sin(t * 90f + foot)))));
                Set(1, foot, Effect("Path Crunch " + foot, .12f, .25f, (b, r) => S.Noise(b, 0f, .12f, 1f, r, 2400f * lift, 300f, t => (1f - t) * ((r.Next(5) == 0) ? 1f : .35f))));
                Set(2, foot, Effect("Bridge Clop " + foot, .16f, .32f, (b, r) => S.Wood(b, 0f, 300f * lift, 1f, r, 32f)));
                Set(3, foot, Effect("Splash " + foot, .18f, .22f, (b, r) => S.Noise(b, 0f, .18f, 1f, r, 1600f * lift, 250f, t => Mathf.Sin(Mathf.PI * Mathf.Sqrt(t)))));
                Set(4, foot, Effect("Clink " + foot, .12f, .2f, (b, r) => S.Chime(b, 0f, 1250f * lift, .12f, .6f)));
                Set(5, foot, Effect("Stone Tap " + foot, .09f, .25f, (b, r) => S.Wood(b, 0f, 880f * lift, .8f, r, 80f)));
            }
        }

        void Set(int surface, int foot, AudioClip clip)
        {
            if (footsteps[surface] == null) footsteps[surface] = new AudioClip[2];
            footsteps[surface][foot] = clip;
        }

        // ---- Music (runs on a worker thread: pure math only) ----

        struct Note { public float Beat, Length; public int Midi; public Note(float beat, float length, int midi) { Beat = beat; Length = length; Midi = midi; } }

        /// <summary>Resources folder holding the pre-rendered tracks used by browser builds.</summary>
        public const string BakedMusicFolder = "Music";

        /// <summary>The same track the game renders at runtime, for baking into the browser build.</summary>
        public static float[] RenderMusicForBake(MusicMood mood) => RenderMusic(mood);

        static float[] RenderMusic(MusicMood mood)
        {
            System.Random random = new System.Random((int)mood * 7919);
            switch (mood)
            {
                case MusicMood.Menu: return LobbyWaltz(random);
                case MusicMood.Park: return ParkStroll(random);
                default: return QuietTension(random);
            }
        }

        static int[] Triad(int root, bool minor, bool seventh = false) => seventh ? new[] { root, root + 4, root + 7, root + 10 } : new[] { root, root + (minor ? 3 : 4), root + 7 };

        /// <summary>Lobby: a slow, nostalgic F-major waltz. Piano melody, picked guitar, flute counter-line, pizzicato bass.</summary>
        static float[] LobbyWaltz(System.Random random)
        {
            const float bpm = 84f; const int bars = 16, beatsPerBar = 3;
            float beat = 60f / bpm;
            float[] mix = S.Buffer(bars * beatsPerBar * beat);
            int[] roots = { 53, 50, 46, 48, 53, 45, 46, 48, 50, 46, 53, 48, 46, 48, 53, 53 };
            bool[] minor = { false, true, false, false, false, true, false, false, true, false, false, false, false, false, false, false };
            Note[] melody =
            {
                new Note(0, 1, 69), new Note(1, 1, 72), new Note(2, 1, 77), new Note(3, 2, 76), new Note(5, 1, 74),
                new Note(6, 1, 74), new Note(7, 1, 72), new Note(8, 1, 70), new Note(9, 3, 72),
                new Note(12, 1, 69), new Note(13, 1, 72), new Note(14, 1, 77), new Note(15, 2, 79), new Note(17, 1, 77),
                new Note(18, 1, 76), new Note(19, 1, 74), new Note(20, 1, 72), new Note(21, 3, 76),
                new Note(24, 1, 74), new Note(25, 1, 77), new Note(26, 1, 81), new Note(27, 2, 79), new Note(29, 1, 77),
                new Note(30, 1, 77), new Note(31, 1, 76), new Note(32, 1, 72), new Note(33, 3, 76),
                new Note(36, 1, 74), new Note(37, 1, 72), new Note(38, 1, 70), new Note(39, 1, 72), new Note(40, 1, 74), new Note(41, 1, 76),
                new Note(42, 3, 77), new Note(45, 2, 72)
            };
            foreach (Note n in melody) S.Piano(mix, n.Beat * beat, S.Hz(n.Midi), n.Length * beat + .9f, .32f, random, true);
            int[] counter = { 69, 70, 69, 67, 65, 67, 69, 72 };
            for (int i = 0; i < counter.Length; i++) S.Flute(mix, (24 + i * 3) * beat, S.Hz(counter[i]), 3f * beat, .1f, random, true);
            int[] pattern = { 0, 7, 12, 1, 7, 1 };
            for (int bar = 0; bar < bars; bar++)
            {
                int[] chord = Triad(roots[bar], minor[bar]);
                for (int e = 0; e < 6; e++)
                {
                    int step = pattern[e];
                    int midi = step == 1 ? chord[1] + 12 : roots[bar] + step;
                    S.Pluck(mix, (bar * beatsPerBar + e * .5f) * beat, S.Hz(midi), 1.4f, .14f, random, .995f, 3, true);
                }
                S.Pluck(mix, bar * beatsPerBar * beat, S.Hz(roots[bar] - 12), 1.1f, .3f, random, .985f, 2, true);
            }
            S.Room(mix, .35f);
            S.Master(mix, .8f);
            return mix;
        }

        /// <summary>Park: a light C-major stroll. Flute melody, guitar arpeggios, sparse piano, pizzicato bass.</summary>
        static float[] ParkStroll(System.Random random)
        {
            const float bpm = 92f; const int bars = 16, beatsPerBar = 4;
            float beat = 60f / bpm;
            float[] mix = S.Buffer(bars * beatsPerBar * beat);
            int[] roots = { 48, 55, 57, 53, 48, 55, 53, 55, 57, 53, 48, 55, 57, 53, 55, 48 };
            bool[] minor = { false, false, true, false, false, false, false, false, true, false, false, false, true, false, false, false };
            Note[] flute =
            {
                new Note(0, 1, 76), new Note(1, 1, 79), new Note(2, 2, 84), new Note(4, 1, 83), new Note(5, 1, 81), new Note(6, 2, 79),
                new Note(8, 1.5f, 81), new Note(9.5f, .5f, 79), new Note(10, 2, 76), new Note(12, 4, 77),
                new Note(16, 1, 76), new Note(17, 1, 79), new Note(18, 1, 84), new Note(19, 1, 86), new Note(20, 2, 83), new Note(22, 2, 79),
                new Note(24, 1, 81), new Note(25, 1, 77), new Note(26, 2, 74), new Note(28, 4, 79)
            };
            foreach (Note n in flute) S.Flute(mix, n.Beat * beat, S.Hz(n.Midi), n.Length * beat, .16f, random, true);
            Note[] piano =
            {
                new Note(32, 2, 72), new Note(34, 2, 76), new Note(36, 2, 77), new Note(38, 1, 76), new Note(39, 1, 74),
                new Note(40, 3, 72), new Note(43, 1, 74), new Note(44, 4, 71),
                new Note(48, 1, 69), new Note(49, 1, 72), new Note(50, 2, 76), new Note(52, 2, 77), new Note(54, 2, 81),
                new Note(56, 2, 79), new Note(58, 2, 74), new Note(60, 4, 72)
            };
            foreach (Note n in piano) S.Piano(mix, n.Beat * beat, S.Hz(n.Midi), n.Length * beat + .8f, .28f, random, true);
            for (int bar = 0; bar < bars; bar++)
            {
                int third = minor[bar] ? 3 : 4;
                int[] pattern = { 0, 7, 12, 12 + third, 19, 12 + third, 12, 7 };
                for (int e = 0; e < 8; e++) S.Pluck(mix, (bar * beatsPerBar + e * .5f) * beat, S.Hz(roots[bar] + pattern[e]), 1.2f, .12f, random, .995f, 3, true);
                S.Pluck(mix, bar * beatsPerBar * beat, S.Hz(roots[bar] - 12), 1f, .28f, random, .985f, 2, true);
                S.Pluck(mix, (bar * beatsPerBar + 2) * beat, S.Hz(roots[bar] - 5), 1f, .22f, random, .985f, 2, true);
            }
            S.Room(mix, .3f);
            S.Master(mix, .8f);
            return mix;
        }

        /// <summary>Tension: never aggressive. A-minor cello pizzicato ostinato, quiet woodwind pulses, low soft piano.</summary>
        static float[] QuietTension(System.Random random)
        {
            const float bpm = 104f; const int bars = 16, beatsPerBar = 4;
            float beat = 60f / bpm;
            float[] mix = S.Buffer(bars * beatsPerBar * beat);
            int[] roots = { 45, 41, 38, 40, 45, 41, 38, 40, 45, 41, 38, 40, 45, 41, 38, 40 };
            bool[] minor = { true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false };
            int[] ostinato = { 0, 0, 7, 0, 0, 12, 7, 0 };
            for (int bar = 0; bar < bars; bar++)
            {
                int root = roots[bar] - 12;
                for (int e = 0; e < 8; e++) S.Pluck(mix, (bar * beatsPerBar + e * .5f) * beat, S.Hz(root + ostinato[e]), .7f, e % 2 == 0 ? .34f : .22f, random, .985f, 3, true);
                foreach (int m in Triad(roots[bar], minor[bar])) S.Piano(mix, bar * beatsPerBar * beat, S.Hz(m), beatsPerBar * beat + .6f, .07f, random, true);
                int[] chord = Triad(roots[bar] + 24, minor[bar]);
                S.Flute(mix, (bar * beatsPerBar + 1.5f) * beat, S.Hz(chord[1]), .35f * beat, .07f, random, true);
                S.Flute(mix, (bar * beatsPerBar + 3.5f) * beat, S.Hz(chord[2]), .35f * beat, .06f, random, true);
                if (bar >= 8 && bar % 2 == 0) { S.Piano(mix, (bar * beatsPerBar + 2) * beat, S.Hz(76), beat * 1.6f, .14f, random, true); S.Piano(mix, (bar * beatsPerBar + 3) * beat, S.Hz(77), beat * 1.6f, .12f, random, true); }
            }
            S.Room(mix, .3f);
            S.Master(mix, .72f);
            return mix;
        }
    }
}
