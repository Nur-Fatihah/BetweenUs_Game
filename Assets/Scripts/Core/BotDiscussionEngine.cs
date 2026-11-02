using System;
using System.Collections.Generic;
using UnityEngine;

namespace Crony
{
    /// <summary>A line a bot says in the meeting, and what it does to the room's accusations.</summary>
    public struct DialogueLine
    {
        public string text;
        /// <summary>Player this line points at (-1 for none) and how hard it pushes the room's vote.</summary>
        public int accuse;
        public float weight;
        /// <summary>Player this line vouches for (-1 for none); lowers the room's accusations against them.</summary>
        public int defends;

        public static DialogueLine Say(string text) => new DialogueLine { text = text, accuse = -1, defends = -1 };
    }

    /// <summary>What the engine needs to know about the meeting it is speaking in.</summary>
    public sealed class DiscussionContext
    {
        public Func<int, string> NameOf;
        /// <summary>The checkpoint a player last finished (or pretended to), e.g. "Hawker Rush station".</summary>
        public Func<int, string> AlibiOf;
        public Func<int, PlayerRole> RoleOf;
        /// <summary>Everyone who can speak and vote in this meeting.</summary>
        public List<int> Present = new List<int>();
        public int ReporterId = -1, BodyId = -1;
        /// <summary>Round time the body was reported; emergency meetings leave this at -infinity.</summary>
        public float DiscoveryTime = float.NegativeInfinity;
        /// <summary>Where the last disruption was repaired this round, if any (used by the crony's cover story).</summary>
        public string LastRepairPlace;
    }

    /// <summary>
    /// Turns each bot's memories into a suspicion score for every other player (0 = trusted, 1 = certain) and picks
    /// what the bot says during the Discussion stage. Crewmates argue from evidence, the Impostor feeds on the room's
    /// doubts and turns on accusers, and the crony covers for the Impostor.
    /// </summary>
    public sealed class BotDiscussionEngine
    {
        // Suspicion weights. Scores start at zero, every remembered event adds its weight to the actor, and the
        // total is clamped to 0..1, so two medium sightings (0.6) still fall short of an accusation (0.7) while a
        // body sighting plus any other oddity crosses it.
        public const float HighWeight = .6f;      // spotted within DiscoveryRadius of a body within DiscoveryWindow of its discovery
        public const float MediumWeight = .3f;    // stump sighting, ignored disruption, or lingering by a downed player earlier
        public const float AlibiWeight = -.4f;    // seen finishing a checkpoint mini-game next to this bot
        public const float RepairWeight = -.2f;   // seen fixing a disruption: helpful, though not a full alibi
        public const float WitnessWeight = 1f;    // saw the knock-down itself
        public const float AccuseThreshold = .7f;
        public const float HedgeThreshold = .4f;
        public const float DiscoveryWindow = 5f;
        public const float DiscoveryRadius = 5f;

        readonly Dictionary<int, Dictionary<int, float>> matrix = new Dictionary<int, Dictionary<int, float>>();
        // The single most damning memory per (bot, suspect), so accusations can name a concrete reason.
        readonly Dictionary<int, Dictionary<int, MemoryEntry>> reasons = new Dictionary<int, Dictionary<int, MemoryEntry>>();
        readonly Dictionary<int, Dictionary<int, MemoryEntry>> alibis = new Dictionary<int, Dictionary<int, MemoryEntry>>();

        public void Clear() { matrix.Clear(); reasons.Clear(); alibis.Clear(); }

