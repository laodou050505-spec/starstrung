using System.Collections.Generic;
using UnityEngine;

namespace Starstrung
{
    /// <summary>
    /// 真机手部星光指引：
    /// - 指尖星光点：落在捏合点（拇指与食指中点），空手淡青白光、捏合时亮起暖金光，让玩家即时确认手势与捏合状态；
    /// - 细星光射线：沿手部射线轻柔延伸；指中界面按钮 / 宝石星 / 缝隙时自动吸附到目标点并提亮，未指中时延伸 1.6 m 辅助校准指向。
    /// 仅在 XR 真机激活时显示（编辑器鼠标模拟不绘制），完全复用《摘星》原生手绘线条与四芒星材质。
    /// </summary>
    public class HandPointerView : MonoBehaviour
    {
        public GameRunner runner;
        public MenuPicker menuPicker;

        static readonly Color IdleRay = new(0.75f, 0.88f, 1.0f, 1f);
        static readonly Color HitRay = new(0.95f, 0.96f, 1.0f, 1f);
        static readonly Color PinchGold = new(1.0f, 0.86f, 0.56f, 1f);

        class HandVisual
        {
            public LineRenderer line;
            public Renderer tipDot;
            public Renderer endDot;
            public float alpha;
            public float pinchA;
            public Vector3 smoothEnd;
            public bool hasEnd;
        }

        readonly HandVisual[] visuals = { new(), new() };
        readonly Vector3[] linePts = new Vector3[2];

        void Start()
        {
            if (!runner) runner = FindAnyObjectByType<GameRunner>();
            if (!menuPicker) menuPicker = GetComponent<MenuPicker>() ?? FindAnyObjectByType<MenuPicker>();
        }

        void EnsureVisual(HandVisual v, int side)
        {
            if (v.line) return;
            var root = new GameObject("HandPointer" + side).transform;
            root.SetParent(transform, false);
            v.line = UIKit.Line(root, "Ray", new List<Vector3> { Vector3.zero, Vector3.forward }, 0.0018f, IdleRay, local: false);
            v.tipDot = UIKit.Glyph(root, "Tip", Vector3.zero, 0.024f);
            v.endDot = UIKit.Glyph(root, "End", Vector3.zero, 0.026f);
        }

        void LateUpdate()
        {
            if (!runner) return;
            bool showXR = runner.InXR;
            HandState l = default, r = default;
            if (showXR) runner.ReadHands(out l, out r);
            // 当双手都在视野中且锁中同一个眼动目标时，仅让主活跃手画连线，另一只闲置手只显示轻柔指尖光点避免画面杂乱
            Vector3 pL = Vector3.zero, pR = Vector3.zero;
            bool nearL = false, nearR = false;
            bool hitL = showXR && l.tracked && TryGetTargetPos(0, out pL, out nearL);
            bool hitR = showXR && r.tracked && TryGetTargetPos(1, out pR, out nearR);
            bool sameTarget = hitL && hitR && (pL - pR).sqrMagnitude < 0.01f;
            bool suppressRayL = sameTarget && !l.pinch && (r.pinch || r.hasRay);
            bool suppressRayR = sameTarget && !r.pinch && l.pinch;
            UpdateHand(0, showXR, l, hitL, pL, nearL, suppressRayL);
            UpdateHand(1, showXR, r, hitR, pR, nearR, suppressRayR);
        }

