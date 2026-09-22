using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.PXR;
/// <summary>
/// VR 启动引导：把追踪原点设为「地板(Floor)」。
///
/// 背景：PxrTrackingOrigin 默认值是 Eye(=0)，此时世界坐标 y=0 位于头显（眼高）处，
/// 场景中 y=0 的物体（桌宠、菜单）会悬在离地约 1.6m 的半空，手柄位置也整体偏移。
/// 必须在 XR 会话建立后设置，故用重试循环等待子系统就绪。
/// </summary>
public class VRBootstrap : MonoBehaviour
{
    [Tooltip("强制把追踪原点设置为地板模式（推荐）")]
    public bool forceFloorOrigin = true;

    [Tooltip("最多重试次数（每秒一次，等待 XR 会话就绪）")]
    public int maxAttempts = 15;

    [Header("场景布置（按用户朝向）")]
    public VRModelSwitcher switcher;
    public VRWorldMenu menu;
    public VRHelpPanel help;
    [Tooltip("桌宠与用户的水平距离（米）")]
    public float petDistance = 2.0f;
    [Tooltip("面板中心高度（米）")]
    public float panelHeight = 1.45f;
    [Tooltip("面板与桌宠的水平间距（米）")]
    public float panelLateralOffset = 1.0f;

    /// <summary>Floor 与 Stage 都是地板基准，均视为正确。</summary>
    private static bool IsFloorReferenced(PxrTrackingOrigin mode)
    {
        return mode == PxrTrackingOrigin.Floor || mode == PxrTrackingOrigin.Stage;
    }

    private IEnumerator Start()
    {
        if (forceFloorOrigin)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                yield return new WaitForSeconds(1f);

                PxrTrackingOrigin mode = PxrTrackingOrigin.Eye;
                try { PXR_System.GetTrackingOrigin(out mode); }
                catch { }
                var cam0 = Camera.main;
                float hy = cam0 != null ? cam0.transform.position.y : -999f;

                if (IsFloorReferenced(mode))
                {
                    Debug.Log("[VRBootstrap] 追踪基准正确 = " + mode + "（地板基准），headY=" + hy.ToString("F2") + "m");
                    break;
                }

                Debug.Log("[VRBootstrap] attempt " + attempt + " 追踪基准 = " + mode + "（非地板基准），尝试纠正；headY=" + hy.ToString("F2") + "m");
                ApplyFloorOrigin();

                if (attempt == maxAttempts)
                    Debug.LogWarning("[VRBootstrap] 仍未切到地板基准模式，桌宠可能仍悬空");
            }
        }
        else
        {
            yield return new WaitForSeconds(1.5f);
        }

        // 必须等用户真正戴上（头部高度合理）再布置，否则会把桌宠放到错误位置
        yield return WaitForHeadPose(25f);
        LayoutInFrontOfUser();
    }

    /// <summary>
    /// 等待用户真正佩戴头显（userPresence）后再布置场景。
    /// 若只按"头部高度>0.3m"判断，头显放在桌上时就会用歪着的朝向布置，
    /// 结果人物和菜单会偏到视线左前方（约 45°）。
    /// </summary>
    private IEnumerator WaitForHeadPose(float timeout)
    {
        float deadline = Time.time + timeout * 0.8f;   // 大部分时间只等"已佩戴"
        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

        while (Time.time < deadline)
        {
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

            bool present = false;
            if (head.isValid) head.TryGetFeatureValue(CommonUsages.userPresence, out present);

            if (present)
            {
                Debug.Log("[VRBootstrap] 检测到头显已佩戴，按当前视线布置场景");
                yield break;
            }
            yield return new WaitForSeconds(0.4f);
        }

        Debug.LogWarning("[VRBootstrap] 未检测到佩戴状态，按当前视线布置（可随时用菜单「送到我面前」重排）");
    }

    /// <summary>把桌宠放在用户正前方地面，左右两侧各放一个面板。</summary>
    public void LayoutInFrontOfUser()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[VRBootstrap] Camera.main 未找到，跳过场景布置");
            return;
        }

        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 userPos = new Vector3(cam.transform.position.x, 0f, cam.transform.position.z);
        Vector3 petPos = userPos + fwd * petDistance;
        Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);

        if (switcher != null)
        {
            switcher.spawnPosition = new Vector3(petPos.x, 0f, petPos.z);
            switcher.Next(0);   // 在原索引处重生，应用新位置并自动落地
        }
        if (menu != null)
            menu.SetAnchorPosition(petPos - right * panelLateralOffset + Vector3.up * panelHeight);
        if (help != null)
            help.SetAnchorPosition(petPos + right * panelLateralOffset + Vector3.up * panelHeight);

        Debug.Log("[VRBootstrap] layout done. user=" + userPos.ToString("F2") + " pet=" + petPos.ToString("F2") + " fwd=" + fwd.ToString("F2"));
    }

    private void ApplyFloorOrigin()
    {
        // 1) PICO 原生 API（PXR_Loader 路径下权威）
        try
        {
            PXR_System.SetTrackingOrigin(PxrTrackingOrigin.Floor);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VRBootstrap] PICO SetTrackingOrigin(Floor) failed: " + e.Message);
        }

        // 2) Unity XR 子系统路径（OpenXR 兼容）
        try
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetInstances(subsystems);
            for (int i = 0; i < subsystems.Count; i++)
            {
                var s = subsystems[i];
                if (s == null || !s.running) continue;
                bool ok = s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                Debug.Log("[VRBootstrap] subsystem.TrySetTrackingOriginMode(Floor) = " + ok);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[VRBootstrap] subsystem floor origin failed: " + e.Message);
        }
    }
}
