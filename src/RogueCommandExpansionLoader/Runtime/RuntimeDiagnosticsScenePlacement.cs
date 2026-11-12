using UnityEngine;
using UnityEngine.SceneManagement;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Places the MCP diagnostics bridge in the scene whose load event created it.
    /// </summary>
    public static class RuntimeDiagnosticsScenePlacement
    {
        public static bool ShouldEnsureOnSceneLoaded(
            bool hasDiagnosticsHost,
            string loadedSceneName,
            bool isSingleLoad,
            bool hostAlreadyInLoadedScene)
        {
            if (!hasDiagnosticsHost || isSingleLoad || hostAlreadyInLoadedScene || string.IsNullOrWhiteSpace(loadedSceneName))
            {
                return true;
            }

            return string.Equals(loadedSceneName, "Game", System.StringComparison.Ordinal);
        }

        public static bool TryMoveHostToScene(GameObject host, Scene targetScene)
        {
            try
            {
                if (host == null || !targetScene.IsValid() || !targetScene.isLoaded)
                {
                    return false;
                }

                if (host.scene.handle != targetScene.handle)
                {
                    SceneManager.MoveGameObjectToScene(host, targetScene);
                }

                return host.scene.handle == targetScene.handle;
            }
            catch
            {
                return false;
            }
        }
    }
}
