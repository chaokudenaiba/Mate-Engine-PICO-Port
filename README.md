# Mate Engine PICO Port

把桌面版 Mate Engine 桌宠移植到 **PICO 4 Ultra**（Unity 2022.3 + PICO Unity Integration SDK，PXR 运行时）。

> 上游项目说明与许可见 [`README_upstream_MateEngine.md`](README_upstream_MateEngine.md) 与 [`LICENSE.md`](LICENSE.md)。
>
> **本仓库只包含移植代码与配置**（脚本、编辑器工具、Android 清单、ProjectSettings）。
> 原始美术资源（动画 / 模型 / 字体 / 着色器，约 1.9 GB，含原项目付费内容与第三方资源）
> 不随仓库分发，恢复方法见下文「本地恢复成可构建工程」。

---

## 一、这一版能做什么（已在实机验证）

| 功能 | 操作 |
|---|---|
| **透视（Passthrough）** | 自动开启，能看到真实房间；人物与手柄位置正确（地板追踪原点） |
| **手柄射线** | 指到按钮会变橙色；扣**扳机**（食指）点击 |
| **抓取移动桌宠** | 任一只手扣扳机选中 → 转动手柄左右 / 上下移动 → 同手**摇杆**前推 / 后拉推远拉近 → 松扳机放下 |
| **放大缩小** | **右手柄侧键按住 + 摇杆**前后（以脚底为基准，不会跳地面） |
| **抚摸互动** | 任一只手靠近**头部 / 腰部**轻拍 → 动画 + 爱心 / 星辰特效 + 手柄星光拖尾 |
| **跳舞 + 拖尾** | 菜单「跳舞 ▶」/「停止跳舞」，双手音符 / 星光拖尾 |
| **切换模型** | 模型列表换装，位置 / 朝向 / 大小保留，不落地跳变 |
| **菜单开关** | **左手柄侧键**按一下显示 / 隐藏；每次显示都摆到**视线左前方** |
| **操作说明** | 7 页图文 + 程序化绘制示意图，显示在**视线右前方**（与菜单并排不重叠） |
| **AI 聊天** | 功能已移除（代码 / 配置已删除），菜单入口保留，点击提示「正在开发中」 |

**纯本地应用**：APK 已移除 `INTERNET` 与 `RECORD_AUDIO` 权限，设备端权限列表只剩 `WRITE_SETTINGS`（PICO SDK 自带、未使用）。

---

## 二、本仓库包含的内容

```
Assets/Editor/                 构建脚本：BuildPICOVR.cs（一键搭场景 + 打包）、AutoBuildHook.cs（静默构建触发）
Assets/MATE ENGINE - Scripts/  Mate Engine 脚本；其中 VR/ 是本次移植的核心：
                                VRBootstrap / VRControllerRig / VRHeadTracker / VRPetInteractor /
                                VRWorldMenu / VRModelMenu / VRModelSwitcher / VRDancePlayer /
                                VRPettingHandler / VRPetPhysics / VRAvatarDriver / VRBlobShadow /
                                VRPassthrough / VRHelpPanel
Assets/Plugins/Android/        应用级 AndroidManifest.xml（启动 Activity 声明 + 移除 INTERNET 权限）
Assets/Resources/              PICO 配置资源（PXR_ProjectSetting 等）
Assets/XR/                     XR 加载器配置（Android 上必须初始化 PXR Loader）
Assets/StreamingAssets/        Mods / CustomDances 等小体积数据
Assets/MATE ENGINE - Scenes/   构建产出的 VR 场景（会由 BuildPICOVR 重新生成）
ProjectSettings/               项目设置（Android / IL2CPP / ARM64 / 追踪原点等）
Packages/manifest.json         依赖清单（含嵌入式 PICO SDK 的引用）
```

## 三、本地恢复成可构建工程

本仓库不含美术资源。要还原成可构建工程，请从**本地完整工程**补齐以下被忽略的大体积目录：

