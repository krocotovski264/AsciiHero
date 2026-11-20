using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AsciiHero.Editor
{
    // Batch-mode entry point for the GitHub Actions build (game-ci/unity-builder, buildMethod).
    // Prepares the project exactly like the first interactive launch, then builds the Android APK.
    public static class CIBuild
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/Build Android APK")]
        public static void BuildAndroidMenu() { Build(Path.GetFullPath("Builds/Android/AsciiHero.apk"), false); }

        public static void BuildAndroid()
        {
            string path = Arg("-customBuildPath");
            if (string.IsNullOrEmpty(path)) path = "Builds/Android/AsciiHero.apk";
            if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(path, "AsciiHero.apk");
            Build(path, true);
        }

        /// the WebGL build (the cinematics' engine: the page opened with ?cine=<name>)
        public static void BuildWebGL()
        {
            int code = 0;
            try
            {
                ProjectBootstrap.Ensure();
                AnimatorProjectPatcher.Patch(false);
                AssetDatabase.SaveAssets();
                string path = Arg("-customBuildPath"); if (string.IsNullOrEmpty(path)) path = "Builds/WebGL/AsciiHero";
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
                if (scenes.Length == 0) { EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }; scenes = new[] { ScenePath }; }
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;     // (served by a plain local server)
                PlayerSettings.WebGL.decompressionFallback = false;
                PlayerSettings.WebGL.dataCaching = false;
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
                PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.initialMemorySize = 512; PlayerSettings.WebGL.maximumMemorySize = 4096;      // (the story world alone is over 2 GB on the wasm heap)
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
                Directory.CreateDirectory(Path.GetFullPath(path));
                var options = new BuildPlayerOptions { scenes = scenes, locationPathName = path, target = BuildTarget.WebGL, targetGroup = BuildTargetGroup.WebGL, options = BuildOptions.None };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var sum = report.summary;
                Debug.Log("[CIBuild] WebGL " + sum.result + " · " + sum.totalErrors + " errors · " + (sum.totalSize / (1024f * 1024f)).ToString("0.0") + " MB · " + path);
                if (sum.result != BuildResult.Succeeded) code = 1;
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Build(string path, bool exitWhenDone)
        {
            int code = 0;
            try
            {
                ProjectBootstrap.Ensure();
                AnimatorProjectPatcher.Patch(false);
                SetIcons();
                AssetDatabase.SaveAssets();

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
                if (scenes.Length == 0)
                {
                    EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                    scenes = new[] { ScenePath };
                }
                string version = Arg("-androidVersionCode");
                int vc;
                if (!string.IsNullOrEmpty(version) && int.TryParse(version, out vc) && vc > 0) PlayerSettings.Android.bundleVersionCode = vc;

                EditorUserBuildSettings.buildAppBundle = false;
                // Always sign with the same test key (Unity/ci/test.keystore). Unity's default debug key is generated
                // per machine, so every CI build had a different signature and Android refused to update the app.
                string ks = Path.GetFullPath("ci/test.keystore");
                if (File.Exists(ks))
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = ks; PlayerSettings.Android.keystorePass = "asciihero";
                    PlayerSettings.Android.keyaliasName = "test"; PlayerSettings.Android.keyaliasPass = "asciihero";
                }
                else Debug.LogWarning("[CIBuild] ci/test.keystore not found: the APK gets a machine-specific debug signature");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var sum = report.summary;
                Debug.Log("[CIBuild] " + sum.result + " · " + sum.totalErrors + " errors · " + (sum.totalSize / (1024f * 1024f)).ToString("0.0") + " MB · " + path);
                if (sum.result != BuildResult.Succeeded) code = 1;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            if (exitWhenDone) EditorApplication.Exit(code);
        }

        /// the app's icon: the helmet with the violet light in the slots of its visor (Assets/Icons/icon.png) on every
        /// Android icon kind (the adaptive one: the picture as its background, an empty foreground)
        static void SetIcons()
        {
            try
            {
                AssetDatabase.ImportAsset("Assets/Icons/icon.png", ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset("Assets/Icons/icon_fg_empty.png", ImportAssetOptions.ForceUpdate);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icons/icon.png");
                var empty = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icons/icon_fg_empty.png");
                if (tex == null) { Debug.LogWarning("[CIBuild] Assets/Icons/icon.png not found: the default icon stays"); return; }
                var target = UnityEditor.Build.NamedBuildTarget.Android;
                foreach (var kind in PlayerSettings.GetSupportedIconKinds(target))
                {
                    var icons = PlayerSettings.GetPlatformIcons(target, kind);
                    foreach (var ic in icons)
                    {
                        var layers = new Texture2D[Math.Max(1, ic.maxLayerCount)];
                        for (int i = 0; i < layers.Length; i++) layers[i] = i == 0 ? tex : (empty != null ? empty : tex);
                        ic.SetTextures(layers);
                    }
                    PlayerSettings.SetPlatformIcons(target, kind, icons);
                }
                Debug.Log("[CIBuild] icon set: " + tex.width + "x" + tex.height);
            }
            catch (Exception e) { Debug.LogWarning("[CIBuild] the icon could not be set: " + e.Message); }
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
