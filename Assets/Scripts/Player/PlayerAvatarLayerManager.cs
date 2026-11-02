using System;
using Unity.Netcode;
using UnityEngine;

namespace Crony
{
    [Serializable]
    public struct AvatarLookState : INetworkSerializable, IEquatable<AvatarLookState>
    {
        public const int CategoryCount = 9;

        public ushort skinIndex;
        public ushort faceIndex;
        public ushort tattooIndex;
        public ushort hairIndex;
        public ushort hatIndex;
        public ushort topIndex;
        public ushort bottomIndex;
        public ushort shoesIndex;
        public ushort accessoryIndex;
        public byte skinR, skinG, skinB, skinA;
        public byte hairR, hairG, hairB, hairA;
        public byte hatR, hatG, hatB, hatA;
        public byte clothingR, clothingG, clothingB, clothingA;
        public byte bottomR, bottomG, bottomB, bottomA;
        public byte shoesR, shoesG, shoesB, shoesA;

        public static AvatarLookState CreateDefault()
        {
            AvatarLookState state = new AvatarLookState();
            state.SkinTint = new Color32(217, 162, 126, 255);
            state.HairTint = new Color32(58, 43, 39, 255);
            state.HatTint = new Color32(36, 42, 49, 255);
            state.ClothingTint = new Color32(224, 166, 78, 255);
            state.BottomTint = new Color32(80, 101, 139, 255);
            state.ShoesTint = new Color32(229, 229, 222, 255);
            return state;
        }

        public Color32 SkinTint
        {
            readonly get => new Color32(skinR, skinG, skinB, skinA == 0 ? (byte)255 : skinA);
            set { skinR = value.r; skinG = value.g; skinB = value.b; skinA = value.a; }
        }
        public Color32 HairTint
        {
            readonly get => new Color32(hairR, hairG, hairB, hairA == 0 ? (byte)255 : hairA);
            set { hairR = value.r; hairG = value.g; hairB = value.b; hairA = value.a; }
        }
        public Color32 HatTint
        {
            readonly get => hatA == 0 ? new Color32(78, 124, 121, 255) : new Color32(hatR, hatG, hatB, hatA);
            set { hatR = value.r; hatG = value.g; hatB = value.b; hatA = value.a; }
        }
        public Color32 ClothingTint
        {
            readonly get => new Color32(clothingR, clothingG, clothingB, clothingA == 0 ? (byte)255 : clothingA);
            set { clothingR = value.r; clothingG = value.g; clothingB = value.b; clothingA = value.a; }
        }
        public Color32 BottomTint
        {
            readonly get => bottomA == 0 ? new Color32(80, 101, 139, 255) : new Color32(bottomR, bottomG, bottomB, bottomA);
            set { bottomR = value.r; bottomG = value.g; bottomB = value.b; bottomA = value.a; }
        }
        public Color32 ShoesTint
        {
            readonly get => shoesA == 0 ? new Color32(229, 229, 222, 255) : new Color32(shoesR, shoesG, shoesB, shoesA);
            set { shoesR = value.r; shoesG = value.g; shoesB = value.b; shoesA = value.a; }
        }

        public readonly int GetIndex(AvatarItemCategory category)
        {
            switch (category)
            {
                case AvatarItemCategory.Skin: return skinIndex;
                case AvatarItemCategory.Face: return faceIndex;
                case AvatarItemCategory.Tattoo: return tattooIndex;
                case AvatarItemCategory.Hair: return hairIndex;
                case AvatarItemCategory.Hat: return hatIndex;
                case AvatarItemCategory.Top: return topIndex;
                case AvatarItemCategory.Bottom: return bottomIndex;
                case AvatarItemCategory.Shoes: return shoesIndex;
                case AvatarItemCategory.Accessory: return accessoryIndex;
                default: return 0;
            }
        }

