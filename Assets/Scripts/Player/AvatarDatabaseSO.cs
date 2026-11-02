using System;
using System.Collections.Generic;
using UnityEngine;

namespace Crony
{
    [CreateAssetMenu(fileName = "AvatarDatabase", menuName = "Crony/Avatar Database")]
    public sealed class AvatarDatabaseSO : ScriptableObject
    {
        [SerializeField] List<AvatarItemSO> items = new List<AvatarItemSO>();
        [NonSerialized] Dictionary<AvatarItemCategory, List<AvatarItemSO>> categoryCache;

        public IReadOnlyList<AvatarItemSO> Items => items;

        public void Configure(IList<AvatarItemSO> catalogItems)
        {
            items = catalogItems != null ? new List<AvatarItemSO>(catalogItems) : new List<AvatarItemSO>();
            RebuildCache();
        }

        void OnEnable() => RebuildCache();
        void OnValidate() => RebuildCache();

        public IReadOnlyList<AvatarItemSO> GetItems(AvatarItemCategory category)
        {
            EnsureCache();
            return categoryCache.TryGetValue(category, out List<AvatarItemSO> result) ? result : Array.Empty<AvatarItemSO>();
        }

        public AvatarItemSO GetItem(AvatarItemCategory category, int index)
        {
            IReadOnlyList<AvatarItemSO> categoryItems = GetItems(category);
            if (categoryItems.Count == 0) return null;
            return categoryItems[Mathf.Clamp(index, 0, categoryItems.Count - 1)];
        }

        public int GetIndex(AvatarItemCategory category, string itemID)
        {
            IReadOnlyList<AvatarItemSO> categoryItems = GetItems(category);
            for (int i = 0; i < categoryItems.Count; i++)
                if (categoryItems[i] != null && string.Equals(categoryItems[i].ItemID, itemID, StringComparison.Ordinal)) return i;
            return -1;
        }

        public AvatarLookState CreateDefaultState()
        {
            AvatarLookState state = AvatarLookState.CreateDefault();
            for (int i = 0; i < AvatarLookState.CategoryCount; i++)
            {
                AvatarItemCategory category = (AvatarItemCategory)i;
                AvatarItemSO item = GetItem(category, 0);
                state.SetIndex(category, 0);
                if (item == null || !item.SupportColorTint) continue;
                state.SetTint(category, item.DefaultColor);
            }
            return state;
        }

        public AvatarLookState ResolveSavedState(AvatarLookSaveData save)
        {
            AvatarLookState state = CreateDefaultState();
            if (save == null) return state;
            ResolveIndex(ref state, AvatarItemCategory.Skin, save.skinID);
            ResolveIndex(ref state, AvatarItemCategory.Face, save.faceID);
            ResolveIndex(ref state, AvatarItemCategory.Tattoo, save.tattooID);
            ResolveIndex(ref state, AvatarItemCategory.Hair, save.hairID);
            ResolveIndex(ref state, AvatarItemCategory.Hat, save.hatID);
            ResolveIndex(ref state, AvatarItemCategory.Top, save.topID);
            ResolveIndex(ref state, AvatarItemCategory.Bottom, save.bottomID);
            ResolveIndex(ref state, AvatarItemCategory.Shoes, save.shoesID);
            ResolveIndex(ref state, AvatarItemCategory.Accessory, save.accessoryID);
            if (save.hasTints)
            {
                state.SetTint(AvatarItemCategory.Skin, save.skinTint);
                state.SetTint(AvatarItemCategory.Hair, save.hairTint);
                state.SetTint(AvatarItemCategory.Top, save.clothingTint);
                if (save.hatTint.a > 0) state.SetTint(AvatarItemCategory.Hat, save.hatTint);
                if (save.bottomTint.a > 0) state.SetTint(AvatarItemCategory.Bottom, save.bottomTint);
                if (save.shoesTint.a > 0) state.SetTint(AvatarItemCategory.Shoes, save.shoesTint);
            }
            return Sanitize(state);
        }

