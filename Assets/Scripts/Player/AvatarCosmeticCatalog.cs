using System;
using UnityEngine;

namespace Crony
{
    [Serializable]
    public struct AvatarCosmeticOption
    {
        public string displayName;
        public Sprite sprite;
        public Color tint;
    }

    [CreateAssetMenu(fileName = "AvatarCosmeticCatalog", menuName = "Crony/Avatar Cosmetic Catalog")]
    public sealed class AvatarCosmeticCatalog : ScriptableObject
    {
        [Header("Headwear: hair, hijab, and songkok")]
        public AvatarCosmeticOption[] headwear;
        [Header("Outfits: Baju Melayu, Cheongsam, Saree, and casual wear")]
        public AvatarCosmeticOption[] outfits;
        [Header("Accessories")]
        public AvatarCosmeticOption[] accessories;
        [Header("Skin tones")]
        public Color[] skinTones;
    }
}
