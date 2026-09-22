using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.PXR;

/// <summary>
/// 一键完成：搭建 Mate Engine VR 场景（PXR 相机 + Zome 桌宠 + 灯光/地面）→ 更新 Build Settings → 构建 Android APK。
/// 用法：Unity -batchmode -projectPath <port> -executeMethod BuildPICOVR.Build -quit
/// </summary>
public static class BuildPICOVR
{
    private const string ScenePath = "Assets/MATE ENGINE - Scenes/Mate Engine VR.unity";
    private const string ApkPath = "D:/PICO-object/_downloads/MateEngine-PICO.apk";
    private const string WalkLayerName = "VRWalk";

    public static void Build()
    {
        Debug.Log("[BuildPICOVR] ==== Build() ENTERED ====");
        try
        {
            Debug.Log("[BuildPICOVR] calling BuildScene()");
            BuildScene();
            Debug.Log("[BuildPICOVR] BuildScene() done, calling BuildApk()");
            BuildApk();
            Debug.Log("[BuildPICOVR] ALL DONE");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[BuildPICOVR] FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// 编辑器菜单入口（打开本项目的 Unity 编辑器后，菜单栏 → Mate Engine → 构建 PICO APK）。
    /// 与命令行 Build() 做的事完全一样，只是构建完不退出编辑器，方便在 GUI 里用。
    /// </summary>
    [MenuItem("Mate Engine/构建 PICO APK (VR) %#b")]
    public static void BuildFromMenu()
    {
        Debug.Log("[BuildPICOVR] ==== 菜单构建开始 ====");
        try
        {
            BuildScene();
            Debug.Log("[BuildPICOVR] BuildScene() done, calling BuildApk()");
            BuildApk();
            Debug.Log("[BuildPICOVR] ALL DONE");
        }
        catch (Exception e)
        {
            Debug.LogError("[BuildPICOVR] FAILED: " + e);
        }
    }

    /// <summary>仅重新构建 APK（不重建场景），用于数据配置修复后的快速重建。</summary>
    public static void BuildApkOnly()    {
        Debug.Log("[BuildPICOVR] ==== BuildApkOnly() ENTERED ====");
        try
        {
            BuildApk();
            Debug.Log("[BuildPICOVR] APK ONLY DONE");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[BuildPICOVR] APK ONLY FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- XR Rig: PXR_Manager 挂根节点，相机作为其子节点（满足 eyeCamera 同层级查找）----
        var xrHost = new GameObject("XR Rig");
        xrHost.AddComponent<PXR_Manager>();

        // ---- Main Camera (taken over by PICO XR at runtime) ----
        var camGo = new GameObject("Main Camera");
        camGo.transform.SetParent(xrHost.transform, false);
        camGo.tag = "MainCamera";
        camGo.transform.position = Vector3.zero;
        camGo.transform.rotation = Quaternion.identity;
        var cam = camGo.AddComponent<Camera>();
        // 透视（Passthrough）：相机渲染透明黑背景，真实房间从空白处透出
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowHDR = false;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 1000f;
        cam.fieldOfView = 90f;
        camGo.AddComponent<AudioListener>();

        // ---- 无天空盒：真实房间即背景（保留环境光为角色照明）----
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.72f);
        RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.48f);
        RenderSettings.ambientGroundColor = new Color(0.24f, 0.24f, 0.24f);
        RenderSettings.fog = false;

        // ---- Directional Light ----
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        light.color = new Color(1f, 0.98f, 0.95f);
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ---- 无虚拟地面：桌宠直接站在真实地板上（接触阴影由 VRBlobShadow 提供）----

        // ---- Model switcher (Zome / aldina / Lazuli_Clothes / Ayrina) ----
        var switcherGo = new GameObject("ModelSwitcher");
        var switcher = switcherGo.AddComponent<VRModelSwitcher>();
        switcher.modelPrefabs = new System.Collections.Generic.List<GameObject>
        {
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MATE ENGINE - Avatar/Zome.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MATE ENGINE - Avatar/DLCs/aldina.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MATE ENGINE - Avatar/DLCs/Lazuli_VRM_Clothes.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MATE ENGINE - Avatar/DLCs/ME_02/Ayrina/Ayrina.prefab"),
        };
        foreach (var p in switcher.modelPrefabs)
        {
            if (p == null)
            {
                Debug.LogError("[BuildPICOVR] a model prefab failed to load!");
                throw new Exception("model prefab missing");
            }
        }
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/MATE ENGINE - Animations/AvatarAnimatorControllerV2.controller");
        switcher.animatorController = controller;
        switcher.spawnPosition = new Vector3(0f, 0f, 2.2f);
        switcher.spawnScale = 1f;
        Debug.Log("[BuildPICOVR] VRModelSwitcher ready with " + switcher.modelPrefabs.Count + " models, controller=" + (controller != null));

        // ---- VR 交互 / 菜单 / 舞蹈 / AI 聊天 ----
        var font = FindChineseFont();
        Debug.Log("[BuildPICOVR] font=" + (font != null ? font.name : "null"));

        var rigGo = new GameObject("Controllers");
        var rig = rigGo.AddComponent<VRControllerRig>();

        var menuGo = new GameObject("VRMenu");
        var menu = menuGo.AddComponent<VRWorldMenu>();

        var danceGo = new GameObject("VRDance");
        var dance = danceGo.AddComponent<VRDancePlayer>();

        // ---- AI 聊天：已整体移除（代码/配置都删掉了，后续用新方案重做）----
        // 菜单里的「AI 聊天」按钮保留，点击只提示"正在开发中"。

        var helpGo = new GameObject("VRHelp");
        var help = helpGo.AddComponent<VRHelpPanel>();

        var interGo = new GameObject("VRInteractor");
        var interactor = interGo.AddComponent<VRPetInteractor>();

        menu.switcher = switcher;
        menu.dance = dance;
        menu.help = help;
        menu.interactor = interactor;
        menu.fontOverride = font;

        // 原版特效对象（场景内手工调好的对象，连对象一起搬过来）
        var originalFx = ImportOriginalObjects(scene);

        // 前 4 个 = 舞蹈拖尾：换成内置加法混合材质（保留原贴图），挂到双手
        // 最后一个 Galaxy 拖尾单独留给"摸头星辰拖尾"（跟随抚摸的手），避免与舞蹈争抢
        var danceTrails = new List<GameObject>();
        for (int i = 0; i < 3 && i < originalFx.Count; i++)
        {
            if (originalFx[i] == null) continue;
            RebuildFxMaterials(originalFx[i], "Legacy Shaders/Particles/Additive",
                               "TrailFX" + i + "_", new Color(1f, 1f, 1f, 0.85f));
            danceTrails.Add(originalFx[i]);
        }

        // 第 4 个（Galaxy 拖尾）→ 抚摸专用
        GameObject pettingTrail = null;
        if (originalFx.Count > 3 && originalFx[3] != null)
        {
            RebuildFxMaterials(originalFx[3], "Legacy Shaders/Particles/Additive",
                               "PetTrailFX_", new Color(1f, 1f, 1f, 0.9f));
            pettingTrail = originalFx[3];
        }

        // 最后一个 = 摸头触碰特效（心形贴图 + 内置着色器）
        GameObject hoverEffect = originalFx.Count > 4 ? originalFx[4] : null;
        FixHoverEffectMaterial(hoverEffect);

        dance.switcher = switcher;
        dance.menu = menu;
        dance.danceTrails = danceTrails;
        Debug.Log("[BuildPICOVR] 舞蹈拖尾: " + danceTrails.Count + " 个 | 触碰特效: " + (hoverEffect != null ? hoverEffect.name : "无"));

        help.menu = menu;
        help.fontOverride = font;

        interactor.rig = rig;
        interactor.switcher = switcher;
        interactor.menu = menu;
        interactor.help = help;
        interactor.hmdCamera = cam;

        // ---- 还原原版手感：重力飘摆 / 部位抚摸 ----
        // （自动走动已按用户要求移除）

        var physGo = new GameObject("VRPetPhysics");
        var physics = physGo.AddComponent<VRPetPhysics>();
        physics.switcher = switcher;
        interactor.physics = physics;

        var petGo = new GameObject("VRPetting");
        var petting = petGo.AddComponent<VRPettingHandler>();
        petting.switcher = switcher;
        petting.rig = rig;
        petting.hmdCamera = cam;
        petting.hoverEffect = hoverEffect;
        petting.pettingTrail = pettingTrail;
        petting.dance = dance;
        petting.regions = BuildPettingRegions();
        Debug.Log("[BuildPICOVR] 抚摸特效: 触碰特效=" + (hoverEffect != null ? hoverEffect.name : "无") +
                  " 星辰拖尾=" + (pettingTrail != null ? pettingTrail.name : "无"));

        // 自动走动功能已按用户要求移除（不创建该组件、菜单里也没有入口）

        // 原版拖拽音效（Drag_v2 / Place_v2，随机音高）
        interactor.dragStartClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            "Assets/MATE ENGINE - Sounds/ME_02/PET/Drag_v2.mp3");
        interactor.dragStopClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
            "Assets/MATE ENGINE - Sounds/ME_02/PET/Place_v2.mp3");
        Debug.Log("[BuildPICOVR] 拖拽音效: 抓取=" + (interactor.dragStartClip != null) +
                  " 放下=" + (interactor.dragStopClip != null));

