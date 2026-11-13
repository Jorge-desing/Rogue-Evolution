using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RogueCommandExpansion.Authoring
{
    // This one-asset build is authorized separately from the Level 2 policy.
    public static class BulwarkFoundryBuilder
    {
        private const string Name = "BulwarkFoundry";
        private const string Model = "Assets/RogueCommandExpansionSample/Models/BulwarkFoundry_LODs.fbx";
        private const string Textures = "Assets/RogueCommandExpansionSample/Models/Textures/BulwarkFoundry_";
        private const string Prefab = "Assets/TacticalOperations/Prefabs/BulwarkFoundryVisual.prefab";
        private const string MaterialPath = "Assets/TacticalOperations/Materials/BulwarkFoundry_PBR.mat";
        private const string Bundle = "tactical-operations.bundle";

        [Serializable] private sealed class Policy
        {
            public int schemaVersion;
            public string assetId, authorizationStatus, geometryBudgetStatus;
            public float[] lodScreenRelativeTransitionHeights;
        }

        private static Policy RequirePolicy(string root)
        {
            string path = Path.Combine(root, "content/source-art/bulwark-foundry-build-policy.json");
            Policy policy = JsonUtility.FromJson<Policy>(File.ReadAllText(path));
            if (policy == null || policy.schemaVersion != 1 || policy.assetId != Name ||
                policy.authorizationStatus != "user-authorized-foundry-only-2026-11-13" ||
                policy.geometryBudgetStatus != "UNCALIBRATED_USER_AUTHORIZED" ||
                policy.lodScreenRelativeTransitionHeights == null || policy.lodScreenRelativeTransitionHeights.Length != 3 ||
                policy.lodScreenRelativeTransitionHeights[0] <= policy.lodScreenRelativeTransitionHeights[1] ||
                policy.lodScreenRelativeTransitionHeights[1] <= policy.lodScreenRelativeTransitionHeights[2] ||
                policy.lodScreenRelativeTransitionHeights[0] > 1 || policy.lodScreenRelativeTransitionHeights[2] <= 0)
                throw new InvalidDataException("Bulwark Foundry scoped geometry policy is absent or invalid.");
            return policy;
        }

        private static Texture2D Texture(string suffix, bool linear, bool normal)
        {
            string path = Textures + suffix + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Missing foundry texture", path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !linear && !normal;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            if (linear) { importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = false; }
            importer.SaveAndReimport();
            var effective = AssetImporter.GetAtPath(path) as TextureImporter;
            if (effective == null || effective.sRGBTexture != (!linear && !normal) ||
                (normal && effective.textureType != TextureImporterType.NormalMap) ||
                (linear && (effective.alphaSource != TextureImporterAlphaSource.FromInput || effective.alphaIsTransparency)))
                throw new InvalidDataException("Foundry texture importer contract failed: " + path);
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (result == null || result.width != 2048 || result.height != 2048)
                throw new InvalidDataException("Foundry texture import has an unexpected size: " + path);
            return result;
        }

        private static Material Variant(Material source, string name, Color tint)
        {
            string path="Assets/TacticalOperations/Materials/BulwarkFoundry_"+name+".mat";
            Material result=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(result==null) { result=new Material(source); AssetDatabase.CreateAsset(result,path); }
            else result.CopyPropertiesFromMaterial(source);
            result.shader=source.shader;
            result.SetColor("_BaseColor",tint);
            result.enableInstancing=true;
            EditorUtility.SetDirty(result);
            return result;
        }

        public static string BuildPrefab(string root)
        {
            Policy policy = RequirePolicy(root);
            if (Application.unityVersion != "2021.3.45f2") throw new InvalidOperationException("Wrong Unity version.");
            Texture2D albedo=Texture("Albedo",false,false), metallic=Texture("MetallicGloss",true,false),
                normal=Texture("Normal",false,true), emission=Texture("Emission",false,false);
            Shader shader=Shader.Find("RogueCommand/VehiclePBR_VertexWear");
            if (shader == null) throw new InvalidDataException("Foundry shader missing.");
            Material mat=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null) { mat=new Material(shader); AssetDatabase.CreateAsset(mat,MaterialPath); }
            mat.shader=shader; mat.SetTexture("_BaseMap",albedo); mat.SetTexture("_MetallicGlossMap",metallic);
            mat.SetTexture("_BumpMap",normal); mat.SetTexture("_EmissionMap",emission);
            mat.SetFloat("_GlossMapScale",1); mat.SetFloat("_Metallic",1); mat.SetFloat("_Smoothness",1);
            mat.SetColor("_BaseColor",Color.white); mat.SetColor("_EmissionColor",Color.white); mat.SetColor("_TeamColor",Color.white);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.EnableKeyword("_NORMALMAP"); mat.EnableKeyword("_EMISSION");
            mat.enableInstancing=true; EditorUtility.SetDirty(mat);
            Material gunmetal=Variant(mat,"Gunmetal",new Color(.55f,.59f,.63f,1));
            Material steel=Variant(mat,"Steel",new Color(.9f,.92f,.94f,1));

            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport);
            GameObject source=AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            if (source == null) throw new FileNotFoundException("Foundry FBX not imported.",Model);
            GameObject prefabRoot = new GameObject("BulwarkFoundryVisual");
            try
            {
                GameObject model=(GameObject)PrefabUtility.InstantiatePrefab(source);
                if (model == null) throw new InvalidDataException("Could not instantiate foundry FBX.");
                model.transform.SetParent(prefabRoot.transform,false);
                var levels=new List<Renderer>[] { new List<Renderer>(),new List<Renderer>(),new List<Renderer>() };
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    int level=-1;
                    for (int i=0;i<3;i++) if (renderer.name.StartsWith("LOD"+i+"_",StringComparison.Ordinal)) level=i;
                    if (level<0) throw new InvalidDataException("Unexpected renderer in foundry FBX: "+renderer.name);
                    var filter=renderer.GetComponent<MeshFilter>();
                    Mesh mesh=filter == null ? null : filter.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0 || mesh.colors32.Length != mesh.vertexCount || mesh.uv.Length != mesh.vertexCount)
                        throw new InvalidDataException("Missing foundry mesh, UV or RGBA vertex wear: "+renderer.name);
                    var original=renderer.sharedMaterials;
                    var mats=new Material[Math.Max(1,original.Length)];
                    for(int i=0;i<mats.Length;i++)
                    {
                        string originalName=i<original.Length && original[i]!=null ? original[i].name : String.Empty;
                        mats[i]=originalName.IndexOf("Gunmetal",StringComparison.OrdinalIgnoreCase)>=0 ||
                            originalName.IndexOf("Workshop",StringComparison.OrdinalIgnoreCase)>=0 ? gunmetal :
                            originalName.IndexOf("Machined",StringComparison.OrdinalIgnoreCase)>=0 ? steel : mat;
                    }
                    renderer.sharedMaterials=mats; levels[level].Add(renderer);
                }
                var lods=new LOD[3];
                for(int i=0;i<3;i++)
                {
                    if(levels[i].Count==0) throw new InvalidDataException("Foundry LOD"+i+" is empty.");
                    lods[i]=new LOD(policy.lodScreenRelativeTransitionHeights[i],levels[i].ToArray());
                }
                var group=prefabRoot.AddComponent<LODGroup>(); group.SetLODs(lods); group.RecalculateBounds();
                if (prefabRoot.GetComponentsInChildren<MonoBehaviour>(true).Length!=0 ||
                    prefabRoot.GetComponentsInChildren<Collider>(true).Length!=0)
                    throw new InvalidDataException("Foundry prefab must remain visual-only.");
                bool success; PrefabUtility.SaveAsPrefabAsset(prefabRoot,Prefab,out success);
                if(!success) throw new IOException("Could not save foundry prefab.");
                AssetImporter.GetAtPath(Prefab).SetAssetBundleNameAndVariant(Bundle,String.Empty);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                return Prefab;
            }
            finally { UnityEngine.Object.DestroyImmediate(prefabRoot); }
        }

        public static void Build()
        {
            string root=Path.GetFullPath(Path.Combine(Application.dataPath,"..","..",".."));
            RequirePolicy(root); // before prefab, output directory, or bundle writes
            string old=Path.Combine(root,"content/packs/tactical-operations/bundles/"+Bundle);
            var existing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AssetBundle baseline=AssetBundle.LoadFromFile(old);
            if(baseline==null) throw new IOException("Cannot open current Tactical Operations bundle.");
            try { foreach(string path in baseline.GetAllAssetNames()) existing.Add(path); }
            finally { baseline.Unload(true); }
            BuildPrefab(root);
            string output=Path.Combine(root,"artifacts/bulwark-foundry-build/bundles");
            Directory.CreateDirectory(output);
            string[] paths=AssetDatabase.GetAssetPathsFromAssetBundle(Bundle);
            if(Array.IndexOf(paths,Prefab)<0) throw new InvalidDataException("Foundry prefab bundle label missing.");
            var spec=new AssetBundleBuild { assetBundleName=Bundle, assetNames=paths };
            AssetBundleManifest manifest=BuildPipeline.BuildAssetBundles(output,new[]{spec},
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);
            if(manifest==null) throw new InvalidDataException("Foundry bundle build failed.");
            string built=Path.Combine(output,Bundle);
            AssetBundle result=AssetBundle.LoadFromFile(built);
            if(result==null) throw new InvalidDataException("Foundry bundle cannot be reopened.");
            try
            {
                var current=new HashSet<string>(result.GetAllAssetNames(),StringComparer.OrdinalIgnoreCase);
                existing.ExceptWith(current);
                if(existing.Count>0) throw new InvalidDataException("Rebuilt bundle dropped existing addresses: "+String.Join(", ",existing));
                string address=Prefab.ToLowerInvariant();
                GameObject prefab=result.LoadAsset<GameObject>(address);
                if(prefab==null || prefab.GetComponent<LODGroup>()==null || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length!=0)
                    throw new InvalidDataException("Foundry prefab missing or invalid in built bundle.");
            }
            finally { result.Unload(true); }
            File.Copy(Path.Combine(output,"bundles"),Path.Combine(output,"assetbundle-manifest.bundle"),true);
            Debug.Log("FOUNDRY_BUNDLE_VALIDATED UNCALIBRATED_USER_AUTHORIZED " + built);
        }
    }
}
