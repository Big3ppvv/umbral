#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Umbra.Editor
{
    /// <summary>
    /// Prepares the standalone project without replacing an open, modified scene.
    /// Runtime objects are supplied by UmbraController's runtime bootstrap.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Umbra.unity";
        public const string SpriteMaterialPath = "Assets/Resources/UmbraSprite.mat";

        static ProjectSetup()
        {
            EditorApplication.delayCall += PrepareAfterImport;
        }

        private static void PrepareAfterImport()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;
            if (EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += PrepareAfterImport;
                return;
            }

            try
            {
                EnsureProject();
            }
            catch (Exception exception)
            {
                Debug.LogError("UMBRA: no se pudo preparar la escena. Usa UMBRA/Preparar proyecto.\n" + exception);
            }
        }

        [MenuItem("UMBRA/Preparar proyecto", false, 0)]
        public static void EnsureProject()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureSpriteMaterial();

            bool createdScene = false;
            Scene originalScene = SceneManager.GetActiveScene();
            bool canOpenGeneratedScene = originalScene.IsValid()
                && string.IsNullOrEmpty(originalScene.path)
                && !originalScene.isDirty
                && originalScene.rootCount == 0;

            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                AssetDatabase.Refresh();
                Scene generatedScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    if (!EditorSceneManager.SaveScene(generatedScene, ScenePath))
                        throw new IOException("No se pudo guardar " + ScenePath);
                    createdScene = true;
                }
                finally
                {
                    // Keep the user's current scene and unsaved changes intact.
                    EditorSceneManager.CloseScene(generatedScene, true);
                    if (originalScene.IsValid() && originalScene.isLoaded)
                        SceneManager.SetActiveScene(originalScene);
                }
            }

            if (createdScene)
            {
                PlayerSettings.companyName = "Umbra Studio";
                PlayerSettings.productName = "UMBRA - El último faro";
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 800;
                PlayerSettings.runInBackground = false;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                AssetDatabase.SaveAssets();
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(scene => scene.path == ScenePath);
            if (index < 0)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            else if (!scenes[index].enabled)
            {
                scenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            if (createdScene && canOpenGeneratedScene && !Application.isBatchMode)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        private static void EnsureSpriteMaterial()
        {
            // A Resources asset retains the sprite shader in standalone/WebGL builds.
            // Preserve a material the project owner has already authored at this path.
            if (File.Exists(SpriteMaterialPath))
                return;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                throw new InvalidOperationException("No se encontró Sprites/Default. UMBRA requiere la canalización de renderizado integrada de Unity.");
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
            var material = new Material(shader) { name = "UmbraSprite" };
            AssetDatabase.CreateAsset(material, SpriteMaterialPath);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("UMBRA/Build/Linux", false, 20)]
        public static void BuildLinux()
        {
            Build(BuildTarget.StandaloneLinux64, "Linux", "UMBRA.x86_64");
        }

        [MenuItem("UMBRA/Build/Windows", false, 21)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Windows", "UMBRA.exe");
        }

        [MenuItem("UMBRA/Build/WebGL", false, 22)]
        public static void BuildWebGL()
        {
            Build(BuildTarget.WebGL, "WebGL", string.Empty);
        }

        private static void Build(BuildTarget target, string platform, string executable)
        {
            EnsureProject();
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new BuildFailedException("Falta el módulo de " + platform + ". Instálalo desde Unity Hub para Unity 2022.3 LTS.");

            string outputFolder;
            if (Application.isBatchMode)
            {
                outputFolder = BatchBuildFolder(platform);
            }
            else
            {
                string defaultFolder = Path.GetFullPath(Path.Combine("Builds", platform));
                outputFolder = EditorUtility.SaveFolderPanel("Compilar UMBRA para " + platform, Path.GetDirectoryName(defaultFolder), platform);
                if (string.IsNullOrEmpty(outputFolder))
                    return;
            }

            Directory.CreateDirectory(outputFolder);
            string outputPath = string.IsNullOrEmpty(executable)
                ? outputFolder
                : Path.Combine(outputFolder, executable);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("La compilación de UMBRA falló: " + report.summary.result + ". Revisa la consola de Unity.");

            Debug.Log("UMBRA compilado para " + platform + ": " + outputPath);
        }

        private static string BatchBuildFolder(string platform)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] != "-umbraBuildPath")
                    continue;
                if (i + 1 >= arguments.Length || arguments[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new BuildFailedException("-umbraBuildPath requiere la ruta de una carpeta de salida.");
                return Path.GetFullPath(arguments[i + 1]);
            }
            return Path.GetFullPath(Path.Combine("Builds", platform));
        }
    }
}
#endif