        // 基础动画参数驱动（还原原版 AvatarAnimatorController 的 isMale/isFemale/isIdle/IdleIndex）
        var driverGo = new GameObject("VRAvatarDriver");
        var driver = driverGo.AddComponent<VRAvatarDriver>();
        driver.switcher = switcher;
        driver.dance = dance;
        driver.interactor = interactor;

        // 模型列表菜单（内置模型 + 上传的 VRM）
        var modelMenuGo = new GameObject("VRModelMenu");
        var modelMenu = modelMenuGo.AddComponent<VRModelMenu>();
        modelMenu.switcher = switcher;
        modelMenu.fontOverride = font;
        interactor.modelMenu = modelMenu;
        menu.modelMenu = modelMenu;
        help.modelMenu = modelMenu;

        var bootGo = new GameObject("VRBootstrap");
        var boot = bootGo.AddComponent<VRBootstrap>();
        boot.switcher = switcher;
        boot.menu = menu;
        boot.help = help;
        menu.bootstrap = boot;

        // ---- 透视 + 接触阴影 ----
        var passGo = new GameObject("VRPassthrough");
        passGo.AddComponent<VRPassthrough>();

        var shadowGo = new GameObject("VRBlobShadow");
        var blob = shadowGo.AddComponent<VRBlobShadow>();
        blob.switcher = switcher;

