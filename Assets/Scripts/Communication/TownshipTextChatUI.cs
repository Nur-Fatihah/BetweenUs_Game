using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
namespace Crony
{
    /// <summary>HUD chat dock, unread feed, voice button and the draggable pop-out chat window.</summary>
    public sealed class TownshipTextChatUI : MonoBehaviour
    {
        enum Tab { Chat, Players, Phrases }
        public static TownshipTextChatUI Instance {get;private set;}
        /// <summary>True while the player is typing or a chat dialog is modal; gameplay keys and movement must ignore input.</summary>
        public static bool CapturesInput => Instance!=null&&Instance.Captures;
        /// <summary>True when Escape belongs to the chat (closing the window, a dialog or leaving the text field).</summary>
        public static bool HandlesEscape => Instance!=null&&(Instance.IsOpen||Instance.reports.IsOpen||Instance.ConsentOpen);
        public bool IsOpen => window!=null&&window.activeSelf;
        bool ConsentOpen => consent!=null&&consent.activeSelf;
        bool Typing => IsOpen&&input!=null&&(input.isFocused||Time.frameCount-focusRequestedFrame<3);
        bool Captures => Typing||reports.IsOpen||ConsentOpen;

        static readonly string[] Phrases={"Where was the body?","I was at Hawker Stalls","I was near the Lake","I need help with a task","My task is complete","Please share your evidence","I have no evidence yet","Skip vote","Please use text chat","Let's stay respectful"};
        RectTransform dock;
        GameObject window,support,consent,voiceMenu,feedRoot; Text titleLabel,notice,micLabel,badgeLabel,voiceModeLabel; InputField input; Text placeholder; Button sendButton,chatButton,micButton;
        RectTransform chatPage,playersPage,phrasesPage,content,playersContent; ScrollRect scroll; Button[] tabButtons;
        PlayerReportUI reports;TextChatManager manager;Tab tab;bool dirty,stickToBottom;int unread,focusRequestedFrame=-10,submittedFrame=-10;float nextStatusRefresh;
        readonly Dictionary<Sprite,Sprite> portraits=new Dictionary<Sprite,Sprite>();
        readonly Dictionary<ulong,Text> speakingLabels=new Dictionary<ulong,Text>();
        readonly List<(GameObject go,CanvasGroup group,float born)> feed=new List<(GameObject,CanvasGroup,float)>();

        void Awake(){Instance=this;Build();}
        void Start(){manager=TextChatManager.Instance;manager.Changed+=MarkDirty;manager.Notice+=ShowNotice;manager.Received+=OnReceived;TownshipVoiceManager.StatusChanged+=RefreshMic;RefreshMic();Close();}
        void OnDestroy(){foreach(var sprite in portraits.Values)Destroy(sprite);portraits.Clear();TownshipVoiceManager.StatusChanged-=RefreshMic;if(manager!=null){manager.Changed-=MarkDirty;manager.Notice-=ShowNotice;manager.Received-=OnReceived;}if(Instance==this)Instance=null;}

        public void Build()
        {
            if(window!=null)return;
            var canvas=GetComponent<Canvas>();if(canvas==null)canvas=gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=180;
            var scaler=GetComponent<CanvasScaler>();if(scaler==null)scaler=gameObject.AddComponent<CanvasScaler>();PixelUiTheme.ConfigureCanvas(scaler);
            if(GetComponent<GraphicRaycaster>()==null)gameObject.AddComponent<GraphicRaycaster>();
            BuildDock();BuildFeed();BuildWindow();BuildConsent();
            reports=gameObject.GetComponent<PlayerReportUI>();if(reports==null)reports=gameObject.AddComponent<PlayerReportUI>();reports.Build(transform);
            window.SetActive(false);
        }

