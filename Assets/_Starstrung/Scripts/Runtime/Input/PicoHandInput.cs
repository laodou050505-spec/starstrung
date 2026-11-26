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
        bool eyeStarted; float eyeNextTry;
        readonly bool[] pinch = new bool[2], stale = new bool[2];
        readonly float[] pinchT0 = new float[2], anchorT = new float[2], lostT = { -9f, -9f }, frozenFor = new float[2], tipDist = { 9f, 9f };
        readonly Vector3[] anchorP = new Vector3[2], freezeSig = new Vector3[2];
        HandState lastL, lastR;
        Vector3 gazeDir;
        bool diagWasL, diagWasR; float diagT;

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
            bool aimOk = PXR_HandTracking.GetAimState(type, ref aim);
            bool sys = aimOk && ((ulong)aim.aimStatus & (ulong)HandAimStatus.AimIndexPinching) != 0;
            tipDist[side] = d;
            // 捏合：真正的捏是“OK 手势”，中指 / 无名指 / 小指至少一根没握死；已经捏住后不再要求（拖动时手指一收不会断）
            int others = 0; for (int i = 1; i < 4; i++) if (Ratio(i) > T.otherFingerOpen) others++;
            bool start = d < T.pinchOn && others >= 1;
            bool was = pinch[side];
            pinch[side] = (sys && d < T.pinchOff) || (was ? d < T.pinchOff : start);
            var pp = (thumb + tips[0]) * 0.5f;
            if (pinch[side] && !was) { pinchT0[side] = Time.time; anchorP[side] = pp; anchorT[side] = Time.time; }
            if (pinch[side])
            {
                if ((pp - anchorP[side]).sqrMagnitude > 0.025f * 0.025f) { anchorP[side] = pp; anchorT[side] = Time.time; }
                // 捏着很久、手又几乎没动：多半是松手后手指自然蜷着，当作已松开
                if (Time.time - pinchT0[side] > T.staleAfter && Time.time - anchorT[side] > 1.5f) { pinch[side] = false; stale[side] = true; }
            }
            float maxOther = 0f; for (int i = 1; i < 4; i++) maxOther = Mathf.Max(maxOther, Ratio(i));
            if (pinch[side] && was && maxOther < 1.15f && Time.time - anchorT[side] > 0.5f) { pinch[side] = false; stale[side] = true; }   // 捏变成了握拳
            if (stale[side]) { if (d > T.pinchOff) stale[side] = false; else pinch[side] = false; }

            var palm = (wrist + prox[0] + prox[1] + prox[3]) * 0.25f;
            h = new HandState
            {
                tracked = true, pinch = pinch[side], pinchPoint = pp, indexTip = tips[0],
                palm = palm, palmNormal = WorldRot(q) * Vector3.down, curl = curl / 4f,
            };
            // 手掌射线：肩膀（头部下方、按头的朝向偏到这一侧）→ 手掌
            var head = cam.transform;
            var flatR = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
            var shoulder = head.position + flatR * (side == 1 ? T.shoulderSide : -T.shoulderSide) - Vector3.up * T.shoulderDown;
            var dir = palm - shoulder;
            if (dir.sqrMagnitude > 0.01f * 0.01f)
            {
                // 手放低时射线往上抬（舒适：远处的星在眼睛高度，不抬也能指到）
                float drop = shoulder.y - palm.y;
                float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(T.liftFrom, T.liftFull, drop)) * T.lowHandLiftDeg;
                var axis = Vector3.Cross(dir, Vector3.up);
                if (lift > 0.01f && axis.sqrMagnitude > 1e-6f) dir = Quaternion.AngleAxis(lift, axis.normalized) * dir;   // 已在编辑器里数值验证：正角度 = 往上抬
                h.ray = new Ray(palm, dir.normalized); h.hasRay = true;
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
            var info = new EyeTrackingDataGetInfo { displayTime = 0, flags = EyeTrackingDataGetFlags.PXR_EYE_POSITION | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION };
            if (PXR_MotionTracking.GetEyeTrackingData(ref info, ref eye) != 0 || eye.eyeDatas == null) return false;
            var cmb = eye.eyeDatas[(int)PerEyeUsage.Combined];
            if (cmb.isPoseValid == 0) return false;
            if (cmb.isOpennessValid != 0 && cmb.openness < 0.2f) return false; // 闭眼 / 眨眼
            var o = new Vector3(cmb.pose.position.x, cmb.pose.position.y, -cmb.pose.position.z);
            var qq = new Quaternion(cmb.pose.orientation.x, cmb.pose.orientation.y, -cmb.pose.orientation.z, -cmb.pose.orientation.w);
            ray = new Ray(World(o), (WorldRot(qq) * Vector3.forward).normalized);
            return true;
        }

        void Update()
        {
            if (!Active) { Left = Right = default; return; }
            float now = Time.time;
            bool hands = PXR_HandTracking.GetActiveInputDevice() == ActiveInputDevice.HandTrackingActive;
            if (hands && ReadHand(HandType.HandLeft, 0, out var nl)) { Left = lastL = nl; lostT[0] = now; }
            else if (now - lostT[0] < tuning.lostGrace) Left = lastL;
            else { Left = default; pinch[0] = false; }
            if (hands && ReadHand(HandType.HandRight, 1, out var nr)) { Right = lastR = nr; lostT[1] = now; }
            else if (now - lostT[1] < tuning.lostGrace) Right = lastR;
            else { Right = default; pinch[1] = false; }

            Diag = $"L={(Left.tracked ? (Left.pinch ? "PINCH" : "open") : "lost")} d={tipDist[0] * 100:0.0}cm R={(Right.tracked ? (Right.pinch ? "PINCH" : "open") : "lost")} d={tipDist[1] * 100:0.0}cm eye={EyeTracked}";
            if (Left.pinch != diagWasL || Right.pinch != diagWasR) { Debug.Log("[SS-IN] pinch-change " + Diag); diagWasL = Left.pinch; diagWasR = Right.pinch; }
            if (now - diagT > 3f) { diagT = now; Debug.Log("[SS-IN] status " + Diag); }

            var t = cam.transform;
            EyeTracked = ReadEye(out var e);
            var target = EyeTracked ? e : new Ray(t.position, t.forward);
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, tuning.gazeSmoothing));
            gazeDir = gazeDir == Vector3.zero ? target.direction : Vector3.Slerp(gazeDir, target.direction, k);
            Gaze = new Ray(target.origin, gazeDir.normalized);
        }
    }
}
