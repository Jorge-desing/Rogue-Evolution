using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace RogueCommandExpansionLoader.Content
{
    /// <summary>
    /// Central registry for all mod-loaded AssetBundles.
    /// Ensures that bundles are not loaded multiple times and provides a single point of access.
    /// </summary>
    public sealed class GlobalBundleRegistry
    {
        private readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        private readonly object _lock = new object();

        /// <summary>
        /// Loads an AssetBundle asynchronously from the specified path.
        /// If the bundle is already loaded, returns the existing instance.
        /// </summary>
        public async Task<AssetBundle> LoadBundleAsync(string bundleId, string path)
        {
            lock (_lock)
            {
                if (_bundles.TryGetValue(bundleId, out var existing))
                {
                    return existing;
                }
            }

            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(path);
            while (!request.isDone)
            {
                await Task.Yield();
            }

            AssetBundle bundle = request.assetBundle;

            if (bundle == null)
            {
                throw new InvalidOperationException($"Failed to load AssetBundle '{bundleId}' from path '{path}'.");
            }

            lock (_lock)
            {
                if (_bundles.TryGetValue(bundleId, out var existing))
                {
                    // Another thread loaded it in the meantime.
                    // Since LoadFromFileAsync creates a new instance if not cached by Unity, 
                    // and we want to maintain a single instance, we should be careful.
                    // However, Unity's internal cache usually handles this.
                    return existing;
                }

                _bundles[bundleId] = bundle;
                return bundle;
            }
        }

        /// <summary>
        /// Loads a bundle asynchronously from a caller-validated stream. The caller owns the stream
        /// and must keep it open until the returned bundle is unloaded.
        /// </summary>
        public async Task<AssetBundle> LoadBundleAsync(string bundleId, Stream stream)
        {
            if (String.IsNullOrWhiteSpace(bundleId))
            {
                throw new ArgumentException("A bundle ID is required.", nameof(bundleId));
            }
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            lock (_lock)
            {
                if (_bundles.TryGetValue(bundleId, out var existing))
                {
                    return existing;
                }
            }

            AssetBundleCreateRequest request = AssetBundle.LoadFromStreamAsync(stream, 0, 0);
            while (!request.isDone)
            {
                await Task.Yield();
            }

            AssetBundle bundle = request.assetBundle;
            if (bundle == null)
            {
                throw new InvalidOperationException("Failed to load AssetBundle '" + bundleId + "' from stream.");
            }

            lock (_lock)
            {
                if (_bundles.TryGetValue(bundleId, out var existing))
                {
                    if (!System.Object.ReferenceEquals(existing, bundle))
                    {
                        bundle.Unload(true);
                    }
                    return existing;
                }

                _bundles[bundleId] = bundle;
                return bundle;
            }
        }
        /// <summary>
        /// Removes a specific bundle from the registry without unloading it. The owner decides
        /// when to unload it and dispose any associated stream.
        /// </summary>
        public bool RemoveBundle(string bundleId, AssetBundle expectedBundle)
        {
            lock (_lock)
            {
                if (!_bundles.TryGetValue(bundleId, out var existing) ||
                    !System.Object.ReferenceEquals(existing, expectedBundle))
                {
                    return false;
                }

                return _bundles.Remove(bundleId);
            }
        }
    }
}
