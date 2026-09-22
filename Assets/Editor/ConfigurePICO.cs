using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using Unity.XR.PXR;

public static class ConfigurePICO
{
    public static void Run()
    {
        // 1. Switch to Android
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[ConfigurePICO] SwitchActiveBuildTarget failed");
                return;
            }
        }
        Debug.Log("[ConfigurePICO] build target: " + EditorUserBuildSettings.activeBuildTarget);

        // 2. Player Settings
        PlayerSettings.companyName = "MateEngine";
        PlayerSettings.productName = "Mate Engine PICO";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.mateengine.pico");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetPropertyInt("activeInputHandler", 2, BuildTargetGroup.Android);
        PlayerSettings.SetPropertyInt("activeInputHandler", 2);
        PlayerSettings.colorSpace = ColorSpace.Linear;

        // 3. Enable PICO XR loader (same flow as PICO Portal window)
        var buildTargetSettings = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
            .Select(guid => AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid)))
            .FirstOrDefault();
        if (buildTargetSettings == null)
        {
            buildTargetSettings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(buildTargetSettings, "Assets/XRGeneralSettingsPerBuildTarget.asset");
            Debug.Log("[ConfigurePICO] created XRGeneralSettingsPerBuildTarget asset");
        }

        var generalSettings = buildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (generalSettings == null)
        {
            generalSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            AssetDatabase.AddObjectToAsset(generalSettings, buildTargetSettings);
            buildTargetSettings.SetSettingsForBuildTarget(BuildTargetGroup.Android, generalSettings);

            var managerSettings = ScriptableObject.CreateInstance<XRManagerSettings>();
            AssetDatabase.AddObjectToAsset(managerSettings, buildTargetSettings);
            generalSettings.Manager = managerSettings;

            EditorUtility.SetDirty(buildTargetSettings);
            AssetDatabase.SaveAssets();
        }

        if (generalSettings.Manager)
        {
            while (generalSettings.Manager.activeLoaders.Count > 0)
            {
                var loaderName = generalSettings.Manager.activeLoaders[0].GetType().FullName;
                XRPackageMetadataStore.RemoveLoader(generalSettings.Manager, loaderName, BuildTargetGroup.Android);
            }
            bool success = XRPackageMetadataStore.AssignLoader(generalSettings.Manager, "PXR_Loader", BuildTargetGroup.Android);
            Debug.Log("[ConfigurePICO] AssignLoader(PXR_Loader) success=" + success);
        }

        AssetDatabase.SaveAssets();

        Debug.Log("[ConfigurePICO] PICO config applied: minSdk=" + PlayerSettings.Android.minSdkVersion +
                  " targetSdk=" + PlayerSettings.Android.targetSdkVersion +
                  " backend=" + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) +
                  " arch=" + PlayerSettings.Android.targetArchitectures +
                  " id=" + PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android) +
                  " loaders=" + string.Join(",", generalSettings.Manager.activeLoaders.Select(l => l.GetType().Name)));
    }
}
