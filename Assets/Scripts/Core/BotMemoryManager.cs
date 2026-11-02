using System.Collections.Generic;
using UnityEngine;

namespace Crony
{
    /// <summary>What a bot noticed. Each kind feeds a different weight in <see cref="BotDiscussionEngine"/>.</summary>
    public enum EvidenceType
    {
        /// <summary>Seen within the discovery radius of a body around the moment it was reported.</summary>
        BodyDiscovery,
        /// <summary>Seen lingering beside a downed player before anyone reported them.</summary>
        ProximityToTarget,
        /// <summary>Seen at, or coming out of, a hollow stump. Only the Impostor team travels by stump.</summary>
        NearStump,
        /// <summary>Seen finishing the repair of a disruption.</summary>
        DisruptionRepair,
        /// <summary>Seen standing at an active disruption's repair station without fixing it.</summary>
        DisruptionIgnored,
        /// <summary>Seen completing a checkpoint mini-game nearby: an alibi.</summary>
        TaskAlibi,
        /// <summary>Saw the knock-down itself. Rare, and the strongest evidence a bot can hold.</summary>
        WitnessedTakedown
    }

    /// <summary>One remembered observation. Ids are <see cref="MatchPlayer.id"/>; -1 means "no target".</summary>
    public struct MemoryEntry
    {
        public float timestamp;
        public int actorID;
        public int targetID;
        public Vector2 position;
        public EvidenceType type;
        /// <summary>Human-readable place ("Central Lake", "Hawker Rush station") used when the bot speaks.</summary>
        public string place;
    }

    /// <summary>
    /// Per-bot short-term memory of spatial events. Bots only remember the last <see cref="MemorySpan"/> seconds of
    /// round time, so stale sightings fade out instead of piling up across a whole match.
    /// </summary>
    public sealed class BotMemoryManager
    {
        public const float MemorySpan = 60f;
        // A bot sampling the same scene twice a second sees "Dev at the stump" many times; one entry per window is enough.
        const float RepeatWindow = 4f;

        static readonly List<MemoryEntry> Empty = new List<MemoryEntry>();
        readonly Dictionary<int, List<MemoryEntry>> memories = new Dictionary<int, List<MemoryEntry>>();

        public void Clear() => memories.Clear();

        /// <summary>Records what <paramref name="observerId"/> saw. Repeats inside a few seconds refresh the old entry.</summary>
        public void LogSpatialEvent(int observerId, MemoryEntry entry)
        {
            if (observerId == entry.actorID) return;
            if (!memories.TryGetValue(observerId, out List<MemoryEntry> log))
                memories[observerId] = log = new List<MemoryEntry>();
            for (int i = log.Count - 1; i >= 0; i--)
            {
                MemoryEntry old = log[i];
                if (old.type == entry.type && old.actorID == entry.actorID && old.targetID == entry.targetID &&
                    entry.timestamp - old.timestamp < RepeatWindow)
                {
                    log[i] = entry;
                    return;
                }
            }
            log.Add(entry);
        }

        public IReadOnlyList<MemoryEntry> Recall(int observerId) =>
            memories.TryGetValue(observerId, out List<MemoryEntry> log) ? log : Empty;

        /// <summary>Forgets everything older than <see cref="MemorySpan"/> seconds of round time.</summary>
        public void Prune(float now)
        {
            foreach (List<MemoryEntry> log in memories.Values)
                log.RemoveAll(entry => now - entry.timestamp > MemorySpan);
        }
    }
}
