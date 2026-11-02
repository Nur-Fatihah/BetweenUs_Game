using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
namespace Crony
{
    // Values travel over the network as bytes: append new decisions only.
    public enum ChatDecision { Allowed, Masked, Empty, Cooldown, Distress, Blocked, Violation, Restricted, Intercepted }
    /// <summary>Policy categories from the safety brief. Each maps to a different response in SafetySystem.</summary>
    public enum SafetyCategory : byte { None, Profanity, TargetedInsult, IdentityAbuse, SexualHarassment, ThreatOrDoxxing, Extremism, SelfHarmEncouragement, Distress, Spam, Impersonation, Evasion }
    /// <summary>Bounded pre-publication rules. Contextual review belongs to the moderation service.</summary>
    public sealed class ChatSafetyFilter
    {
        public const int MaxLength = 180;
        static Regex Rule(string pattern) => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(25));
        static readonly Regex PrivateData = Rule(@"(?:https?://|www\.)\S+|\b[\w.%+-]+@[\w.-]+\.[a-z]{2,}\b|(?:\+?\d[\s().-]*){8,}|\b(?:\d{1,3}\.){3}\d{1,3}\b|\b[a-z0-9-]+\.(?:com|net|org|io|gg|co|my)\b\S*|\b\d+\s+(?:[a-z]+\s+){0,4}(?:street|road|avenue|lane|jalan)\b");
        static readonly Regex Profanity = Rule(@"\b(?:fuck\w*|shit\w*|bitch\w*|asshole\w*|cunt\w*|bastard\w*|piss\w*)\b");
        // First-person distress is support, never misconduct.
        static readonly Regex Distress = Rule(@"\b(?:i (?:want to|will|might|am going to|wanna|feel like i should) (?:die|kill myself|hurt myself|end it|end my life)|i(?: am|'m) suicidal|i (?:cannot|can't) go on|i don't want to (?:live|be alive))\b");
        static readonly Regex SelfHarmEncouragement = Rule(@"\b(?:kys|kill yourself|go die|you should die|hurt yourself|end yourself|end your life|cut yourself|nobody would miss you)\b");
        // "Kill" is a game verb here; threats only count when they reach outside the match or expose a person.
        static readonly Regex Threat = Rule(@"\b(?:i(?:'ll| will)|gonna|going to)\s+(?:find|hurt|kill|beat|stab|shoot|visit)\s+you\b.*\b(?:irl|in real life|at school|after school|your house|your home|where you live|outside the game|for real)\b|\bi know where you live\b|\b(?:your|his|her|their) (?:home address|address|real name|school|phone number|ip) is\b|\bi(?:'ll| will) (?:leak|post|share) your (?:address|real name|photos?|number|ip)\b");
        static readonly Regex Extremism = Rule(@"\b(?:heil hitler|sieg heil|white power|1488|race war|gas the \w+|join (?:isis|the kkk|al.?qaeda)|death to (?:all )?\w+s)\b");
        static readonly Regex IdentityAbuse = Rule(@"\b(?:nigg\w*|fagg?ot\w*|tranny\w*|retard\w*|chink\w*|kike\w*|go back to your country|women belong in the kitchen)\b|\b(?:all |you )?(?:women|girls|muslims|jews|christians|hindus|blacks|indians|malays|chinese|gays)\s+(?:are|should)\s+(?:trash|stupid|animals|die|be (?:banned|killed))\b");
        static readonly Regex SexualHarassment = Rule(@"\b(?:send (?:me )?(?:nudes|pics|photos of you)|show me your (?:body|boobs|tits)|nudes?|wanna have sex|sex with you|rape\w*|sexy body)\b");
        static readonly Regex TargetedInsult = Rule(@"\b(?:you(?:'re| are)|ur|u r)\s+(?:so\s+)?(?:worthless|stupid|an idiot|trash|garbage|pathetic|ugly|a loser|useless|dumb)\b|\bnobody likes you\b|\b(?:just|go|pls|please|you should) uninstall\b|\b(?:fuck|screw) (?:you|off)\b|\byou (?:bitch|bastard|idiot|loser)\b");
        static readonly Regex Impersonation = Rule(@"^\s*(?:\[?(?:system|crony|moderator|mod|admin|staff)\]?\s*[:>\]])");
        // Joins letters split with spaces or symbols ("k y s", "f.u.c.k") so dodges are judged like the plain text.
        static readonly Regex SplitLetters = Rule(@"\b(?:[a-z0-9@$][\s._*\-]+){2,}[a-z0-9@$]\b");
        struct Burst { public double Last, Until; public int Count, Repeats; public string LastText; }
        readonly Dictionary<ulong, Burst> bursts = new Dictionary<ulong, Burst>();