        public AvatarLookSaveData CreateSaveData(AvatarLookState state)
        {
            return new AvatarLookSaveData
            {
                version = AvatarLookSaveData.CurrentSchemaVersion,
                skinID = GetItem(AvatarItemCategory.Skin, state.GetIndex(AvatarItemCategory.Skin))?.ItemID,
                faceID = GetItem(AvatarItemCategory.Face, state.GetIndex(AvatarItemCategory.Face))?.ItemID,
                tattooID = GetItem(AvatarItemCategory.Tattoo, state.GetIndex(AvatarItemCategory.Tattoo))?.ItemID,
                hairID = GetItem(AvatarItemCategory.Hair, state.GetIndex(AvatarItemCategory.Hair))?.ItemID,
                hatID = GetItem(AvatarItemCategory.Hat, state.GetIndex(AvatarItemCategory.Hat))?.ItemID,
                topID = GetItem(AvatarItemCategory.Top, state.GetIndex(AvatarItemCategory.Top))?.ItemID,
                bottomID = GetItem(AvatarItemCategory.Bottom, state.GetIndex(AvatarItemCategory.Bottom))?.ItemID,
                shoesID = GetItem(AvatarItemCategory.Shoes, state.GetIndex(AvatarItemCategory.Shoes))?.ItemID,
                accessoryID = GetItem(AvatarItemCategory.Accessory, state.GetIndex(AvatarItemCategory.Accessory))?.ItemID,
                skinTint = state.SkinTint,
                hairTint = state.HairTint,
                hatTint = state.HatTint,
                clothingTint = state.ClothingTint,
                bottomTint = state.BottomTint,
                shoesTint = state.ShoesTint,
                hasTints = true
            };
        }

        public AvatarLookState Sanitize(AvatarLookState state)
        {
            for (int i = 0; i < AvatarLookState.CategoryCount; i++)
            {
                AvatarItemCategory category = (AvatarItemCategory)i;
                int count = GetItems(category).Count;
                state.SetIndex(category, count == 0 ? 0 : Mathf.Clamp(state.GetIndex(category), 0, count - 1));
            }
            state.SkinTint = ClampTint(state.SkinTint);
            state.HairTint = ClampTint(state.HairTint);
            state.HatTint = ClampTint(state.HatTint);
            state.ClothingTint = ClampTint(state.ClothingTint);
            state.BottomTint = ClampTint(state.BottomTint);
            state.ShoesTint = ClampTint(state.ShoesTint);
            return state;
        }

        static Color32 ClampTint(Color32 color)
        {
            color.a = 255;
            return color;
        }

        void ResolveIndex(ref AvatarLookState state, AvatarItemCategory category, string id)
        {
            int index = string.IsNullOrEmpty(id) ? -1 : GetIndex(category, id);
            if (index >= 0) state.SetIndex(category, index);
        }

        void EnsureCache()
        {
            if (categoryCache == null) RebuildCache();
        }

        void RebuildCache()
        {
            categoryCache = new Dictionary<AvatarItemCategory, List<AvatarItemSO>>();
            if (items == null) items = new List<AvatarItemSO>();
            for (int i = 0; i < AvatarLookState.CategoryCount; i++)
                categoryCache[(AvatarItemCategory)i] = new List<AvatarItemSO>();
            foreach (AvatarItemSO item in items)
                if (item != null && categoryCache.TryGetValue(item.Category, out List<AvatarItemSO> categoryItems))
                    categoryItems.Add(item);
            foreach (List<AvatarItemSO> categoryItems in categoryCache.Values)
                categoryItems.Sort((left, right) => string.Compare(left.ItemID, right.ItemID, StringComparison.Ordinal));
        }
    }

    [Serializable]
    public sealed class AvatarLookSaveData
    {
        public const int CurrentSchemaVersion = 1;
        public int version;
        public string skinID;
        public string faceID;
        public string tattooID;
        public string hairID;
        public string hatID;
        public string topID;
        public string bottomID;
        public string shoesID;
        public string accessoryID;
        public Color32 skinTint;
        public Color32 hairTint;
        public Color32 hatTint;
        public Color32 clothingTint;
        public Color32 bottomTint;
        public Color32 shoesTint;
        public bool hasTints;
    }
}
