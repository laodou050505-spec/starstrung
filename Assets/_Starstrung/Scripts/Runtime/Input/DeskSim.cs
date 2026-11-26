using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starstrung
{
    /// <summary>
    /// 编辑器模拟头和手（没有头显时的主要验收手段），操作和 Three.js 原型一致：
    /// - 鼠标 = 当前手。手掌射线 = 相机 → 鼠标；射线擦过 1 m 内的星 / 缝隙 / 星盘时，手自动伸到它旁边（触发“近摘、近放”）；
    /// - 左键按住 = 捏；Tab = 换手（另一只手停在原地）；H = 当前手模拟跟丢；
    /// - 右键拖动 / Q E = 转头；
    /// - F5 开始 / 停止录制手部动作，F6 回放最近一次录制（自动测试用，文件在 Docs/手部录制/）。
    /// 脚本也可以直接调 SetHand() 摆手（cs.py 自动测试用）。
    /// </summary>
    public class DeskSim : MonoBehaviour
    {
        public Camera cam;
        public float eyeHeight = 1.6f;
        public float yaw, pitch = -18f;
        /// <summary>手可以“伸过去”的目标（世界坐标），由 GameRunner 每帧提供。</summary>
        public Func<IEnumerable<Vector3>> snapTargets;
        public bool active = true;

        public HandState Left, Right;
        public int activeSide = 1;
        readonly bool[] lost = new bool[2];
        readonly Vector3[] point = { new(-0.18f, 1.2f, 0.45f), new(0.18f, 1.2f, 0.45f) };
        readonly bool[] pinching = new bool[2];
        float depth = 0.45f;
        Vector2 dragFrom; bool dragging;
        bool scripted;

        // 录制 / 回放
        [Serializable] public struct Frame { public float t; public bool lt, lp, rt, rp; public Vector3 l, r, lo, ld, ro, rd; public float yaw, pitch; }
        [Serializable] class Recording { public List<Frame> frames = new(); }
        // 必须标成不序列化：进入运行模式时 Unity 会把空的可序列化字段变成“空对象”，回放就会读空列表报错
        [System.NonSerialized] Recording rec; float recT; [System.NonSerialized] Recording play; int playI; float playT;
        public bool Recording_ => rec != null;
        public bool Playing => play != null;

        /// <summary>脚本直接摆手（side 0 左 1 右）；调用后鼠标不再控制，直到 ReleaseScript()。</summary>
        public void SetHand(int side, Vector3 p, bool pinch, bool tracked = true)
        {
            scripted = true;
            point[side] = p; pinching[side] = pinch; lost[side] = !tracked;
        }
        public void ReleaseScript() => scripted = false;

        /// <summary>判断是否处于真实 XR 头显或非编辑器构建中（此时严禁运行桌面模拟，避免锁死头显相机位姿）。</summary>
        static bool ShouldDisableForXR => !Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive;

        void Awake()
        {
            if (ShouldDisableForXR)
            {
                active = false;
                enabled = false;
            }
        }

        void OnEnable()
        {
            if (ShouldDisableForXR)
            {
                active = false;
                enabled = false;
            }
        }

        void Update()
        {
            if (ShouldDisableForXR)
            {
                active = false;
                enabled = false;
                return;
            }
            if (!active || !cam) return;
            var kb = Keyboard.current; var ms = Mouse.current;
            float dt = Time.deltaTime;
            if (play != null) { Playback(dt); return; }
            if (kb != null)
            {
                if (kb.qKey.isPressed) yaw -= 90f * dt;
                if (kb.eKey.isPressed) yaw += 90f * dt;
                if (kb.tabKey.wasPressedThisFrame) { pinching[activeSide] = false; activeSide = 1 - activeSide; }
                if (kb.hKey.wasPressedThisFrame) lost[activeSide] = !lost[activeSide];
                if (kb.f5Key.wasPressedThisFrame) ToggleRecord();
                if (kb.f6Key.wasPressedThisFrame) PlayLast();
            }
            if (ms != null)
            {
                if (ms.rightButton.wasPressedThisFrame) { dragging = true; dragFrom = ms.position.ReadValue(); }
                if (ms.rightButton.wasReleasedThisFrame) dragging = false;
                if (dragging) { var p = ms.position.ReadValue(); yaw += (p.x - dragFrom.x) * 0.25f; pitch = Mathf.Clamp(pitch - (p.y - dragFrom.y) * 0.25f, -70f, 55f); dragFrom = p; }
            }
            cam.transform.SetPositionAndRotation(new Vector3(0, eyeHeight, 0), Quaternion.Euler(-pitch, yaw, 0));

            if (!scripted && ms != null)
            {
                var ray = cam.ScreenPointToRay(ms.position.ReadValue());
                pinching[activeSide] = ms.leftButton.isPressed;
                // 手的深度：射线擦过的最近目标；捏着拖动时保持深度
                float want = 0.45f, best = 0.045f;
                if (snapTargets != null)
                    foreach (var p in snapTargets())
                    {
                        float t = Vector3.Dot(p - ray.origin, ray.direction);
                        if (t < 0.15f || t > 1f) continue;
                        float off = Vector3.Distance(ray.GetPoint(t), p);
                        if (off < best) { best = off; want = t - 0.01f; }
                    }
                if (!pinching[activeSide]) depth = Mathf.Lerp(depth, want, Mathf.Min(1f, dt * 20f));
                point[activeSide] = ray.GetPoint(depth);
                Build(activeSide, ray);
                Build(1 - activeSide, null);
            }
            else { Build(0, ScriptRay(0)); Build(1, ScriptRay(1)); }

            if (rec != null) Record(dt);
        }

        Ray? ScriptRay(int side) => new Ray(cam.transform.position, (point[side] - cam.transform.position).normalized);

        void Build(int side, Ray? ray)
        {
            var h = new HandState();
            if (!lost[side])
            {
                h.tracked = true; h.pinch = pinching[side]; h.pinchPoint = point[side]; h.indexTip = point[side];
                h.palm = point[side] + new Vector3(0, -0.02f, -0.06f); h.palmNormal = Vector3.down; h.curl = 1.6f;
                if (ray.HasValue) { h.ray = ray.Value; h.hasRay = true; }
            }
            if (side == 0) Left = h; else Right = h;
        }

        // ───────── 录制 / 回放 ─────────
        string Dir => System.IO.Path.Combine(Application.dataPath, "../Docs/手部录制");
        void ToggleRecord()
        {
            if (rec == null) { rec = new Recording(); recT = 0; Debug.Log("[DeskSim] 开始录制"); return; }
            System.IO.Directory.CreateDirectory(Dir);
            var path = System.IO.Path.Combine(Dir, $"rec_{DateTime.Now:MMdd_HHmmss}.json");
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(rec));
            Debug.Log($"[DeskSim] 录制结束：{rec.frames.Count} 帧 → {path}");
            rec = null;
        }
        void Record(float dt)
        {
            recT += dt;
            rec.frames.Add(new Frame
            {
                t = recT, lt = Left.tracked, lp = Left.pinch, rt = Right.tracked, rp = Right.pinch, l = Left.pinchPoint, r = Right.pinchPoint,
                lo = Left.ray.origin, ld = Left.ray.direction, ro = Right.ray.origin, rd = Right.ray.direction, yaw = yaw, pitch = pitch,
            });
        }
        /// <summary>回放一段录制（path 为空 = 最近一次）。</summary>
        public bool PlayLast(string path = null)
        {
            if (path == null)
            {
                if (!System.IO.Directory.Exists(Dir)) return false;
                var files = System.IO.Directory.GetFiles(Dir, "rec_*.json");
                if (files.Length == 0) return false;
                Array.Sort(files); path = files[^1];
            }
            play = JsonUtility.FromJson<Recording>(System.IO.File.ReadAllText(path));
            playI = 0; playT = 0;
            Debug.Log($"[DeskSim] 回放 {path}（{play.frames.Count} 帧）");
            return true;
        }
        void Playback(float dt)
        {
            if (play.frames == null || play.frames.Count == 0) { play = null; return; }
            playT += dt;
            while (playI + 1 < play.frames.Count && play.frames[playI + 1].t <= playT) playI++;
            var f = play.frames[playI];
            yaw = f.yaw; pitch = f.pitch;
            cam.transform.SetPositionAndRotation(new Vector3(0, eyeHeight, 0), Quaternion.Euler(-pitch, yaw, 0));
            Left = new HandState { tracked = f.lt, pinch = f.lp, pinchPoint = f.l, indexTip = f.l, ray = new Ray(f.lo, f.ld), hasRay = f.ld != Vector3.zero };
            Right = new HandState { tracked = f.rt, pinch = f.rp, pinchPoint = f.r, indexTip = f.r, ray = new Ray(f.ro, f.rd), hasRay = f.rd != Vector3.zero };
            if (playI >= play.frames.Count - 1) { play = null; Debug.Log("[DeskSim] 回放结束"); }
        }
    }
}