        public ChatDecision Inspect(string raw, out string clean, out SafetyCategory category)
        {
            clean = string.Empty; category = SafetyCategory.None;
            if (string.IsNullOrWhiteSpace(raw)) return ChatDecision.Empty;
            if (raw.Length > MaxLength) return ChatDecision.Blocked;
            var text = new StringBuilder(raw.Length);
            foreach (char c in raw.Normalize(NormalizationForm.FormKC))
                if (!char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format) text.Append(c);
            string candidate = text.ToString().Trim();
            try {
                category = Classify(candidate);
                if (category == SafetyCategory.Distress) return ChatDecision.Distress;
                if (category == SafetyCategory.None || category == SafetyCategory.Profanity)
                {
                    SafetyCategory hidden = Classify(Deobfuscate(candidate));
                    // Disguising serious abuse never lowers its severity; disguising lesser abuse is its own aggravated strike.
                    if (hidden != SafetyCategory.None && hidden != SafetyCategory.Distress && hidden != SafetyCategory.Profanity)
                    { category = hidden == SafetyCategory.TargetedInsult || hidden == SafetyCategory.Impersonation ? SafetyCategory.Evasion : hidden; return ChatDecision.Violation; }
                    if (hidden == SafetyCategory.Profanity) category = SafetyCategory.Profanity;
                }
                if (category != SafetyCategory.None && category != SafetyCategory.Profanity) return ChatDecision.Violation;
                // Swearing is held back with a short explanation but never counts as a strike.
                if (category == SafetyCategory.Profanity) return ChatDecision.Intercepted;
                // A player's own contact details are masked to protect them.
                clean = PrivateData.Replace(Profanity.Replace(candidate, "***"), "***");
                return clean.Length == 0 ? ChatDecision.Empty : clean == candidate ? ChatDecision.Allowed : ChatDecision.Masked;
            } catch (RegexMatchTimeoutException) { return ChatDecision.Blocked; }
        }

        static SafetyCategory Classify(string text)
        {
            if (Distress.IsMatch(text)) return SafetyCategory.Distress;
            if (Threat.IsMatch(text)) return SafetyCategory.ThreatOrDoxxing;
            if (SelfHarmEncouragement.IsMatch(text)) return SafetyCategory.SelfHarmEncouragement;
            if (Extremism.IsMatch(text)) return SafetyCategory.Extremism;
            if (IdentityAbuse.IsMatch(text)) return SafetyCategory.IdentityAbuse;
            if (SexualHarassment.IsMatch(text)) return SafetyCategory.SexualHarassment;
            if (TargetedInsult.IsMatch(text)) return SafetyCategory.TargetedInsult;
            if (Impersonation.IsMatch(text)) return SafetyCategory.Impersonation;
            if (Profanity.IsMatch(text)) return SafetyCategory.Profanity;
            return SafetyCategory.None;
        }

        static string Deobfuscate(string text)
        {
            string joined = SplitLetters.Replace(text.ToLowerInvariant(), m => Regex.Replace(m.Value, @"[\s._*\-]", ""));
            var leet = new StringBuilder(joined.Length);
            foreach (char c in joined)
                leet.Append(c == '0' ? 'o' : c == '1' ? 'i' : c == '3' ? 'e' : c == '4' || c == '@' ? 'a' : c == '5' || c == '$' ? 's' : c == '7' ? 't' : c);
            return leet.ToString();
        }

        public ChatDecision Process(ulong sender, string raw, double now, out string clean, out SafetyCategory category)
        {
            var decision = Inspect(raw, out clean, out category);
            // Seeking help never adds a strike or consumes a spam allowance.
            if (decision == ChatDecision.Distress) return decision;
            bursts.TryGetValue(sender, out var burst);
            if (now < burst.Until) { clean = ""; return ChatDecision.Cooldown; }
            burst.Count = now - burst.Last < 1.5 ? burst.Count + 1 : 1;
            string key = (raw ?? "").Trim().ToLowerInvariant();
            burst.Repeats = key == burst.LastText && now - burst.Last < 30 ? burst.Repeats + 1 : 0;
            burst.Last = now; burst.LastText = key;
            if (burst.Count > 4 || burst.Repeats >= 2) { burst.Until = now + 3; burst.Count = 0; burst.Repeats = 0; decision = ChatDecision.Cooldown; category = SafetyCategory.Spam; clean = ""; }
            bursts[sender] = burst;
            return decision;
        }
        public void Forget(ulong sender) => bursts.Remove(sender);
    }
}
