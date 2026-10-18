using System;
using System.Collections.Generic;
using System.Text;

namespace RogueCommandExpansionLoader.Runtime
{
    /// <summary>
    /// Persists the stage identities that correspond to a StageMap's serialized LevelMaps.
    /// Tokens are base64url-encoded so the game can safely ignore the added JSON string field.
    /// </summary>
    public static class BlueprintRewardStageOrder
    {
        public const string JsonPropertyName = "rclStageOrderV1";

        private const int MaximumStageCount = 512;
        private const int MaximumTokenLength = 4096;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool TryCreateNativeStageToken(string stageAssetName, out string token)
        {
            token = null;
            if (String.IsNullOrWhiteSpace(stageAssetName))
            {
                return false;
            }

            token = "N_" + EncodeTokenPayload(stageAssetName);
            return token.Length <= MaximumTokenLength;
        }

        public static bool TryCreateCustomStageToken(string packId, string stageId, out string token)
        {
            token = null;
            if (String.IsNullOrWhiteSpace(packId) || String.IsNullOrWhiteSpace(stageId))
            {
                return false;
            }

            token = "C_" + EncodeTokenPayload(packId + "\0" + stageId);
            return token.Length <= MaximumTokenLength;
        }

        public static bool TryReadNativeStageToken(string token, out string stageAssetName)
        {
            stageAssetName = null;
            if (String.IsNullOrEmpty(token) || !token.StartsWith("N_", StringComparison.Ordinal))
            {
                return false;
            }

            string payload;
            if (!TryDecodeTokenPayload(token.Substring(2), out payload) || String.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            stageAssetName = payload;
            return true;
        }

        public static bool TryReadCustomStageToken(string token, out string packId, out string stageId)
        {
            packId = null;
            stageId = null;
            if (String.IsNullOrEmpty(token) || !token.StartsWith("C_", StringComparison.Ordinal))
            {
                return false;
            }

            string payload;
            if (!TryDecodeTokenPayload(token.Substring(2), out payload))
            {
                return false;
            }

            int separator = payload.IndexOf('\0');
            if (separator <= 0 || separator == payload.Length - 1 ||
                payload.IndexOf('\0', separator + 1) >= 0)
            {
                return false;
            }

            packId = payload.Substring(0, separator);
            stageId = payload.Substring(separator + 1);
            return true;
        }

        public static bool TryAppendSerializedOrder(
            string json,
            IList<string> stageTokens,
            out string updatedJson)
        {
            updatedJson = json;
            string encodedOrder;
            if (!TryEncodeStageOrder(stageTokens, out encodedOrder))
            {
                return false;
            }

            int rootClose;
            int valueStart;
            int valueLength;
            bool found;
            bool hasMembers;
            if (!TryLocateProperty(json, out rootClose, out valueStart, out valueLength, out found, out hasMembers))
            {
                return false;
            }

            string serializedValue = "\"" + encodedOrder + "\"";
            if (found)
            {
                string existingOrder;
                List<string> existingTokens;
                if (!TryDecodeJsonString(json, valueStart, valueLength, out existingOrder) ||
                    !TryDecodeStageOrder(existingOrder, out existingTokens))
                {
                    return false;
                }

                if (String.Equals(existingOrder, encodedOrder, StringComparison.Ordinal))
                {
                    updatedJson = json;
                    return true;
                }

                updatedJson = json.Substring(0, valueStart) + serializedValue +
                    json.Substring(valueStart + valueLength);
                return true;
            }

            string insertion = (hasMembers ? "," : String.Empty) + "\"" + JsonPropertyName + "\":" + serializedValue;
            updatedJson = json.Insert(rootClose, insertion);
            return true;
        }

        public static bool TryReadSerializedOrder(string json, out List<string> stageTokens)
        {
            stageTokens = null;
            int rootClose;
            int valueStart;
            int valueLength;
            bool found;
            bool hasMembers;
            if (!TryLocateProperty(json, out rootClose, out valueStart, out valueLength, out found, out hasMembers) ||
                !found)
            {
                return false;
            }

            string encodedOrder;
            return TryDecodeJsonString(json, valueStart, valueLength, out encodedOrder) &&
                TryDecodeStageOrder(encodedOrder, out stageTokens);
        }

        private static bool TryEncodeStageOrder(IList<string> stageTokens, out string encodedOrder)
        {
            encodedOrder = null;
            if (stageTokens == null || stageTokens.Count == 0 || stageTokens.Count > MaximumStageCount)
            {
                return false;
            }

            var builder = new StringBuilder();
            var customStageTokens = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < stageTokens.Count; i++)
            {
                string token = stageTokens[i];
                string ignoredName;
                string ignoredId;
                bool isKnownToken = TryReadNativeStageToken(token, out ignoredName) ||
                    TryReadCustomStageToken(token, out ignoredName, out ignoredId);
                if (!isKnownToken || token.Length > MaximumTokenLength)
                {
                    return false;
                }

                if (token.StartsWith("C_", StringComparison.Ordinal) && !customStageTokens.Add(token))
                {
                    return false;
                }

                if (i > 0)
                {
                    builder.Append(';');
                }

                builder.Append(token);
            }

            encodedOrder = builder.ToString();
            return true;
        }

