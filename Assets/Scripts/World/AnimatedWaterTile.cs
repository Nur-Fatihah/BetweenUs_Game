using UnityEngine;
using UnityEngine.Tilemaps;

namespace Crony
{
    /// <summary>
    /// Lake tile whose caustic light drifts through a few frames. The tilemap renderer plays the animation itself,
    /// so it costs nothing per frame on the CPU. Keeps a grid collider like the static water tile it replaces, except
    /// for the "covered" twin used under roads, paths and bridges, which must stay walkable.
    /// </summary>
    [CreateAssetMenu(fileName = "AnimatedWaterTile", menuName = "Crony/Animated Water Tile")]
    public sealed class AnimatedWaterTile : TileBase
    {
        public Sprite[] frames = new Sprite[0];
        public float framesPerSecond = 2.5f;
        public Tile.ColliderType colliderType = Tile.ColliderType.Grid;

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.sprite = frames.Length > 0 ? frames[0] : null;
            tileData.colliderType = colliderType;
            tileData.color = Color.white;
            tileData.flags = TileFlags.LockTransform;
        }

        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData)
        {
            if (frames.Length < 2) return false;
            tileAnimationData.animatedSprites = frames;
            tileAnimationData.animationSpeed = framesPerSecond;
            // Offsetting the start time per cell keeps the whole lake from pulsing in lockstep.
            tileAnimationData.animationStartTime = (position.x * 0.37f + position.y * 0.61f) % 1f;
            return true;
        }
    }
}