        void BuildDock()
        {
            dock=CommunicationUI.Rect("Chat and voice dock",transform,new Vector2(.5f,0),new Vector2(.5f,0));dock.pivot=new Vector2(.5f,0);dock.sizeDelta=new Vector2(520,58);dock.anchoredPosition=new Vector2(0,16);
            chatButton=CommunicationUI.Button("CHAT  [T]",dock,new Vector2(0,0),new Vector2(.44f,1),Toggle,PixelUiTheme.Sky,18);
            var badge=CommunicationUI.Panel("Unread badge",chatButton.transform,new Vector2(1,1),new Vector2(1,1),PixelUiTheme.Coral);badge.sprite=PixelUiTheme.GetDotSprite();badge.raycastTarget=false;badge.rectTransform.sizeDelta=new Vector2(32,32);badge.rectTransform.anchoredPosition=new Vector2(-4,-4);
            badgeLabel=CommunicationUI.Label("0",badge.transform,Vector2.zero,Vector2.one,15);badgeLabel.alignment=TextAnchor.MiddleCenter;badgeLabel.color=Color.white;badgeLabel.fontStyle=FontStyle.Bold;badge.gameObject.SetActive(false);
            micButton=CommunicationUI.Button("VOICE OFF",dock,new Vector2(.46f,0),new Vector2(1,1),OnMicClicked,PixelUiTheme.Almond,16);micLabel=micButton.GetComponentInChildren<Text>();
            voiceMenu=CommunicationUI.Card("Voice menu",dock,new Vector2(.46f,1),new Vector2(1,1)).gameObject;var vm=(RectTransform)voiceMenu.transform;vm.pivot=new Vector2(.5f,0);vm.offsetMin=new Vector2(0,8);vm.offsetMax=new Vector2(0,140);
            voiceModeLabel=CommunicationUI.Button("Mode: hold V to talk",voiceMenu.transform,new Vector2(.05f,.53f),new Vector2(.95f,.92f),()=>{var v=TownshipVoiceManager.Instance;if(v!=null)v.ToggleToTalk=!v.ToggleToTalk;RefreshMic();},PixelUiTheme.Linen,15).GetComponentInChildren<Text>();
            CommunicationUI.Button("Turn voice off",voiceMenu.transform,new Vector2(.05f,.08f),new Vector2(.95f,.47f),()=>{TownshipVoiceManager.Instance?.SetEnabled(false);voiceMenu.SetActive(false);},PixelUiTheme.Coral,15);
            voiceMenu.SetActive(false);
        }

        void BuildFeed()
        {
            var r=CommunicationUI.Rect("Recent chat feed",transform,new Vector2(.5f,0),new Vector2(.5f,0));r.pivot=new Vector2(.5f,0);r.sizeDelta=new Vector2(620,220);r.anchoredPosition=new Vector2(0,84);feedRoot=r.gameObject;
            var layout=feedRoot.AddComponent<VerticalLayoutGroup>();layout.childAlignment=TextAnchor.LowerCenter;layout.spacing=6;layout.childControlHeight=true;layout.childControlWidth=false;layout.childForceExpandHeight=false;layout.childForceExpandWidth=false;
        }

