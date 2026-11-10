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

        static void Ensure()
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
                return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, PipelinePath);
            AssetDatabase.AddObjectToAsset(renderer, pipeline);
            AssetDatabase.SaveAssets();
            return pipeline;
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

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0)
                return;
            var so = new SerializedObject(assets[0]);
            var input = so.FindProperty("activeInputHandler");
            if (input != null && input.intValue != 1)
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
