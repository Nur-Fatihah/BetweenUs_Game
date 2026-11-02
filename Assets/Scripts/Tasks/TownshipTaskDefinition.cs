using UnityEngine;

namespace Crony
{
    public enum TownshipTaskKind : byte
    {
        NasiLemak,
        IceCream,
        ParkBench,
        BadmintonNet,
        CommunityPermits,
        BusSchedule,
        LakeCleanup,
        Router,
        SolarBatteries,
        NoticeBoard
    }

    [CreateAssetMenu(fileName = "TownshipTask", menuName = "Crony/Township Task Definition")]
    public sealed class TownshipTaskDefinition : ScriptableObject
    {
        [SerializeField] TownshipTaskKind kind;
        [SerializeField] string taskName;
        [SerializeField] string location;
        [TextArea(2, 4)] [SerializeField] string instructions;
        [SerializeField] string[] actionNames = { "Step 1", "Step 2", "Step 3", "Step 4" };
        [SerializeField] int[] requiredActions = { 0, 1, 2, 3 };

        public TownshipTaskKind Kind => kind;
        public string TaskName => taskName;
        public string Location => location;
        public string Instructions => instructions;
        public string[] ActionNames => actionNames;
        public int[] RequiredActions => requiredActions;
    }
}
