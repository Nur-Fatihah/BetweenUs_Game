using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
namespace Crony
{
    /// <summary>Vivox transport; NGO owns match phase and participant life state. Voice is opt-in.</summary>
    public sealed class TownshipVoiceManager : MonoBehaviour
    {
        public static TownshipVoiceManager Instance {get;private set;}
        public bool EnabledByUser {get;private set;}
        public bool Consent {get;private set;}
        public bool ToggleToTalk {get=>PlayerPrefs.GetInt("Crony.VoiceToggleToTalk",0)==1;set{PlayerPrefs.SetInt("Crony.VoiceToggleToTalk",value?1:0);PlayerPrefs.Save();if(channelReady)SetStatus(MutedStatus);}}
        public bool PartyOnly {get;set;}
        public string Status {get;private set;}="Voice off";
        public bool Transmitting=>transmitting;
        public bool Connecting=>busy||(EnabledByUser&&!channelReady);
        public static event System.Action StatusChanged;
        string MutedStatus=>ToggleToTalk?"Mic muted · press V":"Mic muted · hold V";
        readonly List<VivoxParticipant> participants=new List<VivoxParticipant>(16);
        readonly Dictionary<string,ulong> clients=new Dictionary<string,ulong>();
        readonly Dictionary<string,string> accounts=new Dictionary<string,string>();
        readonly RaycastHit2D[] hits=new RaycastHit2D[16];
        readonly Dictionary<VivoxParticipant,bool> localMute=new Dictionary<VivoxParticipant,bool>();
        readonly Dictionary<ulong,bool> speaking=new Dictionary<ulong,bool>();
        bool channelReady; bool registered; bool initialized,subscribed,busy,disposed,transmitting;int generation;float nextTick;string channel,session;RoundPhase lastPhase;bool lastGhost;
        public void SetConsent(bool consent){Consent=consent;PlayerPrefs.SetInt("Crony.VoiceConsent",consent?1:0);PlayerPrefs.Save();if(!consent)SetEnabled(false);else StatusChanged?.Invoke();}
        void Awake(){Instance=this;Consent=PlayerPrefs.GetInt("Crony.VoiceConsent",0)==1;CommunicationSafety.Changed+=ApplyMutes;}
        void SetStatus(string value){Status=value;StatusChanged?.Invoke();}
        /// <summary>
        /// The Vivox browser plugin calls a Unity web API that this Unity version removed, so voice crashes the
        /// browser build. Voice is offered in the desktop downloads only; text chat works everywhere.
        /// </summary>
        public static bool Supported => Application.platform != RuntimePlatform.WebGLPlayer;
        public const string UnsupportedMessage = "Voice chat is in the downloadable Windows/Mac version. Text chat works here.";