        void BuildWindow()
        {
            var body=CommunicationUI.Window("CHAT",transform,new Vector2(660,560),Vector2.zero,new Vector2(16,150),Close,out window,out titleLabel);
            tabButtons=new Button[3];string[] names={"CHAT","PLAYERS","QUICK PHRASES"};
            for(int i=0;i<3;i++){int index=i;float x=i*.335f;tabButtons[i]=CommunicationUI.Button(names[i],body,new Vector2(x,.9f),new Vector2(x+.325f,1),()=>SelectTab((Tab)index),PixelUiTheme.Tan,16);}
            notice=CommunicationUI.Label("",body,new Vector2(0,.83f),new Vector2(1,.895f),15);notice.color=PixelUiTheme.MutedInk;

            chatPage=CommunicationUI.Rect("Chat page",body,Vector2.zero,new Vector2(1,.825f));
            var viewport=CommunicationUI.Card("Chat history",chatPage,new Vector2(0,.17f),Vector2.one);viewport.gameObject.AddComponent<RectMask2D>().padding=new Vector4(4,4,4,4);
            scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.viewport=(RectTransform)viewport.transform;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
            content=VerticalList("Messages",viewport.transform);scroll.content=content;
            var field=CommunicationUI.Panel("Message input",chatPage,new Vector2(0,0),new Vector2(.78f,.14f),Color.white);var outline=field.gameObject.AddComponent<Outline>();outline.effectColor=PixelUiTheme.Chocolate;outline.effectDistance=new Vector2(2,-2);
            input=field.gameObject.AddComponent<InputField>();input.characterLimit=ChatSafetyFilter.MaxLength;input.lineType=InputField.LineType.SingleLine;input.caretColor=PixelUiTheme.Ink;input.selectionColor=new Color(1f,.67f,.29f,.35f);
            input.textComponent=CommunicationUI.Label("",field.transform,new Vector2(.03f,0),new Vector2(.97f,1),18);input.textComponent.verticalOverflow=VerticalWrapMode.Overflow;
            placeholder=CommunicationUI.Label("Type a message…",field.transform,new Vector2(.03f,0),new Vector2(.97f,1),18);placeholder.color=PixelUiTheme.MutedInk;placeholder.fontStyle=FontStyle.Italic;input.placeholder=placeholder;
            input.onEndEdit.AddListener(OnEndEdit);
            sendButton=CommunicationUI.Button("SEND",chatPage,new Vector2(.8f,0),new Vector2(1,.14f),Send,PixelUiTheme.Amber,18);

            playersPage=CommunicationUI.Rect("Players page",body,Vector2.zero,new Vector2(1,.825f));
            var playersView=CommunicationUI.Card("Players list",playersPage,Vector2.zero,Vector2.one);playersView.gameObject.AddComponent<RectMask2D>().padding=new Vector4(4,4,4,4);
            var playersScroll=playersView.gameObject.AddComponent<ScrollRect>();playersScroll.horizontal=false;playersScroll.viewport=(RectTransform)playersView.transform;playersScroll.movementType=ScrollRect.MovementType.Clamped;playersScroll.scrollSensitivity=30;
            playersContent=VerticalList("Player rows",playersView.transform);playersScroll.content=playersContent;

            phrasesPage=CommunicationUI.Rect("Quick phrases page",body,Vector2.zero,new Vector2(1,.825f));
            for(int i=0;i<Phrases.Length;i++){string phrase=Phrases[i];float x=i%2*.51f,y=.81f-i/2*.2f;CommunicationUI.Button(phrase,phrasesPage,new Vector2(x,y),new Vector2(x+.49f,y+.18f),()=>{manager.Send(phrase);SelectTab(Tab.Chat);},PixelUiTheme.Linen,16);}

            support=CommunicationUI.Card("Private support",body,Vector2.zero,new Vector2(1,.825f)).gameObject;
            CommunicationUI.Label("You deserve support. This message was not shared with other players.\n\n"+SupportResources.Guidance,support.transform,new Vector2(.05f,.3f),new Vector2(.95f,.95f),18).verticalOverflow=VerticalWrapMode.Overflow;
            CommunicationUI.Button("Find a helpline near you",support.transform,new Vector2(.05f,.06f),new Vector2(.62f,.22f),()=>Application.OpenURL(SupportResources.HelplineUrl),PixelUiTheme.Mint,16);
            CommunicationUI.Button("Back to chat",support.transform,new Vector2(.65f,.06f),new Vector2(.95f,.22f),()=>support.SetActive(false),PixelUiTheme.Linen,16);support.SetActive(false);
            SelectTab(Tab.Chat);
        }

        void BuildConsent()
        {
            consent=CommunicationUI.Shade(transform,.6f).gameObject;
            var body=CommunicationUI.Window("Turn on voice chat?",consent.transform,new Vector2(640,400),new Vector2(.5f,.5f),Vector2.zero,()=>consent.SetActive(false),out _,out _);
            CommunicationUI.Label("Voice uses Unity Vivox to carry live audio to players in your game. Crony does not record it.\n\nYour mic stays muted until you hold V (or press V in toggle mode). You can mute, block or report anyone, and text chat always stays available.",body,new Vector2(0,.3f),new Vector2(1,1),18).verticalOverflow=VerticalWrapMode.Overflow;
            CommunicationUI.Button("Agree and turn on",body,new Vector2(0,.02f),new Vector2(.58f,.22f),()=>{consent.SetActive(false);var v=TownshipVoiceManager.Instance;if(v==null)return;v.SetConsent(true);v.SetEnabled(true);},PixelUiTheme.Mint,17);
            CommunicationUI.Button("Not now",body,new Vector2(.62f,.02f),new Vector2(1,.22f),()=>consent.SetActive(false),PixelUiTheme.Linen,17);
            consent.SetActive(false);
        }

