using UnityEngine;
using UnityEngine.U2D;

namespace Crony
{
    /// <summary>Owner camera follow with a tight pixel-grid-snapped 2D viewport.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        // Gameplay viewport in art pixels; smaller values zoom in and limit what each role can scout.
        public const int ViewWidthPixels = 192;
        public const int ViewHeightPixels = 108;
        public static float GameplayOrthographicSize => ViewHeightPixels * .5f / PixelWorldAssetGenerator.PixelsPerUnit;

        [SerializeField] Transform target;
        [SerializeField, Min(0f)] float smoothTime = .08f;
        [SerializeField, Min(1)] int assetsPixelsPerUnit = PixelWorldAssetGenerator.PixelsPerUnit;

        Camera attachedCamera;
        PixelPerfectCamera pixelPerfectCamera;
        Vector3 velocity;

        public Transform Target => target;

        void Awake()
        {
            attachedCamera = GetComponent<Camera>();
            attachedCamera.orthographic = true;
            ApplyGameplayZoom(attachedCamera);
            attachedCamera.allowHDR = true;
            attachedCamera.allowMSAA = false;
            attachedCamera.allowDynamicResolution = false;
            ConfigurePixelCamera(attachedCamera, assetsPixelsPerUnit);
            pixelPerfectCamera = GetComponent<PixelPerfectCamera>();
        }

        /// <summary>Sets the zoom on the plain camera and on whichever pixel-perfect component drives it.</summary>
        public static void ApplyGameplayZoom(Camera camera)
        {
            camera.orthographicSize = GameplayOrthographicSize;
            var urp = camera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
            if (urp != null)
            {
                urp.refResolutionX = ViewWidthPixels;
                urp.refResolutionY = ViewHeightPixels;
            }
            var builtIn = camera.GetComponent<PixelPerfectCamera>();
            if (builtIn != null)
            {
                builtIn.refResolutionX = ViewWidthPixels;
                builtIn.refResolutionY = ViewHeightPixels;
            }
        }

        public static void ConfigurePixelCamera(Camera camera, int pixelsPerUnit = 16)
        {
            // The saved scene may contain a URP camera even when using Built-in rendering.
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null) return;
            var urp = camera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
            int width = ViewWidthPixels;
            int height = ViewHeightPixels;
            if (urp != null) urp.enabled = false;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.allowHDR = camera.allowMSAA = camera.allowDynamicResolution = false;
            var pixel = camera.GetComponent<PixelPerfectCamera>();
            if (pixel == null) pixel = camera.gameObject.AddComponent<PixelPerfectCamera>();
            pixel.assetsPPU = pixelsPerUnit;
            pixel.refResolutionX = width;
            pixel.refResolutionY = height;
            pixel.upscaleRT = false;
            pixel.pixelSnapping = true;
            pixel.cropFrameX = pixel.cropFrameY = false;
            pixel.enabled = true;
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            velocity = Vector3.zero;
        }

        public void ClearTarget(Transform oldTarget)
        {
            if (target == oldTarget) target = null;
        }

        void LateUpdate()
        {
            if (attachedCamera == null) attachedCamera = GetComponent<Camera>();
            if (attachedCamera != null && pixelPerfectCamera == null && Mathf.Abs(attachedCamera.orthographicSize - GameplayOrthographicSize) > .001f)
                attachedCamera.orthographicSize = GameplayOrthographicSize;
            if (target == null) return;

            Vector3 desired = new Vector3(target.position.x, target.position.y, -10f);
            Vector3 next = smoothTime <= 0f
                ? desired
                : Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            float pixelWorldSize = 1f / Mathf.Max(1, assetsPixelsPerUnit);
            next.x = Mathf.Round(next.x / pixelWorldSize) * pixelWorldSize;
            next.y = Mathf.Round(next.y / pixelWorldSize) * pixelWorldSize;
            next.z = -10f;
            transform.position = next;
        }
    }
}
