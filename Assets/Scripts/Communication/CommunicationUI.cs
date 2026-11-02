using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
namespace Crony
{
    /// <summary>Pixel-themed uGUI builders shared by chat, voice and safety windows.</summary>
    public static class CommunicationUI
    {
        public static readonly Color Ink=PixelUiTheme.Ink, Paper=PixelUiTheme.Cream;
        public static readonly Color Township=new Color(.86f,.93f,.88f), Meeting=new Color(1f,.92f,.7f), Ghost=new Color(.88f,.85f,.96f), Lobby=new Color(.87f,.91f,.97f), System=new Color(.93f,.93f,.9f);
        public static Color ChannelColor(ChatChannel channel)=>channel==ChatChannel.Ghost?Ghost:channel==ChatChannel.Meeting?Meeting:channel==ChatChannel.Township?Township:channel==ChatChannel.Lobby?Lobby:System;
        public static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max)
        {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;return r;}
        public static Image Panel(string name,Transform parent,Vector2 min,Vector2 max,Color color)
        {var r=Rect(name,parent,min,max);var image=r.gameObject.AddComponent<Image>();image.color=color;image.sprite=PixelUiTheme.GetRoundSprite();image.type=Image.Type.Sliced;return image;}
        public static Image Card(string name,Transform parent,Vector2 min,Vector2 max)
        {var image=Panel(name,parent,min,max,Color.white);PixelUiTheme.ApplyCard(image.gameObject);image.raycastTarget=true;return image;}
        public static Text Label(string name,Transform parent,Vector2 min,Vector2 max,int size=20)
        {var r=Rect(name,parent,min,max);var text=r.gameObject.AddComponent<Text>();text.font=PixelUiTheme.BodyFont;text.text=name;text.fontSize=PlayerPrefs.GetInt("Crony.LargeText",0)==1?Mathf.RoundToInt(size*1.12f):size;text.color=Ink;text.alignment=TextAnchor.MiddleLeft;text.supportRichText=false;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;}
        public static Button Button(string name,Transform parent,Vector2 min,Vector2 max,UnityAction action)=>Button(name,parent,min,max,action,PixelUiTheme.Almond);
        public static Button Button(string name,Transform parent,Vector2 min,Vector2 max,UnityAction action,Color face,int size=17)
        {
            var image=Panel(name,parent,min,max,face);var b=image.gameObject.AddComponent<Button>();PixelUiTheme.StyleButton(b,face);
            var label=Label(name,b.transform,new Vector2(.04f,0),new Vector2(.96f,1),size);label.alignment=TextAnchor.MiddleCenter;label.fontStyle=FontStyle.Bold;label.color=PixelUiTheme.ButtonTextColor(face);
            if(action!=null)b.onClick.AddListener(()=>{GameAudioManager.PlayUiClick();action();});return b;
        }
        public static void SetFace(Button button,Color face)
        {if(button==null)return;PixelUiTheme.SetButtonFace(button,face);var label=button.GetComponentInChildren<Text>();if(label!=null)label.color=PixelUiTheme.ButtonTextColor(face);}

        /// <summary>A framed pop-out window with a draggable title bar and close button. Returns the body rect.</summary>
        public static RectTransform Window(string title,Transform parent,Vector2 size,Vector2 anchor,Vector2 position,UnityAction close,out GameObject root,out Text titleLabel)
        {
            var r=Rect(title+" Window",parent,anchor,anchor);r.pivot=anchor;r.sizeDelta=size;r.anchoredPosition=position;root=r.gameObject;
            var frame=r.gameObject.AddComponent<Image>();PixelUiTheme.ApplyWindow(r.gameObject,false);frame.raycastTarget=true;
            var bar=Panel("Title bar",r,new Vector2(0,1),Vector2.one,PixelUiTheme.Chocolate);bar.rectTransform.offsetMin=new Vector2(6,-50);bar.rectTransform.offsetMax=new Vector2(-6,-6);bar.raycastTarget=true;
            bar.gameObject.AddComponent<DraggableWindow>().Bind(r);
            titleLabel=Label(title,bar.transform,new Vector2(.03f,0),new Vector2(.8f,1),20);titleLabel.color=PixelUiTheme.Cream;titleLabel.fontStyle=FontStyle.Bold;
            if(close!=null){var x=Button("X",bar.transform,new Vector2(1,.5f),new Vector2(1,.5f),close,PixelUiTheme.Coral,18);var xr=(RectTransform)x.transform;xr.pivot=new Vector2(1,.5f);xr.sizeDelta=new Vector2(38,34);xr.anchoredPosition=new Vector2(-6,0);}
            var body=Rect("Body",r,Vector2.zero,Vector2.one);body.offsetMin=new Vector2(14,14);body.offsetMax=new Vector2(-14,-58);return body;
        }
        public static Image Shade(Transform parent,float alpha=.55f)
        {var shade=Panel("Modal shade",parent,Vector2.zero,Vector2.one,new Color(.02f,.07f,.06f,alpha));shade.sprite=null;shade.raycastTarget=true;return shade;}
    }

    /// <summary>Drags a window by its title bar and keeps it on screen.</summary>
    public sealed class DraggableWindow : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        RectTransform window; Canvas canvas;
        public void Bind(RectTransform target){window=target;}
        public void OnBeginDrag(PointerEventData e){canvas=GetComponentInParent<Canvas>();window.SetAsLastSibling();}
        public void OnDrag(PointerEventData e)
        {
            if(window==null)return;float scale=canvas!=null?canvas.scaleFactor:1f;window.anchoredPosition+=e.delta/scale;
            var parent=window.parent as RectTransform;if(parent==null)return;
            Vector3[] corners=new Vector3[4];window.GetWorldCorners(corners);var bounds=parent.rect;Vector2 shift=Vector2.zero;
            Vector2 min=parent.InverseTransformPoint(corners[0]),max=parent.InverseTransformPoint(corners[2]);
            if(min.x<bounds.xMin)shift.x=bounds.xMin-min.x;else if(max.x>bounds.xMax)shift.x=bounds.xMax-max.x;
            if(min.y<bounds.yMin)shift.y=bounds.yMin-min.y;else if(max.y>bounds.yMax)shift.y=bounds.yMax-max.y;
            window.anchoredPosition+=shift;
        }
    }
}
