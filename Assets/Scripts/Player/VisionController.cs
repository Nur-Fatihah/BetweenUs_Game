using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Crony
{
    /// <summary>Owner-only 2D point light. ShadowCaster2D colliders on room walls enforce local line of sight.</summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class VisionController : NetworkBehaviour
    {
        [SerializeField] Light2D playerLight;
        [SerializeField, Min(.5f)] float normalRadius = 2.6f;
        [SerializeField, Min(.5f)] float blackoutRadius = 1.1f;
        [SerializeField] float localLightIntensity = .55f;

        void Awake()
        {
            if (playerLight == null) playerLight = GetComponentInChildren<Light2D>(true);
            if (playerLight == null)
            {
                GameObject lightObject = new GameObject("Local Vision Light");
                lightObject.transform.SetParent(transform, false);
                playerLight = lightObject.AddComponent<Light2D>();
            }
            playerLight.lightType = Light2D.LightType.Point;
            GlowLight.TargetAllLayers(playerLight);
            playerLight.pointLightInnerRadius = 0f;
            playerLight.pointLightOuterRadius = normalRadius;
            playerLight.intensity = localLightIntensity;
            playerLight.shadowsEnabled = true;
            playerLight.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            if (GameManager.Instance != null) GameManager.Instance.BlackoutActive.OnValueChanged += OnBlackoutChanged;
            if (GameManager.Instance != null) GameManager.Instance.Phase.OnValueChanged += OnPhaseChanged;
            PlayerController player = GetComponent<PlayerController>();
            if (player != null) player.Status.OnValueChanged += OnStatusChanged;
            RefreshVision();
        }

        public override void OnNetworkDespawn()
        {
            if (GameManager.Instance != null) GameManager.Instance.BlackoutActive.OnValueChanged -= OnBlackoutChanged;
            if (GameManager.Instance != null) GameManager.Instance.Phase.OnValueChanged -= OnPhaseChanged;
            PlayerController player = GetComponent<PlayerController>();
            if (player != null) player.Status.OnValueChanged -= OnStatusChanged;
        }

        void OnBlackoutChanged(bool previous, bool current) => RefreshVision();
        void OnPhaseChanged(RoundPhase previous, RoundPhase current) => RefreshVision();
        void OnStatusChanged(PlayerLifeState previous, PlayerLifeState current) => RefreshVision();

        public void RefreshVision()
        {
            if (playerLight == null) return;
            bool shouldRender = IsSpawned && IsOwner && GameManager.Instance != null &&
                                GameManager.Instance.Phase.Value == RoundPhase.Playing &&
                                StatusIsAlive();
            playerLight.enabled = shouldRender;
            if (!shouldRender) return;
            bool blackout = GameManager.Instance.BlackoutActive.Value;
            playerLight.pointLightOuterRadius = blackout ? blackoutRadius : normalRadius;
            playerLight.pointLightInnerRadius = blackout ? .1f : .7f;
            // A soft warm lantern: real lighting is now on, so full intensity would blow out under bloom.
            playerLight.color = new Color(1f, .9f, .72f);
            playerLight.intensity = blackout ? .9f : localLightIntensity;
        }

        bool StatusIsAlive()
        {
            PlayerController player = GetComponent<PlayerController>();
            return player != null && player.Status.Value == PlayerLifeState.Alive;
        }
    }
}