        static RectTransform VerticalList(string name,Transform parent)
        {
            var list=CommunicationUI.Rect(name,parent,new Vector2(0,1),Vector2.one);list.pivot=new Vector2(.5f,1);
            var layout=list.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(10,10,10,10);layout.spacing=6;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;return list;
        }

        public void Toggle(){if(IsOpen)Close();else Open();}
        public void Open(){Build();if(!IsOpen){window.SetActive(true);window.transform.SetAsLastSibling();ClearFeed();unread=0;RefreshBadge();SelectTab(Tab.Chat);}RestoreFocus();}
        public void Close(){if(window==null)return;window.SetActive(false);support.SetActive(false);voiceMenu.SetActive(false);if(input!=null)input.DeactivateInputField();if(EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject!=null&&EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))EventSystem.current.SetSelectedGameObject(null);}
        public void RestoreFocus(){if(IsOpen&&tab==Tab.Chat&&input.interactable){input.Select();input.ActivateInputField();focusRequestedFrame=Time.frameCount;}}
        public void ShowNotice(string text,bool distress){Open();notice.text=text;support.SetActive(distress);if(distress){input.text="";support.GetComponentInChildren<Button>().Select();}}

        void SelectTab(Tab next)
        {
            tab=next;
            chatPage.gameObject.SetActive(tab==Tab.Chat);playersPage.gameObject.SetActive(tab==Tab.Players);phrasesPage.gameObject.SetActive(tab==Tab.Phrases);
            for(int i=0;i<tabButtons.Length;i++)CommunicationUI.SetFace(tabButtons[i],i==(int)tab?PixelUiTheme.Gold:PixelUiTheme.Tan);
            if(tab==Tab.Chat){dirty=true;stickToBottom=true;RestoreFocus();}
            else if(tab==Tab.Players)ShowPlayers();
            if(notice!=null&&!support.activeSelf)notice.text=tab==Tab.Players?"Mute, block or report anyone. Only you are affected.":tab==Tab.Phrases?"Tap a phrase to send it instantly.":"Enter to send · Esc to leave the text box · right-click a message to report";
        }

        void OnEndEdit(string text)
        {
            var keys=Keyboard.current;bool enter=keys!=null&&(keys.enterKey.wasPressedThisFrame||keys.numpadEnterKey.wasPressedThisFrame);
            if(!enter)return;submittedFrame=Time.frameCount;
            if(!string.IsNullOrWhiteSpace(text)){Send();RestoreFocus();}
        }

        void Send(){if(manager==null||!input.interactable)return;if(string.IsNullOrWhiteSpace(input.text)){RestoreFocus();return;}manager.Send(input.text);input.text="";stickToBottom=true;}

        void OnReceived(ChatMessage message)
        {
            if(IsOpen){dirty=true;return;}
            // The meeting screen already shows every line as a dialogue box.
            if(MeetingScreen.IsOpen){unread++;RefreshBadge();return;}
            bool own=message.Account=="local-player"||(manager.Online&&NetworkManager.Singleton!=null&&message.ClientId==NetworkManager.Singleton.LocalClientId);
            if(!own&&message.Channel!=ChatChannel.System){unread++;RefreshBadge();}
            AddFeedLine(message);dirty=true;
        }
        void MarkDirty()=>dirty=true;
        void RefreshBadge(){var badge=badgeLabel.transform.parent.gameObject;badge.SetActive(unread>0);badgeLabel.text=unread>9?"9+":unread.ToString();}

