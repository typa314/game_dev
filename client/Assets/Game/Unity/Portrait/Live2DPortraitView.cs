using System;
using System.Collections.Generic;
using Game.Domain.Portrait;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Expression;
using Live2D.Cubism.Framework.Motion;
using Live2D.Cubism.Framework.MouthMovement;
using Live2D.Cubism.Framework.Raycasting;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// L1/L2 Live2D 立繪（design/09、design/17 §F5）。
    /// 整合 Live2D Cubism SDK for Unity (5-r.6.1)，支援：
    /// 1. 載入與綁定 CubismModel Prefab（含官方範例模型 Mao / Koharu / Natori 等）
    /// 2. 語意化表情切換（SetExpression -> CubismExpressionController）
    /// 3. 語意化動作播放與點擊部位反應（PlayMotion / OnTouched / RaycastTouch）
    /// 4. 口型同步（SetLipSync -> CubismMouthController & ParamMouthOpenY）
    /// 5. 視線跟隨（LookAt -> ParamAngleX/Y、ParamEyeBallX/Y）
    /// </summary>
    public sealed class Live2DPortraitView : MonoBehaviour, IPortraitView
    {
        private static readonly Dictionary<string, int> SemanticExpressionMap =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "neutral", 0 },
                { "smile", 1 },
                { "tsundere", 2 },
                { "shy", 3 },
                { "angry", 4 },
                { "sad", 5 },
                { "surprised", 6 },
                { "seduce", 7 }
            };

        private readonly Dictionary<string, AnimationClip> _motionMap =
            new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);

        private CubismParameter _paramMouthOpenY;
        private CubismParameter _paramAngleX;
        private CubismParameter _paramAngleY;
        private CubismParameter _paramEyeBallX;
        private CubismParameter _paramEyeBallY;
        private Vector2 _lookTargetNormalized;

        public CubismModel Model { get; private set; }
        public CubismExpressionController ExpressionController { get; private set; }
        public CubismMotionController MotionController { get; private set; }
        public CubismMouthController MouthController { get; private set; }
        public CubismRaycaster Raycaster { get; private set; }

        public ResolvedPortrait CurrentPortrait { get; private set; }
        public string CurrentExpression { get; private set; } = "neutral";
        public int CurrentExpressionIndex => ExpressionController != null ? ExpressionController.CurrentExpressionIndex : -1;
        public string CurrentMotion { get; private set; } = "idle";
        public bool IsMotionLooping { get; private set; }
        public float LipSyncLevel { get; private set; }
        public int TouchReactionCount { get; private set; }
        public string LastTouchedArea { get; private set; }

        public float MouthOpenParameterValue => _paramMouthOpenY != null ? _paramMouthOpenY.Value : LipSyncLevel;
        public float AngleXParameterValue => _paramAngleX != null ? _paramAngleX.Value : 0f;
        public float AngleYParameterValue => _paramAngleY != null ? _paramAngleY.Value : 0f;

        /// <summary>
        /// 從指定 Prefab 實例化 Live2D 模型並綁定控制器與參數表。
        /// </summary>
        public CubismModel LoadFromPrefab(GameObject modelPrefab, AnimationClip[] clips = null)
        {
            if (modelPrefab == null)
            {
                throw new ArgumentNullException(nameof(modelPrefab));
            }

            if (Model != null && Model.gameObject != null)
            {
                if (Application.isPlaying)
                    Destroy(Model.gameObject);
                else
                    DestroyImmediate(Model.gameObject);
            }

            GameObject instance = Instantiate(modelPrefab, transform, false);
            instance.name = modelPrefab.name;
            CubismModel cubismModel = instance.GetComponent<CubismModel>() ?? instance.GetComponentInChildren<CubismModel>();
            if (cubismModel == null)
            {
                throw new InvalidOperationException($"Prefab '{modelPrefab.name}' 不包含 CubismModel 組件。");
            }

            BindModel(cubismModel, clips);
            return cubismModel;
        }

        /// <summary>
        /// 綁定已存在的 CubismModel 實例並快取核心參數。
        /// </summary>
        public void BindModel(CubismModel model, AnimationClip[] clips = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));

            ExpressionController = model.GetComponent<CubismExpressionController>();
            MotionController = model.GetComponent<CubismMotionController>() ?? model.gameObject.AddComponent<CubismMotionController>();
            MouthController = model.GetComponent<CubismMouthController>();
            Raycaster = model.GetComponent<CubismRaycaster>() ?? model.gameObject.AddComponent<CubismRaycaster>();

            if (MouthController != null)
            {
                MouthController.BlendMode = CubismParameterBlendMode.Override;
                MouthController.Refresh();
            }

            CacheParameters(model);
            RegisterMotionClips(clips);

            CurrentExpression = "neutral";
            CurrentMotion = "idle";
            LipSyncLevel = 0f;
            StepModelUpdate(0f);
        }

        public void Show(ResolvedPortrait portrait)
        {
            CurrentPortrait = portrait;
            CurrentExpression = "neutral";
            CurrentMotion = "idle";
        }

        /// <summary>
        /// 切換語意化表情（如 neutral, smile, shy, angry, surprised 或 exp_01..exp_08）。
        /// </summary>
        public void SetExpression(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr))
            {
                return;
            }

            CurrentExpression = expr.Trim();

            if (ExpressionController != null &&
                ExpressionController.ExpressionsList != null &&
                ExpressionController.ExpressionsList.CubismExpressionObjects != null &&
                ExpressionController.ExpressionsList.CubismExpressionObjects.Length > 0)
            {
                var list = ExpressionController.ExpressionsList.CubismExpressionObjects;
                int resolvedIndex = -1;

                if (SemanticExpressionMap.TryGetValue(CurrentExpression, out int mappedIdx))
                {
                    resolvedIndex = Mathf.Clamp(mappedIdx, 0, list.Length - 1);
                }
                else
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (list[i] != null && list[i].name.IndexOf(CurrentExpression, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            resolvedIndex = i;
                            break;
                        }
                    }
                }

                if (resolvedIndex >= 0)
                {
                    ExpressionController.CurrentExpressionIndex = resolvedIndex;
                    ExpressionController.OnLateUpdate();
                }
            }
        }

        /// <summary>
        /// 播放語意化動作（idle, greet, tap_head, tap_body, happy, awaken）。
        /// </summary>
        public void PlayMotion(string motion, bool loop = false)
        {
            if (string.IsNullOrWhiteSpace(motion))
            {
                return;
            }

            CurrentMotion = motion.Trim();
            IsMotionLooping = loop;

            if (MotionController != null && _motionMap.TryGetValue(CurrentMotion, out AnimationClip clip) && clip != null)
            {
                int priority = string.Equals(CurrentMotion, "idle", StringComparison.OrdinalIgnoreCase)
                    ? CubismMotionPriority.PriorityIdle
                    : CubismMotionPriority.PriorityNormal;
                MotionController.PlayAnimation(clip, isLoop: loop, priority: priority);
            }
        }

        /// <summary>
        /// 設定口型開合程度（0..1），驅動 CubismMouthController 與 ParamMouthOpenY。
        /// </summary>
        public void SetLipSync(float level)
        {
            LipSyncLevel = Mathf.Clamp01(level);

            if (MouthController != null)
            {
                MouthController.MouthOpening = LipSyncLevel;
                MouthController.OnLateUpdate();
            }

            if (_paramMouthOpenY != null)
            {
                _paramMouthOpenY.Value = LipSyncLevel;
            }
        }

        /// <summary>
        /// 視線跟隨（將輸入座標轉換為 -1..1 視線偏移，驅動 ParamAngleX/Y 與 ParamEyeBallX/Y）。
        /// </summary>
        public void LookAt(Vector2 screenPos)
        {
            float nx = Mathf.Clamp(screenPos.x, -1f, 1f);
            float ny = Mathf.Clamp(screenPos.y, -1f, 1f);
            if (Mathf.Abs(screenPos.x) > 1f || Mathf.Abs(screenPos.y) > 1f)
            {
                float w = Screen.width > 0 ? Screen.width : 1920f;
                float h = Screen.height > 0 ? Screen.height : 1080f;
                nx = Mathf.Clamp((screenPos.x / w) * 2f - 1f, -1f, 1f);
                ny = Mathf.Clamp((screenPos.y / h) * 2f - 1f, -1f, 1f);
            }

            _lookTargetNormalized = new Vector2(nx, ny);
            ApplyLookAtParameters();
        }

        /// <summary>
        /// 觸發部位點擊反應（Head -> 切換表情與 tap_head；Body -> 播放 tap_body）。
        /// 若傳入未知或空區域則回傳 false（供 G3 陽性對照驗證）。
        /// </summary>
        public bool OnTouched(string hitArea)
        {
            if (string.IsNullOrWhiteSpace(hitArea))
            {
                return false;
            }

            string area = hitArea.Trim();
            if (string.Equals(area, "Head", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(area, "tap_head", StringComparison.OrdinalIgnoreCase))
            {
                LastTouchedArea = "Head";
                TouchReactionCount++;
                SetExpression(TouchReactionCount % 2 == 1 ? "smile" : "shy");
                PlayMotion("tap_head", loop: false);
                return true;
            }

            if (string.Equals(area, "Body", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(area, "tap_body", StringComparison.OrdinalIgnoreCase))
            {
                LastTouchedArea = "Body";
                TouchReactionCount++;
                PlayMotion("tap_body", loop: false);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 使用 CubismRaycaster 對模型網格執行射線檢測並觸發對應點擊反應。
        /// </summary>
        public string RaycastTouch(Ray ray)
        {
            if (Raycaster == null)
            {
                return null;
            }

            var results = new CubismRaycastHit[4];
            int count = Raycaster.Raycast(ray, results);
            for (int i = 0; i < count; i++)
            {
                var drawable = results[i].Drawable;
                if (drawable == null) continue;

                var hitComp = drawable.GetComponent<CubismHitDrawable>();
                string areaName = hitComp != null && !string.IsNullOrEmpty(hitComp.Name) ? hitComp.Name : drawable.name;
                if (OnTouched(areaName))
                {
                    return LastTouchedArea;
                }
            }
            return null;
        }

        /// <summary>
        /// 推進單幀 Live2D 模型參數與原生頂點更新（可用於 EditMode/PlayMode 決定性測試與效能基準量測）。
        /// </summary>
        public void StepModelUpdate(float deltaTime = 1f / 60f)
        {
            if (Model == null) return;

            if (ExpressionController != null)
            {
                ExpressionController.OnLateUpdate();
            }

            ApplyLookAtParameters();

            if (_paramMouthOpenY != null)
            {
                _paramMouthOpenY.Value = LipSyncLevel;
            }

            Model.ForceUpdateNow();
        }

        private void ApplyLookAtParameters()
        {
            if (_paramAngleX != null) _paramAngleX.Value = _lookTargetNormalized.x * 30f;
            if (_paramAngleY != null) _paramAngleY.Value = _lookTargetNormalized.y * 30f;
            if (_paramEyeBallX != null) _paramEyeBallX.Value = _lookTargetNormalized.x;
            if (_paramEyeBallY != null) _paramEyeBallY.Value = _lookTargetNormalized.y;
        }

        private void CacheParameters(CubismModel model)
        {
            _paramMouthOpenY = null;
            _paramAngleX = null;
            _paramAngleY = null;
            _paramEyeBallX = null;
            _paramEyeBallY = null;

            var parameters = model.Parameters;
            if (parameters == null) return;

            for (int i = 0; i < parameters.Length; i++)
            {
                CubismParameter p = parameters[i];
                if (p == null) continue;
                string id = p.Id;
                if (string.Equals(id, "ParamMouthOpenY", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id, "PARAM_MOUTH_OPEN_Y", StringComparison.OrdinalIgnoreCase))
                {
                    _paramMouthOpenY = p;
                }
                else if (string.Equals(id, "ParamAngleX", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(id, "PARAM_ANGLE_X", StringComparison.OrdinalIgnoreCase))
                {
                    _paramAngleX = p;
                }
                else if (string.Equals(id, "ParamAngleY", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(id, "PARAM_ANGLE_Y", StringComparison.OrdinalIgnoreCase))
                {
                    _paramAngleY = p;
                }
                else if (string.Equals(id, "ParamEyeBallX", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(id, "PARAM_EYE_BALL_X", StringComparison.OrdinalIgnoreCase))
                {
                    _paramEyeBallX = p;
                }
                else if (string.Equals(id, "ParamEyeBallY", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(id, "PARAM_EYE_BALL_Y", StringComparison.OrdinalIgnoreCase))
                {
                    _paramEyeBallY = p;
                }
            }
        }

        private void RegisterMotionClips(AnimationClip[] clips)
        {
            _motionMap.Clear();
            if (clips == null || clips.Length == 0) return;

            string[] semantics = { "idle", "greet", "tap_head", "tap_body", "happy", "awaken" };
            for (int i = 0; i < semantics.Length; i++)
            {
                _motionMap[semantics[i]] = clips[i % clips.Length];
            }

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && !string.IsNullOrEmpty(clips[i].name))
                {
                    _motionMap[clips[i].name] = clips[i];
                }
            }
        }
    }
}