        /// <summary>Rebuilds one bot's suspicion row from its memory. Called once at the start of each meeting.</summary>
        public void Recompute(int botId, IReadOnlyList<MemoryEntry> memory, DiscussionContext context)
        {
            Dictionary<int, float> scores = Row(matrix, botId);
            Dictionary<int, MemoryEntry> why = Row(reasons, botId);
            Dictionary<int, MemoryEntry> cleared = Row(alibis, botId);
            scores.Clear(); why.Clear(); cleared.Clear();
            foreach (int id in context.Present) if (id != botId) scores[id] = 0f;

            // A player who stood at a disruption and then fixed it was walking up to repair it, not ignoring it.
            Dictionary<int, float> repairedAt = new Dictionary<int, float>();
            foreach (MemoryEntry entry in memory)
                if (entry.type == EvidenceType.DisruptionRepair) repairedAt[entry.actorID] = Mathf.Max(entry.timestamp, repairedAt.TryGetValue(entry.actorID, out float t) ? t : 0f);

            Dictionary<int, float> strongest = new Dictionary<int, float>();
            foreach (MemoryEntry entry in memory)
            {
                if (!scores.ContainsKey(entry.actorID)) continue;
                float weight = WeightOf(entry, context, repairedAt);
                scores[entry.actorID] += weight;
                if (weight > 0f && weight > (strongest.TryGetValue(entry.actorID, out float best) ? best : 0f))
                {
                    strongest[entry.actorID] = weight;
                    why[entry.actorID] = entry;
                }
                if (entry.type == EvidenceType.TaskAlibi) cleared[entry.actorID] = entry;
            }
            List<int> ids = new List<int>(scores.Keys);
            foreach (int id in ids) scores[id] = Mathf.Clamp01(scores[id]);
        }

        static float WeightOf(MemoryEntry entry, DiscussionContext context, Dictionary<int, float> repairedAt)
        {
            bool nearDiscovery = Mathf.Abs(entry.timestamp - context.DiscoveryTime) <= DiscoveryWindow;
            switch (entry.type)
            {
                case EvidenceType.WitnessedTakedown: return WitnessWeight;
                case EvidenceType.BodyDiscovery: return nearDiscovery ? HighWeight : MediumWeight;
                case EvidenceType.ProximityToTarget:
                    // Hanging around the body that was just reported is the high-weight case; earlier lingering near
                    // someone downed is only odd.
                    return entry.targetID == context.BodyId && nearDiscovery ? HighWeight : MediumWeight;
                case EvidenceType.NearStump: return MediumWeight;
                case EvidenceType.DisruptionIgnored:
                    return repairedAt.TryGetValue(entry.actorID, out float fixedAt) && fixedAt >= entry.timestamp ? 0f : MediumWeight;
                case EvidenceType.DisruptionRepair: return RepairWeight;
                case EvidenceType.TaskAlibi: return AlibiWeight;
                default: return 0f;
            }
        }

        public float Suspicion(int botId, int targetId) =>
            matrix.TryGetValue(botId, out Dictionary<int, float> row) && row.TryGetValue(targetId, out float value) ? value : 0f;

        /// <summary>The player this bot distrusts most, or -1 if it has no row yet.</summary>
        public int GetTopSuspect(int botId, out float score)
        {
            score = 0f;
            int best = -1;
            if (!matrix.TryGetValue(botId, out Dictionary<int, float> row)) return best;
            foreach (KeyValuePair<int, float> pair in row)
                if (pair.Value > score) { score = pair.Value; best = pair.Key; }
            return best;
        }

        /// <summary>Average suspicion of <paramref name="targetId"/> among the given observers (excluding the target).</summary>
        public float RoomSuspicion(int targetId, IEnumerable<int> observers)
        {
            float total = 0f; int count = 0;
            foreach (int observer in observers)
            {
                if (observer == targetId || !matrix.ContainsKey(observer)) continue;
                total += Suspicion(observer, targetId); count++;
            }
            return count == 0 ? 0f : total / count;
        }

        /// <summary>The line a bot opens the discussion with, chosen from its role and its memory.</summary>
        public DialogueLine GenerateDialogueLine(int botId, DiscussionContext context)
        {
            switch (context.RoleOf(botId))
            {
                case PlayerRole.Impostor: return ImpostorOpening(botId, context);
                case PlayerRole.Crony: return CronyOpening(botId, context);
                default: return CrewOpening(botId, context);
            }
        }

