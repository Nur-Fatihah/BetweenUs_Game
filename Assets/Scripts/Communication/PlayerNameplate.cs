using UnityEngine;
namespace Crony
{
    /// <summary>Name and microphone status stay above every avatar layer.</summary>
    public sealed class PlayerNameplate : MonoBehaviour
    {
        TextMesh label, mic;
        SpriteRenderer[] sprites;
        string cachedName;
        Unity.Collections.FixedString64Bytes lastNetworkName;
        PlayerController player;
        public void Bind(string name,TextMesh existing=null)
        {
            sprites=GetComponentsInChildren<SpriteRenderer>();player=GetComponent<PlayerController>();
            label=existing!=null?existing:Make("Player name");label.text=name;cachedName=name;
            label.font=PixelUiTheme.BodyFont;label.fontSize=48;label.characterSize=.035f;
            label.anchor=TextAnchor.LowerCenter;label.color=Color.white;label.richText=false;
            var renderer=label.GetComponent<MeshRenderer>();renderer.sharedMaterial=label.font.material;renderer.sortingLayerName="Avatar";renderer.sortingOrder=30000;
            mic=Make("Speaking indicator");mic.text="[MIC]";mic.color=new Color(.65f,1f,.75f);mic.gameObject.SetActive(false);
        }
        TextMesh Make(string title){var go=new GameObject(title);go.transform.SetParent(transform,false);var t=go.AddComponent<TextMesh>();t.font=PixelUiTheme.BodyFont;t.fontSize=40;t.characterSize=.025f;t.anchor=TextAnchor.LowerCenter;var r=t.GetComponent<MeshRenderer>();r.sharedMaterial=t.font.material;r.sortingLayerName="Avatar";r.sortingOrder=30001;return t;}
        void LateUpdate()
        {
            if(label==null)return;float top=transform.position.y+.8f;
            for(int i=0;i<sprites.Length;i++)if(sprites[i]!=null&&sprites[i].enabled&&sprites[i].sprite!=null)top=Mathf.Max(top,sprites[i].bounds.max.y);
            label.transform.position=new Vector3(transform.position.x,top+.14f,-1f);
            if(player!=null&&player.IsSpawned){var name=player.DisplayName.Value;if(!name.Equals(lastNetworkName)){lastNetworkName=name;cachedName=name.ToString();label.text=cachedName;}}
            mic.transform.position=label.transform.position+new Vector3(0,.32f,0);
            bool speaking=player!=null&&TownshipVoiceManager.Instance!=null&&TownshipVoiceManager.Instance.IsSpeaking(player.OwnerClientId);
            if(mic.gameObject.activeSelf!=speaking)mic.gameObject.SetActive(speaking);
        }
    }
}
