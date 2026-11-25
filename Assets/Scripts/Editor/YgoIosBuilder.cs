#if UNITY_IOS
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace YGOPortable.Editor
{
    public static class YgoIosBuilder
    {
        private const string MainScene = "Assets/Scenes/Main.unity";
        private const string NativePlugin = "Assets/Plugins/iOS/libygo_native.a";
        private const string KeychainPlugin = "Assets/Plugins/SaveSyncKeychain.mm";

        [MenuItem("YGO Portable/Export iOS Xcode Project")]
        public static void Export()
        {
            var arguments = Environment.GetCommandLineArgs();
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string output = Path.GetFullPath(GetOption(arguments, "-ygoIosOutput")
                ?? GetOption(arguments, "-customBuildPath") ?? GetOption(arguments, "-buildPath") ?? "Builds/iOS", projectRoot);
            string bundleId = GetOption(arguments, "-ygoIosBundleId");
            string teamId = GetOption(arguments, "-ygoIosTeamId");
            string profileId = GetOption(arguments, "-ygoIosProvisioningProfile");
            string identity = GetOption(arguments, "-ygoIosSigningIdentity") switch
            {
                "AppleDevelopment" or "Apple Development" => "Apple Development",
                "AppleDistribution" or "Apple Distribution" => "Apple Distribution",
                null => null,
                _ => throw new BuildFailedException("Use AppleDevelopment or AppleDistribution as the signing identity.")
            };

            if (teamId != null || profileId != null || identity != null)
            {
                if (teamId == null || !Guid.TryParse(profileId, out _) || identity == null)
                    throw new BuildFailedException("Specify the Apple team ID, provisioning profile UUID, and signing identity together.");
            }

            ConfigureNativePlugin(projectRoot, NativePlugin);
            ConfigureNativePlugin(projectRoot, KeychainPlugin);
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            if (bundleId != null)
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, bundleId);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MainScene },
                locationPathName = output,
                target = BuildTarget.iOS
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"iOS Xcode export {report.summary.result} ({report.summary.totalErrors} errors).");

            if (Array.IndexOf(arguments, "-ygoIosFullVisuals") < 0)
                OptimizeTagForceSprites(output);

            string plistPath = Path.Combine(output, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("CFBundleDisplayName", "YGO Portable");
            plist.root.SetBoolean("UIFileSharingEnabled", true);
            plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
            plist.WriteToFile(plistPath);

            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Security.framework", false);
            if (teamId != null) ConfigureSigning(project, teamId, profileId, identity);
            project.WriteToFile(projectPath);
            Debug.Log($"[iOS] Exported Xcode project to {output}");
        }

        private static void OptimizeTagForceSprites(string output)
        {
            string root = Path.Combine(output, "Data", "Raw", "TagForce");
            if (!Directory.Exists(root)) return;

            long saved = 0;
            foreach (string file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                int maxEdge = Path.GetFileName(Path.GetDirectoryName(file)) switch
                {
                    "Standing" or "Characters_Standing" => 640,
                    "SD" or "Characters_SD" => 256,
                    _ => 0,
                };
                if (maxEdge > 0) saved += ShrinkSprite(file, maxEdge);
            }
            Debug.Log($"[iOS] Optimized Tag Force character sprites in the lightweight export ({saved / 1_000_000d:F1} MB saved).");
        }

        private static int ShrinkSprite(string file, int maxEdge)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Texture2D resized = null;
            try
            {
                byte[] original = File.ReadAllBytes(file);
                if (!source.LoadImage(original)) return 0;
                int edge = Mathf.Max(source.width, source.height);
                if (edge <= maxEdge) return 0;

                float scale = maxEdge / (float)edge;
                int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
                int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
                var pixels = new Color32[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        pixels[y * width + x] = source.GetPixelBilinear((x + .5f) / width, (y + .5f) / height);

                resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
                resized.SetPixels32(pixels);
                byte[] encoded = resized.EncodeToPNG();
                if (encoded.Length >= original.Length) return 0;
                File.WriteAllBytes(file, encoded);
                return original.Length - encoded.Length;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                if (resized != null) UnityEngine.Object.DestroyImmediate(resized);
            }
        }

        private static void ConfigureNativePlugin(string projectRoot, string asset)
        {
            if (!File.Exists(Path.Combine(projectRoot, asset)))
                throw new BuildFailedException($"Missing iOS plugin at {asset}.");

            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            if (!(AssetImporter.GetAtPath(asset) is PluginImporter importer))
                throw new BuildFailedException($"Unity could not import the iOS plugin at {asset}.");
            if (!importer.GetCompatibleWithAnyPlatform() && !importer.GetCompatibleWithEditor()
                && importer.GetCompatibleWithPlatform(BuildTarget.iOS))
                return;

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(false);
            importer.SetCompatibleWithPlatform(BuildTarget.iOS, true);
            importer.SaveAndReimport();
        }

        private static void ConfigureSigning(PBXProject project, string teamId, string profileId, string identity)
        {
            string appTarget = project.GetUnityMainTargetGuid();
            project.SetBuildProperty(appTarget, "DEVELOPMENT_TEAM", teamId);
            project.SetBuildProperty(appTarget, "CODE_SIGN_STYLE", "Manual");
            project.SetBuildProperty(appTarget, "CODE_SIGN_IDENTITY", identity);
            project.SetBuildProperty(appTarget, "CODE_SIGN_IDENTITY[sdk=iphoneos*]", identity);
            project.SetBuildProperty(appTarget, "PROVISIONING_PROFILE", profileId);
            project.SetBuildProperty(appTarget, "PROVISIONING_PROFILE_SPECIFIER", "");

            // The app target signs UnityFramework when embedding it.
            string frameworkTarget = project.GetUnityFrameworkTargetGuid();
            project.SetBuildProperty(frameworkTarget, "CODE_SIGNING_ALLOWED", "NO");
            project.SetBuildProperty(frameworkTarget, "PROVISIONING_PROFILE", "");
            project.SetBuildProperty(frameworkTarget, "PROVISIONING_PROFILE_SPECIFIER", "");
        }

        private static string GetOption(string[] arguments, string name)
        {
            int index = Array.IndexOf(arguments, name);
            if (index < 0)
                return null;
            if (index + 1 == arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1])
                || arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new BuildFailedException($"Expected a value after {name}.");
            return arguments[index + 1];
        }
    }
}
#endif
