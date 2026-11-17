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

            string projectPath = PBXProject.GetPBXProjectPath(output);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Security.framework", false);
            if (teamId != null) ConfigureSigning(project, teamId, profileId, identity);
            project.WriteToFile(projectPath);
            Debug.Log($"[iOS] Exported Xcode project to {output}");
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
