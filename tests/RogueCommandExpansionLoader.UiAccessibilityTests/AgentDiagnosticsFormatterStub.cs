using System.Text.Json;

namespace RogueCommandExpansionLoader.Runtime
{
    public static class AgentDiagnosticsFormatter
    {
        public static string SerializeUiAccessibilityPayload(object payload) => JsonSerializer.Serialize(payload);
    }
}
