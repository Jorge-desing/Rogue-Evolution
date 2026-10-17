using System;
using System.Collections.Generic;
using RogueCommandExpansionLoader.Contracts;

namespace RogueCommandExpansionLoader.Modules
{
    /// <summary>
    /// Runs an ordered collection of compiled AI modules against one game-owned AI instance.
    /// Lifecycle callbacks are dispatched once and cleanup runs in reverse order.
    /// </summary>
    internal sealed class CompositeAiRuntimeBehaviour : IRogueCommandAiRuntimeBehaviour
    {
        private readonly string _profileId;
        private readonly List<string> _moduleIds;
        private readonly List<IRogueCommandAiRuntimeBehaviour> _behaviours;
        private bool _initialized;
        private bool _destroyed;

        public CompositeAiRuntimeBehaviour(
            string profileId,
            IList<string> moduleIds,
            IList<IRogueCommandAiRuntimeBehaviour> behaviours)
        {
            if (moduleIds == null)
            {
                throw new ArgumentNullException("moduleIds");
            }

            if (behaviours == null)
            {
                throw new ArgumentNullException("behaviours");
            }

            if (moduleIds.Count != behaviours.Count)
            {
                throw new ArgumentException("Each AI runtime behaviour must have one module ID.");
            }

            _profileId = profileId ?? "<unknown>";
            _moduleIds = new List<string>(moduleIds.Count);
            _behaviours = new List<IRogueCommandAiRuntimeBehaviour>(behaviours.Count);
            for (int i = 0; i < behaviours.Count; i++)
            {
                if (String.IsNullOrWhiteSpace(moduleIds[i]) || behaviours[i] == null)
                {
                    throw new ArgumentException("AI runtime modules and their IDs cannot be null or empty.");
                }

                _moduleIds.Add(moduleIds[i]);
                _behaviours.Add(behaviours[i]);
            }
        }

        public void OnInitialized()
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Cannot initialize a destroyed AI module stack.");
            }

            if (_initialized)
            {
                return;
            }

            for (int i = 0; i < _behaviours.Count; i++)
            {
                if (_destroyed)
                {
                    return;
                }

                try
                {
                    _behaviours[i].OnInitialized();
                }
                catch (Exception exception)
                {
                    if (!_destroyed)
                    {
                        _destroyed = true;
                        for (int cleanupIndex = _behaviours.Count - 1; cleanupIndex >= 0; cleanupIndex--)
                        {
                            TryDestroyAt(cleanupIndex);
                        }
                    }

                    throw new InvalidOperationException(
                        "AI module '" + _moduleIds[i] + "' failed to initialize for profile '" +
                        _profileId + "'.", exception);
                }
            }

            if (_destroyed)
            {
                return;
            }

            _initialized = true;
        }

        public void Tick()
        {
            if (_destroyed || !_initialized)
            {
                return;
            }

            for (int i = 0; i < _behaviours.Count; i++)
            {
                if (_destroyed)
                {
                    return;
                }

                try
                {
                    _behaviours[i].Tick();
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        "AI module '" + _moduleIds[i] + "' failed during Tick for profile '" +
                        _profileId + "'.", exception);
                }
            }
        }

        public void OnDestroyed()
        {
            if (_destroyed)
            {
                return;
            }

            _destroyed = true;
            Exception firstFailure = null;
            for (int i = _behaviours.Count - 1; i >= 0; i--)
            {
                try
                {
                    _behaviours[i].OnDestroyed();
                }
                catch (Exception exception)
                {
                    if (firstFailure == null)
                    {
                        firstFailure = new InvalidOperationException(
                            "AI module '" + _moduleIds[i] + "' failed to clean up for profile '" +
                            _profileId + "'.", exception);
                    }
                }
            }

            if (firstFailure != null)
            {
                throw firstFailure;
            }
        }

        private void TryDestroyAt(int index)
        {
            try
            {
                _behaviours[index].OnDestroyed();
            }
            catch
            {
                // Preserve the initialization error while still attempting every cleanup.
            }
        }
    }
}
