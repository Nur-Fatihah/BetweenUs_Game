using UnityEngine;

namespace Crony
{
    public enum SurfaceType : byte { Grass, Concrete, Wood, Water, Metal, Tile }

    [DisallowMultipleComponent]
    public sealed class SurfaceAudioMarker : MonoBehaviour
    {
        [SerializeField] SurfaceType surface = SurfaceType.Grass;
        public SurfaceType Surface => surface;

        public void SetSurface(SurfaceType value) => surface = value;
    }
}
