// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using UnityEditor;
using UnityEngine;
using System.IO;

// Menu: RVA > Build Shader Bundle
// Builds every asset tagged with an AssetBundle name into <project>/AssetBundles/.
// Aurora_Shader.shader.meta and AuroraBlit.shader.meta carry the tag "rva_shaders".
public class BuildRVABundle
{
    [MenuItem("RVA/Build Shader Bundle")]
    static void Build()
    {
        var outDir = "AssetBundles";
        var fullPath = Path.GetFullPath(outDir);
        Debug.Log($"[BuildRVABundle] Building to: {fullPath}");

        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

        var bundleNames = AssetDatabase.GetAllAssetBundleNames();
        Debug.Log($"[BuildRVABundle] {bundleNames.Length} bundle(s) registered:");
        foreach (var name in bundleNames)
        {
            Debug.Log($"  - {name}");
            foreach (var asset in AssetDatabase.GetAssetPathsFromAssetBundle(name))
                Debug.Log($"      {asset}");
        }
        if (bundleNames.Length == 0)
        {
            Debug.LogError("[BuildRVABundle] NO BUNDLES REGISTERED: select Aurora_Shader.shader and check the AssetBundle dropdown at the bottom of the Inspector");
            return;
        }

        var manifest = BuildPipeline.BuildAssetBundles(
            outDir,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            Debug.LogError("[BuildRVABundle] Build returned null, build failed (see errors above)");
            return;
        }

        Debug.Log("[BuildRVABundle] Build succeeded. Bundles produced:");
        foreach (var bundle in manifest.GetAllAssetBundles())
            Debug.Log($"  - {bundle}");

        EditorUtility.DisplayDialog("RVA shader bundle", "Build complete:\n" + fullPath, "OK");
    }
}
