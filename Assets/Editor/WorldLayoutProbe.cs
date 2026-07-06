#if UNITY_6000_5_OR_NEWER
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Showcase.Runtime;

namespace UIDocumentDesignSystem.BuildTools
{
    // One-shot diagnostic: enter play mode, switch the showcase into world
    // mode, wait for the exhibits to fit, then dump resolvedStyle metrics for
    // the same .ds-btn on the FLAT page and on the WORLD exhibit — to explain
    // layout divergence (the "buttons overflow their section in world space"
    // report) with numbers instead of screenshot forensics. Run:
    //   Unity -batchmode -executeMethod
    //     UIDocumentDesignSystem.BuildTools.WorldLayoutProbe.Run
    // (no -quit: the probe exits the editor itself when done)
    [InitializeOnLoad]
    static class WorldLayoutProbeHook
    {
        static WorldLayoutProbeHook()
        {
            if (SessionState.GetBool(WorldLayoutProbe.FLAG, false))
                EditorApplication.update += WorldLayoutProbe.Tick;
        }
    }

    public static class WorldLayoutProbe
    {
        public const string FLAG = "WorldLayoutProbe.armed";
        const string PROBE_TEXT = "Randomize colors";   // exists once on each side
        static int _frames;
        static bool _shown;
        static bool _flatMeasured;

        public static void Run()
        {
            // Batchmode starts on an empty Untitled scene; the bootstrap
            // refuses to run outside the Showcase scene.
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Showcase/Showcase.unity");
            SessionState.SetBool(FLAG, true);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        public static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            _frames++;

            var corridor = Object.FindAnyObjectByType<WorldSpaceCorridor>();
            if (corridor == null)
            {
                if (_frames > 900) { Debug.LogError("[WorldLayoutProbe] Corridor never appeared."); Done(1); }
                return;
            }

            if (!_flatMeasured)
            {
                if (_frames < 55) return;   // let the bootstrap lay out
                var showcase = GameObject.Find("Showcase");
                var flatRoot = showcase != null ? showcase.GetComponent<UIDocument>()?.rootVisualElement : null;
                if (flatRoot == null) return;
                Dump("FLAT ", flatRoot);    // must happen BEFORE Show() hides the flat page (display:none → all zeros)
                _flatMeasured = true;
                return;
            }

            if (!_shown)
            {
                Debug.Log($"[WorldLayoutProbe] frame {_frames}: calling corridor.Show()");
                corridor.Show();
                _shown = true;
                return;
            }

            // Find the world exhibit carrying the probe button; give the fit a
            // chance to reveal it but take whatever exists once patience runs
            // out — LAYOUT numbers are what we're after.
            VisualElement worldButtons = null;
            int roots = 0;
            foreach (var root in corridor.ExhibitRoots)
            {
                roots++;
                if (FindBtn(root) == null) continue;
                worldButtons = root;
                break;
            }
            if (worldButtons == null)
            {
                if (_frames > 1200)
                {
                    Debug.LogError($"[WorldLayoutProbe] No '{PROBE_TEXT}' exhibit found (roots={roots}).");
                    Done(1);
                }
                return;
            }
            if (_frames < 400) return;   // settling time

            Dump("WORLD", worldButtons);
            Done(0);
        }

        static Button FindBtn(VisualElement root)
        {
            Button hit = null;
            root.Query<Button>(className: "ds-btn").ForEach(b =>
            {
                if (hit == null && b.text == PROBE_TEXT) hit = b;
            });
            return hit;
        }

        static void Dump(string tag, VisualElement root)
        {
            var btn = FindBtn(root);
            if (btn == null)
            {
                Debug.Log($"[WorldLayoutProbe] {tag}: no '{PROBE_TEXT}' button found");
                return;
            }
            var rs = btn.resolvedStyle;
            var row = btn.parent;
            VisualElement section = btn;
            while (section != null && !section.ClassListContains("ds-section")) section = section.parent;

            // Isolate TEXT MEASUREMENT from the box model — if world panels
            // measure the same string wider at the same resolved font size,
            // the divergence is in the text engine, not the styles.
            Vector2 textSize = btn.MeasureTextSize(PROBE_TEXT, 0, VisualElement.MeasureMode.Undefined,
                                                              0, VisualElement.MeasureMode.Undefined);

            string font = "?";
            var fd = rs.unityFontDefinition;
            if (fd.fontAsset != null) font = "fontAsset:" + fd.fontAsset.name;
            else if (fd.font != null)  font = "font:" + fd.font.name;
            else if (rs.unityFont != null) font = "legacyFont:" + rs.unityFont.name;

            Debug.Log($"[WorldLayoutProbe] {tag} btn '{btn.text}': " +
                      $"w={rs.width:F1} h={rs.height:F1} fontSize={rs.fontSize:F1} {font} " +
                      $"textMeasure={textSize.x:F1}x{textSize.y:F1} letterSpacing={rs.letterSpacing:F2} " +
                      $"padL={rs.paddingLeft:F1} padR={rs.paddingRight:F1} | " +
                      $"row w={row?.resolvedStyle.width:F1} | " +
                      $"section w={(section != null ? section.resolvedStyle.width : -1f):F1} | " +
                      $"panel ppp={btn.panel?.scaledPixelsPerPoint:F2}");
        }

        static void Done(int code)
        {
            SessionState.SetBool(FLAG, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(code);
        }
    }
}
#endif
