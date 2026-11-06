using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PowderPulse.EditorTools
{
    /// <summary>
    /// Project settings Powder Pulse depends on, applied whenever the editor loads:
    /// plain-http downloads (DLNA media servers don't do https) and the Xbox app's capabilities
    /// (home network, internet, USB drives, music library).
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        static ProjectSetup() { Apply(); }

        public static void Apply()
        {
            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.companyName = "Powder Pulse";
            PlayerSettings.productName = "Powder Pulse";
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.InternetClient, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.InternetClientServer, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.PrivateNetworkClientServer, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.RemovableStorage, true);
            PlayerSettings.WSA.SetCapability(PlayerSettings.WSACapability.MusicLibrary, true);
            PlayerSettings.runInBackground = false;
        }
    }

    /// <summary>Called by the GitHub build: exports the Xbox (UWP) Visual Studio solution.</summary>
    public static class XboxBuild
    {
        public static void Build()
        {
            ProjectSetup.Apply();
            PlayerSettings.WSA.packageName = "PowderPulse.Unity";
            PlayerSettings.WSA.applicationDescription = "Powder Pulse";
            PlayerSettings.WSA.tileShortName = "Powder Pulse";
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.WindowsStoreApps, ScriptingImplementation.IL2CPP);
            EditorUserBuildSettings.wsaUWPBuildType = WSAUWPBuildType.D3D;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/SampleScene.unity" };
            // GameCI passes -customBuildPath; otherwise build next to the project
            string output = "build/WSAPlayer";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-customBuildPath") output = args[i + 1];
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.WSAPlayer,
                targetGroup = BuildTargetGroup.WSA,
                options = BuildOptions.None
            });
            Debug.Log("Xbox build: " + report.summary.result + ", " + report.summary.totalErrors + " errors, output " + output);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
