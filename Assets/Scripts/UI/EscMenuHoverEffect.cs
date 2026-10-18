using UnityEngine;
using UnityEngine.EventSystems;

namespace LudumDare60.UI
{
    [DisallowMultipleComponent]
    public sealed class EscMenuHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private RectTransform visual;
        [SerializeField] private EscMenuSweepGraphic sweep;
        [SerializeField, Min(0)] private float leftShift = 22;
        [SerializeField, Min(0.01f)] private float smoothTime = 0.12f;
        [SerializeField, Min(0.1f)] private float sweepDuration = 0.85f;
        [SerializeField, Min(0)] private float sweepPause = 0.25f;
        private UnityEngine.UI.Selectable selectable;
        private Vector2 restPosition;
        private float velocity, amount, elapsed;
        private bool hovered;

        public void Configure(RectTransform target, EscMenuSweepGraphic graphic, float shift)
        { visual = target; sweep = graphic; leftShift = shift; }

        private void Awake()
        {
            selectable = GetComponent<UnityEngine.UI.Selectable>();
            if (visual != null) restPosition = visual.anchoredPosition;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (selectable != null && !selectable.IsInteractable()) return;
            if (!hovered) elapsed = 0;
            hovered = true;
        }

        public void OnPointerExit(PointerEventData eventData) => hovered = false;

        private void Update()
        {
            if (selectable != null && !selectable.IsInteractable()) hovered = false;
            if (!hovered && amount == 0) return;
            // The pause menu runs while timeScale is zero. Its input rectangle stays stationary.
            var delta = Time.unscaledDeltaTime;
            amount = Mathf.SmoothDamp(amount, hovered ? 1 : 0, ref velocity, smoothTime, Mathf.Infinity, delta);
            if (!hovered && amount < 0.001f) { amount = 0; velocity = 0; }
            if (visual != null) visual.anchoredPosition = restPosition + Vector2.left * (leftShift * amount);
            elapsed += delta;
            if (sweep != null)
                sweep.SetSweep(Mathf.Repeat(elapsed, sweepDuration + sweepPause) / sweepDuration, amount);
        }

        private void OnDisable()
        {
            hovered = false; amount = velocity = elapsed = 0;
            if (visual != null) visual.anchoredPosition = restPosition;
            if (sweep != null) sweep.SetSweep(0, 0);
        }
    }
}
