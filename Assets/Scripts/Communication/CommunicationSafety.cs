using System;
using System.Collections.Generic;
using UnityEngine;
namespace Crony
{
    // Values are persisted by index in pending-reports.json; append new categories only.
    public enum PlayerReportCategory { HarassmentBullying, HateSpeech, CheatingExploiting, OffensiveNameAvatar, SexualHarassment, ThreatsDoxxing, Extremism, SelfHarmEncouragement, PlayerInDistress, SpamImpersonation }
    public enum ReportTarget { Player, Message, Voice, Match }
    /// <summary>Carries no reporter identity: the backend derives it from the authenticated session and never shows it to the accused.</summary>
    [Serializable] public sealed class PlayerReport
    {
        public string id, matchId, targetAccount, messageId, createdUtc, messageTime, evidenceText;
        public PlayerReportCategory category;
        public ReportTarget target;
    }
    /// <summary>A moderation decision the local player received, kept on this device so it can be appealed.</summary>
    [Serializable] public sealed class OwnIncident { public string reference, utc, message; public EnforcementLevel level; public bool appealed; }
    [Serializable] public sealed class Appeal { public string reference, createdUtc, note; }
    public interface ICommunicationBackend
    {
        // Implement with an authenticated HTTPS service; never trust a client supplied account claim.
        System.Threading.Tasks.Task<bool> SubmitReportAsync(PlayerReport report);
        System.Threading.Tasks.Task<bool> SubmitAppealAsync(Appeal appeal);
    }
    public static class CommunicationSafety
    {
        [Serializable] sealed class Saved { public List<string> ids = new List<string>(); }
        static readonly HashSet<string> blocked = new HashSet<string>();
        static readonly HashSet<string> muted = new HashSet<string>();
        // Host-imposed Level 2+ voice restrictions for the current match; never persisted.
        static readonly HashSet<string> voiceRestricted = new HashSet<string>();
        public static bool LocalVoiceRestricted { get; private set; }
        public static event Action Changed;
        public static ICommunicationBackend Backend { get; set; }
        [Serializable] sealed class PendingReports { public List<PlayerReport> reports = new List<PlayerReport>(); }
        [Serializable] sealed class PendingAppeals { public List<Appeal> appeals = new List<Appeal>(); }
        [Serializable] sealed class OwnRecord { public List<OwnIncident> incidents = new List<OwnIncident>(); }
        static string DataPath(string file) => System.IO.Path.Combine(Application.persistentDataPath, file);
        static T Read<T>(string file) where T : class, new()
        {
            try { var path = DataPath(file); if(System.IO.File.Exists(path)) return JsonUtility.FromJson<T>(System.IO.File.ReadAllText(path)) ?? new T(); } catch(Exception) { }
            return new T();
        }
        static void Write(string file, object value) => System.IO.File.WriteAllText(DataPath(file), JsonUtility.ToJson(value));
        public static void SavePendingReport(PlayerReport report)
        {
            var pending = Read<PendingReports>("pending-reports.json");
            if(pending.reports == null) pending.reports = new List<PlayerReport>();
            if(pending.reports.Count >= 50) pending.reports.RemoveAt(0);
            pending.reports.Add(report);
            Write("pending-reports.json", pending);
        }
        public static void SavePendingAppeal(Appeal appeal)
        {
            var pending = Read<PendingAppeals>("pending-appeals.json");
            if(pending.appeals == null) pending.appeals = new List<Appeal>();
            if(pending.appeals.Count >= 20) pending.appeals.RemoveAt(0);
            pending.appeals.Add(appeal);
            Write("pending-appeals.json", pending);
        }
        public static List<OwnIncident> OwnIncidents { get { var record = Read<OwnRecord>("my-safety-record.json"); return record.incidents ?? new List<OwnIncident>(); } }
        public static void RecordOwnIncident(string reference, EnforcementLevel level, string message)
        {
            var record = Read<OwnRecord>("my-safety-record.json");
            if(record.incidents == null) record.incidents = new List<OwnIncident>();
            if(record.incidents.Exists(i => i.reference == reference)) return;
            if(record.incidents.Count >= 20) record.incidents.RemoveAt(0);
            record.incidents.Add(new OwnIncident { reference = reference, level = level, message = message, utc = DateTime.UtcNow.ToString("O") });
            try { Write("my-safety-record.json", record); } catch(Exception) { }
            Changed?.Invoke();
        }
        /// <summary>Marks the incident appealed and queues the appeal. Returns false when it was already appealed or is unknown.</summary>
        public static bool MarkAppealed(string reference)
        {
            var record = Read<OwnRecord>("my-safety-record.json");
            var incident = record.incidents?.Find(i => i.reference == reference);
            if(incident == null || incident.appealed) return false;
            incident.appealed = true;
            Write("my-safety-record.json", record);
            return true;
        }
        static bool loaded;
        static void Load() { if(loaded)return; loaded=true; try { var s=JsonUtility.FromJson<Saved>(PlayerPrefs.GetString("Crony.BlockedAccounts.v1","{}")); if(s?.ids!=null)foreach(var id in s.ids)blocked.Add(id); } catch(ArgumentException){} }
        public static int BlockedCount { get { Load(); return blocked.Count; } }
        public static int PendingReportCount => Read<PendingReports>("pending-reports.json").reports?.Count ?? 0;
        public static void ClearBlocks() { Load(); blocked.Clear(); PlayerPrefs.SetString("Crony.BlockedAccounts.v1",JsonUtility.ToJson(new Saved())); PlayerPrefs.Save(); Changed?.Invoke(); }
        public static bool IsBlocked(string id) { Load(); return !string.IsNullOrEmpty(id)&&blocked.Contains(id); }
        public static bool IsMuted(string id) => IsBlocked(id)||(!string.IsNullOrEmpty(id)&&(muted.Contains(id)||voiceRestricted.Contains(id)));
        public static void SetMuted(string id,bool value) { if(string.IsNullOrEmpty(id))return;if(value)muted.Add(id);else muted.Remove(id);Changed?.Invoke(); }
        public static void SetBlocked(string id,bool value) { if(string.IsNullOrEmpty(id))return;Load();if(value)blocked.Add(id);else blocked.Remove(id);PlayerPrefs.SetString("Crony.BlockedAccounts.v1",JsonUtility.ToJson(new Saved{ids=new List<string>(blocked)}));PlayerPrefs.Save();Changed?.Invoke(); }
        public static void SetVoiceRestricted(string id,bool value) { if(string.IsNullOrEmpty(id))return;if(value)voiceRestricted.Add(id);else voiceRestricted.Remove(id);Changed?.Invoke(); }
        public static void SetLocalVoiceRestricted(bool value) { LocalVoiceRestricted=value;Changed?.Invoke(); }
        public static void ClearMatchRestrictions() { voiceRestricted.Clear();LocalVoiceRestricted=false;Changed?.Invoke(); }
    }
}
