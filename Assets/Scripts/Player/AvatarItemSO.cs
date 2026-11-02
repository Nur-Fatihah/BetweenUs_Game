using UnityEngine;

namespace Crony
{
    public enum AvatarItemCategory : byte
    {
        Skin,
        Face,
        Tattoo,
        Hair,
        Hat,
        Top,
        Bottom,
        Shoes,
        Accessory
    }

    [CreateAssetMenu(fileName = "AvatarItem", menuName = "Crony/Avatar Item")]
    public sealed class AvatarItemSO : ScriptableObject
    {
        [SerializeField] string itemID;
        [SerializeField] string displayName;
        [SerializeField] AvatarItemCategory category;
        [SerializeField] Sprite sprite;
        [SerializeField] bool supportColorTint;
        [SerializeField] Color defaultColor = Color.white;

        public string ItemID => itemID;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? itemID : displayName;
        public AvatarItemCategory Category => category;
        public Sprite Sprite => PixelArtAssetGenerator.GetRuntimeLayerSprite(category, itemID);
        public Sprite PreviewIconSprite => PixelArtAssetGenerator.GetCardPreviewSprite(category, itemID);
        public bool SupportColorTint => supportColorTint;
        public Color DefaultColor => defaultColor;

        public void Configure(string id, string title, AvatarItemCategory itemCategory, Sprite itemSprite, bool tintable, Color tint)
        {
            itemID = id;
            displayName = title;
            category = itemCategory;
            sprite = itemSprite;
            supportColorTint = tintable;
            defaultColor = tint;
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemID)) itemID = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = itemID;
        }
    }
}