        public async void SetEnabled(bool value)
        {
            if(value&&!Supported){SetStatus("Voice: download version only");return;}
            if(!value){EnabledByUser=false;generation++;transmitting=false;if(initialized)VivoxService.Instance.MuteInputDevice();SetStatus("Voice off");await LeaveSafely();return;}
            if(!Consent){SetStatus("Voice off · consent required");return;}
            if(busy||EnabledByUser)return;EnabledByUser=true;busy=true;int request=++generation;SetStatus("Voice connecting…");
            try{
                if(UnityServices.State!=ServicesInitializationState.Initialized)await UnityServices.InitializeAsync();
                if(!AuthenticationService.Instance.IsSignedIn)await AuthenticationService.Instance.SignInAnonymouslyAsync();
                if(!initialized){await VivoxService.Instance.InitializeAsync();initialized=true;}
                if(!subscribed){VivoxService.Instance.ChannelJoined+=Joined;VivoxService.Instance.ChannelLeft+=Left;VivoxService.Instance.ParticipantAddedToChannel+=Added;VivoxService.Instance.ParticipantRemovedFromChannel+=Removed;subscribed=true;}
                VivoxService.Instance.MuteInputDevice();
                if(!VivoxService.Instance.IsLoggedIn)await VivoxService.Instance.LoginAsync(new LoginOptions{DisplayName=PlayerPrefs.GetString("Crony.PlayerName","Player")});
                if(disposed||request!=generation||!EnabledByUser){await LeaveSafely();return;}
                if(GameManager.Instance!=null&&GameManager.Instance.IsSpawned){GameManager.Instance.RegisterVoiceIdentityRpc(AuthenticationService.Instance.PlayerId);registered=true;}
                else SetStatus("Voice ready · join an online game");
            }catch(Exception e){EnabledByUser=false;Debug.LogWarning("Voice could not start: "+e.Message);SetStatus("Voice unavailable · text chat still works");}finally{busy=false;}
        }
        public void Register(ulong client,string voiceId,string account,string matchSession)
        {clients[voiceId]=client;accounts[voiceId]=account;session=matchSession;nextTick=0;}
        void Update()
        {
            if(!EnabledByUser||!initialized||busy)return;
            var manager=GameManager.Instance;var network=NetworkManager.Singleton;
            if(manager==null||!manager.IsSpawned||network==null||!network.IsListening){registered=false;if(channel!=null){VivoxService.Instance.MuteInputDevice();transmitting=false;SwitchChannel(null,false);session=null;SetStatus("Voice ready · join an online game");}return;}
            if(!registered){manager.RegisterVoiceIdentityRpc(AuthenticationService.Instance.PlayerId);registered=true;}
            var local=manager.GetPlayer(network.LocalClientId);if(local==null)return;
            bool ghost=local.Status.Value!=PlayerLifeState.Alive;var phase=manager.Phase.Value;
            if(session!=null&&(channel==null||lastPhase!=phase||lastGhost!=ghost)){lastPhase=phase;lastGhost=ghost;SwitchChannel(session+"-"+(ghost?"ghost":phase==RoundPhase.Playing&&!PartyOnly?"proximity":"meeting"),phase==RoundPhase.Playing&&!ghost&&!PartyOnly);return;}
            if(!channelReady)return;
            var keys=Keyboard.current;bool held=keys!=null&&keys.vKey.isPressed&&!TownshipTextChatUI.CapturesInput;
            bool desired=ToggleToTalk?(keys!=null&&keys.vKey.wasPressedThisFrame?!transmitting:transmitting):held;
            if(!Application.isFocused||channel==null||ghost||CommunicationSafety.LocalVoiceRestricted)desired=false;
            if(desired!=transmitting){transmitting=desired;if(desired)VivoxService.Instance.UnmuteInputDevice();else VivoxService.Instance.MuteInputDevice();SetStatus(desired?"Talking · V":MutedStatus);}
            if(Time.unscaledTime<nextTick)return;nextTick=Time.unscaledTime+.1f;
            if(channel!=null&&phase==RoundPhase.Playing&&!ghost&&!PartyOnly){Vector3 pos=local.transform.position;var mapped=new Vector3(pos.x,0,pos.y);VivoxService.Instance.Set3DPosition(mapped,mapped,Vector3.forward,Vector3.up,channel,false);}
            ApplyMutes();
        }
        async void SwitchChannel(string next,bool spatial)
        {
            if(busy)return;busy=true;channelReady=false;int request=generation;transmitting=false;VivoxService.Instance.MuteInputDevice();
            try{await LeaveSafely();if(next==null||!EnabledByUser||request!=generation||disposed)return;
                channel=next;
                if(spatial)await VivoxService.Instance.JoinPositionalChannelAsync(next,ChatCapability.AudioOnly,new Channel3DProperties(18,3,1,AudioFadeModel.LinearByDistance));
                else await VivoxService.Instance.JoinGroupChannelAsync(next,ChatCapability.AudioOnly);
                if(request!=generation||disposed)await LeaveSafely();
            }catch(Exception e){channel=null;EnabledByUser=false;Debug.LogWarning("Voice channel failed: "+e.Message);SetStatus("Voice connection failed · tap to retry");}finally{busy=false;}
        }
        async Task LeaveSafely(){try{if(initialized)await VivoxService.Instance.LeaveAllChannelsAsync();}catch(Exception){}channel=null;channelReady=false;participants.Clear();localMute.Clear();speaking.Clear();}
        async void Joined(string name){if(!EnabledByUser||disposed)return;try{await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single,name);VivoxService.Instance.MuteInputDevice();transmitting=false;channelReady=true;SetStatus(MutedStatus);}catch(Exception){SetEnabled(false);}}
        void Left(string name){if(channel==name){channel=null;channelReady=false;transmitting=false;}}
        void Added(VivoxParticipant p){if(!participants.Contains(p))participants.Add(p);ApplyMutes();}
        void Removed(VivoxParticipant p){participants.Remove(p);localMute.Remove(p);if(clients.TryGetValue(p.PlayerId,out var id))speaking.Remove(id);}
        public bool IsSpeaking(ulong id)=>speaking.TryGetValue(id,out var value)&&value;
        void ApplyMutes()
        {
            var gm=GameManager.Instance;var network=NetworkManager.Singleton;if(gm==null||network==null)return;var listener=gm.GetPlayer(network.LocalClientId);if(listener==null)return;
            for(int i=0;i<participants.Count;i++){
                var p=participants[i];if(p.IsSelf){speaking[network.LocalClientId]=transmitting&&p.SpeechDetected;continue;}
                bool known=clients.TryGetValue(p.PlayerId,out var id);var player=known?gm.GetPlayer(id):null;
                bool mute=player==null||!accounts.TryGetValue(p.PlayerId,out var account)||CommunicationSafety.IsMuted(account);
                if(player!=null&&listener.Status.Value==PlayerLifeState.Alive&&player.Status.Value!=PlayerLifeState.Alive)mute=true;
                if(!mute&&gm.Phase.Value==RoundPhase.Playing&&!PartyOnly){int count=Physics2D.Linecast(listener.transform.position,player.transform.position,ContactFilter2D.noFilter,hits);for(int j=0;j<count;j++){var collider=hits[j].collider;if(collider!=null&&!collider.isTrigger&&collider.GetComponentInParent<PlayerController>()==null){mute=true;break;}}}
                if(!localMute.TryGetValue(p,out var old)||old!=mute){if(mute)p.MutePlayerLocally();else p.UnmutePlayerLocally();localMute[p]=mute;}
                if(known)speaking[id]=!mute&&p.SpeechDetected;
            }
        }
        void OnApplicationFocus(bool focused){if(!focused&&initialized){VivoxService.Instance.MuteInputDevice();if(transmitting){transmitting=false;if(channelReady)SetStatus(MutedStatus);}}}
        async void OnDestroy(){disposed=true;generation++;CommunicationSafety.Changed-=ApplyMutes;if(initialized){VivoxService.Instance.MuteInputDevice();if(subscribed){VivoxService.Instance.ChannelJoined-=Joined;VivoxService.Instance.ChannelLeft-=Left;VivoxService.Instance.ParticipantAddedToChannel-=Added;VivoxService.Instance.ParticipantRemovedFromChannel-=Removed;}await LeaveSafely();try{await VivoxService.Instance.LogoutAsync();}catch(Exception){}}if(Instance==this)Instance=null;}
    }
}
