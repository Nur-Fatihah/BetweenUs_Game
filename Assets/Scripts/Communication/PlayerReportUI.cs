using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace Crony
{
    public sealed class PlayerReportUI : MonoBehaviour
    {
        public bool IsOpen => panel != null && panel.activeSelf;
        GameObject panel; Text title,status,targetLabel; Button muteButton,blockButton; string account,message,playerName; ReportTarget target; float lastReport=-30; Button first;
        // One report per subject and reason per match, so repeated clicks or pile-ons don't inflate volume.
        readonly HashSet<string> sent=new HashSet<string>();
        static readonly PlayerReportCategory[] Order={PlayerReportCategory.HarassmentBullying,PlayerReportCategory.HateSpeech,PlayerReportCategory.SexualHarassment,PlayerReportCategory.ThreatsDoxxing,PlayerReportCategory.Extremism,PlayerReportCategory.SelfHarmEncouragement,PlayerReportCategory.SpamImpersonation,PlayerReportCategory.CheatingExploiting,PlayerReportCategory.OffensiveNameAvatar,PlayerReportCategory.PlayerInDistress};
        static readonly string[] Labels={"Bullying / insults","Identity-based abuse","Sexual harassment","Threats / doxxing","Extremism","Encouraging self-harm","Spam / impersonation","Cheating","Offensive name / avatar","May be in distress"};
        public void Build(Transform parent)
        {
            panel=CommunicationUI.Shade(parent,.65f).gameObject;
            var body=CommunicationUI.Window("Player safety",panel.transform,new Vector2(660,680),new Vector2(.5f,.5f),Vector2.zero,Close,out _,out title);
            CommunicationUI.Label("Mute or block takes effect for you immediately.",body,new Vector2(0,.92f),new Vector2(1,1),17);
            muteButton=first=CommunicationUI.Button("Mute voice",body,new Vector2(0,.81f),new Vector2(.49f,.91f),()=>{CommunicationSafety.SetMuted(account,!CommunicationSafety.IsMuted(account));status.text=CommunicationSafety.IsMuted(account)?"Voice muted for you.":"Voice unmuted.";RefreshButtons();},PixelUiTheme.Sky);
            blockButton=CommunicationUI.Button("Block player",body,new Vector2(.51f,.81f),new Vector2(1,.91f),()=>{CommunicationSafety.SetBlocked(account,!CommunicationSafety.IsBlocked(account));status.text=CommunicationSafety.IsBlocked(account)?"Blocked: their messages are hidden and voice is muted.":"Player unblocked.";RefreshButtons();},PixelUiTheme.Coral);
            targetLabel=CommunicationUI.Button("Reporting: player",body,new Vector2(0,.69f),new Vector2(1,.78f),CycleTarget,PixelUiTheme.Linen,16).GetComponentInChildren<Text>();
            for(int i=0;i<Order.Length;i++){var category=Order[i];float x=i%2*.51f,y=.585f-i/2*.105f;CommunicationUI.Button(Labels[i],body,new Vector2(x,y),new Vector2(x+.49f,y+.09f),()=>Submit(category),category==PlayerReportCategory.PlayerInDistress?PixelUiTheme.Mint:PixelUiTheme.Almond,15);}
            status=CommunicationUI.Label("Reports never punish anyone automatically. Your name is not shown to the player.",body,new Vector2(0,0),new Vector2(1,.1f),15);status.color=PixelUiTheme.MutedInk;
            panel.SetActive(false);
        }
        void CycleTarget()
        {
            do target=(ReportTarget)(((int)target+1)%4); while(target==ReportTarget.Message&&string.IsNullOrEmpty(message));
            RefreshTarget();
        }
        void RefreshTarget()=>targetLabel.text="Reporting: "+(target==ReportTarget.Message?"this message":target==ReportTarget.Voice?"a voice incident":target==ReportTarget.Match?"the whole match":"the player")+"  (tap to change)";
        void RefreshButtons()
        {
            muteButton.GetComponentInChildren<Text>().text=CommunicationSafety.IsMuted(account)?"Unmute voice":"Mute voice";
            blockButton.GetComponentInChildren<Text>().text=CommunicationSafety.IsBlocked(account)?"Unblock player":"Block player";
        }
        public void Open(string id,string name,string messageId="") {account=id;playerName=name;message=messageId;target=string.IsNullOrEmpty(messageId)?ReportTarget.Player:ReportTarget.Message;title.text="Player safety · "+name;status.text="Choose what you're reporting and a reason, or mute / block right away.";RefreshButtons();RefreshTarget();panel.SetActive(true);panel.transform.SetAsLastSibling();first.Select();}
        public Transform FocusRoot => panel != null ? panel.transform : null;
        public void FocusFirst() => first.Select();
        public void Close(){panel.SetActive(false);TownshipTextChatUI.Instance?.RestoreFocus();}
        async void Submit(PlayerReportCategory category)
        {
            var chat=TextChatManager.Instance;
            string subject=(target==ReportTarget.Match?"match":account)+"|"+target+"|"+(target==ReportTarget.Message?message:"")+"|"+category+"|"+chat.SessionId;
            if(sent.Contains(subject)){status.text="You already reported this. Moderators review each report once.";return;}
            if(Time.unscaledTime-lastReport<10){status.text="Please wait a few seconds before sending another report.";return;}
            lastReport=Time.unscaledTime;sent.Add(subject);status.text="Sending report…";
            // Limited evidence: only the reported message itself, never surrounding chat or the reporter's identity.
            var quoted=target==ReportTarget.Message?chat.History.Find(m=>m.Id==message):null;
            var report=new PlayerReport{id=Guid.NewGuid().ToString("N"),matchId=chat.SessionId,targetAccount=target==ReportTarget.Match?"":account,messageId=target==ReportTarget.Message?message:"",
                messageTime=quoted?.Time??"",evidenceText=quoted?.Text??"",createdUtc=DateTime.UtcNow.ToString("O"),category=category,target=target};
            string care=category==PlayerReportCategory.PlayerInDistress?" Thank you for looking out for "+playerName+". If they may be in danger now, contact local emergency services.":"";
            if(CommunicationSafety.Backend==null){try{CommunicationSafety.SavePendingReport(report);status.text="Report saved on this device. It will need a moderation service to reach moderators."+care;}catch(Exception){status.text="Could not save the report. You can still mute or block.";}return;}
            try{bool accepted=await CommunicationSafety.Backend.SubmitReportAsync(report);if(this!=null)status.text=(accepted?"Report received. Your identity is not shown to this player.":"Report could not be sent. Please try again.")+care;if(!accepted)sent.Remove(subject);}catch(Exception){if(this!=null)status.text="Report service unavailable. Please try again.";sent.Remove(subject);}
        }
    }
}