        void AddFeedLine(ChatMessage message)
        {
            var pill=CommunicationUI.Panel("Feed line",feedRoot.transform,Vector2.zero,Vector2.zero,new Color(PixelUiTheme.Chocolate.r,PixelUiTheme.Chocolate.g,PixelUiTheme.Chocolate.b,.88f));pill.raycastTarget=false;
            var fit=pill.gameObject.AddComponent<HorizontalLayoutGroup>();fit.padding=new RectOffset(14,14,6,6);fit.childControlWidth=true;fit.childControlHeight=true;
            pill.gameObject.AddComponent<ContentSizeFitter>().horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            var line=CommunicationUI.Label("",pill.transform,Vector2.zero,Vector2.one,16);line.color=message.Channel==ChatChannel.System?PixelUiTheme.Gold:PixelUiTheme.Cream;line.horizontalOverflow=HorizontalWrapMode.Overflow;
            string text=message.Channel==ChatChannel.System?message.Text:message.Name+": "+message.Text;line.text=text.Length>70?text.Substring(0,67)+"…":text;
            var group=pill.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;
            feed.Add((pill.gameObject,group,Time.unscaledTime));
            while(feed.Count>4){Destroy(feed[0].go);feed.RemoveAt(0);}
        }
        void ClearFeed(){foreach(var item in feed)if(item.go!=null)Destroy(item.go);feed.Clear();}