        public void SetIndex(AvatarItemCategory category, int index)
        {
            ushort safeIndex = (ushort)Mathf.Clamp(index, 0, ushort.MaxValue);
            switch (category)
            {
                case AvatarItemCategory.Skin: skinIndex = safeIndex; break;
                case AvatarItemCategory.Face: faceIndex = safeIndex; break;
                case AvatarItemCategory.Tattoo: tattooIndex = safeIndex; break;
                case AvatarItemCategory.Hair: hairIndex = safeIndex; break;
                case AvatarItemCategory.Hat: hatIndex = safeIndex; break;
                case AvatarItemCategory.Top: topIndex = safeIndex; break;
                case AvatarItemCategory.Bottom: bottomIndex = safeIndex; break;
                case AvatarItemCategory.Shoes: shoesIndex = safeIndex; break;
                case AvatarItemCategory.Accessory: accessoryIndex = safeIndex; break;
            }
        }

        public readonly Color32 GetTint(AvatarItemCategory category)
        {
            if (category == AvatarItemCategory.Skin) return SkinTint;
            if (category == AvatarItemCategory.Hair) return HairTint;
            if (category == AvatarItemCategory.Hat) return HatTint;
            if (category == AvatarItemCategory.Top) return ClothingTint;
            if (category == AvatarItemCategory.Bottom) return BottomTint;
            if (category == AvatarItemCategory.Shoes) return ShoesTint;
            return Color.white;
        }

        public void SetTint(AvatarItemCategory category, Color color) => SetTint(category, (Color32)color);

        public void SetTint(AvatarItemCategory category, Color32 color)
        {
            if (category == AvatarItemCategory.Skin) SkinTint = color;
            else if (category == AvatarItemCategory.Hair) HairTint = color;
            else if (category == AvatarItemCategory.Hat) HatTint = color;
            else if (category == AvatarItemCategory.Top) ClothingTint = color;
            else if (category == AvatarItemCategory.Bottom) BottomTint = color;
            else if (category == AvatarItemCategory.Shoes) ShoesTint = color;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref skinIndex);
            serializer.SerializeValue(ref faceIndex);
            serializer.SerializeValue(ref tattooIndex);
            serializer.SerializeValue(ref hairIndex);
            serializer.SerializeValue(ref hatIndex);
            serializer.SerializeValue(ref topIndex);
            serializer.SerializeValue(ref bottomIndex);
            serializer.SerializeValue(ref shoesIndex);
            serializer.SerializeValue(ref accessoryIndex);
            serializer.SerializeValue(ref skinR); serializer.SerializeValue(ref skinG); serializer.SerializeValue(ref skinB); serializer.SerializeValue(ref skinA);
            serializer.SerializeValue(ref hairR); serializer.SerializeValue(ref hairG); serializer.SerializeValue(ref hairB); serializer.SerializeValue(ref hairA);
            serializer.SerializeValue(ref hatR); serializer.SerializeValue(ref hatG); serializer.SerializeValue(ref hatB); serializer.SerializeValue(ref hatA);
            serializer.SerializeValue(ref clothingR); serializer.SerializeValue(ref clothingG); serializer.SerializeValue(ref clothingB); serializer.SerializeValue(ref clothingA);
            serializer.SerializeValue(ref bottomR); serializer.SerializeValue(ref bottomG); serializer.SerializeValue(ref bottomB); serializer.SerializeValue(ref bottomA);
            serializer.SerializeValue(ref shoesR); serializer.SerializeValue(ref shoesG); serializer.SerializeValue(ref shoesB); serializer.SerializeValue(ref shoesA);
        }

