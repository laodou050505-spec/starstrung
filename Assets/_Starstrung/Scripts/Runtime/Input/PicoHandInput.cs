using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;

namespace Starstrung
{
    /// <summary>
    /// 头显输入：PICO 手部追踪 + 眼动。照 WarTable.MRInput（真机验证过的读法）改写，差别：
    /// 1. 眼动启动失败会每隔 tuning.eyeRetry 秒重试（第一次启动系统弹授权框，授权前取数失败，不能失败一次就整局退回头部朝向）；
    /// 2. 手掌射线从肩膀经过手掌（远摘 / 远放的选星规则）；
    /// 3. 阈值全部来自 InputTuning。
    /// 只在头显上工作；编辑器里由 DeskSim 提供模拟手。
    /// </summary>
    public class PicoHandInput : MonoBehaviour
    {
        public Camera cam;
        /// <summary>XR 原点（追踪空间 → 世界）。</summary>
        public Transform origin;
        public InputTuning tuning;

        public bool Active => XRSettings.isDeviceActive && cam;
        public HandState Left, Right;
        public bool EyeTracked { get; private set; }
        public Ray Gaze { get; private set; }
        public Vector3 Head => cam.transform.position;
        /// <summary>诊断：adb logcat -s Unity | grep "\[SS-IN\]"</summary>
        public string Diag { get; private set; } = "";

        HandJointLocations locs = new() { jointLocations = new HandJointLocation[(int)HandJoint.JointMax] };
        EyeTrackingData eye = new() { eyeDatas = new PerEyeData[(int)PerEyeUsage.EyeCount] };
        HandAimState aim;
        bool eyeStarted; float eyeNextTry; float lastEyeOkT = -9f;
        readonly bool[] pinch = new bool[2], stale = new bool[2], preLock = new bool[2];
        readonly float[] pinchT0 = new float[2], anchorT = new float[2], lostT = { -9f, -9f }, frozenFor = new float[2], tipDist = { 9f, 9f }, prevDist = { 9f, 9f };
        readonly Vector3[] anchorP = new Vector3[2], freezeSig = new Vector3[2], smoothRayDir = new Vector3[2], lockPalm = new Vector3[2];
        readonly string[] raySource = { "-", "-" };
        HandState lastL, lastR;
        Vector3 gazeDir;
        bool diagWasL, diagWasR; float diagT;

        /// <summary>当前指定手是否处于“预捏合/预松手”稳定锁定状态（供指尖星光提前渐金反馈）。</summary>
        public bool IsPreLocked(int side) => side >= 0 && side <= 1 && preLock[side];

        void OnDisable()
        {
            if (eyeStarted) { var stop = new EyeTrackingStopInfo(); PXR_MotionTracking.StopEyeTracking(ref stop); eyeStarted = false; }
        }

        Vector3 World(Vector3 p) => origin ? origin.TransformPoint(p) : p;
        Quaternion WorldRot(Quaternion q) => origin ? origin.rotation * q : q;

