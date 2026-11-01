using System;
using System.Collections.Generic;

namespace RogueCommandExpansionLoader.Runtime
{
    public readonly struct AgentSessionRecord
    {
        public readonly string AgentId;
        public readonly string AgentRole;
        public readonly string Notes;
        public readonly float RegisteredAtSeconds;
        public readonly float LastPingSeconds;
        public readonly int PingCount;

        public AgentSessionRecord(
            string agentId,
            string agentRole,
            string notes,
            float registeredAtSeconds,
            float lastPingSeconds,
            int pingCount)
        {
            AgentId = agentId ?? string.Empty;
            AgentRole = agentRole ?? "GenericAgent";
            Notes = notes ?? string.Empty;
            RegisteredAtSeconds = registeredAtSeconds;
            LastPingSeconds = lastPingSeconds;
            PingCount = pingCount;
        }
    }

    public readonly struct AgentAdvisoryLock
    {
        public readonly string ResourceKey;
        public readonly string OwnerAgentId;
        public readonly float AcquiredAtSeconds;
        public readonly float ExpiresAtSeconds;
        public readonly float RemainingSeconds;

        public AgentAdvisoryLock(
            string resourceKey,
            string ownerAgentId,
            float acquiredAtSeconds,
            float expiresAtSeconds,
            float remainingSeconds)
        {
            ResourceKey = resourceKey ?? string.Empty;
            OwnerAgentId = ownerAgentId ?? string.Empty;
            AcquiredAtSeconds = acquiredAtSeconds;
            ExpiresAtSeconds = expiresAtSeconds;
            RemainingSeconds = Math.Max(0f, remainingSeconds);
        }
    }

    public sealed class AgentCoordinationSnapshot
    {
        public int OmittedSessionCount { get; }
        public int OmittedLockCount { get; }
        public IReadOnlyList<AgentSessionRecord> ActiveSessions { get; }
        public IReadOnlyList<AgentAdvisoryLock> ActiveLocks { get; }

        public AgentCoordinationSnapshot(
            int omittedSessionCount,
            int omittedLockCount,
            IReadOnlyList<AgentSessionRecord> activeSessions,
            IReadOnlyList<AgentAdvisoryLock> activeLocks)
        {
            OmittedSessionCount = Math.Max(0, omittedSessionCount);
            OmittedLockCount = Math.Max(0, omittedLockCount);
            ActiveSessions = activeSessions ?? Array.Empty<AgentSessionRecord>();
            ActiveLocks = activeLocks ?? Array.Empty<AgentAdvisoryLock>();
        }

        public static AgentCoordinationSnapshot Empty { get; } =
            new AgentCoordinationSnapshot(0, 0, Array.Empty<AgentSessionRecord>(), Array.Empty<AgentAdvisoryLock>());
    }

    public static class RuntimeAgentCoordinator
    {
        private static readonly object _sync = new object();
        private const int MaxTrackedSessions = 16;
        private const int MaxTrackedLocks = 16;
        private const float DefaultSessionTtlSeconds = 300f;
        private const float MaxLockDurationSeconds = 120f;

        private sealed class InternalSession
        {
            public string AgentId;
            public string AgentRole;
            public string Notes;
            public float RegisteredAt;
            public float LastPing;
            public int PingCount;
        }

        private sealed class InternalLock
        {
            public string ResourceKey;
            public string OwnerAgentId;
            public float AcquiredAt;
            public float ExpiresAt;
        }

        private static readonly Dictionary<string, InternalSession> _sessions =
            new Dictionary<string, InternalSession>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, InternalLock> _locks =
            new Dictionary<string, InternalLock>(StringComparer.OrdinalIgnoreCase);

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

        public static void ResetForTests()
        {
            lock (_sync)
            {
                _sessions.Clear();
                _locks.Clear();
            }
        }

