using System.Collections;
using UnityEngine;
using Unity.XR.PXR;

/// <summary>
/// 视频透视（Video See-Through / Passthrough）：让用户看到真实房间，虚拟内容叠加其上。
///
/// 前提（缺一不可）：
/// 1. Assets/Resources/PXR_ProjectSetting.asset → videoSeeThrough=1（构建时写入清单 enable_vst=1）
/// 2. 主相机 clearFlags=SolidColor + 背景 (0,0,0,0)，不渲染天空盒/地面
/// 3. 运行时 PXR_Manager.EnableVideoSeeThrough = true（本组件）
/// </summary>
public class VRPassthrough : MonoBehaviour
{
    [Tooltip("启动后自动开启透视")]
    public bool enableOnStart = true;

    [Tooltip("延迟秒数，等 XR 会话与渲染管线就绪")]
    public float startDelay = 1.5f;

    private bool current;

    private IEnumerator Start()
    {
        ApplyCameraTransparent();

        if (!enableOnStart) yield break;

        yield return new WaitForSeconds(startDelay);
        Enable(true);
    }

    public void Enable(bool on)
    {
        try
        {
            PXR_Manager.EnableVideoSeeThrough = on;
            current = on;
            Debug.Log("[VRPassthrough] PXR_Manager.EnableVideoSeeThrough = " + on);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VRPassthrough] 开启透视失败: " + e.Message);
        }

        ApplyCameraTransparent();
    }

    public void Toggle() { Enable(!current); }

    public bool IsEnabled { get { return current; } }

    /// <summary>相机渲染透明黑背景，让真实世界从空白处透出。</summary>
    private void ApplyCameraTransparent()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;
        Debug.Log("[VRPassthrough] camera '" + cam.name + "' set transparent (SolidColor, alpha 0), HDR off");
    }
}
