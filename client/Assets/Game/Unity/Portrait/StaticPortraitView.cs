using Game.Domain.Portrait;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// L0 靜態立繪（design/09 §1）：SpriteRenderer 顯示 art.stand，加上程式呼吸效果（sine 微幅縮放 y）。
    /// 用法：PortraitResolver.Resolve(...) 的結果丟給 Show()。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class StaticPortraitView : MonoBehaviour, IPortraitView
    {
        [SerializeField] private float breathAmplitude = 0.01f;   // y 縮放幅度（比例）
        [SerializeField] private float breathPeriodSec = 3.5f;

        private SpriteRenderer _renderer;
        private Vector3 _baseScale;
        private float _t;
        private ResolvedPortrait _current;
        private string _expression = "neutral";

        public ResolvedPortrait Current { get { return _current; } }
        public string Expression { get { return _expression; } }

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _baseScale = transform.localScale;
        }

        public void Show(ResolvedPortrait portrait)
        {
            _current = portrait;
            string key = portrait != null && portrait.Art != null ? portrait.Art.Stand : null;
            AssetService assets = GameServices.Assets;
            if (assets == null)
            {
                Debug.LogWarning("[Portrait] GameServices 尚未就緒");
                return;
            }
            _renderer.sprite = assets.LoadSprite(key);
            _expression = "neutral";
        }

        public void SetExpression(string expr)
        {
            _expression = expr;
            // TODO: 疊上表情差分 key char/{charId}/portrait/{stageId|costumeId}/face_{expr}（頭身對位，偏移 0 px，design/15 §3）。
        }

        public void PlayMotion(string motion, bool loop = false)
        {
            // TODO: 靜態立繪用程式動作模擬（greet = 小幅跳動、tap_head = 縮放彈跳...）。
        }

        public void LookAt(Vector2 screenPos)
        {
            // 靜態立繪不做視線跟隨。TODO: 可改為整體微幅傾斜。
        }

        private void Update()
        {
            if (breathPeriodSec <= 0f) return;
            _t += Time.deltaTime;
            float k = 1f + breathAmplitude * Mathf.Sin(_t * 2f * Mathf.PI / breathPeriodSec);
            transform.localScale = new Vector3(_baseScale.x, _baseScale.y * k, _baseScale.z);
        }

        private void OnDisable()
        {
            transform.localScale = _baseScale;
        }
    }
}
