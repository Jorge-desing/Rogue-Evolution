using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    public readonly struct AgentSessionRecord
    {
        public readonly string AgentId;
        public readonly string AgentRole;
        public readonly string Notes;
        public readonly string Status;
        public readonly float SecondsSinceLastPing;
        public readonly string AssignedSector;
        public readonly IReadOnlyList<string> Capabilities;
        public readonly int CommandsDispatchedCount;
        public readonly float RegisteredAtSeconds;
        public readonly float LastPingSeconds;
        public readonly int PingCount;

        public AgentSessionRecord(
            string agentId,
            string agentRole,
            string notes,
            string status,
            float secondsSinceLastPing,
            string assignedSector,
            IReadOnlyList<string> capabilities,
            int commandsDispatchedCount,
            float registeredAtSeconds,
            float lastPingSeconds,
            int pingCount)
        {
            AgentId = agentId ?? string.Empty;
            AgentRole = agentRole ?? "GenericAgent";
            Notes = notes ?? string.Empty;
            Status = status ?? "Active";
            SecondsSinceLastPing = Math.Max(0f, secondsSinceLastPing);
            AssignedSector = assignedSector ?? "Global";
            Capabilities = capabilities ?? Array.Empty<string>();
            CommandsDispatchedCount = Math.Max(0, commandsDispatchedCount);
            RegisteredAtSeconds = registeredAtSeconds;
            LastPingSeconds = lastPingSeconds;
            PingCount = pingCount;
        }

        public AgentSessionRecord(
            string agentId,
            string agentRole,
            string notes,
            float registeredAtSeconds,
            float lastPingSeconds,
            int pingCount)
            : this(agentId, agentRole, notes, "Active", 0f, "Global", Array.Empty<string>(), 0, registeredAtSeconds, lastPingSeconds, pingCount)
        {
        }
    }

    public readonly struct AgentAdvisoryLock
    {
        public readonly string ResourceKey;
        public readonly string ResourceCategory;
        public readonly string OwnerAgentId;
        public readonly float AcquiredAtSeconds;
        public readonly float ExpiresAtSeconds;
        public readonly float RemainingSeconds;
        public readonly int ContentionCount;

        public AgentAdvisoryLock(
            string resourceKey,
            string resourceCategory,
            string ownerAgentId,
            float acquiredAtSeconds,
            float expiresAtSeconds,
            float remainingSeconds,
            int contentionCount = 0)
        {
            ResourceKey = resourceKey ?? string.Empty;
            ResourceCategory = resourceCategory ?? "Global";
            OwnerAgentId = ownerAgentId ?? string.Empty;
            AcquiredAtSeconds = acquiredAtSeconds;
            ExpiresAtSeconds = expiresAtSeconds;
            RemainingSeconds = Math.Max(0f, remainingSeconds);
            ContentionCount = Math.Max(0, contentionCount);
        }

        public AgentAdvisoryLock(
            string resourceKey,
            string ownerAgentId,
            float acquiredAtSeconds,
            float expiresAtSeconds,
            float remainingSeconds)
            : this(resourceKey, "Global", ownerAgentId, acquiredAtSeconds, expiresAtSeconds, remainingSeconds, 0)
        {
        }
    }

    public readonly struct AgentCoordinationMessage
    {
        public readonly string MessageId;
        public readonly string SenderAgentId;
        public readonly string MessageType;
        public readonly string Content;
        public readonly float TimestampSeconds;
        public readonly string TargetAgentId;

        public AgentCoordinationMessage(
            string messageId,
            string senderAgentId,
            string messageType,
            string content,
            float timestampSeconds,
            string targetAgentId = "all")
        {
            MessageId = messageId ?? string.Empty;
            SenderAgentId = senderAgentId ?? string.Empty;
            MessageType = messageType ?? "TacticalIntent";
            Content = content ?? string.Empty;
            TimestampSeconds = timestampSeconds;
            TargetAgentId = string.IsNullOrWhiteSpace(targetAgentId) ? "all" : targetAgentId.Trim();
        }
    }

    public sealed class AgentCoordinationSnapshot
    {
        public int OmittedSessionCount { get; }
        public int OmittedLockCount { get; }
        public int OmittedMessageCount { get; }
        public int TotalSessionCount { get; }
        public int TotalLockCount { get; }
        public int TotalMessageCount { get; }
        public int ActiveSessionCount { get; }
        public int ActiveLockCount { get; }
        public IReadOnlyList<AgentSessionRecord> ActiveSessions { get; }
        public IReadOnlyList<AgentAdvisoryLock> ActiveLocks { get; }
        public IReadOnlyList<AgentCoordinationMessage> RecentMessages { get; }

        public AgentCoordinationSnapshot(
            int omittedSessionCount,
            int omittedLockCount,
            int omittedMessageCount,
            int totalSessionCount,
            int totalLockCount,
            int totalMessageCount,
            int activeSessionCount,
            int activeLockCount,
            IReadOnlyList<AgentSessionRecord> activeSessions,
            IReadOnlyList<AgentAdvisoryLock> activeLocks,
            IReadOnlyList<AgentCoordinationMessage> recentMessages)
        {
            OmittedSessionCount = Math.Max(0, omittedSessionCount);
            OmittedLockCount = Math.Max(0, omittedLockCount);
            OmittedMessageCount = Math.Max(0, omittedMessageCount);
            TotalSessionCount = Math.Max(0, totalSessionCount);
            TotalLockCount = Math.Max(0, totalLockCount);
            TotalMessageCount = Math.Max(0, totalMessageCount);
            ActiveSessionCount = Math.Max(0, activeSessionCount);
            ActiveLockCount = Math.Max(0, activeLockCount);
            ActiveSessions = activeSessions ?? Array.Empty<AgentSessionRecord>();
            ActiveLocks = activeLocks ?? Array.Empty<AgentAdvisoryLock>();
            RecentMessages = recentMessages ?? Array.Empty<AgentCoordinationMessage>();
        }

        public AgentCoordinationSnapshot(
            int omittedSessionCount,
            int omittedLockCount,
            IReadOnlyList<AgentSessionRecord> activeSessions,
            IReadOnlyList<AgentAdvisoryLock> activeLocks)
            : this(omittedSessionCount, omittedLockCount, 0,
                   activeSessions?.Count ?? 0, activeLocks?.Count ?? 0, 0,
                   activeSessions?.Count ?? 0, activeLocks?.Count ?? 0,
                   activeSessions, activeLocks, Array.Empty<AgentCoordinationMessage>())
        {
        }

        public static AgentCoordinationSnapshot Empty { get; } =
            new AgentCoordinationSnapshot(0, 0, 0, 0, 0, 0, 0, 0,
                Array.Empty<AgentSessionRecord>(), Array.Empty<AgentAdvisoryLock>(), Array.Empty<AgentCoordinationMessage>());
    }

    public static class RuntimeAgentCoordinator
    {
        private static readonly object _sync = new object();
        private const int MaxTrackedSessions = 16;
        private const int MaxTrackedLocks = 32;
        private const int MaxTrackedMessages = 32;
        private const float DefaultSessionTtlSeconds = 300f;
        private const float MaxLockDurationSeconds = 120f;
        private const float ActiveThresholdSeconds = 30f;
        private const float IdleThresholdSeconds = 120f;

        private sealed class InternalSession
        {
            public string AgentId;
            public string AgentRole;
            public string Notes;
            public string AssignedSector;
            public List<string> Capabilities;
            public int CommandsDispatched;
            public float RegisteredAt;
            public float LastPing;
            public int PingCount;
        }

        private sealed class InternalLock
        {
            public string ResourceKey;
            public string ResourceCategory;
            public string OwnerAgentId;
            public float AcquiredAt;
            public float ExpiresAt;
            public int ContentionCount;
        }

        private static readonly Dictionary<string, InternalSession> _sessions =
            new Dictionary<string, InternalSession>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, InternalLock> _locks =
            new Dictionary<string, InternalLock>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<AgentCoordinationMessage> _messages =
            new List<AgentCoordinationMessage>();
        private static int _messageSequence = 0;

        public static int RegisteredSessionCount
        {
            get
            {
                lock (_sync)
                {
                    return _sessions.Count;
                }
            }
        }

        public static int ActiveLockCount
        {
            get
            {
                lock (_sync)
                {
                    return _locks.Count;
                }
            }
        }

        public static int TotalMessageCount
        {
            get
            {
                lock (_sync)
                {
                    return _messages.Count;
                }
            }
        }

        public static void ResetForTests()
        {
            lock (_sync)
            {
                _sessions.Clear();
                _locks.Clear();
                _messages.Clear();
                _messageSequence = 0;
            }
        }

        public static string ResolveCategory(string resourceKey)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) return "Global";
            string k = resourceKey.ToLowerInvariant();
            if (k.StartsWith("unit:") || k.StartsWith("u_")) return "Unit";
            if (k.StartsWith("building:") || k.StartsWith("b_")) return "Building";
            if (k.StartsWith("factory:") || k.StartsWith("f_") || k.Contains("factory")) return "Factory";
            if (k.StartsWith("camera") || k.Contains("viewport")) return "Camera";
            if (k.StartsWith("director") || k.Contains("wave") || k.Contains("doctrine")) return "Director";
            return "Global";
        }

        public static string ResolveStatus(float currentTime, float lastPing)
        {
            float delta = currentTime - lastPing;
            if (delta <= ActiveThresholdSeconds) return "Active";
            if (delta <= IdleThresholdSeconds) return "Idle";
            return "Stale";
        }

        public static bool TryRegisterSession(
            string agentId,
            string agentRole,
            string notes,
            float currentTime,
            out string resultMessage)
        {
            return TryRegisterSession(agentId, agentRole, notes, "Global", null, currentTime, out resultMessage);
        }

        public static bool TryRegisterSession(
            string agentId,
            string agentRole,
            string notes,
            string assignedSector,
            IReadOnlyList<string> capabilities,
            float currentTime,
            out string resultMessage)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                resultMessage = "AgentId must not be null or whitespace.";
                return false;
            }

            string cleanId = agentId.Trim();
            string cleanRole = string.IsNullOrWhiteSpace(agentRole) ? "Agent" : agentRole.Trim();
            string cleanNotes = notes?.Trim() ?? string.Empty;
            string cleanSector = string.IsNullOrWhiteSpace(assignedSector) ? "Global" : assignedSector.Trim();
            var caps = capabilities != null ? new List<string>(capabilities) : new List<string> { "TacticalMicro" };

            lock (_sync)
            {
                PruneExpired(currentTime);

                if (_sessions.TryGetValue(cleanId, out InternalSession existing))
                {
                    existing.AgentRole = cleanRole;
                    existing.Notes = cleanNotes;
                    existing.AssignedSector = cleanSector;
                    existing.Capabilities = caps;
                    existing.LastPing = currentTime;
                    existing.PingCount++;
                    resultMessage = $"Updated active session for agent '{cleanId}' (pings: {existing.PingCount}, sector: {cleanSector}).";
                    return true;
                }

                if (_sessions.Count >= MaxTrackedSessions)
                {
                    resultMessage = $"Max active agent sessions reached ({MaxTrackedSessions}).";
                    return false;
                }

                _sessions[cleanId] = new InternalSession
                {
                    AgentId = cleanId,
                    AgentRole = cleanRole,
                    Notes = cleanNotes,
                    AssignedSector = cleanSector,
                    Capabilities = caps,
                    CommandsDispatched = 0,
                    RegisteredAt = currentTime,
                    LastPing = currentTime,
                    PingCount = 1
                };

                resultMessage = $"Registered agent '{cleanId}' with role '{cleanRole}' in sector '{cleanSector}'.";
                return true;
            }
        }

        public static bool TryPing(
            string agentId,
            string message,
            float currentTime,
            out string replyMessage)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                replyMessage = "AgentId must not be null or whitespace.";
                return false;
            }

            string cleanId = agentId.Trim();
            lock (_sync)
            {
                PruneExpired(currentTime);

                if (_sessions.TryGetValue(cleanId, out InternalSession session))
                {
                    session.LastPing = currentTime;
                    session.PingCount++;
                    string status = ResolveStatus(currentTime, session.LastPing);
                    replyMessage = $"Pong from Rogue Command runtime to '{cleanId}' (status: {status}, pings: {session.PingCount}). Msg: {message ?? "none"}";
                    return true;
                }

                if (_sessions.Count < MaxTrackedSessions)
                {
                    _sessions[cleanId] = new InternalSession
                    {
                        AgentId = cleanId,
                        AgentRole = "AutoPingAgent",
                        Notes = message ?? string.Empty,
                        AssignedSector = "Global",
                        Capabilities = new List<string> { "AutoPing" },
                        CommandsDispatched = 0,
                        RegisteredAt = currentTime,
                        LastPing = currentTime,
                        PingCount = 1
                    };
                    replyMessage = $"Pong to newly registered agent '{cleanId}'. Msg: {message ?? "none"}";
                    return true;
                }

                replyMessage = $"Pong to unregistered agent '{cleanId}' (session table full).";
                return true;
            }
        }

        public static void RecordCommandDispatched(string agentId)
        {
            if (string.IsNullOrWhiteSpace(agentId)) return;
            string cleanId = agentId.Trim();
            lock (_sync)
            {
                if (_sessions.TryGetValue(cleanId, out InternalSession session))
                {
                    session.CommandsDispatched++;
                }
            }
        }

        public static bool TryDeregisterSession(
            string agentId,
            out string resultMessage)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                resultMessage = "AgentId cannot be empty.";
                return false;
            }

            string cleanId = agentId.Trim();
            lock (_sync)
            {
                bool sessionFound = _sessions.Remove(cleanId);

                int releasedLocks = 0;
                var locksToRemove = new List<string>();
                foreach (var kvp in _locks)
                {
                    if (string.Equals(kvp.Value.OwnerAgentId, cleanId, StringComparison.OrdinalIgnoreCase))
                    {
                        locksToRemove.Add(kvp.Key);
                    }
                }
                for (int i = 0; i < locksToRemove.Count; i++)
                {
                    _locks.Remove(locksToRemove[i]);
                    releasedLocks++;
                }

                if (sessionFound)
                {
                    resultMessage = $"Deregistered session for agent '{cleanId}' and released {releasedLocks} lock(s).";
                    return true;
                }

                if (releasedLocks > 0)
                {
                    resultMessage = $"Released {releasedLocks} orphaned lock(s) for agent '{cleanId}' (session not found).";
                    return true;
                }

                resultMessage = $"Session for agent '{cleanId}' not found.";
                return false;
            }
        }

        public static bool TryPostMessage(
            string senderAgentId,
            string messageType,
            string content,
            string targetAgentId,
            float currentTime,
            out string resultMessage)
        {
            if (string.IsNullOrWhiteSpace(senderAgentId))
            {
                resultMessage = "SenderAgentId cannot be empty.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(content))
            {
                resultMessage = "Message content cannot be empty.";
                return false;
            }

            string cleanSender = senderAgentId.Trim();
            string cleanType = string.IsNullOrWhiteSpace(messageType) ? "TacticalIntent" : messageType.Trim();
            string cleanContent = content.Trim();
            string cleanTarget = string.IsNullOrWhiteSpace(targetAgentId) ? "all" : targetAgentId.Trim();

            lock (_sync)
            {
                _messageSequence++;
                string msgId = "msg_" + _messageSequence;
                var msg = new AgentCoordinationMessage(msgId, cleanSender, cleanType, cleanContent, currentTime, cleanTarget);

                if (_messages.Count >= MaxTrackedMessages)
                {
                    _messages.RemoveAt(0);
                }
                _messages.Add(msg);

                resultMessage = $"Posted coordination message '{msgId}' ({cleanType}) from '{cleanSender}' to '{cleanTarget}'.";
                return true;
            }
        }

        public static bool TryAcquireLock(
            string agentId,
            string resourceKey,
            float durationSeconds,
            float currentTime,
            out string resultMessage)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                resultMessage = "AgentId cannot be empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(resourceKey))
            {
                resultMessage = "ResourceKey cannot be empty.";
                return false;
            }

            string cleanAgent = agentId.Trim();
            string cleanResource = resourceKey.Trim();
            string category = ResolveCategory(cleanResource);
            float clampedDuration = Math.Max(1.0f, Math.Min(durationSeconds <= 0f ? 30.0f : durationSeconds, MaxLockDurationSeconds));

            lock (_sync)
            {
                PruneExpired(currentTime);

                if (_locks.TryGetValue(cleanResource, out InternalLock currentLock))
                {
                    if (currentLock.ExpiresAt > currentTime &&
                        !string.Equals(currentLock.OwnerAgentId, cleanAgent, StringComparison.OrdinalIgnoreCase))
                    {
                        currentLock.ContentionCount++;
                        float remaining = currentLock.ExpiresAt - currentTime;
                        resultMessage = $"Resource '{cleanResource}' ({currentLock.ResourceCategory}) is locked by '{currentLock.OwnerAgentId}' for {remaining:F1}s more (contentions: {currentLock.ContentionCount}).";
                        return false;
                    }

                    currentLock.OwnerAgentId = cleanAgent;
                    currentLock.ResourceCategory = category;
                    currentLock.AcquiredAt = currentTime;
                    currentLock.ExpiresAt = currentTime + clampedDuration;
                    resultMessage = $"Renewed advisory lock on '{cleanResource}' ({category}) for agent '{cleanAgent}' ({clampedDuration:F1}s).";
                    return true;
                }

                if (_locks.Count >= MaxTrackedLocks)
                {
                    resultMessage = $"Maximum active advisory locks reached ({MaxTrackedLocks}).";
                    return false;
                }

                _locks[cleanResource] = new InternalLock
                {
                    ResourceKey = cleanResource,
                    ResourceCategory = category,
                    OwnerAgentId = cleanAgent,
                    AcquiredAt = currentTime,
                    ExpiresAt = currentTime + clampedDuration,
                    ContentionCount = 0
                };

                resultMessage = $"Acquired advisory lock on '{cleanResource}' ({category}) for agent '{cleanAgent}' ({clampedDuration:F1}s).";
                return true;
            }
        }

        public static bool TryAcquireLockBatch(
            string agentId,
            IReadOnlyList<string> resourceKeys,
            float durationSeconds,
            float currentTime,
            out string resultMessage)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                resultMessage = "AgentId cannot be empty.";
                return false;
            }
            if (resourceKeys == null || resourceKeys.Count == 0)
            {
                resultMessage = "ResourceKeys list cannot be empty.";
                return false;
            }

            int acquired = 0;
            string lastFailure = string.Empty;
            for (int i = 0; i < resourceKeys.Count; i++)
            {
                string rk = resourceKeys[i];
                if (string.IsNullOrWhiteSpace(rk)) continue;
                if (TryAcquireLock(agentId, rk, durationSeconds, currentTime, out string msg))
                {
                    acquired++;
                }
                else
                {
                    lastFailure = msg;
                }
            }

            if (acquired == resourceKeys.Count)
            {
                resultMessage = $"Acquired batch locks on all {acquired} resource(s) for agent '{agentId.Trim()}'.";
                return true;
            }

            if (acquired > 0)
            {
                resultMessage = $"Acquired {acquired} of {resourceKeys.Count} lock(s) for agent '{agentId.Trim()}'. Last conflict: {lastFailure}";
                return true;
            }

            resultMessage = $"Failed to acquire any locks for agent '{agentId.Trim()}'. Last conflict: {lastFailure}";
            return false;
        }

        public static bool TryReleaseLock(
            string agentId,
            string resourceKey,
            out string resultMessage)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
            {
                resultMessage = "ResourceKey cannot be empty.";
                return false;
            }

            string cleanResource = resourceKey.Trim();
            string cleanAgent = agentId?.Trim() ?? string.Empty;

            lock (_sync)
            {
                if (!_locks.TryGetValue(cleanResource, out InternalLock currentLock))
                {
                    resultMessage = $"Resource '{cleanResource}' was not locked.";
                    return true;
                }

                if (!string.IsNullOrEmpty(cleanAgent) &&
                    !string.Equals(currentLock.OwnerAgentId, cleanAgent, StringComparison.OrdinalIgnoreCase))
                {
                    resultMessage = $"Agent '{cleanAgent}' is not the owner of lock '{cleanResource}' (owned by '{currentLock.OwnerAgentId}').";
                    return false;
                }

                _locks.Remove(cleanResource);
                resultMessage = $"Released advisory lock on '{cleanResource}'.";
                return true;
            }
        }

        public static bool TryReleaseLockBatch(
            string agentId,
            IReadOnlyList<string> resourceKeys,
            out string resultMessage)
        {
            if (resourceKeys == null || resourceKeys.Count == 0)
            {
                resultMessage = "ResourceKeys list cannot be empty.";
                return false;
            }

            int released = 0;
            for (int i = 0; i < resourceKeys.Count; i++)
            {
                string rk = resourceKeys[i];
                if (string.IsNullOrWhiteSpace(rk)) continue;
                if (TryReleaseLock(agentId, rk, out _))
                {
                    released++;
                }
            }

            resultMessage = $"Released {released} of {resourceKeys.Count} requested lock(s) for agent '{agentId?.Trim() ?? "any"}'.";
            return released > 0;
        }

        public static AgentCoordinationSnapshot GetSnapshot(float currentTime)
        {
            lock (_sync)
            {
                PruneExpired(currentTime);

                int totalSessions = _sessions.Count;
                var sessions = new List<AgentSessionRecord>(totalSessions);
                int omittedSessions = 0;
                foreach (InternalSession s in _sessions.Values)
                {
                    if (sessions.Count < MaxTrackedSessions)
                    {
                        float delta = Math.Max(0f, currentTime - s.LastPing);
                        string status = ResolveStatus(currentTime, s.LastPing);
                        sessions.Add(new AgentSessionRecord(
                            s.AgentId,
                            s.AgentRole,
                            s.Notes,
                            status,
                            delta,
                            s.AssignedSector ?? "Global",
                            s.Capabilities != null ? s.Capabilities.AsReadOnly() : Array.Empty<string>(),
                            s.CommandsDispatched,
                            s.RegisteredAt,
                            s.LastPing,
                            s.PingCount));
                    }
                    else
                    {
                        omittedSessions++;
                    }
                }

                int totalLocks = _locks.Count;
                var locks = new List<AgentAdvisoryLock>(totalLocks);
                int omittedLocks = 0;
                foreach (InternalLock l in _locks.Values)
                {
                    if (locks.Count < MaxTrackedLocks)
                    {
                        float remaining = Math.Max(0f, l.ExpiresAt - currentTime);
                        locks.Add(new AgentAdvisoryLock(
                            l.ResourceKey,
                            l.ResourceCategory ?? "Global",
                            l.OwnerAgentId,
                            l.AcquiredAt,
                            l.ExpiresAt,
                            remaining,
                            l.ContentionCount));
                    }
                    else
                    {
                        omittedLocks++;
                    }
                }

                int totalMsgs = _messages.Count;
                var msgs = new List<AgentCoordinationMessage>(totalMsgs);
                int omittedMsgs = 0;
                for (int i = 0; i < _messages.Count; i++)
                {
                    if (msgs.Count < MaxTrackedMessages)
                    {
                        msgs.Add(_messages[i]);
                    }
                    else
                    {
                        omittedMsgs++;
                    }
                }

                return new AgentCoordinationSnapshot(
                    omittedSessions,
                    omittedLocks,
                    omittedMsgs,
                    totalSessions,
                    totalLocks,
                    totalMsgs,
                    sessions.Count,
                    locks.Count,
                    sessions,
                    locks,
                    msgs);
            }
        }

        private static void PruneExpired(float currentTime)
        {
            var expiredLocks = new List<string>();
            foreach (var kvp in _locks)
            {
                if (currentTime >= kvp.Value.ExpiresAt)
                {
                    expiredLocks.Add(kvp.Key);
                }
            }
            for (int i = 0; i < expiredLocks.Count; i++)
            {
                _locks.Remove(expiredLocks[i]);
            }

            var staleSessions = new List<string>();
            foreach (var kvp in _sessions)
            {
                if (currentTime - kvp.Value.LastPing > DefaultSessionTtlSeconds)
                {
                    staleSessions.Add(kvp.Key);
                }
            }
            for (int i = 0; i < staleSessions.Count; i++)
            {
                _sessions.Remove(staleSessions[i]);
            }
        }
    }
}
