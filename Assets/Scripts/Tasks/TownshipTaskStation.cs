using Unity.Netcode;
using UnityEngine;

namespace Crony
{
    /// <summary>Server-validated multi-step task station shared by all ten park tasks.</summary>
    [RequireComponent(typeof(NetworkObject), typeof(BoxCollider2D))]
    public class TownshipTaskStation : NetworkBehaviour
    {
        [SerializeField] TownshipTaskDefinition definition;
        [SerializeField] TownshipTaskMiniGameUI miniGameUI;
        [SerializeField] float useRange = 2.1f;
        public readonly NetworkVariable<int> Progress = new NetworkVariable<int>(0);
        public readonly NetworkVariable<bool> Completed = new NetworkVariable<bool>(false);

        protected virtual TownshipTaskKind DefaultKind => TownshipTaskKind.NasiLemak;
        public TownshipTaskKind Kind => definition != null ? definition.Kind : DefaultKind;
        public string TaskName => definition != null ? definition.TaskName : Kind.ToString();
        public string Location => definition != null ? definition.Location : string.Empty;
        public string Instructions => definition != null ? definition.Instructions : "Complete the actions in order.";
        public string[] ActionNames => definition != null ? definition.ActionNames : System.Array.Empty<string>();
        public int[] RequiredActions => definition != null ? definition.RequiredActions : System.Array.Empty<int>();
        public int StepCount => definition != null && definition.RequiredActions != null ? definition.RequiredActions.Length : 0;
        public int CurrentAction => StepCount == 0 ? -1 : definition.RequiredActions[Mathf.Clamp(Progress.Value, 0, StepCount - 1)];
        public string CurrentActionLabel
        {
            get
            {
                int current = CurrentAction;
                string[] actions = definition != null ? definition.ActionNames : null;
                return current >= 0 && actions != null && current < actions.Length ? actions[current] : "Use the matching action key";
            }
        }

        protected virtual void Awake()
        {
            EnsureTriggerCollider();
            if (miniGameUI == null)
                miniGameUI = FindAnyObjectByType<TownshipTaskMiniGameUI>(FindObjectsInactive.Include);
        }

        protected virtual void OnValidate() => EnsureTriggerCollider();

        void EnsureTriggerCollider()
        {
            BoxCollider2D trigger = GetComponent<BoxCollider2D>();
            if (trigger == null) return;
            trigger.isTrigger = true;
            if (trigger.size == Vector2.zero) trigger.size = new Vector2(2.4f, 2.4f);
        }

        public void BindMiniGameUI(TownshipTaskMiniGameUI manager) => miniGameUI = manager;

        public void OpenMiniGameUI()
        {
            TownshipTaskMiniGameUI ui = miniGameUI != null
                ? miniGameUI
                : FindAnyObjectByType<TownshipTaskMiniGameUI>(FindObjectsInactive.Include);
            if (ui != null) ui.OpenStation(this);
            else TownshipTaskMiniGameUI.Open(this);
        }

        public override void OnNetworkSpawn()
        {
            RefreshGlow();
            if (GameManager.Instance != null) GameManager.Instance.ActiveSabotage.OnValueChanged += OnSabotageChanged;
            Progress.OnValueChanged += OnProgressChanged;
            Completed.OnValueChanged += OnCompletedChanged;
        }

        public override void OnNetworkDespawn()
        {
            if (GameManager.Instance != null) GameManager.Instance.ActiveSabotage.OnValueChanged -= OnSabotageChanged;
            Progress.OnValueChanged -= OnProgressChanged;
            Completed.OnValueChanged -= OnCompletedChanged;
        }

        void OnProgressChanged(int previous, int current)
        {
            if (current > previous) GameAudioManager.PlayTaskInteraction();
        }

        void OnSabotageChanged(byte previous, byte current) => RefreshGlow();

        /// <summary>Beacon while open, alarm while it repairs the active sabotage, soft mint once done.</summary>
        void RefreshGlow()
        {
            GameManager manager = GameManager.Instance;
            bool alarm = manager != null && manager.ActiveSabotage.Value != (byte)SabotageType.None &&
                GameManager.RepairTaskFor((SabotageType)manager.ActiveSabotage.Value) == Kind;
            GlowLight.Attach(transform, alarm ? GlowProfile.Alarm : Completed.Value ? GlowProfile.Done : GlowProfile.Beacon);
        }

        void OnCompletedChanged(bool previous, bool current)
        {
            RefreshGlow();
            if (current && !previous) GameAudioManager.PlayTaskComplete();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestActionRpc(int selectedAction, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            GameManager manager = GameManager.Instance;
            if (!IsServer || manager == null || manager.Phase.Value != RoundPhase.Playing || Completed.Value || StepCount == 0) return;
            PlayerController player = manager.GetPlayer(sender);
            // A crony may go through the motions to blend in, but their work never moves the station.
            if (player == null || manager.IsImpostorTeam(sender) || player.Status.Value != PlayerLifeState.Alive ||
                (player.transform.position - transform.position).sqrMagnitude > useRange * useRange) return;

            if (manager.ActiveSabotage.Value != (byte)SabotageType.None &&
                GameManager.RepairTaskFor((SabotageType)manager.ActiveSabotage.Value) == Kind)
            {
                if (manager.TryRepairSabotageServer(sender, (byte)Kind)) return;
            }

            if (definition.RequiredActions[Mathf.Clamp(Progress.Value, 0, StepCount - 1)] == selectedAction)
            {
                int next = Progress.Value + 1;
                Progress.Value = next;
                if (next >= StepCount)
                {
                    Completed.Value = true;
                    manager.NotifyTaskCompleted(this);
                }
            }
            else
            {
                Progress.Value = 0;
            }
        }

        /// <summary>Marks the checkpoint done without playing it, used when a new host rebuilds the match.</summary>
        public void RestoreCompletedServer()
        {
            if (!IsServer) return;
            Progress.Value = StepCount;
            Completed.Value = true;
        }

                public void ResetForNewMatchServer()
        {
            if (!IsServer) return;
            Progress.Value = 0;
            Completed.Value = false;
        }

        public static TownshipTaskStation FindNearest(Vector3 position, float range, out float distance)
        {
            TownshipTaskStation nearest = null;
            distance = range;
            foreach (TownshipTaskStation station in FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude))
            {
                if (!station.IsSpawned || station.Completed.Value) continue;
                float candidate = Vector2.Distance(position, station.transform.position);
                if (candidate <= distance)
                {
                    nearest = station;
                    distance = candidate;
                }
            }
            return nearest;
        }

        public static TownshipTaskStation FindByKind(TownshipTaskKind kind)
        {
            foreach (TownshipTaskStation station in FindObjectsByType<TownshipTaskStation>(FindObjectsInactive.Exclude))
                if (station.Kind == kind) return station;
            return null;
        }
    }
}
