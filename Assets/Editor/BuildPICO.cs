using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildPICO
{
    public static void Run()
    {
        string[] scenes = new string[] {
            "Assets/EmptyTest.unity"
        };
        string apk = "D:/PICO-object/_downloads/MateEngine-PICO.apk";
        Debug.Log("[BuildPICO] starting Android build -> " + apk);
        BuildPlayerOptions opt = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apk,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        BuildReport report = BuildPipeline.BuildPlayer(opt);
        Debug.Log("[BuildPICO] Build result: " + report.summary.result + " size=" + report.summary.totalSize);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log("[BuildPICO] BUILD COMPLETED OK");
        }
        else
        {
            Debug.LogError("[BuildPICO] BUILD FAILED");
        }
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
