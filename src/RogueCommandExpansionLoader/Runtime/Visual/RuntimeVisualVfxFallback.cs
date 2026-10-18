using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace RogueCommandExpansionLoader.Runtime.Visual
{
    /// <summary>
    /// Resilient particle and combat visual feedback generator.
    /// Tries to resolve native Rogue Command particle prefabs from resources/scene,
    /// falling back to lightweight programmatic ParticleSystems generated directly in C#.
    /// </summary>
    public static class RuntimeVisualVfxFallback
    {
        private static Material _particleDefaultMaterial;

        private static Material GetParticleMaterial()
        {
            if (_particleDefaultMaterial == null)
            {
                // Rogue Command runs URP. Prefer a pipeline-native particle shader so the
                // procedural fallback does not resolve to a Built-in Render Pipeline shader.
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                               Shader.Find("Universal Render Pipeline/Particles/Simple Lit") ??
                               Shader.Find("Mobile/Particles/Alpha Blended") ??
                               Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _particleDefaultMaterial = new Material(shader);
                    _particleDefaultMaterial.enableInstancing = true;
                    if (_particleDefaultMaterial.HasProperty("_SoftParticlesEnabled"))
                    {
                        _particleDefaultMaterial.SetFloat("_SoftParticlesEnabled", 1f);
                    }
                    if (_particleDefaultMaterial.HasProperty("_CameraFadingEnabled"))
                    {
                        _particleDefaultMaterial.SetFloat("_CameraFadingEnabled", 1f);
                    }
                }
            }
            return _particleDefaultMaterial;
        }

        public static GameObject SpawnDamageSmoke(Transform parent)
        {
            if (parent == null) return null;
            return SpawnDamageSmokeAt(parent, parent.position, parent.rotation);
        }

        public static GameObject SpawnDamageSmokeAt(Transform activeParent, Vector3 worldPosition, Quaternion worldRotation)
        {
            if (activeParent == null) return null;

            try
            {
                GameObject nativeSmoke = TryLoadNativePrefab("Smoke");
                if (nativeSmoke != null)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(nativeSmoke, activeParent);
                    instance.transform.SetPositionAndRotation(worldPosition, worldRotation);
                    return instance;
                }
            }
            catch { /* Fallback to procedural */ }

            GameObject proceduralSmoke = CreateProceduralSmoke(activeParent);
            proceduralSmoke.transform.SetPositionAndRotation(
                worldPosition,
                worldRotation * Quaternion.Euler(-90f, 0f, 0f));
            return proceduralSmoke;
        }

        public static GameObject SpawnDamageFire(Transform parent, EntityController entity = null)
        {
            if (parent == null) return null;

            // 1. Try native onFireParticleSystem from the EntityController itself
            if (entity != null)
            {
                try
                {
                    FieldInfo fireField = typeof(EntityController).GetField("onFireParticleSystem", BindingFlags.Public | BindingFlags.Instance);
                    if (fireField != null)
                    {
                        ParticleSystem ps = fireField.GetValue(entity) as ParticleSystem;
                        if (ps != null && ps.gameObject != null)
                        {
                            GameObject instance = UnityEngine.Object.Instantiate(ps.gameObject, parent);
                            instance.transform.localPosition = Vector3.zero;
                            instance.transform.localRotation = Quaternion.identity;
                            instance.SetActive(true);
                            return instance;
                        }
                    }
                }
                catch { }
            }

            try
            {
                GameObject nativeFire = TryLoadNativePrefab("Fire");
                if (nativeFire != null)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(nativeFire, parent);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    return instance;
                }
            }
            catch { /* Fallback to procedural */ }

            return CreateProceduralFire(parent);
        }

        public static void TriggerMuzzleFlash(Transform muzzleSocket, MonoBehaviour coroutineHost, float duration = 0.07f)
        {
            if (muzzleSocket == null) return;

            // 1. If the authored star-spike mesh exists as a child, pulse its visibility
            Transform authoredFlash = muzzleSocket.Find("Socket_MuzzleFlash") ??
                                      muzzleSocket.Find("MuzzleFlash") ??
                                      muzzleSocket.Find("Visual_MuzzleFlash");
            if (authoredFlash != null)
            {
                if (coroutineHost != null)
                {
                    coroutineHost.StartCoroutine(FlashAuthoredMesh(authoredFlash.gameObject, duration));
                }
                else
                {
                    authoredFlash.gameObject.SetActive(true);
                }
                return;
            }

            // 2. Otherwise spawn a procedural instantaneous particle burst
            CreateProceduralMuzzleBurst(muzzleSocket, duration);
        }

        public static void TriggerMuzzleFlashAt(
            Transform activeParent,
            Vector3 worldPosition,
            Quaternion worldRotation,
            float duration = 0.07f)
        {
            if (activeParent == null) return;
            CreateProceduralMuzzleBurst(activeParent, worldPosition, worldRotation, duration);
        }

        private static IEnumerator FlashAuthoredMesh(GameObject flashObj, float duration)
        {
            if (flashObj == null) yield break;
            flashObj.SetActive(true);
            yield return new WaitForSeconds(duration);
            if (flashObj != null)
            {
                flashObj.SetActive(false);
            }
        }

        private static GameObject CreateProceduralSmoke(Transform parent)
        {
            GameObject smokeObj = new GameObject("RCL_VFX_DamageSmoke");
            smokeObj.transform.SetParent(parent, false);
            smokeObj.transform.localPosition = Vector3.zero;
            smokeObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // Upwards

            ParticleSystem ps = smokeObj.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = smokeObj.GetComponent<ParticleSystemRenderer>();
            if (psr != null && GetParticleMaterial() != null)
            {
                psr.sharedMaterial = GetParticleMaterial();
                psr.enableGPUInstancing = true;
            }

            var main = ps.main;
            main.startLifetime = 1.8f;
            main.startSpeed = 1.3f;
            main.startSize = 0.55f;
            main.startColor = new Color(0.22f, 0.22f, 0.22f, 0.65f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 35;

            var emission = ps.emission;
            emission.rateOverTime = 12f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.08f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0.0f, 0.45f);
            sizeCurve.AddKey(1.0f, 1.85f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

            return smokeObj;
        }

        private static GameObject CreateProceduralFire(Transform parent)
        {
            GameObject fireObj = new GameObject("RCL_VFX_DamageFire");
            fireObj.transform.SetParent(parent, false);
            fireObj.transform.localPosition = Vector3.zero;
            fireObj.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            ParticleSystem ps = fireObj.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = fireObj.GetComponent<ParticleSystemRenderer>();
            if (psr != null && GetParticleMaterial() != null)
            {
                psr.sharedMaterial = GetParticleMaterial();
                psr.enableGPUInstancing = true;
            }

            var main = ps.main;
            main.startLifetime = 0.85f;
            main.startSpeed = 2.1f;
            main.startSize = 0.40f;
            main.startColor = new Color(1.0f, 0.58f, 0.12f, 0.95f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 20f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.05f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0.0f, 0.35f);
            sizeCurve.AddKey(0.5f, 1.0f);
            sizeCurve.AddKey(1.0f, 0.1f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

            // Add dynamic flickering light
            Light fireLight = fireObj.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1.0f, 0.62f, 0.18f);
            fireLight.range = 3.8f;
            fireLight.intensity = 1.4f;

            return fireObj;
        }

        private static void CreateProceduralMuzzleBurst(Transform muzzleSocket, float duration)
        {
            if (muzzleSocket == null) return;
            CreateProceduralMuzzleBurst(muzzleSocket, muzzleSocket.position, muzzleSocket.rotation, duration);
        }

        private static void CreateProceduralMuzzleBurst(
            Transform activeParent,
            Vector3 worldPosition,
            Quaternion worldRotation,
            float duration)
        {
            GameObject burstObj = new GameObject("RCL_VFX_MuzzleBurst");
            burstObj.transform.SetParent(activeParent, false);
            burstObj.transform.SetPositionAndRotation(worldPosition, worldRotation);

            ParticleSystem ps = burstObj.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = burstObj.GetComponent<ParticleSystemRenderer>();
            if (psr != null && GetParticleMaterial() != null)
            {
                psr.sharedMaterial = GetParticleMaterial();
                psr.enableGPUInstancing = true;
            }

            var main = ps.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = duration;
            main.startSpeed = 3.5f;
            main.startSize = 0.65f;
            main.startColor = new Color(1.0f, 0.95f, 0.45f, 1.0f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 8;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 6) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.02f;

            ps.Play();
            UnityEngine.Object.Destroy(burstObj, duration + 0.05f);
        }

        private static GameObject TryLoadNativePrefab(string keyword)
        {
            try
            {
                GameObject[] loaded = Resources.FindObjectsOfTypeAll<GameObject>();
                for (int i = 0; i < loaded.Length; i++)
                {
                    if (loaded[i] != null &&
                        loaded[i].name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        loaded[i].GetComponentInChildren<ParticleSystem>(true) != null)
                    {
                        return loaded[i];
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