```
Assets/MATE ENGINE - Animations/    动画（约 739 MB）
Assets/MATE ENGINE - Avatar/        模型与 DLC（约 474 MB）
Assets/MATE ENGINE - Fonts/         中文字体资源（约 165 MB）
Assets/MATE ENGINE - Shaders/       着色器包（约 160 MB）
Assets/Mochie/  Assets/UMotionExamples/  Assets/UMotionEditor/  Assets/RecoveredFromME/
Assets/이루아_IruaStore/  Assets/[CooGee Works]/  Assets/MATE ENGINE - Sounds/  等
Packages/com.unity.xr.picoxr/       嵌入式 PICO SDK（约 296 MB）
```

环境需求：Unity **2022.3.62f3c1**、Android Build Support（IL2CPP / ARM64）、PICO Unity Integration SDK 3.4.0。

> ⚠️ 构建脚本 `BuildPICOVR.cs` 会从**原版桌面工程**拷贝一份场景用于导入原版特效对象，
> 路径写死为 `D:/PICO-object/Mate-Engine-Public-Release-X3.3.0/Assets/MATE ENGINE - Scenes/Mate Engine Main.unity`。
> 换机器时请修改该常量，或把原版工程放到相同路径。

## 四、构建方式

### 方式 A：编辑器菜单（推荐）
1. 用 Unity 打开工程，等待导入 / 编译完成
2. 菜单 **Mate Engine → 构建 PICO APK (VR)**
3. 产物：`D:/PICO-object/_downloads/MateEngine-PICO.apk`

### 方式 B：静默（无界面）构建
在 `D:/PICO-object/_downloads/` 放一个空的请求文件 `build_request.flag`，然后执行：

```powershell
powershell -ExecutionPolicy Bypass -File "D:\PICO-object\_downloads\Run-UnityBatch.ps1" `
  -LogFile "D:\PICO-object\_downloads\build.log" `
  -ExtraArgs "-executeMethod","BuildPICOVR.Build" -DoneMarker "ALL DONE" -TimeoutSec 2700
```

> ⚠️ 这台开发机上命令行的 `-executeMethod` **不会被执行**（编辑器启动阶段会卡住；
> 空项目正常，说明与本项目状态相关，原因未完全定位）。实际触发构建的是
> `Assets/Editor/AutoBuildHook.cs` 对请求文件的轮询；`-executeMethod` 参数保留只是为了和历史命令一致。

### 方式 C：命令行（其它机器可试）
```bash
Unity.exe -batchmode -nographics -projectPath <工程路径> -executeMethod BuildPICOVR.Build -logFile build.log
```

### 安装到设备
```bash
adb install -r MateEngine-PICO.apk
adb shell monkey -p com.mateengine.pico -c android.intent.category.LAUNCHER 1   # 验证启动入口
adb shell dumpsys package com.mateengine.pico | grep lastUpdateTime              # 验证已更新
```

## 五、移植要点与踩过的坑

- **追踪原点**：`PXR_ProjectSetting.stageMode = 1`（地板参考系），否则人物与手柄整体悬空
- **相机驱动**：自建相机没有 TrackedPoseDriver，`VRHeadTracker` 在 `Application.onBeforeRender` 里写入 HMD 位姿
- **手柄输入**：PXR 运行时下要用传统 XR 输入（`InputDevices.GetDeviceAtXRNode` + `CommonUsages`），Input System 的 XRController 读不到
- **扳机 / 侧键滞回**：按住状态需要滞回阈值，否则会闪烁
- **粒子着色器**：Mochie / @Xxuebi / CooGee 等自定义粒子着色器在 Android 上不渲染，构建时统一替换为内置 `Sprites/Default` / `Legacy Shaders/Particles/Additive`
- **构建耗时**：`ProjectSettings/GraphicsSettings.asset` 的 always-included 里若带上 `Mochie/Particles`（34 万变体），构建会从十几分钟涨到 1~2 小时 —— 该项目零引用，已移除
- **编辑器联网**：`ThryEditor`（Poiyomi 面板框架）的在线翻译 / 远程公告 / 短链解析会在网络被拦时把编辑器主线程卡死，已永久关闭（见 `HelperWeb.cs` 顶部补丁说明）
- **Android 清单**：自定义应用级清单会替换 Unity 生成的清单，**必须自己声明启动 Activity**，否则装出来的包点不开；`INTERNET` 权限只能在应用级清单里用 `tools:node="remove"` 移除
- **不要提交**：`Library/`、`Temp/`、`Logs/`、`UserSettings/`、构建产物 `*.apk`