        public static bool TryRegisterSession(
            string agentId,
            string agentRole,
            string notes,
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

            lock (_sync)
            {
                PruneExpired(currentTime);

                if (_sessions.TryGetValue(cleanId, out InternalSession existing))
                {
                    existing.AgentRole = cleanRole;
                    existing.Notes = cleanNotes;
                    existing.LastPing = currentTime;
                    existing.PingCount++;
                    resultMessage = $"Updated active session for agent '{cleanId}' (pings: {existing.PingCount}).";
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
                    RegisteredAt = currentTime,
                    LastPing = currentTime,
                    PingCount = 1
                };

                resultMessage = $"Registered agent '{cleanId}' with role '{cleanRole}'.";
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
                    replyMessage = $"Pong from Rogue Command runtime to '{cleanId}'. Active pings: {session.PingCount}. Msg: {message ?? "none"}";
                    return true;
                }

                if (_sessions.Count < MaxTrackedSessions)
                {
                    _sessions[cleanId] = new InternalSession
                    {
                        AgentId = cleanId,
                        AgentRole = "AutoPingAgent",
                        Notes = message ?? string.Empty,
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
            float clampedDuration = Math.Max(1.0f, Math.Min(durationSeconds <= 0f ? 30.0f : durationSeconds, MaxLockDurationSeconds));

            lock (_sync)
            {
                PruneExpired(currentTime);

                if (_locks.TryGetValue(cleanResource, out InternalLock currentLock))
                {
                    if (currentLock.ExpiresAt > currentTime &&
                        !string.Equals(currentLock.OwnerAgentId, cleanAgent, StringComparison.OrdinalIgnoreCase))
                    {
                        float remaining = currentLock.ExpiresAt - currentTime;
                        resultMessage = $"Resource '{cleanResource}' is currently locked by agent '{currentLock.OwnerAgentId}' for {remaining:F1}s more.";
                        return false;
                    }

                    currentLock.OwnerAgentId = cleanAgent;
                    currentLock.AcquiredAt = currentTime;
                    currentLock.ExpiresAt = currentTime + clampedDuration;
                    resultMessage = $"Renewed advisory lock on '{cleanResource}' for agent '{cleanAgent}' ({clampedDuration:F1}s).";
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
                    OwnerAgentId = cleanAgent,
                    AcquiredAt = currentTime,
                    ExpiresAt = currentTime + clampedDuration
                };

                resultMessage = $"Acquired advisory lock on '{cleanResource}' for agent '{cleanAgent}' ({clampedDuration:F1}s).";
                return true;
            }
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

        public static AgentCoordinationSnapshot GetSnapshot(float currentTime)
        {
            lock (_sync)
            {
                PruneExpired(currentTime);

                var sessions = new List<AgentSessionRecord>(_sessions.Count);
                int omittedSessions = 0;
                foreach (InternalSession s in _sessions.Values)
                {
                    if (sessions.Count < MaxTrackedSessions)
                    {
                        sessions.Add(new AgentSessionRecord(
                            s.AgentId,
                            s.AgentRole,
                            s.Notes,
                            s.RegisteredAt,
                            s.LastPing,
                            s.PingCount));
                    }
                    else
                    {
                        omittedSessions++;
                    }
                }

                var locks = new List<AgentAdvisoryLock>(_locks.Count);
                int omittedLocks = 0;
                foreach (InternalLock l in _locks.Values)
                {
                    if (locks.Count < MaxTrackedLocks)
                    {
                        float remaining = Math.Max(0f, l.ExpiresAt - currentTime);
                        locks.Add(new AgentAdvisoryLock(
                            l.ResourceKey,
                            l.OwnerAgentId,
                            l.AcquiredAt,
                            l.ExpiresAt,
                            remaining));
                    }
                    else
                    {
                        omittedLocks++;
                    }
                }

                return new AgentCoordinationSnapshot(
                    omittedSessions,
                    omittedLocks,
                    sessions,
                    locks);
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
