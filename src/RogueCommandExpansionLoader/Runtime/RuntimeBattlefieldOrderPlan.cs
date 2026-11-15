using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RogueCommandExpansionLoader.Runtime
{
    internal sealed class BattlefieldPlanOrder
    {
        public string CommandType = string.Empty;
        public string EntityKeyId = string.Empty;
        public List<string> EntityKeyIds = new List<string>();
        public string TargetEntityKeyId = string.Empty;
        public string GroupCommandType = string.Empty;
        public string FormationType = string.Empty;
        public List<float> WaypointX = new List<float>();
        public List<float> WaypointZ = new List<float>();
        public float DestinationX;
        public float DestinationZ;
        public float TargetX;
        public float TargetZ;
        public float ThreatX;
        public float ThreatZ;
        public float Distance;
        public float Spacing = 4f;
        public int MaxAttackers = 16;
        public bool ReplaceCurrent;
        public float? SkillTargetX;
        public float? SkillTargetZ;
    }

    /// <summary>
    /// Parses and dispatches a bounded, mixed set of native orders requested by an external MCP agent.
    /// The complete plan is shape-checked and its player-controlled actors are resolved before the first order.
    /// Native dispatch is sequential on the Unity thread and cannot be rolled back if an individual order fails.
    /// </summary>
    internal static class RuntimeBattlefieldOrderPlan
    {
        internal const int MaxOrders = 16;
        internal const int MaxEntities = 32;
        internal const int MaxWaypoints = 8;
        private const int MaxJsonCharacters = 32768;

        public static string DispatchJson(string ordersJson, string activeSceneName)
        {
            if (!TryParseOrders(ordersJson, out List<BattlefieldPlanOrder> orders, out string parseError))
            {
                return FailureJson(false, 0, parseError);
            }

            if (!string.Equals(activeSceneName, "Game", StringComparison.Ordinal))
            {
                return FailureJson(false, orders.Count, "Battlefield orders require the active Game scene.");
            }

            if (!TryPreflightActors(orders, out int actorCount, out string preflightError))
            {
                return FailureJson(false, orders.Count, preflightError);
            }

            var actionResults = new JArray();
            int acceptedOrderCount = 0;
            int completeOrderCount = 0;
            int partialOrderCount = 0;
            int failedOrderCount = 0;
            int dispatchedUnitCount = 0;

            for (int index = 0; index < orders.Count; index++)
            {
                BattlefieldPlanOrder order = orders[index];
                JObject action;
                try
                {
                    action = DispatchOrder(index, order);
                }
                catch (Exception ex)
                {
                    action = new JObject
                    {
                        ["orderIndex"] = index,
                        ["commandType"] = order.CommandType,
                        ["accepted"] = false,
                        ["complete"] = false,
                        ["partial"] = false,
                        ["error"] = "Dispatch error: " + ex.Message,
                    };
                }

                bool accepted = action.Value<bool>("accepted");
                bool complete = action.Value<bool>("complete");
                bool partial = action.Value<bool>("partial");
                if (accepted) acceptedOrderCount++;
                if (complete) completeOrderCount++;
                if (partial) partialOrderCount++;
                if (!accepted) failedOrderCount++;
                dispatchedUnitCount += action.Value<int>("dispatchedUnitCount");
                actionResults.Add(action);
            }

            var result = new JObject
            {
                ["kind"] = "battlefieldOrderPlanResult",
                ["success"] = completeOrderCount == orders.Count,
                ["planApplied"] = true,
                ["dispatchStarted"] = true,
                ["orderCount"] = orders.Count,
                ["actorCount"] = actorCount,
                ["acceptedOrderCount"] = acceptedOrderCount,
                ["completeOrderCount"] = completeOrderCount,
                ["partialOrderCount"] = partialOrderCount,
                ["failedOrderCount"] = failedOrderCount,
                ["dispatchedUnitCount"] = dispatchedUnitCount,
                ["executionMode"] = "sequential_same_unity_thread_callback",
                ["rollbackPerformed"] = false,
                ["actions"] = actionResults,
            };
            return result.ToString(Formatting.None);
        }

        internal static bool TryParseOrders(
            string ordersJson,
            out List<BattlefieldPlanOrder> orders,
            out string error)
        {
            orders = new List<BattlefieldPlanOrder>();
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(ordersJson) || ordersJson.Length > MaxJsonCharacters)
            {
                error = "Orders JSON must contain 1 to 16 orders and stay within 32768 characters.";
                return false;
            }

            JArray rows;
            try
            {
                JToken token = JToken.Parse(ordersJson, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                });
                rows = token as JArray;
            }
            catch (Exception ex)
            {
                error = "Orders must be valid JSON without duplicate object fields: " + ex.Message;
                return false;
            }

            if (rows == null || rows.Count < 1 || rows.Count > MaxOrders)
            {
                error = "Orders must be a JSON array containing 1 to " + MaxOrders + " commands.";
                return false;
            }

            var plannedActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < rows.Count; index++)
            {
                JObject row = rows[index] as JObject;
                if (row == null)
                {
                    error = "Order " + index + " must be an object.";
                    return false;
                }

                if (!TryReadString(row, "commandType", true, out string commandType, out error, index)) return false;
                var order = new BattlefieldPlanOrder { CommandType = CanonicalCommandType(commandType) };
                if (string.IsNullOrEmpty(order.CommandType))
                {
                    error = "Order " + index + " has unsupported commandType '" + commandType + "'.";
                    return false;
                }

                if (!TryReadStringList(row, "entityKeyIds", out order.EntityKeyIds, out error, index)) return false;
                JToken argumentsToken;
                JObject arguments = new JObject();
                if (row.TryGetValue("arguments", out argumentsToken))
                {
                    arguments = argumentsToken as JObject;
                    if (arguments == null)
                    {
                        error = "Order " + index + " field 'arguments' must be an object.";
                        return false;
                    }
                }
                if (!TryParseOrder(arguments, order, out error, index) ||
                    !EnsureAllowedFields(row, new[] { "commandType", "entityKeyIds", "arguments" }, out error, index)) return false;
                foreach (string key in ActorKeys(order))
                {
                    if (!plannedActors.Add(key))
                    {
                        error = "Order " + index + " assigns actor '" + key + "' more than once in the plan.";
                        return false;
                    }
                    if (plannedActors.Count > MaxEntities)
                    {
                        error = "A battlefield plan may command at most " + MaxEntities + " distinct units.";
                        return false;
                    }
                }

                orders.Add(order);
            }

            return true;
        }

        private static bool TryParseOrder(JObject row, BattlefieldPlanOrder order, out string error, int index)
        {
            error = string.Empty;
            string[] fields;
            switch (order.CommandType)
            {
                case "Move":
                    fields = new[] { "targetX", "targetZ", "replaceCurrent" };
                    if (!RequireSingleActor(order, out error, index) ||
                        !TryReadNumber(row, "targetX", true, out order.TargetX, out error, index) ||
                        !TryReadNumber(row, "targetZ", true, out order.TargetZ, out error, index) ||
                        !TryReadBoolean(row, "replaceCurrent", false, out order.ReplaceCurrent, out error, index)) return false;
                    order.DestinationX = order.TargetX;
                    order.DestinationZ = order.TargetZ;
                    break;
                case "Attack":
                    fields = new[] { "targetEntityKeyId" };
                    if (!RequireSingleActor(order, out error, index) ||
                        !TryReadString(row, "targetEntityKeyId", true, out order.TargetEntityKeyId, out error, index)) return false;
                    break;
                case "AttackMove":
                    fields = new[] { "targetX", "targetZ" };
                    if (!RequireSingleActor(order, out error, index) ||
                        !TryReadNumber(row, "targetX", true, out order.TargetX, out error, index) ||
                        !TryReadNumber(row, "targetZ", true, out order.TargetZ, out error, index)) return false;
                    break;
                case "Stop":
                case "HoldPosition":
                    fields = Array.Empty<string>();
                    if (!RequireSingleActor(order, out error, index)) return false;
                    break;
                case "GroupMove":
                case "GroupAttackMove":
                    fields = new[] { "targetX", "targetZ" };
                    order.GroupCommandType = order.CommandType == "GroupMove" ? "Move" : "AttackMove";
                    if (!TryReadNumber(row, "targetX", true, out order.TargetX, out error, index) ||
                        !TryReadNumber(row, "targetZ", true, out order.TargetZ, out error, index)) return false;
                    break;
                case "GroupStop":
                case "GroupHoldPosition":
                    fields = Array.Empty<string>();
                    order.GroupCommandType = order.CommandType == "GroupStop" ? "Stop" : "HoldPosition";
                    if (HasField(row, "targetX") || HasField(row, "targetZ"))
                    {
                        error = "Order " + index + " group stop/hold commands do not accept target coordinates.";
                        return false;
                    }
                    break;
                case "FormationMove":
                    fields = new[] { "formationType", "targetX", "targetZ", "spacing" };
                    if (!TryReadString(row, "formationType", true, out order.FormationType, out error, index) ||
                        !TryReadNumber(row, "targetX", true, out order.TargetX, out error, index) ||
                        !TryReadNumber(row, "targetZ", true, out order.TargetZ, out error, index) ||
                        !TryReadNumber(row, "spacing", false, out order.Spacing, out error, index)) return false;
                    if (!HasField(row, "spacing")) order.Spacing = 4f;
                    order.FormationType = CanonicalFormation(order.FormationType);
                    if (string.IsNullOrEmpty(order.FormationType))
                    {
                        error = "Order " + index + " formationType must be Line, Wedge, Column, Box, or Surround.";
                        return false;
                    }
                    break;
                case "ActiveSkill":
                    fields = new[] { "targetEntityKeyId", "targetX", "targetZ" };
                    if (!RequireSingleActor(order, out error, index) ||
                        !TryReadString(row, "targetEntityKeyId", false, out order.TargetEntityKeyId, out error, index) ||
                        !TryReadNumber(row, "targetX", false, out float targetX, out error, index) ||
                        !TryReadNumber(row, "targetZ", false, out float targetZ, out error, index)) return false;
                    bool hasTargetX = HasField(row, "targetX");
                    bool hasTargetZ = HasField(row, "targetZ");
                    if (hasTargetX != hasTargetZ || (!string.IsNullOrEmpty(order.TargetEntityKeyId) && hasTargetX))
                    {
                        error = "Order " + index + " active skill accepts one target entity, one paired position, or no target.";
                        return false;
                    }
                    if (hasTargetX)
                    {
                        order.SkillTargetX = targetX;
                        order.SkillTargetZ = targetZ;
                    }
                    break;
                case "CoordinatedAssault":
                    fields = new[] { "targetEntityKeyId", "maxAttackers" };
                    if (!TryReadString(row, "targetEntityKeyId", true, out order.TargetEntityKeyId, out error, index) ||
                        !TryReadInteger(row, "maxAttackers", false, out order.MaxAttackers, out error, index)) return false;
                    if (order.MaxAttackers < 1 || order.MaxAttackers > 16)
                    {
                        error = "Order " + index + " maxAttackers must be from 1 to 16.";
                        return false;
                    }
                    break;
                case "Retreat":
                    fields = new[] { "threatX", "threatZ", "distance" };
                    if (!RequireSingleActor(order, out error, index) ||
                        !TryReadNumber(row, "threatX", true, out order.ThreatX, out error, index) ||
                        !TryReadNumber(row, "threatZ", true, out order.ThreatZ, out error, index) ||
                        !TryReadNumber(row, "distance", true, out order.Distance, out error, index)) return false;
                    break;
                case "Patrol":
                    fields = new[] { "waypointX", "waypointZ" };
                    if (!RequireSingleActor(order, out error, index) ||
                        !TryReadNumberList(row, "waypointX", out order.WaypointX, out error, index) ||
                        !TryReadNumberList(row, "waypointZ", out order.WaypointZ, out error, index)) return false;
                    if (order.WaypointX.Count < 1 || order.WaypointX.Count != order.WaypointZ.Count ||
                        order.WaypointX.Count > MaxWaypoints)
                    {
                        error = "Order " + index + " patrol needs 1 to " + MaxWaypoints + " paired X/Z waypoints.";
                        return false;
                    }
                    break;
                default:
                    error = "Order " + index + " has an unsupported command type.";
                    return false;
            }

            return EnsureAllowedFields(row, fields, out error, index);
        }

        private static bool TryPreflightActors(
            IReadOnlyList<BattlefieldPlanOrder> orders,
            out int actorCount,
            out string error)
        {
            actorCount = 0;
            error = string.Empty;
            ExistingControllers existing = ExistingControllers.Instance;
            if (existing == null)
            {
                error = "ExistingControllers instance is not available; no orders were dispatched.";
                return false;
            }

            var actorsByKey = new Dictionary<string, EntityController>(StringComparer.OrdinalIgnoreCase);
            var uniqueEntityIds = new HashSet<int>();
            for (int index = 0; index < orders.Count; index++)
            {
                foreach (string key in ActorKeys(orders[index]))
                {
                    EntityController actor = RuntimeEntityKeyResolver.FindUniqueEntityByKey(existing, key);
                    if (actor == null || actor.IsDestroyed || actor.DestroyHasBeenStarted || actor.gameObject == null)
                    {
                        error = "Order " + index + " actor '" + key + "' is missing, destroyed, or ambiguous; no orders were dispatched.";
                        return false;
                    }
                    if (!actor.IsControlledByPlayer || !actor.IsUnit)
                    {
                        error = "Order " + index + " actor '" + key + "' is not a player-controlled unit; no orders were dispatched.";
                        return false;
                    }
                    if (orders[index].CommandType == "ActiveSkill" && !actor.HasActiveSkill)
                    {
                        error = "Order " + index + " actor '" + key + "' has no active skill; no orders were dispatched.";
                        return false;
                    }
                    if (!uniqueEntityIds.Add(actor.UniqueEntityId))
                    {
                        error = "Actor '" + key + "' is assigned more than once in the plan; no orders were dispatched.";
                        return false;
                    }
                    if (orders[index].CommandType == "CoordinatedAssault" && !actor.IsCombatUnit)
                    {
                        error = "Order " + index + " assault actor '" + key + "' is not a combat unit; no orders were dispatched.";
                        return false;
                    }

                    actorsByKey[key] = actor;
                    actorCount++;
                }
            }

            for (int index = 0; index < orders.Count; index++)
            {
                BattlefieldPlanOrder order = orders[index];
                if (order.CommandType == "ActiveSkill" && !string.IsNullOrEmpty(order.TargetEntityKeyId))
                {
                    EntityController caster = actorsByKey[order.EntityKeyId];
                    bool selfSkill = caster.SkillInfo != null && caster.SkillInfo.targetOrigin == TargetOrigin.Self;
                    if (!selfSkill)
                    {
                        EntityController skillTarget = RuntimeEntityKeyResolver.FindUniqueEntityByKey(existing, order.TargetEntityKeyId);
                        if (skillTarget == null || skillTarget.IsDestroyed || skillTarget.DestroyHasBeenStarted || skillTarget.gameObject == null)
                        {
                            error = "Order " + index + " skill target '" + order.TargetEntityKeyId + "' is missing, destroyed, or ambiguous; no orders were dispatched.";
                            return false;
                        }
                    }
                    continue;
                }
                if (order.CommandType != "Attack" && order.CommandType != "CoordinatedAssault") continue;
                EntityController target = RuntimeEntityKeyResolver.FindUniqueEntityByKey(existing, order.TargetEntityKeyId);
                if (target == null || target.IsDestroyed || target.DestroyHasBeenStarted || target.gameObject == null)
                {
                    error = "Order " + index + " target '" + order.TargetEntityKeyId + "' is missing, destroyed, or ambiguous; no orders were dispatched.";
                    return false;
                }

                foreach (string key in ActorKeys(order))
                {
                    EntityController actor = actorsByKey[key];
                    if (actor.IsEnemy == null || !actor.IsEnemy(target))
                    {
                        error = "Order " + index + " target '" + order.TargetEntityKeyId + "' is not verified hostile to actor '" + key + "'; no orders were dispatched.";
                        return false;
                    }
                }
            }

            return true;
        }

        private static JObject DispatchOrder(int index, BattlefieldPlanOrder order)
        {
            string raw;
            int expectedUnits;
            switch (order.CommandType)
            {
                case "Move":
                    raw = AgentDiagnosticsFormatter.SerializeUnitCommandResult(RuntimeGameplayCommandDirector.TryOrderUnitMove(
                        order.EntityKeyId, order.DestinationX, order.DestinationZ, order.ReplaceCurrent));
                    expectedUnits = 1;
                    break;
                case "Attack":
                    raw = AgentDiagnosticsFormatter.SerializeUnitCommandResult(RuntimeGameplayCommandDirector.TryOrderUnitAttack(
                        order.EntityKeyId, order.TargetEntityKeyId));
                    expectedUnits = 1;
                    break;
                case "AttackMove":
                    raw = AgentDiagnosticsFormatter.SerializeUnitCommandResult(RuntimeGameplayCommandDirector.TryOrderAttackMove(
                        order.EntityKeyId, order.TargetX, order.TargetZ));
                    expectedUnits = 1;
                    break;
                case "Stop":
                    raw = AgentDiagnosticsFormatter.SerializeUnitCommandResult(RuntimeGameplayCommandDirector.TryOrderStop(order.EntityKeyId));
                    expectedUnits = 1;
                    break;
                case "HoldPosition":
                    raw = AgentDiagnosticsFormatter.SerializeUnitCommandResult(RuntimeGameplayCommandDirector.TryOrderHoldPosition(order.EntityKeyId));
                    expectedUnits = 1;
                    break;
                case "GroupMove":
                case "GroupStop":
                case "GroupHoldPosition":
                case "GroupAttackMove":
                    raw = AgentDiagnosticsFormatter.SerializeGroupCommandResult(RuntimeGameplayCommandDirector.TryOrderGroupCommand(
                        order.EntityKeyIds, order.GroupCommandType, order.TargetX, order.TargetZ));
                    expectedUnits = order.EntityKeyIds.Count;
                    break;
                case "FormationMove":
                    raw = AgentDiagnosticsFormatter.SerializeFormationOrderResult(RuntimeGameplayCommandDirector.TryOrderFormationMove(
                        order.EntityKeyIds, order.FormationType, order.TargetX, order.TargetZ, order.Spacing));
                    expectedUnits = order.EntityKeyIds.Count;
                    break;
                case "ActiveSkill":
                    raw = AgentDiagnosticsFormatter.SerializeActiveSkillOrderResult(RuntimeGameplayCommandDirector.TryOrderActiveSkill(
                        order.EntityKeyId, order.TargetEntityKeyId, order.SkillTargetX, order.SkillTargetZ));
                    expectedUnits = 1;
                    break;
                case "CoordinatedAssault":
                    raw = AgentDiagnosticsFormatter.SerializeCoordinatedAssaultResult(RuntimeGameplayCommandDirector.TryExecuteCoordinatedAssault(
                        order.TargetEntityKeyId, order.EntityKeyIds, order.MaxAttackers));
                    expectedUnits = Math.Min(order.EntityKeyIds.Count, order.MaxAttackers);
                    break;
                case "Retreat":
                    raw = AgentDiagnosticsFormatter.SerializeRetreatResult(RuntimeGameplayCommandDirector.TryOrderRetreat(
                        order.EntityKeyId, order.ThreatX, order.ThreatZ, order.Distance));
                    expectedUnits = 1;
                    break;
                case "Patrol":
                    raw = AgentDiagnosticsFormatter.SerializeUnitPatrolResult(RuntimeGameplayCommandDirector.TryOrderUnitPatrol(
                        order.EntityKeyId, order.WaypointX, order.WaypointZ));
                    expectedUnits = 1;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported battlefield order '" + order.CommandType + "'.");
            }

            JObject nativeResult = JObject.Parse(raw);
            bool nativeSuccess = nativeResult.Value<bool?>("success") == true;
            int dispatchedUnits = ReadInt(nativeResult, "dispatchedCount") ??
                                  ReadInt(nativeResult, "dispatchedAttackerCount") ??
                                  (nativeSuccess ? 1 : 0);
            bool accepted = nativeSuccess || dispatchedUnits > 0;
            bool complete = nativeSuccess && dispatchedUnits >= expectedUnits;
            return new JObject
            {
                ["orderIndex"] = index,
                ["commandType"] = order.CommandType,
                ["accepted"] = accepted,
                ["complete"] = complete,
                ["partial"] = accepted && !complete,
                ["dispatchedUnitCount"] = dispatchedUnits,
                ["dispatchResult"] = nativeResult,
            };
        }

        private static string FailureJson(bool dispatchStarted, int orderCount, string error)
        {
            return new JObject
            {
                ["kind"] = "battlefieldOrderPlanResult",
                ["success"] = false,
                ["planApplied"] = false,
                ["dispatchStarted"] = dispatchStarted,
                ["orderCount"] = orderCount,
                ["dispatchedOrderCount"] = 0,
                ["rollbackPerformed"] = false,
                ["error"] = error ?? string.Empty,
            }.ToString(Formatting.None);
        }

        private static int? ReadInt(JObject obj, string name)
        {
            JToken token;
            return obj.TryGetValue(name, out token) && token.Type == JTokenType.Integer
                ? (int?)token.Value<int>()
                : null;
        }

        private static List<string> ActorKeys(BattlefieldPlanOrder order)
        {
            return order.EntityKeyIds;
        }

        private static string CanonicalCommandType(string value)
        {
            string[] values = { "Move", "Attack", "AttackMove", "Stop", "HoldPosition", "GroupMove", "GroupStop", "GroupHoldPosition", "GroupAttackMove", "FormationMove", "ActiveSkill", "CoordinatedAssault", "Retreat", "Patrol" };
            return values.FirstOrDefault(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static string CanonicalFormation(string value)
        {
            string[] values = { "Line", "Wedge", "Column", "Box", "Surround" };
            return values.FirstOrDefault(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static bool EnsureAllowedFields(JObject row, IEnumerable<string> allowed, out string error, int index)
        {
            var fieldSet = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (JProperty property in row.Properties())
            {
                if (!fieldSet.Contains(property.Name))
                {
                    error = "Order " + index + " contains unsupported field '" + property.Name + "'.";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private static bool RequireSingleActor(BattlefieldPlanOrder order, out string error, int index)
        {
            if (order.EntityKeyIds.Count != 1)
            {
                error = "Order " + index + " commandType '" + order.CommandType + "' requires exactly one entityKeyIds entry.";
                return false;
            }
            order.EntityKeyId = order.EntityKeyIds[0];
            error = string.Empty;
            return true;
        }

        private static bool TryReadString(JObject row, string name, bool required, out string value, out string error, int index)
        {
            value = string.Empty;
            JToken token;
            bool found = row.TryGetValue(name, out token);
            if (!found && !required) { error = string.Empty; return true; }
            if (!found || token.Type != JTokenType.String)
            {
                error = "Order " + index + " requires string field '" + name + "'.";
                return false;
            }
            value = (token.Value<string>() ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                error = "Order " + index + " field '" + name + "' cannot be empty.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool TryReadNumber(JObject row, string name, bool required, out float value, out string error, int index)
        {
            value = 0f;
            JToken token;
            bool found = row.TryGetValue(name, out token);
            if (!found && !required) { error = string.Empty; return true; }
            if (!found || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
            {
                error = "Order " + index + " requires finite numeric field '" + name + "'.";
                return false;
            }
            try { value = token.Value<float>(); }
            catch { error = "Order " + index + " field '" + name + "' is outside the supported numeric range."; return false; }
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Order " + index + " field '" + name + "' must be finite.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool TryReadBoolean(JObject row, string name, bool defaultValue, out bool value, out string error, int index)
        {
            value = defaultValue;
            JToken token;
            if (!row.TryGetValue(name, out token)) { error = string.Empty; return true; }
            if (token.Type != JTokenType.Boolean)
            {
                error = "Order " + index + " field '" + name + "' must be boolean.";
                return false;
            }
            value = token.Value<bool>();
            error = string.Empty;
            return true;
        }

        private static bool TryReadInteger(JObject row, string name, bool required, out int value, out string error, int index)
        {
            value = 16;
            JToken token;
            bool found = row.TryGetValue(name, out token);
            if (!found && !required) { error = string.Empty; return true; }
            if (!found || token.Type != JTokenType.Integer)
            {
                error = "Order " + index + " requires integer field '" + name + "'.";
                return false;
            }
            try { value = token.Value<int>(); }
            catch { error = "Order " + index + " field '" + name + "' is outside the supported integer range."; return false; }
            error = string.Empty;
            return true;
        }

        private static bool TryReadStringList(JObject row, string name, out List<string> values, out string error, int index)
        {
            values = new List<string>();
            JToken token;
            if (!row.TryGetValue(name, out token) || !(token is JArray array) || array.Count == 0 || array.Count > MaxEntities)
            {
                error = "Order " + index + " field '" + name + "' must contain 1 to " + MaxEntities + " entity keys.";
                return false;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken entry in array)
            {
                if (entry.Type != JTokenType.String)
                {
                    error = "Order " + index + " field '" + name + "' must contain only strings.";
                    return false;
                }
                string key = (entry.Value<string>() ?? string.Empty).Trim();
                if (key.Length == 0 || !seen.Add(key))
                {
                    error = "Order " + index + " field '" + name + "' contains an empty or duplicate entity key.";
                    return false;
                }
                values.Add(key);
            }
            error = string.Empty;
            return true;
        }

        private static bool TryReadNumberList(JObject row, string name, out List<float> values, out string error, int index)
        {
            values = new List<float>();
            JToken token;
            if (!row.TryGetValue(name, out token) || !(token is JArray array) || array.Count == 0 || array.Count > MaxWaypoints)
            {
                error = "Order " + index + " field '" + name + "' must contain 1 to " + MaxWaypoints + " finite numbers.";
                return false;
            }
            foreach (JToken entry in array)
            {
                if (entry.Type != JTokenType.Integer && entry.Type != JTokenType.Float)
                {
                    error = "Order " + index + " field '" + name + "' must contain only finite numbers.";
                    return false;
                }
                float value;
                try { value = entry.Value<float>(); }
                catch { error = "Order " + index + " field '" + name + "' contains a number outside the supported range."; return false; }
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    error = "Order " + index + " field '" + name + "' must contain only finite numbers.";
                    return false;
                }
                values.Add(value);
            }
            error = string.Empty;
            return true;
        }

        private static bool HasField(JObject row, string name) => row.Property(name, StringComparison.Ordinal) != null;
    }
}