        private static bool TryDecodeStageOrder(string encodedOrder, out List<string> stageTokens)
        {
            stageTokens = null;
            if (String.IsNullOrEmpty(encodedOrder) || encodedOrder.Length > MaximumStageCount * MaximumTokenLength)
            {
                return false;
            }

            string[] tokens = encodedOrder.Split(';');
            if (tokens.Length == 0 || tokens.Length > MaximumStageCount)
            {
                return false;
            }

            var decoded = new List<string>(tokens.Length);
            var customStageTokens = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tokens.Length; i++)
            {
                string ignoredName;
                string ignoredId;
                bool isKnownToken = tokens[i].Length <= MaximumTokenLength &&
                    (TryReadNativeStageToken(tokens[i], out ignoredName) ||
                        TryReadCustomStageToken(tokens[i], out ignoredName, out ignoredId));
                if (!isKnownToken)
                {
                    return false;
                }

                if (tokens[i].StartsWith("C_", StringComparison.Ordinal) &&
                    !customStageTokens.Add(tokens[i]))
                {
                    return false;
                }

                decoded.Add(tokens[i]);
            }

            stageTokens = decoded;
            return true;
        }

        private static bool TryLocateProperty(
            string json,
            out int rootClose,
            out int valueStart,
            out int valueLength,
            out bool found,
            out bool hasMembers)
        {
            rootClose = -1;
            valueStart = -1;
            valueLength = 0;
            found = false;
            hasMembers = false;
            if (String.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            int first = 0;
            while (first < json.Length && Char.IsWhiteSpace(json[first])) first++;
            int last = json.Length - 1;
            while (last >= first && Char.IsWhiteSpace(json[last])) last--;
            if (first >= last || json[first] != '{' || json[last] != '}')
            {
                return false;
            }

            rootClose = last;
            int position = first + 1;
            SkipWhitespace(json, ref position, last);
            if (position == last)
            {
                return true;
            }

            while (position < last)
            {
                int keyStart = position;
                int keyLength;
                if (!TrySkipJsonString(json, ref position, last, out keyLength))
                {
                    return false;
                }

                string key;
                if (!TryDecodeJsonString(json, keyStart, keyLength, out key))
                {
                    return false;
                }

                SkipWhitespace(json, ref position, last);
                if (position >= last || json[position] != ':')
                {
                    return false;
                }

                position++;
                SkipWhitespace(json, ref position, last);
                int currentValueStart = position;
                int currentValueLength;
                if (!TrySkipJsonValue(json, ref position, last, out currentValueLength))
                {
                    return false;
                }

                hasMembers = true;
                if (String.Equals(key, JsonPropertyName, StringComparison.Ordinal))
                {
                    if (found)
                    {
                        return false;
                    }

                    found = true;
                    valueStart = currentValueStart;
                    valueLength = currentValueLength;
                }

                SkipWhitespace(json, ref position, last);
                if (position == last)
                {
                    return true;
                }

                if (json[position] != ',')
                {
                    return false;
                }

                position++;
                SkipWhitespace(json, ref position, last);
                if (position >= last)
                {
                    return false;
                }
            }

            return false;
        }

        private static bool TrySkipJsonValue(string json, ref int position, int rootClose, out int valueLength)
        {
            valueLength = 0;
            if (position >= rootClose)
            {
                return false;
            }

            int start = position;
            char first = json[position];
            if (first == '"')
            {
                if (!TrySkipJsonString(json, ref position, rootClose, out valueLength))
                {
                    return false;
                }

                return true;
            }

            if (first == '[' || first == '{')
            {
                var expectedClosers = new Stack<char>();
                expectedClosers.Push(first == '[' ? ']' : '}');
                position++;
                bool inString = false;
                bool escaped = false;
                while (position < rootClose)
                {
                    char current = json[position];
                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (current == '\\')
                        {
                            escaped = true;
                        }
                        else if (current == '"')
                        {
                            inString = false;
                        }
                    }
                    else if (current == '"')
                    {
                        inString = true;
                    }
                    else if (current == '[' || current == '{')
                    {
                        expectedClosers.Push(current == '[' ? ']' : '}');
                    }
                    else if (current == ']' || current == '}')
                    {
                        if (expectedClosers.Count == 0 || expectedClosers.Pop() != current)
                        {
                            return false;
                        }

                        if (expectedClosers.Count == 0)
                        {
                            position++;
                            valueLength = position - start;
                            return true;
                        }
                    }

                    position++;
                }

                return false;
            }

            while (position < rootClose && json[position] != ',' && json[position] != '}')
            {
                position++;
            }

            int end = position;
            while (end > start && Char.IsWhiteSpace(json[end - 1])) end--;
            if (end == start)
            {
                return false;
            }

            position = end;
            valueLength = end - start;
            return true;
        }

