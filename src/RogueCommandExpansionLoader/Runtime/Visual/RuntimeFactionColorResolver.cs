using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>Reads the faction material chosen by the pinned native EntityFactory.</summary>
    public static class RuntimeFactionColorResolver
    {
        public const string NativeColorProperty = "Color_72ECFA4B";

        public static bool TryResolve(EntityController entity, out Color color)
        {
            color = new Color(1f, 1f, 1f, 1f);
            if (entity == null) return false;
            string path = entity.IsControlledByPlayer ? "Materials/Player"
                : entity.IsControlledByAi ? (entity.IsBuilding ? "Materials/DynamicMaterials/AiBuilding" : "Materials/Ai")
                : null;
            if (path == null) return false;
            try
            {
                Material material = Resources.Load(path, typeof(Material)) as Material;
                if (material == null || !material.HasProperty(NativeColorProperty)) return false;
                color = material.GetColor(NativeColorProperty);
                color.a = 1f;
                return true;
            }
            catch { return false; }
        }
    }
}
