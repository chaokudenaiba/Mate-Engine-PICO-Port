using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 构建触发钩子：编辑器运行期间轮询"请求文件"，一旦出现就执行一次构建
/// （内容与菜单 Mate Engine → 构建 PICO APK 完全相同）。
///
/// 背景：这台机器上命令行 -executeMethod 会因为编辑器启动阶段一直重试云服务请求而
/// 永远不执行，所以改成"放一个请求文件"来触发；这样不用重启编辑器就能反复构建。
/// 用法：创建 D:/PICO-object/_downloads/build_request.flag
/// </summary>
public static class AutoBuildHook
{
    private const string FlagPath = "D:/PICO-object/_downloads/build_request.flag";
    private static double lastCheck;

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - lastCheck < 2.0) return;      // 每 2 秒查一次，避免每帧访问磁盘
        lastCheck = now;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(FlagPath)) return;

        try { File.Delete(FlagPath); }
        catch { /* 删不掉也不影响，下一次轮询会再触发一次 */ }

        Debug.Log("[AutoBuildHook] 检测到构建请求，开始构建（等价于菜单操作）");
        BuildPICOVR.BuildFromMenu();
    }
}