        /// <summary>
        /// A bot's reply when <paramref name="accuserId"/> points at <paramref name="accusedId"/>. Returns false when
        /// this bot has nothing to add.
        /// </summary>
        public bool TryReact(int botId, int accusedId, int accuserId, DiscussionContext context, out DialogueLine line)
        {
            line = default;
            PlayerRole role = context.RoleOf(botId);
            string accused = context.NameOf(accusedId), accuser = context.NameOf(accuserId);
            if (accusedId == botId)
            {
                if (role == PlayerRole.Impostor)
                {
                    // Counter-accusation shift: the Impostor never just defends, it turns the room on the accuser.
                    line = accuserId == context.ReporterId
                        ? Pick("That's a self-report! " + accuser + " is trying to frame me!", accuser + " found the body AND blames me? Classic self-report.")
                        : Pick(accuser + " is trying to frame me! I was at the " + context.AlibiOf(botId) + ".", "Why is " + accuser + " so quick to blame me? That's what an Impostor would do.");
                    line.accuse = accuserId; line.weight = .9f;
                    return true;
                }
                line = Pick("It wasn't me! I was finishing the " + context.AlibiOf(botId) + ".", "Not me. Ask anyone who was at the " + context.AlibiOf(botId) + ".");
                return true;
            }

            int impostor = ImpostorIn(context);
            if (role == PlayerRole.Crony && accusedId == impostor && impostor >= 0)
            {
                string where = !string.IsNullOrEmpty(context.LastRepairPlace)
                    ? "near " + context.LastRepairPlace + " fixing the disruption"
                    : "at the " + context.AlibiOf(impostor);
                line = Pick(accused + " was with me " + where + ". It's not them.", "No way. " + accused + " was with me " + where + " the whole time.");
                line.defends = impostor; line.weight = .8f;
                return true;
            }
            if (role == PlayerRole.Impostor || role == PlayerRole.Crony)
            {
                // Piling onto an innocent who is already under fire costs nothing.
                if (context.RoleOf(accusedId) == PlayerRole.Crewmate && UnityEngine.Random.value < .5f)
                {
                    line = Pick("Yeah, I had a bad feeling about " + accused + " too.", "Agreed. " + accused + " has been acting strange.");
                    line.accuse = accusedId; line.weight = .6f;
                    return true;
                }
                return false;
            }

            // Crewmate: back an accusation it has its own evidence for, or vouch for someone it saw working.
            // Only evidence strong enough to accuse on its own is worth adding to a pile-on.
            if (Suspicion(botId, accusedId) >= AccuseThreshold && Row(reasons, botId).TryGetValue(accusedId, out MemoryEntry evidence))
            {
                line = Pick("I agree with " + accuser + ". " + Reason(evidence, context), accuser + " is right. " + Reason(evidence, context));
                line.accuse = accusedId; line.weight = .7f;
                return true;
            }
            if (Row(alibis, botId).TryGetValue(accusedId, out MemoryEntry alibi))
            {
                line = Pick(accused + " was with me at the " + alibi.place + ". They're clear.", "I watched " + accused + " finish the " + alibi.place + ". It's not them.");
                line.defends = accusedId; line.weight = .7f;
                return true;
            }
            return false;
        }

        DialogueLine CrewOpening(int botId, DiscussionContext context)
        {
            int suspect = GetTopSuspect(botId, out float score);
            if (suspect >= 0 && score >= AccuseThreshold && Row(reasons, botId).TryGetValue(suspect, out MemoryEntry evidence))
            {
                DialogueLine accuse = DialogueLine.Say(Reason(evidence, context));
                accuse.accuse = suspect; accuse.weight = evidence.type == EvidenceType.WitnessedTakedown ? 3f : 2f;
                return accuse;
            }
            if (suspect >= 0 && score >= HedgeThreshold && Row(reasons, botId).TryGetValue(suspect, out evidence))
            {
                DialogueLine hedge = Pick("Not sure yet, but " + context.NameOf(suspect) + " seemed off. They were around " + evidence.place + ".",
                    "Keep an eye on " + context.NameOf(suspect) + ". I saw them near " + evidence.place + ".");
                hedge.accuse = suspect; hedge.weight = .6f;
                return hedge;
            }
            foreach (KeyValuePair<int, MemoryEntry> alibi in Row(alibis, botId))
                if (UnityEngine.Random.value < .4f)
                {
                    DialogueLine vouch = DialogueLine.Say(context.NameOf(alibi.Key) + " was with me at the " + alibi.Value.place + ". They're clear.");
                    vouch.defends = alibi.Key; vouch.weight = .5f;
                    return vouch;
                }
            return Pick("I was finishing the " + context.AlibiOf(botId) + ", I didn't see anything.",
                "I was busy at the " + context.AlibiOf(botId) + ". Who has real proof?",
                "Nothing from me, I was at the " + context.AlibiOf(botId) + ". Let's not rush this.");
        }

