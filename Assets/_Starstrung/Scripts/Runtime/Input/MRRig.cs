using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace Starstrung
{
    /// <summary>
    /// MR 启动（照 WarTable.MRRig、《梦境玩具战》MRBootstrap）：
    /// 1. 头显上开启 PICO 视频透视（相机清屏为全透明，真实房间就是背景），追踪原点设为地面；
    /// 2. 强检并自愈主相机的 6DoF TrackedPoseDriver（InputSystem 绑定 + BeforeRender 兜底），防止头显视角锁死；
    /// 3. 应用从后台恢复时 PICO 会关掉透视，直接通知底层强制重新打开。编辑器非 XR 模式下不做透视且禁用 TrackedPoseDriver 防与 DeskSim 冲突。
    /// </summary>
    public class MRRig : MonoBehaviour
    {
        public Camera cam;
        float retry;
        bool floorSet;
        bool fallbackDirectPose;
        float poseCheckTimer;
        UnityEngine.XR.InputDevice centerEyeDev;
        TrackedPoseDriver tpd;

        void Awake()
        {
            if (!cam) cam = Camera.main;
            EnsureHeadTracking();
        }

        void OnEnable()
        {
            EnsureHeadTracking();
            Application.onBeforeRender += OnBeforeRenderFallback;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRenderFallback;
        }

        void Start()
        {
            EnsureHeadTracking();
            Apply();
            TrySetFloorOrigin();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                EnsureHeadTracking();
                Apply();
            }
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) Apply();
        }

        /// <summary>追踪原点已经设成地面（y = 0 是真实地板）。之后读房间数据要用。</summary>
        public static bool FloorSet { get; private set; }
        /// <summary>视频透视开着（看得到真实房间）。结局全星空时可以关掉透视。</summary>
        public bool Passthrough { get; private set; } = true;
        public void SetPassthrough(bool on) { if (Passthrough == on) return; Passthrough = on; Apply(); }

        /// <summary>确保头显上 6DoF TrackedPoseDriver 具备完整的中心眼位置/旋转/状态绑定，且编辑器桌面模式下自动停用。</summary>
        void EnsureHeadTracking()
        {
            if (!cam) cam = Camera.main;
            if (!cam) return;

            // 在真机或已激活 XR 设备的环境中强制关闭可能遗留的 DeskSim，杜绝覆盖主相机变换
            bool xrMode = !Application.isEditor || XRSettings.isDeviceActive;
            if (xrMode)
            {
                var desk = cam.GetComponent<DeskSim>();
                if (desk) { desk.active = false; desk.enabled = false; }
            }

            tpd = cam.GetComponent<TrackedPoseDriver>();
            if (!tpd) tpd = cam.gameObject.AddComponent<TrackedPoseDriver>();

            if (tpd.positionInput.action == null || tpd.positionInput.action.bindings.Count == 0)
            {
                var posAct = new InputAction("Position", InputActionType.Value, "<XRHMD>/centerEyePosition");
                tpd.positionInput = new InputActionProperty(posAct);
            }
            if (tpd.rotationInput.action == null || tpd.rotationInput.action.bindings.Count == 0)
            {
                var rotAct = new InputAction("Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
                tpd.rotationInput = new InputActionProperty(rotAct);
            }
            if (tpd.trackingStateInput.action == null || tpd.trackingStateInput.action.bindings.Count == 0)
            {
                var stateAct = new InputAction("Tracking State", InputActionType.Value, "<XRHMD>/trackingState");
                tpd.trackingStateInput = new InputActionProperty(stateAct);
            }

            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            tpd.ignoreTrackingState = false;
            tpd.enabled = xrMode;

            if (xrMode)
            {
                tpd.positionInput.action?.Enable();
                tpd.rotationInput.action?.Enable();
                tpd.trackingStateInput.action?.Enable();
            }
            else
            {
                tpd.positionInput.action?.Disable();
                tpd.rotationInput.action?.Disable();
                tpd.trackingStateInput.action?.Disable();
            }
        }

        void OnBeforeRenderFallback()
        {
            if (!fallbackDirectPose || !cam || !XRSettings.isDeviceActive) return;
            if (!centerEyeDev.isValid)
                centerEyeDev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!centerEyeDev.isValid) return;

            if (centerEyeDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out Vector3 p))
                cam.transform.localPosition = p;
            if (centerEyeDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out Quaternion q))
                cam.transform.localRotation = q;
        }

        void Apply()
        {
            if (Application.isEditor && !XRSettings.isDeviceActive) return;
            if (!XRSettings.isDeviceActive) return;
            ByteDance.PICO.XR.PXR_Manager.EnableVideoSeeThrough = Passthrough;
            // PXR_Manager.EnableVideoSeeThrough 仅在值变化时调用底层；应用从后台切回前台时必须直接同步底层，避免透视丢失
            ByteDance.PICO.XR.PXR_Plugin.Boundary.UPxr_SetSeeThroughBackground(Passthrough);
            if (!cam) return;
            if (Passthrough) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); }
            else { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; }   // 结局全星空时可以关掉透视
            Debug.Log($"[SS-MR] Apply passthrough={Passthrough} clearAlpha={cam.backgroundColor.a:0.0} tpd={(tpd && tpd.enabled)} posBinds={tpd?.positionInput.action?.bindings.Count ?? 0} rotBinds={tpd?.rotationInput.action?.bindings.Count ?? 0}");
        }

        void TrySetFloorOrigin()
        {
            if (floorSet || !XRSettings.isDeviceActive) return;
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs)
                if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                    floorSet |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            FloorSet = floorSet;
            if (floorSet) Debug.Log("[SS-MR] TrackingOrigin set to Floor");
        }

        void Update()
        {
            if (!XRSettings.isDeviceActive) return;

            // 6DoF 自愈检测：若 TrackedPoseDriver 未能更新相机而底层 CenterEye 已有有效位姿，自动启用 BeforeRender 直接同步兜底
            if (!fallbackDirectPose && cam)
            {
                poseCheckTimer += Time.unscaledDeltaTime;
                if (poseCheckTimer >= 0.5f)
                {
                    poseCheckTimer = 0f;
                    bool camAtZero = cam.transform.localPosition.sqrMagnitude < 1e-6f
                        && Quaternion.Angle(cam.transform.localRotation, Quaternion.identity) < 0.05f;
                    if (camAtZero)
                    {
                        if (!centerEyeDev.isValid) centerEyeDev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                        if (centerEyeDev.isValid
                            && centerEyeDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out Vector3 dp)
                            && centerEyeDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out Quaternion dq)
                            && (dp.sqrMagnitude > 1e-6f || Quaternion.Angle(dq, Quaternion.identity) > 0.05f))
                        {
                            fallbackDirectPose = true;
                            if (tpd) tpd.enabled = false;
                            cam.transform.SetLocalPositionAndRotation(dp, dq);
                            Debug.LogWarning("[SS-MR] TrackedPoseDriver 未更新相机位姿，已启用 XRNode.CenterEye 直接同步兜底");
                        }
                    }
                }
            }
            if (fallbackDirectPose) OnBeforeRenderFallback();

            if (floorSet) return;
            retry -= Time.unscaledDeltaTime;
            if (retry > 0) return;
            retry = 1f;
            TrySetFloorOrigin();
        }
    }
}
