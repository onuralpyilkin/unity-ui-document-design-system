using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UIDocumentDesignSystem.BuildTools
{
    // One-shot diagnostic: what render pipeline does this project ACTUALLY
    // resolve? Used to chase the WebGL shader-stripping mystery (URP's
    // stripper zeroing every URP shader — consistent with it enumerating no
    // valid URP assets). Run:
    //   Unity -batchmode -quit -executeMethod
    //     UIDocumentDesignSystem.BuildTools.PipelineProbe.Print
    public static class PipelineProbe
    {
        public static void Print()
        {
            var def = GraphicsSettings.defaultRenderPipeline;
            Debug.Log($"[PipelineProbe] GraphicsSettings.defaultRenderPipeline = " +
                      (def == null ? "NULL (built-in!)" : $"'{def.name}' ({def.GetType().Name}) path={AssetDatabase.GetAssetPath(def)}"));

            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var rp = QualitySettings.GetRenderPipelineAssetAt(i);
                Debug.Log($"[PipelineProbe] Quality[{i}] '{QualitySettings.names[i]}' → " +
                          (rp == null ? "null" : $"'{rp.name}' path={AssetDatabase.GetAssetPath(rp)}"));
            }

            var cur = GraphicsSettings.currentRenderPipeline;
            Debug.Log($"[PipelineProbe] currentRenderPipeline = " + (cur == null ? "NULL" : cur.name));

            var lit = Shader.Find("Universal Render Pipeline/Simple Lit");
            Debug.Log($"[PipelineProbe] Shader.Find(Simple Lit) = " + (lit == null ? "NULL" : "ok"));
        }
    }
}