        DialogueLine ImpostorOpening(int botId, DiscussionContext context)
        {
            int target = MostDoubtedCrewmate(context, out float doubt);
            if (target >= 0 && doubt >= HedgeThreshold)
            {
                // Gaslighting: amplify a doubt the crew already has instead of inventing one.
                DialogueLine pile = Pick("Yeah, " + context.NameOf(target) + " was acting super strange near the paths. I suspect them too.",
                    "I've been watching " + context.NameOf(target) + ". Something's not right with them.");
                pile.accuse = target; pile.weight = 1f;
                return pile;
            }
            if (target >= 0 && UnityEngine.Random.value < .5f)
            {
                DialogueLine deflect = DialogueLine.Say("Where was " + context.NameOf(target) + "? I never saw them at a station.");
                deflect.accuse = target; deflect.weight = .6f;
                return deflect;
            }
            return Pick("I was finishing the " + context.AlibiOf(botId) + ", I didn't see anything.", "I was at the " + context.AlibiOf(botId) + ". Did anyone see what happened?");
        }

        DialogueLine CronyOpening(int botId, DiscussionContext context)
        {
            int impostor = ImpostorIn(context);
            List<int> crew = context.Present.FindAll(id => context.RoleOf(id) == PlayerRole.Crewmate);
            if (impostor >= 0 && RoomSuspicion(impostor, crew) >= HedgeThreshold)
            {
                DialogueLine cover = DialogueLine.Say(context.NameOf(impostor) + " was with me at the " + context.AlibiOf(impostor) + ". Don't waste votes on them.");
                cover.defends = impostor; cover.weight = .8f;
                return cover;
            }
            int target = MostDoubtedCrewmate(context, out float doubt);
            if (target >= 0 && (doubt >= HedgeThreshold || UnityEngine.Random.value < .4f))
            {
                DialogueLine push = DialogueLine.Say(context.NameOf(target) + " keeps wandering off alone. That's not normal.");
                push.accuse = target; push.weight = .7f;
                return push;
            }
            return Pick("I was doing the " + context.AlibiOf(botId) + ". Quiet round for me.", "I didn't see anything at the " + context.AlibiOf(botId) + ".");
        }

        /// <summary>The crewmate the room (crewmate observers only) already distrusts most.</summary>
        int MostDoubtedCrewmate(DiscussionContext context, out float doubt)
        {
            doubt = 0f;
            int best = -1;
            List<int> crew = context.Present.FindAll(id => context.RoleOf(id) == PlayerRole.Crewmate);
            foreach (int id in crew)
            {
                float room = RoomSuspicion(id, crew);
                if (best < 0 || room > doubt) { best = id; doubt = room; }
            }
            return best;
        }

        // List<int>.Find would return 0, a real player id, when nobody matches.
        static int ImpostorIn(DiscussionContext context)
        {
            foreach (int id in context.Present) if (context.RoleOf(id) == PlayerRole.Impostor) return id;
            return -1;
        }

        static string Reason(MemoryEntry evidence, DiscussionContext context)
        {
            string who = context.NameOf(evidence.actorID);
            string victim = evidence.targetID >= 0 ? context.NameOf(evidence.targetID) : "them";
            switch (evidence.type)
            {
                case EvidenceType.WitnessedTakedown: return "I saw " + who + " take " + victim + " down near " + evidence.place + "!";
                case EvidenceType.BodyDiscovery:
                case EvidenceType.ProximityToTarget:
                    return "I saw " + who + " right next to " + victim + " near " + evidence.place + " just before the body was found!";
                case EvidenceType.NearStump: return who + " came out of the hollow stump by " + evidence.place + ". Crew don't use those.";
                case EvidenceType.DisruptionIgnored: return who + " stood at " + evidence.place + " during the disruption and didn't fix it.";
                default: return who + " was acting strange near " + evidence.place + ".";
            }
        }

        static DialogueLine Pick(params string[] options) => DialogueLine.Say(options[UnityEngine.Random.Range(0, options.Length)]);

        static Dictionary<int, T> Row<T>(Dictionary<int, Dictionary<int, T>> table, int botId)
        {
            if (!table.TryGetValue(botId, out Dictionary<int, T> row)) table[botId] = row = new Dictionary<int, T>();
            return row;
        }
    }
}