        void Update()
        {
            // The meeting screen shows every line as a dialogue box, so the floating feed steps aside.
            if(feedRoot!=null&&feedRoot.activeSelf==MeetingScreen.IsOpen)feedRoot.SetActive(!MeetingScreen.IsOpen);
            // On the title menus chat and voice live in the menu's Settings, Audio & Chat panel instead of a floating dock.
            bool inMenus=CronyRuntime.InTitleMenus&&!(NetworkManager.Singleton!=null&&NetworkManager.Singleton.IsListening);
            if(dock!=null&&dock.gameObject.activeSelf==inMenus){dock.gameObject.SetActive(!inMenus);if(inMenus&&IsOpen)Close();}
            var keys=Keyboard.current;
            for(int i=feed.Count-1;i>=0;i--){float age=Time.unscaledTime-feed[i].born;if(age>9f){Destroy(feed[i].go);feed.RemoveAt(i);}else feed[i].group.alpha=Mathf.Clamp01((9f-age)/1.5f);}
            if(Time.unscaledTime>=nextStatusRefresh){nextStatusRefresh=Time.unscaledTime+.25f;RefreshStatus();}
            if(dirty&&IsOpen&&tab==Tab.Chat){dirty=false;Refresh();}
            if(keys==null)return;
            if(reports.IsOpen){if(keys.escapeKey.wasPressedThisFrame)reports.Close();else if(keys.tabKey.wasPressedThisFrame)CycleFocus(reports.FocusRoot,keys.shiftKey.isPressed);return;}
            if(ConsentOpen){if(keys.escapeKey.wasPressedThisFrame)consent.SetActive(false);return;}
            bool blocked=AvatarDressingRoomUI.IsOpen||TownshipTaskMiniGameUI.IsOpen;
            if(!IsOpen){if((keys.tKey.wasPressedThisFrame||keys.enterKey.wasPressedThisFrame)&&!blocked&&!AnyOtherFieldFocused())Open();return;}
            if(keys.escapeKey.wasPressedThisFrame){if(input.isFocused){input.DeactivateInputField();EventSystem.current?.SetSelectedGameObject(null);focusRequestedFrame=-10;}else Close();return;}
            if(keys.enterKey.wasPressedThisFrame&&!input.isFocused&&submittedFrame!=Time.frameCount&&tab==Tab.Chat)RestoreFocus();
            if(keys.pageUpKey.isPressed)scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+Time.unscaledDeltaTime);
            if(keys.pageDownKey.isPressed)scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-Time.unscaledDeltaTime);
            if(stickToBottom){scroll.verticalNormalizedPosition=0;stickToBottom=false;}
            if(tab==Tab.Players)RefreshSpeaking();
        }

        static bool AnyOtherFieldFocused()
        {
            var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            var field=selected!=null?selected.GetComponent<InputField>():null;return field!=null&&field.isFocused;
        }

        void CycleFocus(Transform root,bool back)
        {
            if(root==null)return;var controls=root.GetComponentsInChildren<Selectable>(false);if(controls.Length==0)return;
            var selected=EventSystem.current.currentSelectedGameObject;int index=-1;
            for(int i=0;i<controls.Length;i++)if(controls[i].gameObject==selected){index=i;break;}
            for(int i=0;i<controls.Length;i++){index=(index+(back?-1:1)+controls.Length)%controls.Length;if(controls[index].IsInteractable()){controls[index].Select();break;}}
        }

        void RefreshStatus()
        {
            if(window==null||manager==null)return;
            ChatChannel channel=CurrentChannel(out bool interference);
            string where=manager.Online?"ONLINE":"PRACTICE";
            titleLabel.text="CHAT · "+channel.ToString().ToUpperInvariant()+" · "+where;
            input.interactable=!interference;sendButton.interactable=!interference;
            placeholder.text=interference?"Chat is jammed by interference. Repair the router!":channel==ChatChannel.Ghost?"Ghost chat: only other ghosts can read this…":channel==ChatChannel.Meeting?"Share what you saw…":"Type a message…";
        }

        ChatChannel CurrentChannel(out bool interference)
        {
            interference=false;var game=GameManager.Instance;
            if(manager.Online&&game!=null&&game.IsSpawned)
            {
                interference=game.InterferenceActive.Value;var network=NetworkManager.Singleton;var local=network!=null?game.GetPlayer(network.LocalClientId):null;
                if(local!=null&&local.Status.Value!=PlayerLifeState.Alive)return ChatChannel.Ghost;
                return game.Phase.Value==RoundPhase.Meeting?ChatChannel.Meeting:game.Phase.Value==RoundPhase.Playing?ChatChannel.Township:ChatChannel.Lobby;
            }
            if(!CronyRuntime.CommunicationAlive)return ChatChannel.Ghost;
            var phase=CronyRuntime.CommunicationPhase;return phase==RoundPhase.Meeting?ChatChannel.Meeting:phase==RoundPhase.Playing?ChatChannel.Township:ChatChannel.Lobby;
        }

        void Refresh()
        {
            if(manager==null)return;
            for(int i=content.childCount-1;i>=0;i--){var child=content.GetChild(i).gameObject;child.SetActive(false);Destroy(child);}
            if(manager.History.Count==0){var empty=CommunicationUI.Label("No messages yet. Say hi!",content,Vector2.zero,Vector2.one,17);empty.color=PixelUiTheme.MutedInk;empty.alignment=TextAnchor.MiddleCenter;empty.gameObject.AddComponent<LayoutElement>().minHeight=60;}
            foreach(var message in manager.History)AddMessageRow(message);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);stickToBottom=true;
        }

        void AddMessageRow(ChatMessage message)
        {
            var captured=message;bool system=message.Channel==ChatChannel.System;
            var row=CommunicationUI.Panel("Message",content,Vector2.zero,Vector2.one,CommunicationUI.ChannelColor(message.Channel));
            var layout=row.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(system?14:62,12,6,8);layout.spacing=2;layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;
            row.gameObject.AddComponent<LayoutElement>().minHeight=system?34:56;
            if(!system){var button=row.gameObject.AddComponent<Button>();button.transition=Selectable.Transition.ColorTint;button.targetGraphic=row;var colors=button.colors;colors.highlightedColor=new Color(1f,.96f,.85f);colors.selectedColor=colors.normalColor;button.colors=colors;button.onClick.AddListener(()=>reports.Open(captured.Account,captured.Name,captured.Id));row.gameObject.AddComponent<ChatMessagePointer>();}
            if(!system){var header=CommunicationUI.Label(message.Name+"   "+message.Time+(message.Channel==ChatChannel.Township?"":"   · "+message.Channel.ToString().ToUpperInvariant()),row.transform,Vector2.zero,Vector2.one,15);header.fontStyle=FontStyle.Bold;header.verticalOverflow=VerticalWrapMode.Overflow;}
            var text=CommunicationUI.Label(message.Text,row.transform,Vector2.zero,Vector2.one,system?15:18);text.verticalOverflow=VerticalWrapMode.Overflow;if(system){text.color=PixelUiTheme.MutedInk;text.fontStyle=FontStyle.Italic;text.alignment=TextAnchor.MiddleCenter;}
            if(!system)AddHeadshot(row.transform,message);
        }

        void AddHeadshot(Transform row,ChatMessage message)
        {
            // A headshot is decorative; the adjacent pseudonym is the accessible identity.
            var head=CommunicationUI.Panel("Avatar headshot",row,new Vector2(0,1),new Vector2(0,1),new Color(.32f,.53f,.45f));head.raycastTarget=false;head.sprite=PixelUiTheme.GetDotSprite();
            head.rectTransform.pivot=new Vector2(0,1);head.rectTransform.sizeDelta=new Vector2(42,42);head.rectTransform.anchoredPosition=new Vector2(10,-7);head.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
            var player=GameManager.Instance!=null?GameManager.Instance.GetPlayer(message.ClientId):null;var avatar=player!=null?player.GetComponent<PlayerAvatarLayerManager>():null;
            if(avatar==null&&!manager.Online&&CronyRuntime.CommunicationPlayers!=null){foreach(var local in CronyRuntime.CommunicationPlayers)if((ulong)local.id==message.ClientId){avatar=local.avatarLayers;break;}}
            if(avatar!=null&&avatar.LayerRenderers!=null)
            {
                head.color=PixelUiTheme.Linen;
                foreach(var layer in avatar.LayerRenderers)
                {
                    if(layer==null||layer.sprite==null||layer.name.Contains("Shadow"))continue;
                    var image=CommunicationUI.Panel("Portrait layer",head.transform,Vector2.zero,Vector2.one,layer.color);image.type=Image.Type.Simple;image.raycastTarget=false;image.preserveAspect=true;
                    if(!portraits.TryGetValue(layer.sprite,out var portrait)){var rect=layer.sprite.rect;rect.y+=rect.height*.5f;rect.height*=.5f;portrait=Sprite.Create(layer.sprite.texture,rect,new Vector2(.5f,.5f),layer.sprite.pixelsPerUnit);portraits[layer.sprite]=portrait;}
                    image.sprite=portrait;
                }
            }
            else{var initial=CommunicationUI.Label(message.Name.Length>0?message.Name.Substring(0,1).ToUpperInvariant():"?",head.transform,Vector2.zero,Vector2.one,20);initial.alignment=TextAnchor.MiddleCenter;initial.color=Color.white;initial.fontStyle=FontStyle.Bold;}
        }

        void ShowPlayers()
        {
            for(int i=playersContent.childCount-1;i>=0;i--){var child=playersContent.GetChild(i).gameObject;child.SetActive(false);Destroy(child);}
            speakingLabels.Clear();int rows=0;
            var network=NetworkManager.Singleton;var game=GameManager.Instance;
            if(manager.Online&&game!=null&&network!=null){foreach(ulong id in network.ConnectedClientsIds){var player=game.GetPlayer(id);if(player!=null){AddPlayerRow(id,manager.AccountFor(id),player.DisplayName.Value.ToString(),id==network.LocalClientId);rows++;}}}
            else if(CronyRuntime.CommunicationPlayers!=null){foreach(var player in CronyRuntime.CommunicationPlayers){AddPlayerRow((ulong)player.id,"practice:"+player.id,player.name,player.id==0);rows++;}}
            if(rows==0){var empty=CommunicationUI.Label("Players appear here when you join a match or lobby.",playersContent,Vector2.zero,Vector2.one,17);empty.alignment=TextAnchor.MiddleCenter;empty.color=PixelUiTheme.MutedInk;empty.gameObject.AddComponent<LayoutElement>().minHeight=80;}
        }

        void AddPlayerRow(ulong client,string account,string name,bool self)
        {
            var row=CommunicationUI.Panel("Player "+name,playersContent,Vector2.zero,Vector2.one,self?PixelUiTheme.Highlight:PixelUiTheme.Cream);row.gameObject.AddComponent<LayoutElement>().minHeight=58;
            var label=CommunicationUI.Label(self?name+"  (you)":name,row.transform,new Vector2(.03f,.45f),new Vector2(.42f,.95f),17);label.fontStyle=FontStyle.Bold;
            var speaking=CommunicationUI.Label("",row.transform,new Vector2(.03f,.05f),new Vector2(.42f,.5f),14);speaking.color=PixelUiTheme.MutedInk;speakingLabels[client]=speaking;
            if(self)return;
            Button mute=null,block=null;
            mute=CommunicationUI.Button(CommunicationSafety.IsMuted(account)?"Unmute":"Mute",row.transform,new Vector2(.44f,.15f),new Vector2(.6f,.85f),()=>{CommunicationSafety.SetMuted(account,!CommunicationSafety.IsMuted(account));notice.text=CommunicationSafety.IsMuted(account)?name+" muted.":name+" unmuted.";mute.GetComponentInChildren<Text>().text=CommunicationSafety.IsMuted(account)?"Unmute":"Mute";},PixelUiTheme.Sky,15);
            block=CommunicationUI.Button(CommunicationSafety.IsBlocked(account)?"Unblock":"Block",row.transform,new Vector2(.62f,.15f),new Vector2(.79f,.85f),()=>{CommunicationSafety.SetBlocked(account,!CommunicationSafety.IsBlocked(account));notice.text=CommunicationSafety.IsBlocked(account)?name+" blocked.":name+" unblocked.";block.GetComponentInChildren<Text>().text=CommunicationSafety.IsBlocked(account)?"Unblock":"Block";mute.GetComponentInChildren<Text>().text=CommunicationSafety.IsMuted(account)?"Unmute":"Mute";},PixelUiTheme.Coral,15);
            CommunicationUI.Button("Report",row.transform,new Vector2(.81f,.15f),new Vector2(.98f,.85f),()=>reports.Open(account,name),PixelUiTheme.Linen,15);
        }

        void RefreshSpeaking()
        {
            var voice=TownshipVoiceManager.Instance;
            foreach(var pair in speakingLabels){if(pair.Value==null)continue;bool talking=voice!=null&&voice.IsSpeaking(pair.Key);pair.Value.text=talking?"● speaking":voice!=null&&voice.EnabledByUser?"voice connected":"";pair.Value.color=talking?new Color(.2f,.6f,.3f):PixelUiTheme.MutedInk;}
        }

        void OnMicClicked()
        {
            var voice=TownshipVoiceManager.Instance;
            if(voice==null){Open();notice.text="Voice chat is not available in this build. Text chat still works.";return;}
            if(!TownshipVoiceManager.Supported){Open();notice.text=TownshipVoiceManager.UnsupportedMessage;return;}
            if(voice.EnabledByUser){voiceMenu.SetActive(!voiceMenu.activeSelf);RefreshMic();return;}
            if(!voice.Consent){consent.SetActive(true);consent.transform.SetAsLastSibling();consent.GetComponentInChildren<Button>().Select();return;}
            voice.SetEnabled(true);
        }

        void RefreshMic()
        {
            if(micButton==null)return;var voice=TownshipVoiceManager.Instance;
            string status=voice!=null?voice.Status:"Voice off";Color face=PixelUiTheme.Almond;
            if(voice!=null&&voice.Transmitting)face=PixelUiTheme.Mint;
            else if(voice!=null&&voice.EnabledByUser&&voice.Connecting)face=PixelUiTheme.Gold;
            else if(voice!=null&&voice.EnabledByUser)face=PixelUiTheme.Sky;
            else if(status.Contains("unavailable")||status.Contains("failed"))face=PixelUiTheme.Coral;
            CommunicationUI.SetFace(micButton,face);
            micLabel.text=!TownshipVoiceManager.Supported?"VOICE · DOWNLOAD VERSION":status.StartsWith("Voice off")?"VOICE OFF · CLICK TO JOIN":status.ToUpperInvariant();
            if(voice!=null)voiceModeLabel.text=voice.ToggleToTalk?"Mode: press V to talk / mute":"Mode: hold V to talk";
            if(voice==null||!voice.EnabledByUser)voiceMenu.SetActive(false);
        }

        public void SetVoiceStatus(string text)=>RefreshMic();
        public void OpenPlayer(ulong client,string account,string name)=>reports.Open(account,name);
    }
}
