using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Crony
{
    /// <summary>Level 1 educate, 2 restrict communication, 3 remove from match, 4 suspend.</summary>
    public enum EnforcementLevel : byte { None, Educate, Restrict, Remove, Suspend }

    public struct EnforcementResult
    {
        public EnforcementLevel Level;
        public SafetyCategory Category;
        public string Reference;
        public bool VoiceRestricted;
        public string Message;
        /// <summary>Just what happens in Crony (level, reference, appeal route), for the education pop-up.</summary>
        public string Consequence;
    }

    /// <summary>
    /// Host-authoritative graduated enforcement. Stores the minimum needed for a decision (reference, account,
    /// category, level, time; never message text) with a 30-day retention window. Never issues a permanent ban:
    /// repeated severe incidents are suspended and flagged for human review.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafetySystem : MonoBehaviour
    {
        public const float RestrictSeconds = 60f;
        const int RetentionDays = 30, MaxRecords = 300;

        [Serializable] public sealed class Incident { public string reference, account, utc; public SafetyCategory category; public EnforcementLevel level; public bool needsHumanReview; }
        [Serializable] sealed class Suspension { public string account, untilUtc; }
        [Serializable] sealed class Ledger { public List<Incident> incidents = new List<Incident>(); public List<Suspension> suspensions = new List<Suspension>(); }

        public static SafetySystem Instance { get; private set; }
        readonly Dictionary<string, int> matchPoints = new Dictionary<string, int>();
        readonly Dictionary<string, float> chatRestrictedUntil = new Dictionary<string, float>();
        readonly HashSet<string> voiceRestricted = new HashSet<string>();
        Ledger ledger;
        static string LedgerPath => Path.Combine(Application.persistentDataPath, "safety-ledger.json");

        void Awake() { if (Instance == null) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static SafetySystem Ensure(GameObject fallbackHost)
        {
            if (Instance == null) Instance = FindAnyObjectByType<SafetySystem>();
            if (Instance == null && fallbackHost != null) Instance = fallbackHost.AddComponent<SafetySystem>();
            return Instance;
        }

        public bool IsChatRestricted(string account, out float secondsLeft)
        {
            secondsLeft = 0f;
            if (IsSuspended(account, out DateTime until)) { secondsLeft = (float)(until - DateTime.UtcNow).TotalSeconds; return true; }
            if (!chatRestrictedUntil.TryGetValue(account, out float expiry) || Time.unscaledTime >= expiry) return false;
            secondsLeft = expiry - Time.unscaledTime;
            return true;
        }

        public bool IsVoiceRestricted(string account) => voiceRestricted.Contains(account);

        public bool IsSuspended(string account, out DateTime untilUtc)
        {
            untilUtc = DateTime.MinValue;
            Load();
            foreach (Suspension s in ledger.suspensions)
                if (s.account == account && DateTime.TryParse(s.untilUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime until) && until > DateTime.UtcNow)
                { untilUtc = until; return true; }
            return false;
        }

        public EnforcementResult Enforce(string account, SafetyCategory category)
        {
            Load();
            bool severe = IsSevere(category);
            int recent = 0, recentRemovals = 0, recentSevere = 0;
            DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (Incident incident in ledger.incidents)
            {
                if (incident.account != account || !DateTime.TryParse(incident.utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime at) || at < cutoff) continue;
                recent++;
                if (incident.level >= EnforcementLevel.Remove) recentRemovals++;
                if (IsSevere(incident.category)) recentSevere++;
            }

            EnforcementLevel level;
            if (severe) level = recentSevere > 0 || recentRemovals > 0 ? EnforcementLevel.Suspend : EnforcementLevel.Remove;
            else
            {
                // Verified history is weighed proportionately: a repeat offender skips the first warning.
                int points = (matchPoints.TryGetValue(account, out int p) ? p : recent >= 2 ? 1 : 0) + Weight(category);
                matchPoints[account] = points;
                level = points >= 4 ? EnforcementLevel.Remove : points >= 2 ? EnforcementLevel.Restrict : EnforcementLevel.Educate;
                if (level == EnforcementLevel.Remove && recentRemovals > 0) level = EnforcementLevel.Suspend;
            }

            var result = new EnforcementResult { Level = level, Category = category, Reference = "CR-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant() };
            if (level >= EnforcementLevel.Restrict) { voiceRestricted.Add(account); result.VoiceRestricted = true; }
            if (level == EnforcementLevel.Restrict) chatRestrictedUntil[account] = Time.unscaledTime + RestrictSeconds;
            if (level == EnforcementLevel.Remove) chatRestrictedUntil[account] = float.MaxValue;
            DateTime suspendedUntil = DateTime.UtcNow.AddDays(severe ? 7 : 1);
            if (level == EnforcementLevel.Suspend) ledger.suspensions.Add(new Suspension { account = account, untilUtc = suspendedUntil.ToString("O") });

            ledger.incidents.Add(new Incident { reference = result.Reference, account = account, utc = DateTime.UtcNow.ToString("O"), category = category, level = level, needsHumanReview = severe || level == EnforcementLevel.Suspend });
            Save();
            result.Message = RuleText(category) + " " + LevelText(level, suspendedUntil) + " Ref " + result.Reference + ".";
            result.Consequence = LevelText(level, suspendedUntil) + " Ref " + result.Reference + ". Think it's a mistake? Appeal in Settings > Safety.";
            return result;
        }

        public void ResetMatch()
        {
            matchPoints.Clear();
            chatRestrictedUntil.Clear();
            voiceRestricted.Clear();
        }

        static bool IsSevere(SafetyCategory c) => c == SafetyCategory.ThreatOrDoxxing || c == SafetyCategory.Extremism || c == SafetyCategory.SelfHarmEncouragement;

        static int Weight(SafetyCategory category)
        {
            switch (category)
            {
                case SafetyCategory.IdentityAbuse:
                case SafetyCategory.SexualHarassment:
                case SafetyCategory.Evasion: return 2;
                default: return 1;
            }
        }

        public static string RuleText(SafetyCategory category)
        {
            switch (category)
            {
                case SafetyCategory.TargetedInsult: return "Message not sent: no targeted insults or bullying. Try \"I don't trust your vote, here's why.\"";
                case SafetyCategory.IdentityAbuse: return "Message not sent: no abuse about race, gender, religion or any identity.";
                case SafetyCategory.SexualHarassment: return "Message not sent: no sexual comments or requests.";
                case SafetyCategory.ThreatOrDoxxing: return "Message not sent: no real-world threats, stalking or sharing anyone's personal details. Game talk like \"I'll vote you out\" is fine.";
                case SafetyCategory.Extremism: return "Message not sent: no extremist recruitment or praise of violence.";
                case SafetyCategory.SelfHarmEncouragement: return "Message not sent: never encourage anyone to hurt themselves.";
                case SafetyCategory.Impersonation: return "Message not sent: don't pose as the system, moderators or another player.";
                case SafetyCategory.Evasion: return "Message not sent: spacing letters or swapping symbols to get past the filter still counts.";
                default: return "Message not sent: it breaks the community rules.";
            }
        }

        static string LevelText(EnforcementLevel level, DateTime suspendedUntil)
        {
            switch (level)
            {
                case EnforcementLevel.Educate: return "Warning recorded.";
                case EnforcementLevel.Restrict: return "Chat is paused for " + (int)RestrictSeconds + " s and your voice is restricted this match. You can keep playing.";
                case EnforcementLevel.Remove: return "You have been removed from this match.";
                case EnforcementLevel.Suspend: return "Communication and online play are suspended until " + suspendedUntil.ToLocalTime().ToString("d MMM HH:mm") + ". A person will review this.";
                default: return string.Empty;
            }
        }

        void Load()
        {
            if (ledger != null) return;
            try { ledger = File.Exists(LedgerPath) ? JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerPath)) : null; } catch (Exception) { ledger = null; }
            if (ledger == null) ledger = new Ledger();
            if (ledger.incidents == null) ledger.incidents = new List<Incident>();
            if (ledger.suspensions == null) ledger.suspensions = new List<Suspension>();
            DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            ledger.incidents.RemoveAll(i => !DateTime.TryParse(i.utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime at) || at < cutoff);
            ledger.suspensions.RemoveAll(s => !DateTime.TryParse(s.untilUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime until) || until < DateTime.UtcNow);
        }

        void Save()
        {
            while (ledger.incidents.Count > MaxRecords) ledger.incidents.RemoveAt(0);
            try { File.WriteAllText(LedgerPath, JsonUtility.ToJson(ledger)); } catch (Exception e) { Debug.LogWarning("Safety ledger could not be saved: " + e.Message); }
        }
    }
}
