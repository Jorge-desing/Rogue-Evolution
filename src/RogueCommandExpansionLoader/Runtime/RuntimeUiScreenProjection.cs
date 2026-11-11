using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Selects the camera argument expected by RectTransformUtility for a
    /// canvas. ScreenSpaceOverlay positions are already expressed in screen
    /// pixels and must not be projected through a world camera.
    /// </summary>
    internal static class RuntimeUiScreenProjection
    {
        public static Camera GetProjectionCamera(Canvas canvas)
        {
            if (canvas == null)
            {
                return null;
            }

            Canvas rootCanvas = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            if (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            if (canvas.worldCamera != null) return canvas.worldCamera;
            return rootCanvas.worldCamera;
        }
    }
}
