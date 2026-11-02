namespace Crony
{
    public sealed class LakeCleanupTask : TownshipTaskStation
    {
        protected override TownshipTaskKind DefaultKind => TownshipTaskKind.LakeCleanup;
    }
}
