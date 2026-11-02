using UnityEngine;

namespace Crony
{
    /// <summary>Saved player settings, applied once at launch so they take effect before any menu is opened.</summary>
    public static class GameSettings
    {
        public const string MasterVolumeKey = "Crony.MasterVolume", FullscreenKey = "Crony.Fullscreen", VSyncKey = "Crony.VSync",
            FrameCapKey = "Crony.FrameCap", StreamerModeKey = "Crony.StreamerMode", TextChatKey = "Crony.TextChat",
            LargeTextKey = "Crony.LargeText", HighContrastKey = "Crony.HighContrast", ReduceEffectsKey = "Crony.ReduceEffects",
            FullEffectsKey = "Crony.FullEffects";
        public static readonly int[] FrameCaps = { 30, 60, 120, -1 };

        public static bool StreamerMode => PlayerPrefs.GetInt(StreamerModeKey, 0) == 1;
        public static bool TextChatEnabled => PlayerPrefs.GetInt(TextChatKey, 1) == 1;
        /// <summary>Full: post-processing, god rays and petals. Light: just the world and dust motes, for slower GPUs.</summary>
        public static bool FullEffects => PlayerPrefs.GetInt(FullEffectsKey, 1) == 1;
        public static int FrameCap => PlayerPrefs.GetInt(FrameCapKey, 60);
        public static string FrameCapLabel(int cap) => cap < 0 ? "Unlimited" : cap + " FPS";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void ApplySaved()
        {
            AudioListener.volume = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
            if (PlayerPrefs.HasKey(FullscreenKey)) Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) == 1;
            QualitySettings.vSyncCount = PlayerPrefs.GetInt(VSyncKey, 1);
            Application.targetFrameRate = FrameCap;
            Camera camera = Camera.main;
            if (camera != null && camera.TryGetComponent(out UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data)) data.renderPostProcessing = FullEffects;
            Atmosphere.SetFull(FullEffects);
        }

        public static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            ApplySaved();
        }

        /// <summary>Restores audio, display, accessibility and chat preferences. Blocks, reports and the safety record are kept.</summary>
        public static void ResetToDefaults()
        {
            string[] keys = { MasterVolumeKey, FullscreenKey, VSyncKey, FrameCapKey, StreamerModeKey, TextChatKey, LargeTextKey, HighContrastKey, FullEffectsKey,
                ReduceEffectsKey, GameAudioManager.MusicVolumeKey, GameAudioManager.EffectsVolumeKey, GameAudioManager.MuteInBackgroundKey, "Crony.VoiceToggleToTalk" };
            foreach (string key in keys) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            GameAudioManager.SetMusicVolume(.4f);
            GameAudioManager.SetEffectsVolume(.6f);
            ApplySaved();
        }
    }
}