        bool ReadHand(HandType type, int side, out HandState h)
        {
            h = default;
            var T = tuning;
            if (!PXR_HandTracking.GetJointLocations(type, ref locs) || locs.isActive == 0 || locs.jointLocations == null) return false;
            const ulong need = (ulong)HandLocationStatus.PositionValid;
            // 只要求腕、拇指尖、食指尖有效（捏合时其余手指常被挡住）
            bool Ok(HandJoint j) => ((ulong)locs.jointLocations[(int)j].locationStatus & need) != 0;
            if (!(Ok(HandJoint.JointWrist) && Ok(HandJoint.JointThumbTip) && Ok(HandJoint.JointIndexTip))) return false;
            Vector3 P(HandJoint j) => World(locs.jointLocations[(int)j].pose.Position.ToVector3());
            var wrist = P(HandJoint.JointWrist); var thumb = P(HandJoint.JointThumbTip);
            var tips = new[] { P(HandJoint.JointIndexTip), P(HandJoint.JointMiddleTip), P(HandJoint.JointRingTip), P(HandJoint.JointLittleTip) };
            var prox = new[] { P(HandJoint.JointIndexProximal), P(HandJoint.JointMiddleProximal), P(HandJoint.JointRingProximal), P(HandJoint.JointLittleProximal) };
            // 冻住的旧数据（手放下离开视野后系统常把最后一帧原样继续报成有效）：按丢失处理
            var sig = wrist + thumb * 2f + tips[0] * 3f;
            if ((sig - freezeSig[side]).sqrMagnitude < 1e-10f) frozenFor[side] += Time.unscaledDeltaTime; else { frozenFor[side] = 0f; freezeSig[side] = sig; }
            if (frozenFor[side] > T.frozenAfter) return false;
            float Ratio(int i) => Vector3.Distance(tips[i], wrist) / Mathf.Max(1e-3f, Vector3.Distance(prox[i], wrist));
            float curl = 0f; for (int i = 0; i < 4; i++) curl += Ratio(i);
            var pj = locs.jointLocations[(int)HandJoint.JointPalm].pose;
            var q = new Quaternion(pj.Orientation.x, pj.Orientation.y, -pj.Orientation.z, -pj.Orientation.w);
            float d = Vector3.Distance(thumb, tips[0]);
            float prevD = prevDist[side] > 1f ? d : prevDist[side];
            prevDist[side] = d;
            bool aimOk = PXR_HandTracking.GetAimState(type, ref aim);
            ulong aimFlags = aimOk ? (ulong)aim.aimStatus : 0UL;
            // 仅在指尖确实靠近（< 3.2 cm）时采信系统食指捏合标志，避免松手粘死
            bool sysPinch = aimOk && d < 0.032f && (
                (aimFlags & (ulong)HandAimStatus.AimIndexPinching) != 0 ||
                aim.touchStrengthRay >= 0.88f);
            tipDist[side] = d;

            // 捏合判定（真机精准黄金阈值：自然张手 4.4~8.9 cm，真捏 0.6~2.6 cm）：
            // - 触发：d <= 2.3 cm 无条件触发；或 d < pinchOn (2.8 cm) 且非紧握拳头；或系统确认捏合（d < 3.2 cm）
            // - 松开：d > pinchOff (4.2 cm) 清脆释放，绝不粘连
            int others = 0; for (int i = 1; i < 4; i++) if (Ratio(i) > T.otherFingerOpen) others++;
            bool start = sysPinch || d < 0.023f || (d < T.pinchOn && others >= 1);
            bool was = pinch[side];
            pinch[side] = was ? (d < T.pinchOff) : start;

            var pp = (thumb + tips[0]) * 0.5f;
            if (pinch[side] && !was) { pinchT0[side] = Time.time; anchorP[side] = pp; anchorT[side] = Time.time; }
            if (pinch[side])
            {
                if ((pp - anchorP[side]).sqrMagnitude > 0.02f * 0.02f) { anchorP[side] = pp; anchorT[side] = Time.time; }
                if (d > 0.026f && Time.time - pinchT0[side] > T.staleAfter && Time.time - anchorT[side] > 4f)
                {
                    pinch[side] = false;
                    stale[side] = true;
                }
            }
            if (stale[side]) { if (d > T.pinchOff) stale[side] = false; else pinch[side] = false; }

            var palm = (wrist + prox[0] + prox[1] + prox[3]) * 0.25f;
            h = new HandState
            {
                tracked = true, pinch = pinch[side], pinchPoint = pp, indexTip = tips[0],
                palm = palm, palmNormal = WorldRot(q) * Vector3.down, curl = curl / 4f,
            };

            // 双向预动作稳像锁（Pre-Pinch Lock + Pre-Release Lock）：
            // - 张手准备捏下去时（d 收缩进入 4.4 cm 内且手掌未大幅移动）：冻结射线防捏合瞬间手指下沉跑偏；
            // - 捏着准备松手放星时（d 扩张超过 2.6 cm 且手掌未大幅移动）：冻结射线防松手瞬间手腕微抖插偏相邻缝！
            bool closingForPinch = !pinch[side] && d < 0.044f && (d < prevD - 0.0004f || preLock[side]);
            bool openingForRelease = pinch[side] && d > 0.026f && (d > prevD + 0.0004f || preLock[side]);
            bool wantLock = closingForPinch || openingForRelease;
            if (wantLock)
            {
                if (!preLock[side]) { preLock[side] = true; lockPalm[side] = palm; }
                else if (Vector3.Distance(palm, lockPalm[side]) > 0.055f) preLock[side] = false; // 手大幅移动，主动换目标则立刻解锁
            }
            else
            {
                preLock[side] = false;
            }

            // 严格零视差“眼心 → 指尖”瞄准射线：
            // 锚点固定在双眼正中心 cam.transform.position（0 cm 上下/左右偏移），
            // 在视野中把指尖星光点盖向哪颗星或按钮，射线就分毫不差穿过该目标中心。
            var head = cam.transform;
            var anchor = head.position;
            var toPinch = pp - anchor;
            float distFromAnchor = toPinch.magnitude;
            if (distFromAnchor > 0.10f)
            {
                var rawDir = toPinch / distFromAnchor;
                if (Vector3.Angle(rawDir, head.forward) <= 85f)
                {
                    if (smoothRayDir[side] == Vector3.zero)
                    {
                        smoothRayDir[side] = rawDir;
                    }
                    else if (preLock[side])
                    {
                        // 预捏合/预松手瞬间极强阻尼锁定，彻底杜绝手指合拢/松开抖动把准星带歪到旁边星
                        float rkLock = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.35f);
                        smoothRayDir[side] = Vector3.Slerp(smoothRayDir[side], rawDir, rkLock).normalized;
                    }
                    else
                    {
                        // 常规瞄准：速度自适应轻滤波（慢瞄稳、快移零滞后）
                        float angDelta = Vector3.Angle(smoothRayDir[side], rawDir);
                        float tau = Mathf.Lerp(0.032f, 0.006f, Mathf.Clamp01(angDelta / 7f));
                        float rk = 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau);
                        smoothRayDir[side] = Vector3.Slerp(smoothRayDir[side], rawDir, rk).normalized;
                    }
                    h.ray = new Ray(pp, smoothRayDir[side]);
                    h.hasRay = true;
                    raySource[side] = preLock[side] ? "eyeL" : "eye";
                }
                else
                {
                    raySource[side] = "out";
                }
            }
            return true;
        }

        bool ReadEye(out Ray ray)
        {
            ray = default;
            if (!eyeStarted)
            {
                if (Time.time < eyeNextTry) return false;
                eyeNextTry = Time.time + tuning.eyeRetry;
                var start = new EyeTrackingStartInfo { needCalibration = 0, mode = EyeTrackingMode.PXR_ETM_BOTH };
                eyeStarted = PXR_MotionTracking.StartEyeTracking(ref start) == 0;
                Debug.Log("[SS-IN] eye start " + (eyeStarted ? "ok" : "failed, retry later"));
                if (!eyeStarted) return false;
            }
            for (int i = 0; i < eye.eyeDatas.Length; i++)
                eye.eyeDatas[i].SetVersion(PXR_MotionTracking.PXR_EYE_TRACKING_API_VERSION);
            var info = new EyeTrackingDataGetInfo { displayTime = 0, flags = EyeTrackingDataGetFlags.PXR_EYE_POSITION | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION };
            if (PXR_MotionTracking.GetEyeTrackingData(ref info, ref eye) != 0 || eye.eyeDatas == null) return false;
            var cmb = eye.eyeDatas[(int)PerEyeUsage.Combined];
            if (cmb.isPoseValid == 0) return false;
            if (cmb.isOpennessValid != 0 && cmb.openness < 0.22f) return false; // 闭眼 / 眨眼
            var qq = new Quaternion(cmb.pose.orientation.x, cmb.pose.orientation.y, -cmb.pose.orientation.z, -cmb.pose.orientation.w);
            var dir = (WorldRot(qq) * Vector3.forward).normalized;
            if (dir.sqrMagnitude < 0.5f) return false;
            // 生理合法性保护：人眼相对头朝向转角不超过 50°，过滤异常帧
            if (Vector3.Angle(dir, cam.transform.forward) > 50f) return false;
            // 与 PICO 官方 PXR_EyeGazeCursorDriver 保持一致：以渲染相机中心为视线原点，消除眼位偏移视差
            ray = new Ray(cam.transform.position, dir);
            return true;
        }

        void Update()
        {
            if (!Active) { Left = Right = default; return; }
            float now = Time.time;
            var dev = PXR_HandTracking.GetActiveInputDevice();
            bool hands = dev != ActiveInputDevice.ControllerActive;
            if (hands && ReadHand(HandType.HandLeft, 0, out var nl)) { Left = lastL = nl; lostT[0] = now; }
            else if (now - lostT[0] < tuning.lostGrace) Left = lastL;
            else { Left = default; pinch[0] = false; preLock[0] = false; prevDist[0] = 9f; smoothRayDir[0] = Vector3.zero; }
            if (hands && ReadHand(HandType.HandRight, 1, out var nr)) { Right = lastR = nr; lostT[1] = now; }
            else if (now - lostT[1] < tuning.lostGrace) Right = lastR;
            else { Right = default; pinch[1] = false; preLock[1] = false; prevDist[1] = 9f; smoothRayDir[1] = Vector3.zero; }

            var t = cam.transform;
            bool gotEye = ReadEye(out var e);
            if (gotEye)
            {
                lastEyeOkT = now;
                EyeTracked = true;
                // 任一只手处于预捏合/预松手稳像锁时，同步锁住眼动方向，防止捏下去那一刻下意识瞟别处切星
                bool anyPreLock = preLock[0] || preLock[1];
                if (gazeDir == Vector3.zero)
                {
                    gazeDir = e.direction;
                }
                else if (anyPreLock)
                {
                    float rkLock = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.35f);
                    gazeDir = Vector3.Slerp(gazeDir, e.direction, rkLock).normalized;
                }
                else
                {
                    // 速度自适应眼动滤波：盯住同一颗星极稳，眼跳切目标 12ms 秒跟
                    float ang = Vector3.Angle(gazeDir, e.direction);
                    float tau = Mathf.Lerp(Mathf.Max(0.015f, tuning.gazeSmoothing), 0.010f, Mathf.Clamp01(ang / 6f));
                    float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau);
                    gazeDir = Vector3.Slerp(gazeDir, e.direction, k).normalized;
                }
                Gaze = new Ray(t.position, gazeDir);
            }
            else if (now - lastEyeOkT < 0.22f && gazeDir != Vector3.zero)
            {
                // 眨眼 220 ms 宽限期：保持上一帧稳定注视方向不掉锁定
                EyeTracked = true;
                Gaze = new Ray(t.position, gazeDir);
            }
            else
            {
                EyeTracked = false;
                float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.001f, tuning.gazeSmoothing));
                gazeDir = gazeDir == Vector3.zero ? t.forward : Vector3.Slerp(gazeDir, t.forward, k).normalized;
                Gaze = new Ray(t.position, gazeDir);
            }

            float eyeAng = Vector3.Angle(Gaze.direction, t.forward);
            Diag = $"L={(Left.tracked ? (Left.pinch ? "PINCH" : "open") : "lost")}({raySource[0]}) d={tipDist[0] * 100:0.0}cm R={(Right.tracked ? (Right.pinch ? "PINCH" : "open") : "lost")}({raySource[1]}) d={tipDist[1] * 100:0.0}cm eye={EyeTracked}({eyeAng:0.0}d) dev={dev}";
            if (Left.pinch != diagWasL || Right.pinch != diagWasR) { Debug.Log("[SS-IN] pinch-change " + Diag); diagWasL = Left.pinch; diagWasR = Right.pinch; }
            if (now - diagT > 3f) { diagT = now; Debug.Log("[SS-IN] status " + Diag); }
        }
    }
}
