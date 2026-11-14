using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AsciiHero.Editor
{
    // First editor launch creates the URP asset, the main scene, and shared project settings.
    // Commit whatever this generates (Assets/Settings, Assets/Scenes, ProjectSettings) so both sides match.
    [InitializeOnLoad]
    public static class ProjectBootstrap
    {
        const string PipelinePath = "Assets/Settings/UniversalRenderPipelineAsset.asset";
        const string ScenePath = "Assets/Scenes/Main.unity";

        static ProjectBootstrap()
        {
            EditorApplication.delayCall += Ensure;
        }

        public static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var pipeline = EnsurePipeline();
            if (pipeline != null)
                AssignPipeline(pipeline);

            EnsureEditorSettings();
            EnsurePlayerSettings();
            EnsureScene();
        }

        static UniversalRenderPipelineAsset EnsurePipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (existing != null)
            {
                LogPipeline(existing);
                return existing;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.shadowDistance = 20;
            pipeline.shadowCascadeCount = 1;
            pipeline.maxAdditionalLightsCount = 8;
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            AssetDatabase.AddObjectToAsset(renderer, pipeline);
            AssetDatabase.SaveAssets();
            LogPipeline(pipeline);
            return pipeline;
        }

        static void LogPipeline(UniversalRenderPipelineAsset p)
        {
            Debug.Log("[Bootstrap] URP: hdr=" + p.supportsHDR + " msaa=" + p.msaaSampleCount + " renderScale=" + p.renderScale + " mainShadowRes=" + p.mainLightShadowmapResolution +
                      " addLights=" + p.additionalLightsRenderingMode + " max=" + p.maxAdditionalLightsCount + " addLightShadows=" + p.supportsAdditionalLightShadows +
                      " softShadows=" + p.supportsSoftShadows + " srpBatcher=" + p.useSRPBatcher + " depthTex=" + p.supportsCameraDepthTexture + " opaqueTex=" + p.supportsCameraOpaqueTexture);
        }

        static void AssignPipeline(UniversalRenderPipelineAsset pipeline)
        {
            if (GraphicsSettings.defaultRenderPipeline != pipeline)
                GraphicsSettings.defaultRenderPipeline = pipeline;

            int current = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != pipeline)
                    QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        static void EnsureEditorSettings()
        {
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                EditorSettings.serializationMode = SerializationMode.ForceText;
            if (EditorSettings.externalVersionControl != "Visible Meta Files")
                EditorSettings.externalVersionControl = "Visible Meta Files";
        }

        static void EnsurePlayerSettings()
        {
            if (PlayerSettings.companyName != "AsciiHero")
                PlayerSettings.companyName = "AsciiHero";
            if (PlayerSettings.productName != "AsciiHero")
                PlayerSettings.productName = "AsciiHero";
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                PlayerSettings.colorSpace = ColorSpace.Linear;

            // phone: landscape only, Android 7+, 64-bit IL2CPP (required by Google Play and by 64-bit-only phones)
            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation)
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            var android = UnityEditor.Build.NamedBuildTarget.Android;
            if (PlayerSettings.GetApplicationIdentifier(android) != "com.asciihero.game")
                PlayerSettings.SetApplicationIdentifier(android, "com.asciihero.game");
            if (PlayerSettings.GetScriptingBackend(android) != ScriptingImplementation.IL2CPP)
                PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel24)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;

            // performance: the skinned character meshes (the glyph layer has four vertices per character) are skinned on
            // the GPU, rendering runs on its own thread, and the player reports CPU / GPU frame times (performance test)
            Debug.Log("[Bootstrap] before: gpuSkinning=" + PlayerSettings.gpuSkinning + " mobileMTRendering=" + PlayerSettings.GetMobileMTRendering(android) +
                      " graphicsJobs=" + PlayerSettings.graphicsJobs + " frameTimingStats=" + PlayerSettings.enableFrameTimingStats +
                      " vSyncCount=" + QualitySettings.vSyncCount + " quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            if (!PlayerSettings.gpuSkinning) PlayerSettings.gpuSkinning = true;
            if (!PlayerSettings.GetMobileMTRendering(android)) PlayerSettings.SetMobileMTRendering(android, true);
            if (!PlayerSettings.enableFrameTimingStats) PlayerSettings.enableFrameTimingStats = true;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
                return;
            var so = new SerializedObject(assets[0]);
            var input = so.FindProperty("activeInputHandler");
            // The input backend only takes effect after an editor restart. Switching it inside a batch build
            // (GitHub Actions) makes the editor and the player compile different engine code and the build fails,
            // so CI keeps whatever the session started with; the game supports both backends.
            if (input != null && input.intValue != 1 && !Application.isBatchMode)
            {
                input.intValue = 1;
                so.ApplyModifiedProperties();
            }
        }

        static void EnsureScene()
        {
            if (File.Exists(ScenePath))
                return;
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