        // ---- 头显位姿驱动（自建相机没有 TrackedPoseDriver，需自行写入位姿）----
        var headGo = new GameObject("VRHeadTracker");
        var headTracker = headGo.AddComponent<VRHeadTracker>();
        headTracker.targetCamera = cam;

        // 运行时 Shader.Find 用到的着色器加入 always-included，避免被裁剪
        // 注意：只加轻量着色器。粒子类自定义着色器变体极多（单个可达数十万变体），
        // 加进来会让构建耗时暴涨，它们通过材质引用即可被正常打包。
        EnsureAlwaysIncludedShader("Unlit/Color");
        EnsureAlwaysIncludedShader("Sprites/Default");
        EnsureAlwaysIncludedShader("Unlit/Transparent");

        Debug.Log("[BuildPICOVR] VR stack wired (rig/menu/dance/chat/interactor/bootstrap/passthrough/shadow)");

        // ---- Save scene ----
        Directory.CreateDirectory("Assets/MATE ENGINE - Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[BuildPICOVR] scene saved: " + ScenePath);

        // ---- Build Settings ----
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("[BuildPICOVR] build settings scenes updated");
    }

    /// <summary>优先找 NotoSansSC（简体中文），找不到则取 Fonts 目录任意字体。</summary>
    private static Font FindChineseFont()
    {
        var guids = AssetDatabase.FindAssets("NotoSansSC", new[] { "Assets/MATE ENGINE - Fonts" });
        foreach (var g in guids)
        {
            var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(g));
            if (f != null) return f;
        }
        var any = AssetDatabase.FindAssets("t:Font", new[] { "Assets/MATE ENGINE - Fonts" });
        if (any != null && any.Length > 0)
            return AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(any[0]));
        return null;
    }

    /// <summary>把运行时 Shader.Find 依赖的着色器写入 GraphicsSettings 的 always-included，避免构建裁剪。</summary>
    private static void EnsureAlwaysIncludedShader(string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning("[BuildPICOVR] shader not found: " + shaderName);
            return;
        }
        var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
        if (gs == null)
        {
            Debug.LogWarning("[BuildPICOVR] GraphicsSettings.asset not found");
            return;
        }
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null)
        {
            Debug.LogWarning("[BuildPICOVR] m_AlwaysIncludedShaders property not found");
            return;
        }
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;

        int idx = arr.arraySize;
        arr.InsertArrayElementAtIndex(idx);
        arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log("[BuildPICOVR] always-included shader added: " + shaderName);
    }

    /// <summary>
    /// 生成独立的走路动画控制器资产（只含 PET_WALK_LEFT/RIGHT 两个状态）。
    /// 不走"往原控制器加图层"的路子——实测 Unity 不会把 API 添加的状态保存进原生控制器资产，
    /// 运行时只切控制器（VRAutoWalk 负责切换与切回）。
    /// </summary>
    private static RuntimeAnimatorController CreateWalkController()
    {
        const string path = "Assets/MATE ENGINE - Animations/VRWalkOnly.controller";

        var left = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/MATE ENGINE - Animations/PET_LOCOMOTION/PET_WALK_LEFT.anim");
        var right = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/MATE ENGINE - Animations/PET_LOCOMOTION/PET_WALK_RIGHT.anim");
        if (left == null || right == null)
        {
            Debug.LogWarning("[BuildPICOVR] 走路动画未找到，自动走动将没有动画");
            return null;
        }

        if (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path) != null)
            AssetDatabase.DeleteAsset(path);

        var ctl = AnimatorController.CreateAnimatorControllerAtPath(path);
        ctl.AddMotion(left);      // 默认状态 = 走路循环
        ctl.AddMotion(right);

        AssetDatabase.SaveAssets();
        Debug.Log("[BuildPICOVR] 走路控制器已生成: " + path + "（" + left.name + " / " + right.name + "）");
        return ctl;
    }

    /// <summary>按原版场景里的配置重建抚摸部位（骨骼/偏移/半径/语音/动画参数/画圈参数一致）。</summary>
    private static List<PetVoiceReactionHandler.VoiceRegion> BuildPettingRegions()
    {
        var list = new List<PetVoiceReactionHandler.VoiceRegion>();

        // 头部（原版：半径 0.12、画圈抚摸 540°、Head Pat 动画、头部语音）
        list.Add(new PetVoiceReactionHandler.VoiceRegion
        {
            name = "Head",
            targetBone = HumanBodyBones.Head,
            offset = new Vector3(0f, 0.1f, 0f),
            hoverRadius = 0.12f,
            patMode = true,
            patCircleDegrees = 540f,
            hoverAnimationState = "Head Pat",
            hoverAnimationLayer = "Base Layer",
            hoverAnimationParameter = "Headpat",
            faceAnimationState = "Head Pat",
            faceAnimationLayer = "Face layer",
            faceAnimationParameter = "Headpat",
            voiceClips = LoadClipsByGuid(new[]
            {
                "cbb8fdb3755ef644a87f8abb317ae682", "25dbd611d820880448a33fcfa3bde5ae",
                "8bdf44593abcf7e48b87a176af8bdff9", "fb39f7dbc19a6a84694d052ac58bce5b",
                "e9f071c787c698b45af728160c93bf5a", "30bb54d4d55167b4a8a2eae727b859e1",
                "fd0586658a42b144c837dda2d5fb0671", "ee7b0dec8553beb4eacbb5f58af5f11a",
                "51a92dec7c8611242b5a22937e68feb0", "bf7915bba2418404da85a36bc0a2c3aa",
                "5c712606c89cced409c03fe37b971784"
            })
        });

        // 腰腹（原版：半径 0.06、Intime Region 动画、敏感语音）
        list.Add(new PetVoiceReactionHandler.VoiceRegion
        {
            name = "Intimate Down",
            targetBone = HumanBodyBones.Hips,
            offset = new Vector3(0f, -0.1f, 0f),
            hoverRadius = 0.06f,
            patMode = false,
            hoverAnimationState = "Intime Region",
            hoverAnimationLayer = "Base Layer",
            hoverAnimationParameter = "IntimeRegion",
            voiceClips = LoadClipsByGuid(new[]
            {
                "049c447712a23b945a95f82b3950b2ca", "3a061f7c25bfa394f9ef2c91c54610e0",
                "c96b77dee5e90b14186830c209672e8a", "6d1d0caadd08b764798bc16a468fac84",
                "9abede5681eea16419ec86bac9cbe1ef", "b9ac9d5a22dade640bb1a19c2ef12e4c",
                "f36a3e86c0c19fe408b548b3606569ce"
            })
        });

        Debug.Log("[BuildPICOVR] 抚摸部位构建完成，共 " + list.Count + " 个（Head / Intimate Down）");
        return list;
    }

    private static List<AudioClip> LoadClipsByGuid(string[] guids)
    {
        var res = new List<AudioClip>();
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(path)) continue;
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (c != null) res.Add(c);
            else Debug.LogWarning("[BuildPICOVR] 语音载入失败: " + path);
        }
        return res;
    }

    /// <summary>
    /// 复制走路片段并去掉根位移曲线。
    /// 原版走路片段带 RootT/RootQ（烘焙的根位移约 1 米），直接用 Playable 播放会让模型整体偏移、
    /// 起步/停下时"闪到别处"。
    /// </summary>
    private static AnimationClip CreateRootlessClip(AnimationClip src, string path)
    {
        if (src == null) return null;

        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);

        var copy = UnityEngine.Object.Instantiate(src);
        copy.name = src.name + "_NoRoot";

        int removed = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(copy))
        {
            string p = b.propertyName;
            if (p == "RootT.x" || p == "RootT.y" || p == "RootT.z" ||
                p == "RootQ.x" || p == "RootQ.y" || p == "RootQ.z" || p == "RootQ.w")
            {
                AnimationUtility.SetEditorCurve(copy, b, null);
                removed++;
            }
        }

        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        Debug.Log("[BuildPICOVR] 走路片段去根位移: " + path + "（移除 " + removed + " 条曲线）");
        return copy;
    }

    /// <summary>
    /// 需要从原版场景原样搬过来的特效对象：
    /// 前 4 个是舞蹈拖尾（跟手指的音符拖尾），最后一个是摸头等触碰特效。
    /// </summary>
    private static readonly string[] OriginalFxNames =
    {
        "Trail 1", "Trail 2", "Trail Galaxy 1", "Trail Galaxy 2",
        "Particle Group"
    };

    /// <summary>
    /// 把特效对象的材质换成"内置着色器材质资产"（保留原贴图与颜色）。
    /// 目的：原版自定义粒子着色器在 Android 上不渲染（粉块/不可见），
    /// 且变体极多会拖慢构建；换成内置着色器后既不进构建、也能正常显示。
    /// </summary>
    private static void RebuildFxMaterials(GameObject fx, string shaderName, string prefix, Color color)
    {
        if (fx == null) return;
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning("[BuildPICOVR] 未找到着色器 " + shaderName);
            return;
        }

        const string dir = "Assets/MATE ENGINE - Scenes/VRGenerated";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/MATE ENGINE - Scenes", "VRGenerated");

        int idx = 0;
        var renderers = fx.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (var rend in renderers)
        {
            var src = rend.sharedMaterial;
            string path = dir + "/" + prefix + idx + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);

            var mat = new Material(shader) { name = prefix + idx };
            if (src != null && src.mainTexture != null) mat.mainTexture = src.mainTexture;   // 保留原贴图
            mat.color = color;
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);

            AssetDatabase.CreateAsset(mat, path);

            // 覆盖所有材质槽（粒子渲染器可能有多槽，只改第一个会漏）
            int slots = Mathf.Max(1, rend.sharedMaterials.Length);
            var mats = new Material[slots];
            for (int k = 0; k < slots; k++) mats[k] = mat;
            rend.sharedMaterials = mats;
            idx++;
        }
        foreach (var tr in fx.GetComponentsInChildren<TrailRenderer>(true))
        {
            string path = dir + "/" + prefix + idx + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
            var mat = new Material(shader) { name = prefix + idx, color = color };
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
            AssetDatabase.CreateAsset(mat, path);
            tr.sharedMaterial = mat;
            idx++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[BuildPICOVR] " + fx.name + " 材质重建 " + idx + " 个（" + shaderName + "）");
    }

    private static List<GameObject> ImportOriginalObjects(Scene vrScene)
    {
        var imported = new List<GameObject>();
        const string srcFile = "D:/PICO-object/Mate-Engine-Public-Release-X3.3.0/Assets/MATE ENGINE - Scenes/Mate Engine Main.unity";
        const string dstAsset = "Assets/MATE ENGINE - Scenes/_OriginalDesktop.unity";

        try
        {
            if (!File.Exists(srcFile))
            {
                Debug.LogWarning("[BuildPICOVR] 原版场景文件不存在，跳过特效导入");
                return imported;
            }

            string dstFull = Path.Combine(Directory.GetCurrentDirectory(), dstAsset);
            Directory.CreateDirectory(Path.GetDirectoryName(dstFull));
            File.Copy(srcFile, dstFull, true);
            AssetDatabase.ImportAsset(dstAsset, ImportAssetOptions.ForceUpdate);

            var loaded = EditorSceneManager.OpenScene(dstAsset, OpenSceneMode.Additive);
            foreach (var n in OriginalFxNames)
            {
                var srcGo = FindInScene(loaded, n);
                if (srcGo == null)
                {
                    Debug.LogWarning("[BuildPICOVR] 原版场景未找到特效对象: " + n);
                    imported.Add(null);
                    continue;
                }
                var clone = UnityEngine.Object.Instantiate(srcGo);
                clone.name = "OrigFX_" + n.Replace(" ", "");
                EditorSceneManager.MoveGameObjectToScene(clone, vrScene);
                clone.SetActive(false);
                imported.Add(clone);
            }
            EditorSceneManager.CloseScene(loaded, true);

            int ok = 0;
            foreach (var g in imported) if (g != null) ok++;
            Debug.Log("[BuildPICOVR] 原版特效对象导入 " + ok + "/" + OriginalFxNames.Length + " 个");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BuildPICOVR] 导入原版特效失败: " + e.Message);
        }
        return imported;
    }

    private static GameObject FindInScene(Scene s, string name)
    {
        foreach (var root in s.GetRootGameObjects())
        {
            var t = FindDeep(root.transform, name);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            var r = FindDeep(t.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>
    /// 生成心形贴图（经典心形隐函数）。
    /// 原版摸头特效用的是 Mochie/Particles 着色器，实测在 PICO（Android/Vulkan）上渲染成粉红方块，
    /// 所以改用内置 Sprites/Default + 程序生成的心形贴图，保证设备上正常显示为爱心。
    /// </summary>
    private static Texture2D CreateHeartTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                nx *= 1.3f;
                ny = ny * 1.3f + 0.18f;
                float a = nx * nx + ny * ny - 1f;
                float v = a * a * a - nx * nx * ny * ny * ny;
                px[y * size + x] = v <= 0f ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 把摸头特效的材质换成"内置着色器 + 心形贴图"，避免设备上显示为方块。
    /// 覆盖**所有渲染器类型**（粒子、拖尾、网格、线）——只处理粒子渲染器会漏掉拖尾/网格，
    /// 那些会保留原自定义着色器而在 Android 上显示为粉红方块。
    /// </summary>
    private static void FixHoverEffectMaterial(GameObject fx)
    {
        if (fx == null) return;

        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[BuildPICOVR] 未找到 Sprites/Default，跳过特效材质替换");
            return;
        }

        const string dir = "Assets/MATE ENGINE - Scenes/VRGenerated";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/MATE ENGINE - Scenes", "VRGenerated");

        string texPath = dir + "/HeartTex.asset";
        string matPath = dir + "/HeartFX.mat";

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) != null) AssetDatabase.DeleteAsset(texPath);
        if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) AssetDatabase.DeleteAsset(matPath);

        var tex = CreateHeartTexture(128);
        AssetDatabase.CreateAsset(tex, texPath);

        var heartMat = new Material(shader) { mainTexture = tex, color = new Color(1f, 0.5f, 0.72f, 1f) };
        AssetDatabase.CreateAsset(heartMat, matPath);
        AssetDatabase.SaveAssets();

        int particles = 0, others = 0;

        // 粒子渲染器：换成心形贴图。注意一个粒子渲染器可能有**多个材质槽**
        // （原版就是 2 个：M_Heart_Pink_glow + Trail48cg），只改 sharedMaterial 会漏掉后面的槽，
        // 那些粒子仍用原自定义着色器 → 显示为粉红方块。
        foreach (var r in fx.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            int slots = Mathf.Max(1, r.sharedMaterials.Length);
            var mats = new Material[slots];
            for (int k = 0; k < slots; k++) mats[k] = heartMat;
            r.sharedMaterials = mats;
            particles++;
        }

        // 其它渲染器（拖尾/网格/线）：换内置着色器，但保留它们各自的贴图，避免改变外观
        foreach (var r in fx.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (r is TrailRenderer || r is LineRenderer || r is MeshRenderer)
            {
                var src = r.sharedMaterial;
                var mat = new Material(shader) { color = Color.white };
                if (src != null && src.mainTexture != null) mat.mainTexture = src.mainTexture;
                string path = dir + "/FXOther" + others + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mat, path);
                r.sharedMaterial = mat;
                others++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[BuildPICOVR] 摸头特效材质已替换：粒子渲染器 " + particles + " 个，其它渲染器 " + others + " 个");
    }

    /// <summary>
    /// 把原版特效用到的自定义着色器换成内置轻量着色器（保留原贴图与颜色）。
    /// 原因：Mochie/@Xxuebi 等自定义粒子着色器在 Android 上变体极多（实测 34 万个），
    /// 构建耗时暴涨到小时级；且部分着色器在设备上渲染异常（粉红方块）。
    /// </summary>
    private static void SimplifyFxMaterials(GameObject fx, bool additive)
    {
        if (fx == null) return;

        string shaderName = additive ? "Legacy Shaders/Particles/Additive" : "Sprites/Default";
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning("[BuildPICOVR] 未找到着色器 " + shaderName + "，跳过材质简化");
            return;
        }

        const string dir = "Assets/MATE ENGINE - Scenes/VRGenerated";
        if (!AssetDatabase.IsValidFolder(dir))
            AssetDatabase.CreateFolder("Assets/MATE ENGINE - Scenes", "VRGenerated");

        string matName = fx.name + (additive ? "_Add" : "_Alpha");
        string matPath = dir + "/FX_" + matName + ".mat";
        var old = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (old != null) AssetDatabase.DeleteAsset(matPath);

        var mat = new Material(shader);
        mat.name = "FX_" + matName;

        var renderers = fx.GetComponentsInChildren<ParticleSystemRenderer>(true);
        if (renderers.Length > 0 && renderers[0].sharedMaterial != null)
        {
            var src = renderers[0].sharedMaterial;
            if (src.mainTexture != null) mat.mainTexture = src.mainTexture;   // 保留原贴图
            if (src.HasProperty("_Color")) mat.color = src.color;
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", new Color(1f, 1f, 1f, 0.7f));
        }

        AssetDatabase.CreateAsset(mat, matPath);
        AssetDatabase.SaveAssets();

        int n = 0;
        foreach (var r in renderers)
        {
            r.sharedMaterial = mat;
            n++;
        }
        // 拖尾渲染器（TrailRenderer）同样可能是重型着色器
        foreach (var tr in fx.GetComponentsInChildren<TrailRenderer>(true))
        {
            tr.sharedMaterial = mat;
            n++;
        }
        Debug.Log("[BuildPICOVR] " + fx.name + " 材质已简化（" + shaderName + "），渲染器 " + n + " 个");
    }

    private static void BuildApk()
    {
        var opt = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = ApkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opt);
        Debug.Log("[BuildPICOVR] Build result: " + report.summary.result + " size=" + report.summary.totalSize);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Build failed: " + report.summary.result);
        Debug.Log("[BuildPICOVR] BUILD COMPLETED OK -> " + ApkPath);
    }
}