        void UpdateHand(int side, bool showXR, HandState h, bool hasTarget, Vector3 targetPos, bool isNear, bool suppressRay)
        {
            var v = visuals[side];
            EnsureVisual(v, side);

            float dt = Time.unscaledDeltaTime;
            bool visible = showXR && h.tracked;
            bool preLocked = visible && runner.pico && runner.pico.IsPreLocked(side);
            v.alpha = Mathf.MoveTowards(v.alpha, visible ? 1f : 0f, dt * (visible ? 12f : 8f));
            float targetPinchVis = visible && h.pinch ? 1f : (preLocked ? 0.45f : 0f);
            v.pinchA = Mathf.MoveTowards(v.pinchA, targetPinchVis, dt * 16f);

            if (v.alpha <= 0.002f)
            {
                UIKit.SetAlpha(v.line, IdleRay, 0f);
                UIKit.GlyphColor(v.tipDot, IdleRay, 0f);
                UIKit.GlyphColor(v.endDot, IdleRay, 0f);
                v.hasEnd = false;
                return;
            }

            // 1) 指尖四芒光点：贴紧真实指尖中点；准备捏合时渐暖金，捏紧时高亮暖金，直观确认命中时机
            v.tipDot.transform.position = h.pinchPoint;
            float tipScale = Mathf.Lerp(0.020f, 0.036f, v.pinchA);
            v.tipDot.transform.localScale = Vector3.one * tipScale;
            Color tipCol = Color.Lerp(IdleRay, PinchGold, v.pinchA);
            UIKit.GlyphColor(v.tipDot, tipCol, v.alpha * Mathf.Lerp(0.8f, 1.4f, v.pinchA));

            // 2) 细星光射线：有锁定目标时（含眼动锁定）直接从指尖轻柔连到目标中心，未锁定时按手部零视差射线延伸
            bool showRay = !suppressRay && !isNear && (hasTarget || h.hasRay);
            if (showRay)
            {
                Vector3 origin = h.pinchPoint;
                Vector3 rawEnd = hasTarget ? targetPos : origin + h.ray.direction * 3.2f;
                if (!v.hasEnd) { v.smoothEnd = rawEnd; v.hasEnd = true; }
                else v.smoothEnd = Vector3.Lerp(v.smoothEnd, rawEnd, 1f - Mathf.Exp(-dt * 38f));

                linePts[0] = origin;
                linePts[1] = v.smoothEnd;
                v.line.positionCount = 2;
                v.line.SetPositions(linePts);

                Color rayCol = hasTarget ? Color.Lerp(HitRay, PinchGold, v.pinchA * 0.8f) : Color.Lerp(IdleRay, PinchGold, v.pinchA * 0.5f);
                float rayAlpha = v.alpha * (hasTarget ? 0.65f : 0.30f);
                v.line.widthMultiplier = hasTarget ? 0.0022f : 0.0014f;
                UIKit.SetAlpha(v.line, rayCol, rayAlpha);

                // 射线终点星芒落点：锁定目标时轻柔聚在星心，未锁定时清晰显示指尖落点
                v.endDot.transform.position = v.smoothEnd;
                float dist = Vector3.Distance(origin, v.smoothEnd);
                float endScale = Mathf.Clamp(dist * (hasTarget ? 0.020f : 0.024f), 0.018f, 0.085f);
                v.endDot.transform.localScale = Vector3.one * endScale;
                UIKit.GlyphColor(v.endDot, rayCol, v.alpha * (hasTarget ? 0.78f : 0.82f));
            }
            else
            {
                UIKit.SetAlpha(v.line, IdleRay, 0f);
                UIKit.GlyphColor(v.endDot, IdleRay, 0f);
                v.hasEnd = false;
            }
        }

        bool TryGetTargetPos(int side, out Vector3 pos, out bool isNear)
        {
            pos = Vector3.zero;
            isNear = false;

            // 优先查菜单按钮目标
            if (menuPicker && menuPicker.active)
            {
                var t = menuPicker.CurrentTarget(side);
                if (t && t.isActiveAndEnabled && t.interactable)
                {
                    pos = t.transform.position;
                    // 若指尖离按钮很近则隐藏射线，保留星盘准星和指尖光点即可
                    runner.ReadHands(out var l, out var r);
                    var h = side == 0 ? l : r;
                    isNear = Vector3.Distance(h.pinchPoint, pos) <= t.radius + 0.08f;
                    return true;
                }
            }

            // 再查关卡内的手部 Hover / Drop 目标
            var g = runner.Game;
            if (g != null && g.Status == "playing")
            {
                string key = side == 0 ? "left" : "right";
                if (g.Hands.TryGetValue(key, out var gh) && gh.Tracked)
                {
                    if (gh.Holding != null && gh.Drop != null)
                    {
                        pos = runner.ToWorld(gh.Drop.Pos);
                        isNear = gh.Drop.Near;
                        return true;
                    }
                    if (gh.Holding == null && gh.Hover != null)
                    {
                        pos = runner.ToWorld(gh.Hover.Pos);
                        isNear = gh.Hover.Near;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
