using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
namespace Crony
{
    public enum ChatChannel : byte { Lobby, Township, Meeting, Ghost, System }
    public sealed class ChatMessage
    {
        public string Id, Account, Name, Text, Time;
        public ulong ClientId;
        public ChatChannel Channel;
    }
    public sealed class TextChatManager : MonoBehaviour
    {
        public static TextChatManager Instance { get; private set; }
        public readonly List<ChatMessage> History = new List<ChatMessage>(100);
        public event Action Changed;
        public event Action<string, bool> Notice;
        public event Action<ChatMessage> Received;
        readonly Dictionary<ulong,string> knownAccounts = new Dictionary<ulong,string>();
        public string AccountFor(ulong id) => knownAccounts.TryGetValue(id,out var key) ? key : (GameManager.Instance != null && GameManager.Instance.IsServer ? GameManager.Instance.CommunicationAccount(id) : "unresolved:" + id);
        readonly ChatSafetyFilter filter = new ChatSafetyFilter();
        public bool Online => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
        const string LocalAccount="local-player";
        void Awake() { if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;CommunicationSafety.Changed+=OnBlocks; }
        void OnDestroy(){CommunicationSafety.Changed-=OnBlocks;if(Instance==this)Instance=null;}
        void OnBlocks(){History.RemoveAll(m=>CommunicationSafety.IsBlocked(m.Account));Changed?.Invoke();}
        public void Send(string text)
        {
            if(!GameSettings.TextChatEnabled){Inform("Text chat is off. Turn it on in Settings > Safety.");return;}
            var decision=filter.Inspect(text,out var clean,out var spotted);
            if(decision==ChatDecision.Intercepted){ChatEducationModal.Show(spotted,"");return;}
            if(decision==ChatDecision.Distress){ShowDecision(decision);return;}
            if(decision==ChatDecision.Empty||decision==ChatDecision.Blocked){ShowDecision(decision);return;}
            // The host is authoritative for violations online, so the raw text still goes to it for enforcement.
            if(Online){ if(GameManager.Instance!=null&&GameManager.Instance.IsSpawned)GameManager.Instance.SendChatRpc(text);else Inform("Chat is connecting. Please try again."); return; }
            var safety=SafetySystem.Ensure(gameObject);
            if(safety.IsChatRestricted(LocalAccount,out _)){ShowDecision(ChatDecision.Restricted);return;}
            decision=filter.Process(0,text,Time.realtimeSinceStartupAsDouble,out clean,out var category);
            if(decision==ChatDecision.Violation){var result=safety.Enforce(LocalAccount,category);if(result.VoiceRestricted)CommunicationSafety.SetLocalVoiceRestricted(true);ShowModeration(result.Consequence,result.Reference,result.Level,result.Category);return;}
            if(decision!=ChatDecision.Allowed&&decision!=ChatDecision.Masked){ShowDecision(decision);return;}
            var phase=CronyRuntime.CommunicationPhase;
            Receive(0,LocalAccount,PlayerPrefs.GetString("Crony.PlayerName","You"),clean,
                !CronyRuntime.CommunicationAlive?ChatChannel.Ghost:phase==RoundPhase.Meeting?ChatChannel.Meeting:phase==RoundPhase.Playing?ChatChannel.Township:ChatChannel.Lobby,Guid.NewGuid().ToString("N"));
            if(decision==ChatDecision.Masked)ShowDecision(decision);
        }
        public void Receive(ulong client,string account,string name,string text,ChatChannel channel,string id)
        {
            knownAccounts[client]=account;
            if(CommunicationSafety.IsBlocked(account))return;
            if(!GameSettings.TextChatEnabled&&channel!=ChatChannel.System)return;
            if(History.Count==100)History.RemoveAt(0);
            var message=new ChatMessage {ClientId=client,Account=account,Name=name,Text=text,Channel=channel,Id=id,Time=DateTime.Now.ToString("HH:mm")};History.Add(message);Changed?.Invoke();Received?.Invoke(message);
            bool own=account==LocalAccount||(Online&&client==NetworkManager.Singleton.LocalClientId);
            if(!own&&channel!=ChatChannel.System)GameAudioManager.PlayChatPing();
        }
        /// <summary>Local-only announcement such as a player joining; never sent to the network.</summary>
        public void PostSystem(string text)=>Receive(ulong.MaxValue,"system","Crony",text,ChatChannel.System,Guid.NewGuid().ToString("N"));
        public void Inform(string message)=>Notice?.Invoke(message,false);
        /// <summary>Explains a moderation decision privately and keeps it on this device for appeal.</summary>
        public void ShowModeration(string consequence,string reference,EnforcementLevel level,SafetyCategory category)
        {
            CommunicationSafety.RecordOwnIncident(reference,level,consequence);
            Inform("Message not sent. "+consequence);
            ChatEducationModal.Show(category,consequence);
        }
        public void ShowDecision(ChatDecision decision)
        {
            if(decision==ChatDecision.Intercepted){ChatEducationModal.Show(SafetyCategory.Profanity,"");return;}
            if(decision==ChatDecision.Distress){Notice?.Invoke("Your message stayed private. You are not in trouble. If you may hurt yourself, contact local emergency services or someone you trust now. You can step away from this match. Choose support resources for help.",true);return;}
            Inform(decision==ChatDecision.Cooldown?"Please wait 3 seconds before sending again, and avoid repeating the same message.":decision==ChatDecision.Restricted?"Your chat is paused right now. You can keep playing; open Settings > Safety for details.":decision==ChatDecision.Masked?"Some words or personal information were replaced with ***.":decision==ChatDecision.Empty?"Type a message first.":"Please use a shorter, respectful message without personal information.");
        }
    }
}
