namespace Crony
{
    /// <summary>Server-authoritative station for replacing a Township Park solar light battery.</summary>
    public sealed class SolarBatteryTask : TownshipTaskStation
    {
        protected override TownshipTaskKind DefaultKind => TownshipTaskKind.SolarBatteries;
    }
}
