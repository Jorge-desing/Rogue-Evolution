using System;
using System.Reflection;

namespace RogueCommandExpansionLoader.Content
{
    internal static class EntityControllerIdentity
    {
        public static void Assign(FieldInfo entityIdField, object controller, string entityId, string context)
        {
            if (entityIdField == null)
            {
                throw new ArgumentNullException("entityIdField");
            }

            if (controller == null)
            {
                throw new InvalidOperationException(context + ": el prefab perdió su EntityController raíz.");
            }

            if (entityIdField.FieldType != typeof(string))
            {
                throw new InvalidOperationException(context + ": EntityController.entityId no es una cadena.");
            }

            if (String.IsNullOrWhiteSpace(entityId))
            {
                throw new InvalidOperationException(context + ": el ID namespaced de la unidad está vacío.");
            }

            entityIdField.SetValue(controller, entityId);
            if (!String.Equals(entityIdField.GetValue(controller) as string, entityId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(context +
                    ": el EntityController clonado no conservó el ID namespaced de la unidad.");
            }
        }
    }
}