        public readonly bool Equals(AvatarLookState other)
        {
            return skinIndex == other.skinIndex && faceIndex == other.faceIndex && tattooIndex == other.tattooIndex &&
                hairIndex == other.hairIndex && hatIndex == other.hatIndex && topIndex == other.topIndex &&
                bottomIndex == other.bottomIndex && shoesIndex == other.shoesIndex && accessoryIndex == other.accessoryIndex &&
                skinR == other.skinR && skinG == other.skinG && skinB == other.skinB && skinA == other.skinA &&
                hairR == other.hairR && hairG == other.hairG && hairB == other.hairB && hairA == other.hairA &&
                hatR == other.hatR && hatG == other.hatG && hatB == other.hatB && hatA == other.hatA &&
                clothingR == other.clothingR && clothingG == other.clothingG && clothingB == other.clothingB && clothingA == other.clothingA &&
                bottomR == other.bottomR && bottomG == other.bottomG && bottomB == other.bottomB && bottomA == other.bottomA &&
                shoesR == other.shoesR && shoesG == other.shoesG && shoesB == other.shoesB && shoesA == other.shoesA;
        }

        public override readonly bool Equals(object obj) => obj is AvatarLookState other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(skinIndex, faceIndex, tattooIndex, hairIndex, hatIndex, topIndex, bottomIndex, shoesIndex);
    }

    /// <summary>Ten aligned SpriteRenderer layers and server-authoritative avatar selection for one network player.</summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerAvatarLayerManager : NetworkBehaviour
    {
        public const int LayerCount = 10;
        const string PreferencesKey = "Crony.AvatarLook.v1";
        static readonly string[] LegacyPreferenceKeys =
        {
            "Crony.Headwear", "Crony.Outfit", "Crony.Accessory", "Crony.SkinTone",
            "Crony.CustomSkin", "Crony.CustomOutfit", "Crony.CustomAccessory"
        };
        static readonly AvatarItemCategory[] CategoryByLayer =
        {
            AvatarItemCategory.Skin, AvatarItemCategory.Skin, AvatarItemCategory.Tattoo, AvatarItemCategory.Face,
            AvatarItemCategory.Bottom, AvatarItemCategory.Shoes, AvatarItemCategory.Top, AvatarItemCategory.Hair,
            AvatarItemCategory.Hat, AvatarItemCategory.Accessory
        };
        static readonly string[] LayerNames =
        {
            "Avatar Shadow", "Avatar Skin", "Avatar Tattoos", "Avatar Face", "Avatar Bottoms",
            "Avatar Footwear", "Avatar Top", "Avatar Hair", "Avatar Headwear", "Avatar Accessories"
        };

        [SerializeField] AvatarDatabaseSO database;
        [SerializeField] SpriteRenderer[] layerRenderers = Array.Empty<SpriteRenderer>();
        [SerializeField] Sprite shadowSprite;

        public readonly NetworkVariable<AvatarLookState> NetworkLook = new NetworkVariable<AvatarLookState>(AvatarLookState.CreateDefault());
        AvatarLookState localLook = AvatarLookState.CreateDefault();
        bool hasLocalLook;

        public AvatarDatabaseSO Database => database;
        public SpriteRenderer[] LayerRenderers => layerRenderers;
        public AvatarLookState CurrentState => IsSpawned ? NetworkLook.Value : (hasLocalLook ? localLook : DefaultState());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ValidatePreferencesAtStartup() => ValidatePreferenceSchema();

        void Awake()
        {
            EnsureLayerHierarchy();
            if (Application.isPlaying && GetComponent<AvatarSecondaryMotion>() == null) gameObject.AddComponent<AvatarSecondaryMotion>();
            if (database != null && !hasLocalLook)
            {
                localLook = DefaultState();
                hasLocalLook = true;
            }
            ApplyLook(CurrentState);
        }

        public override void OnNetworkSpawn()
        {
            NetworkLook.OnValueChanged += OnNetworkLookChanged;
            if (IsServer && database != null) NetworkLook.Value = database.Sanitize(DefaultState());
            if (IsOwner)
            {
                localLook = LoadLocalLook();
                hasLocalLook = true;
                SaveLocalLook(localLook);
                RequestEquip(localLook, false);
            }
            ApplyLook(NetworkLook.Value);
        }

        public override void OnNetworkDespawn() => NetworkLook.OnValueChanged -= OnNetworkLookChanged;

        public bool RequestEquip(AvatarLookState state, bool saveLocally = true)
        {
            if (database == null) return false;
            state = database.Sanitize(state);
            localLook = state;
            hasLocalLook = true;
            if (saveLocally) SaveLocalLook(state);
            if (!IsSpawned)
            {
                ApplyLook(state);
                return true;
            }
            if (!IsOwner) return false;
            SubmitAvatarLookRpc(state);
            return true;
        }

        public AvatarLookState LoadLocalLook()
        {
            return LoadPreferences(database);
        }

        public static AvatarLookState LoadPreferences(AvatarDatabaseSO sourceDatabase)
        {
            if (sourceDatabase == null) return AvatarLookState.CreateDefault();
            ValidatePreferenceSchema();
            string json = PlayerPrefs.GetString(PreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return sourceDatabase.CreateDefaultState();
            AvatarLookSaveData save = JsonUtility.FromJson<AvatarLookSaveData>(json);
            return sourceDatabase.ResolveSavedState(save);
        }

        static void ValidatePreferenceSchema()
        {
            string json = PlayerPrefs.GetString(PreferencesKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                ClearLegacyPreferences();
                return;
            }

            string readError = null;
            try
            {
                AvatarLookSaveData save = JsonUtility.FromJson<AvatarLookSaveData>(json);
                if (save != null && save.version == AvatarLookSaveData.CurrentSchemaVersion)
                {
                    ClearLegacyPreferences();
                    return;
                }
            }
            catch (Exception exception)
            {
                readError = exception.Message;
            }

            ResetAvatarPreferences();
            Debug.LogWarning(string.IsNullOrEmpty(readError)
                ? "Saved avatar preferences use an unsupported schema; avatar preferences were reset."
                : "Saved avatar preferences could not be read; avatar preferences were reset. " + readError);
        }

        static void ResetAvatarPreferences()
        {
            PlayerPrefs.DeleteKey(PreferencesKey);
            ClearLegacyPreferences();
            PlayerPrefs.Save();
        }

        static void ClearLegacyPreferences()
        {
            bool changed = false;
            foreach (string key in LegacyPreferenceKeys)
            {
                if (!PlayerPrefs.HasKey(key)) continue;
                PlayerPrefs.DeleteKey(key);
                changed = true;
            }
            if (changed) PlayerPrefs.Save();
        }

        public static void SavePreferences(AvatarDatabaseSO sourceDatabase, AvatarLookState state)
        {
            if (sourceDatabase == null) return;
            PlayerPrefs.SetString(PreferencesKey, JsonUtility.ToJson(sourceDatabase.CreateSaveData(sourceDatabase.Sanitize(state))));
            PlayerPrefs.Save();
        }

        public void ApplyLook(AvatarLookState state)
        {
            if (layerRenderers == null || layerRenderers.Length != LayerCount) EnsureLayerHierarchy();
            FaceSouth();
            if (database != null) state = database.Sanitize(state);
            for (int i = 0; i < LayerCount; i++)
            {
                SpriteRenderer renderer = layerRenderers[i];
                if (renderer == null) continue;
                renderer.sortingOrder = i;
                if (i == 0)
                {
                    renderer.sprite = shadowSprite != null ? shadowSprite : PixelArtAssetGenerator.GetShadowSprite();
                    renderer.color = Color.white;
                    renderer.enabled = true;
                    continue;
                }

                AvatarItemCategory category = CategoryByLayer[i];
                AvatarItemSO item = database != null ? database.GetItem(category, state.GetIndex(category)) : null;
                if (item == null)
                {
                    renderer.sprite = PixelArtAssetGenerator.GetFallbackSprite(category, "Default", Color.white);
                    renderer.color = category == AvatarItemCategory.Skin ? state.SkinTint : Color.white;
                    renderer.enabled = category == AvatarItemCategory.Skin || category == AvatarItemCategory.Face;
                    continue;
                }

                renderer.sprite = item.Sprite != null
                    ? item.Sprite
                    : PixelArtAssetGenerator.GetFallbackSprite(category, item.ItemID, item.DefaultColor);
                renderer.color = ResolveTint(item, state);
                if (renderer.sprite != null && renderer.sprite.texture != null)
                    renderer.sprite.texture.filterMode = FilterMode.Point;
                renderer.enabled = true;
            }
        }

        public static Color ResolveTint(AvatarItemSO item, AvatarLookState state)
        {
            if (item == null) return Color.white;
            if (!item.SupportColorTint) return item.DefaultColor;
            if (item.Category == AvatarItemCategory.Skin) return state.SkinTint;
            if (item.Category == AvatarItemCategory.Hair) return state.HairTint;
            if (item.Category == AvatarItemCategory.Hat) return state.HatTint;
            if (item.Category == AvatarItemCategory.Top) return state.ClothingTint;
            if (item.Category == AvatarItemCategory.Bottom) return state.BottomTint;
            if (item.Category == AvatarItemCategory.Shoes) return state.ShoesTint;
            return item.DefaultColor;
        }

        [Rpc(SendTo.Server)]
        void SubmitAvatarLookRpc(AvatarLookState requested, RpcParams rpcParams = default)
        {
            if (!IsServer || database == null || rpcParams.Receive.SenderClientId != OwnerClientId) return;
            NetworkLook.Value = database.Sanitize(requested);
        }

        void OnNetworkLookChanged(AvatarLookState previous, AvatarLookState current) => ApplyLook(current);

        AvatarLookState DefaultState() => database != null ? database.CreateDefaultState() : AvatarLookState.CreateDefault();

        void SaveLocalLook(AvatarLookState state)
        {
            SavePreferences(database, state);
        }

        void EnsureLayerHierarchy()
        {
            if (layerRenderers == null || layerRenderers.Length != LayerCount) layerRenderers = new SpriteRenderer[LayerCount];
            bool hasAvatarSortingLayer = false;
            foreach (SortingLayer sortingLayer in SortingLayer.layers)
                if (sortingLayer.name == "Avatar") { hasAvatarSortingLayer = true; break; }

            for (int i = 0; i < LayerCount; i++)
            {
                SpriteRenderer renderer = layerRenderers[i];
                if (renderer == null)
                {
                    Transform existing = transform.Find(LayerNames[i]);
                    GameObject layer = existing != null ? existing.gameObject : new GameObject(LayerNames[i]);
                    if (existing == null) layer.transform.SetParent(transform, false);
                    layer.transform.localPosition = Vector3.zero;
                    layer.transform.localRotation = Quaternion.identity;
                    layer.transform.localScale = Vector3.one;
                    renderer = layer.GetComponent<SpriteRenderer>();
                    if (renderer == null) renderer = layer.AddComponent<SpriteRenderer>();
                    layerRenderers[i] = renderer;
                }
                renderer.sortingLayerName = hasAvatarSortingLayer ? "Avatar" : "Default";
                renderer.sortingOrder = i;
                PixelArtAssetGenerator.FaceSouth(renderer);
            }
        }

        /// <summary>Keep every avatar layer in the shared front-facing south view.</summary>
        public void FaceSouth()
        {
            if (transform.localRotation != Quaternion.identity) transform.localRotation = Quaternion.identity;
            Vector3 facingScale = transform.localScale;
            transform.localScale = new Vector3(Mathf.Abs(facingScale.x), Mathf.Abs(facingScale.y), Mathf.Abs(facingScale.z));
            if (layerRenderers == null) return;
            for (int i = 0; i < layerRenderers.Length; i++)
                PixelArtAssetGenerator.FaceSouth(layerRenderers[i]);
        }
    }
}