        private static bool TrySkipJsonString(string json, ref int position, int limit, out int stringLength)
        {
            stringLength = 0;
            if (position >= limit || json[position] != '"')
            {
                return false;
            }

            int start = position;
            position++;
            while (position < limit)
            {
                char current = json[position++];
                if (current == '"')
                {
                    stringLength = position - start;
                    return true;
                }

                if (current < 0x20)
                {
                    return false;
                }

                if (current == '\\')
                {
                    if (position >= limit)
                    {
                        return false;
                    }

                    char escaped = json[position++];
                    if (escaped == 'u')
                    {
                        if (position + 4 > limit)
                        {
                            return false;
                        }

                        for (int i = 0; i < 4; i++)
                        {
                            if (!IsHex(json[position + i])) return false;
                        }

                        position += 4;
                    }
                    else if (escaped != '"' && escaped != '\\' && escaped != '/' &&
                        escaped != 'b' && escaped != 'f' && escaped != 'n' && escaped != 'r' && escaped != 't')
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static bool TryDecodeJsonString(string json, int start, int length, out string value)
        {
            value = null;
            if (json == null || start < 0 || length < 2 || start + length > json.Length ||
                json[start] != '"' || json[start + length - 1] != '"')
            {
                return false;
            }

            var builder = new StringBuilder(length - 2);
            for (int i = start + 1; i < start + length - 1; i++)
            {
                char current = json[i];
                if (current != '\\')
                {
                    if (current < 0x20) return false;
                    builder.Append(current);
                    continue;
                }

                if (++i >= start + length - 1) return false;
                char escaped = json[i];
                switch (escaped)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= start + length - 1) return false;
                        int codePoint = 0;
                        for (int j = 1; j <= 4; j++)
                        {
                            int digit = HexValue(json[i + j]);
                            if (digit < 0) return false;
                            codePoint = (codePoint << 4) | digit;
                        }

                        builder.Append((char)codePoint);
                        i += 4;
                        break;
                    default: return false;
                }
            }

            value = builder.ToString();
            return true;
        }

        private static void SkipWhitespace(string json, ref int position, int limit)
        {
            while (position < limit && Char.IsWhiteSpace(json[position])) position++;
        }

        private static string EncodeTokenPayload(string payload)
        {
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool TryDecodeTokenPayload(string encoded, out string payload)
        {
            payload = null;
            if (String.IsNullOrEmpty(encoded) || encoded.Length > MaximumTokenLength ||
                (encoded.Length % 4) == 1)
            {
                return false;
            }

            for (int i = 0; i < encoded.Length; i++)
            {
                char current = encoded[i];
                if (!(current >= 'A' && current <= 'Z') && !(current >= 'a' && current <= 'z') &&
                    !(current >= '0' && current <= '9') && current != '-' && current != '_')
                {
                    return false;
                }
            }

            string base64 = encoded.Replace('-', '+').Replace('_', '/');
            int padding = encoded.Length % 4;
            if (padding == 2) base64 += "==";
            else if (padding == 3) base64 += "=";

            try
            {
                payload = StrictUtf8.GetString(Convert.FromBase64String(base64));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9') || (value >= 'A' && value <= 'F') ||
                (value >= 'a' && value <= 'f');
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            return -1;
        }
    }
}
