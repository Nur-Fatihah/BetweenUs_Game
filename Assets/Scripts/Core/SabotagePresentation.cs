using UnityEngine;

namespace Crony
{
    /// <summary>Applies replicated sabotage state to scene-only markers and the physical route gate.</summary>
    public sealed class SabotagePresentation : MonoBehaviour
    {
        [SerializeField] GameObject gasLeakMarker;
        [SerializeField] GameObject waterContaminationMarker;
        [SerializeField] GameObject interferenceMarker;
        [SerializeField] GameObject routeBlockadeGate;
        GameManager manager;

        void Update()
        {
            if (manager != GameManager.Instance)
            {
                Unsubscribe();
                manager = GameManager.Instance;
                Subscribe();
            }
            if (manager == null || !manager.IsSpawned) return;
            Apply((SabotageType)manager.ActiveSabotage.Value);
        }

        void OnDestroy() => Unsubscribe();

        void Subscribe()
        {
            if (manager == null) return;
            manager.ActiveSabotage.OnValueChanged += OnSabotageChanged;
        }

        void Unsubscribe()
        {
            if (manager != null) manager.ActiveSabotage.OnValueChanged -= OnSabotageChanged;
        }

        void OnSabotageChanged(byte previous, byte current) => Apply((SabotageType)current);

        void Apply(SabotageType active)
        {
            SetActive(gasLeakMarker, active == SabotageType.GasLeak);
            SetActive(waterContaminationMarker, active == SabotageType.WaterContamination);
            SetActive(interferenceMarker, active == SabotageType.Interference);
            SetActive(routeBlockadeGate, active == SabotageType.RouteBlockade);
        }

        static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
