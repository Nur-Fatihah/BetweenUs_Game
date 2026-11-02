using Unity.Netcode;
using UnityEngine;

namespace Crony
{
    /// <summary>Server-validated cosmetic selection replicated to every client; no gameplay statistics are attached.</summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AvatarCustomizer : NetworkBehaviour
    {
        [SerializeField] AvatarCosmeticCatalog catalog;
        [SerializeField] SpriteRenderer headwearRenderer;
        [SerializeField] SpriteRenderer outfitRenderer;
        [SerializeField] SpriteRenderer accessoryRenderer;
        [SerializeField] SpriteRenderer faceRenderer;

        public readonly NetworkVariable<byte> HeadwearIndex = new NetworkVariable<byte>(0);
        public readonly NetworkVariable<byte> OutfitIndex = new NetworkVariable<byte>(0);
        public readonly NetworkVariable<byte> AccessoryIndex = new NetworkVariable<byte>(0);
        public readonly NetworkVariable<byte> SkinToneIndex = new NetworkVariable<byte>(0);

        public AvatarCosmeticCatalog Catalog => catalog;

        void Awake()
        {
            if (headwearRenderer == null || outfitRenderer == null || accessoryRenderer == null || faceRenderer == null)
            {
                SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
                foreach (SpriteRenderer renderer in renderers)
                {
                    if (renderer.name == "Cosmetic Headwear") headwearRenderer = renderer;
                    else if (renderer.name == "Cosmetic Outfit") outfitRenderer = renderer;
                    else if (renderer.name == "Cosmetic Accessory") accessoryRenderer = renderer;
                    else if (renderer.name == "Avatar Face") faceRenderer = renderer;
                }
            }
        }

        public override void OnNetworkSpawn()
        {
            HeadwearIndex.OnValueChanged += OnAnyCosmeticChanged;
            OutfitIndex.OnValueChanged += OnAnyCosmeticChanged;
            AccessoryIndex.OnValueChanged += OnAnyCosmeticChanged;
            SkinToneIndex.OnValueChanged += OnAnyCosmeticChanged;
            if (IsOwner)
                RequestCosmetics(PlayerPrefs.GetInt("Crony.Headwear", 0), PlayerPrefs.GetInt("Crony.Outfit", 0),
                    PlayerPrefs.GetInt("Crony.Accessory", 0), PlayerPrefs.GetInt("Crony.SkinTone", 0));
            ApplyCosmetics();
        }

        public override void OnNetworkDespawn()
        {
            HeadwearIndex.OnValueChanged -= OnAnyCosmeticChanged;
            OutfitIndex.OnValueChanged -= OnAnyCosmeticChanged;
            AccessoryIndex.OnValueChanged -= OnAnyCosmeticChanged;
            SkinToneIndex.OnValueChanged -= OnAnyCosmeticChanged;
        }

        public void RequestCosmetics(int headwear, int outfit, int accessory, int skinTone)
        {
            if (!IsOwner || !IsSpawned) return;
            RequestCosmeticsRpc((byte)Mathf.Clamp(headwear, 0, 255), (byte)Mathf.Clamp(outfit, 0, 255),
                (byte)Mathf.Clamp(accessory, 0, 255), (byte)Mathf.Clamp(skinTone, 0, 255));
            PlayerPrefs.SetInt("Crony.Headwear", headwear);
            PlayerPrefs.SetInt("Crony.Outfit", outfit);
            PlayerPrefs.SetInt("Crony.Accessory", accessory);
            PlayerPrefs.SetInt("Crony.SkinTone", skinTone);
            PlayerPrefs.Save();
        }

        [Rpc(SendTo.Server)]
        void RequestCosmeticsRpc(byte headwear, byte outfit, byte accessory, byte skinTone, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || catalog == null) return;
            HeadwearIndex.Value = (byte)ClampIndex(headwear, catalog.headwear);
            OutfitIndex.Value = (byte)ClampIndex(outfit, catalog.outfits);
            AccessoryIndex.Value = (byte)ClampIndex(accessory, catalog.accessories);
            SkinToneIndex.Value = (byte)ClampIndex(skinTone, catalog.skinTones);
        }

        static int ClampIndex<T>(int value, T[] options)
        {
            if (options == null || options.Length == 0) return 0;
            return Mathf.Clamp(value, 0, options.Length - 1);
        }

        void OnAnyCosmeticChanged(byte previous, byte current) => ApplyCosmetics();

        void ApplyCosmetics()
        {
            if (catalog == null) return;
            ApplyOption(headwearRenderer, catalog.headwear, HeadwearIndex.Value, true);
            ApplyOption(outfitRenderer, catalog.outfits, OutfitIndex.Value, false);
            ApplyOption(accessoryRenderer, catalog.accessories, AccessoryIndex.Value, true);
            if (faceRenderer != null && catalog.skinTones != null && catalog.skinTones.Length > 0)
                faceRenderer.color = catalog.skinTones[Mathf.Clamp(SkinToneIndex.Value, 0, catalog.skinTones.Length - 1)];
        }

        static void ApplyOption(SpriteRenderer target, AvatarCosmeticOption[] options, int index, bool hideWhenEmpty)
        {
            if (target == null || options == null || options.Length == 0) return;
            AvatarCosmeticOption option = options[Mathf.Clamp(index, 0, options.Length - 1)];
            target.sprite = option.sprite;
            target.color = option.tint;
            target.enabled = option.sprite != null && (!hideWhenEmpty || option.sprite.name != "None");
        }
    }
}
