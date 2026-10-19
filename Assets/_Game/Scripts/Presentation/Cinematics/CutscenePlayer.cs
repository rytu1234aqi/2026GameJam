using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Spotlight.Presentation.Cinematics
{
    /// <summary>只负责演出与完成通知，不访问流程或存档服务。</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CutscenePlayer : MonoBehaviour, IPointerClickHandler
    {
        public CutscenePanelReferences Panel;
        public bool IsPlaying { get; private set; }
        public bool IsTransitioning { get { return IsPlaying && (transitioning || ending); } }
        public int SlideIndex { get; private set; }
        public string RevealedText { get; private set; }
        public int VisibleCharacterCount { get; private set; }
        private CutsceneSequenceSO sequence;
        private Action completion;
        private CutsceneSlide slide;
        private int[] boundaries;
        private string body;
        private float elapsed, reveal, hold, fade;
        private bool transitioning, ending, listeners;
        private bool advancedThisTick;
        private Image endingVeil;

        public void Begin(CutsceneSequenceSO value, Action complete)
        {
            Cancel(); advancedThisTick = false;
            if (Panel == null) throw new InvalidOperationException("缺少过场界面。");
            string error; if (!Panel.Validate(out error)) throw new InvalidOperationException(error);
            sequence = value; completion = complete;
            if (value == null || value.Slides == null || value.Slides.Length == 0) { Finish(); return; }
            Panel.ContinueButton.onClick.AddListener(Advance); Panel.SkipButton.onClick.AddListener(Skip); listeners = true;
            Panel.gameObject.SetActive(true); Panel.transform.SetAsLastSibling();
            Panel.RootGroup.blocksRaycasts = true; Panel.RootGroup.interactable = true; Panel.RootGroup.alpha = 0;
            PrepareEndingVeil();
            Panel.SkipButton.gameObject.SetActive(value.AllowSkip); IsPlaying = true; SlideIndex = 0; ShowSlide();
        }
        private void ShowSlide()
        {
            slide = sequence.Slides[SlideIndex] ?? new CutsceneSlide(); body = slide.Body ?? "";
            boundaries = StringInfo.ParseCombiningCharacters(body); reveal = hold = elapsed = fade = 0;
            VisibleCharacterCount = 0; RevealedText = ""; transitioning = true; ending = false;
            Panel.IllustrationA.sprite = Panel.IllustrationB.sprite;
            Panel.IllustrationA.color = Panel.IllustrationA.sprite != null ? Color.white : Color.clear;
            Panel.IllustrationB.sprite = slide.Illustration; Panel.IllustrationB.color = Color.clear;
            Panel.TitleText.text = slide.Title ?? ""; Panel.BodyText.text = "";
            Panel.CounterText.text = (SlideIndex + 1) + " / " + sequence.Slides.Length;
            Panel.ContinueText.text = "显示全文"; Panel.TextGroup.alpha = 0;
            if (Panel.HintText != null) Panel.HintText.text = "点击画面或按空格 / 回车继续";
            Motion();
        }
        private void Update()
        {
            if (!IsPlaying) return;
            Tick(Time.unscaledDeltaTime);
            if (IsPlaying && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))) Advance();
        }
        public void OnPointerClick(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) Advance(); }
        public void Tick(float unscaledSeconds)
        {
            // Tick 建立新的输入周期；UI Submit 与同一周期的键盘处理只能推进一次。
            advancedThisTick = false;
            if (!IsPlaying) return;
            float dt = Safe(unscaledSeconds, 0, 0, 60); elapsed += dt; Motion();
            float duration = Safe(slide.FadeSeconds, .65f, 0, 30);
            if (ending)
            {
                // 整个过场保持遮挡，只把最上层淡为黑色；根透明会露出菜单。
                fade += dt; Panel.RootGroup.alpha = 1;
                endingVeil.color = new Color(0, 0, 0, duration <= 0 ? 1 : Mathf.Clamp01(fade / duration));
                if (fade >= duration) Finish(); return;
            }
            if (transitioning)
            {
                fade += dt; float t = duration <= 0 ? 1 : Mathf.Clamp01(fade / duration);
                Panel.RootGroup.alpha = SlideIndex == 0 ? t : 1; Panel.TextGroup.alpha = t;
                Panel.IllustrationA.color = new Color(1, 1, 1, Panel.IllustrationA.sprite == null ? 0 : 1 - t);
                Panel.IllustrationB.color = new Color(1, 1, 1, slide.Illustration == null ? 0 : t);
                if (t >= 1) transitioning = false; return;
            }
            if (VisibleCharacterCount < boundaries.Length)
            {
                reveal += dt * Safe(slide.CharactersPerSecond, 26, .01f, 10000);
                Reveal(Mathf.Min(boundaries.Length, Mathf.FloorToInt(reveal)));
            }
            else if (slide.AutoAdvance)
            { hold += dt; if (hold >= Safe(slide.HoldSeconds, 2, 0, 3600)) Advance(); }
        }
        private void Reveal(int count)
        {
            VisibleCharacterCount = count;
            int end = count >= boundaries.Length ? body.Length : boundaries[count];
            RevealedText = body.Substring(0, end); Panel.BodyText.text = RevealedText;
            Panel.ContinueText.text = count < boundaries.Length ? "显示全文" : SlideIndex + 1 < sequence.Slides.Length ? "继续" : "开始旅程";
        }
        public void Advance()
        {
            if (!IsPlaying || IsTransitioning || advancedThisTick) return;
            advancedThisTick = true;
            if (VisibleCharacterCount < boundaries.Length) { Reveal(boundaries.Length); hold = 0; return; }
            if (SlideIndex + 1 < sequence.Slides.Length) { SlideIndex++; ShowSlide(); }
            else { ending = true; fade = 0; }
        }
        public void Skip()
        { if (IsPlaying && sequence.AllowSkip && !ending) { ending = true; transitioning = false; fade = 0; } }
        public void Cancel()
        {
            completion = null; IsPlaying = false; transitioning = ending = false;
            if (Panel != null)
            {
                if (listeners) { Panel.ContinueButton.onClick.RemoveListener(Advance); Panel.SkipButton.onClick.RemoveListener(Skip); }
                Panel.gameObject.SetActive(false);
            }
            listeners = false;
            if (endingVeil != null) endingVeil.color = Color.clear;
        }
        private void PrepareEndingVeil()
        {
            // 同一个播放器重复 Begin 时复用遮罩，不增加 Canvas 或输入入口。
            if (endingVeil == null)
            {
                RectTransform rect = new GameObject("Ending Black Veil", typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(Panel.transform, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                endingVeil = rect.gameObject.AddComponent<Image>(); endingVeil.raycastTarget = false;
            }
            endingVeil.transform.SetAsLastSibling(); endingVeil.color = Color.clear;
        }
        private void Finish()
        { Action callback = completion; Cancel(); if (callback != null) callback(); }
        private void OnDisable() { if (IsPlaying) Cancel(); }
        private void Motion()
        {
            float t = Mathf.Clamp01(elapsed / Safe(slide.MotionSeconds, 8, .01f, 3600));
            float zoom = Mathf.Lerp(Safe(slide.ZoomStart, 1, .1f, 10), Safe(slide.ZoomEnd, 1.06f, .1f, 10), t);
            Panel.IllustrationRoot.localScale = new Vector3(zoom, zoom, 1);
            Vector2 a = new Vector2(Safe(slide.PanStart.x, 0, -1, 1), Safe(slide.PanStart.y, 0, -1, 1));
            Vector2 b = new Vector2(Safe(slide.PanEnd.x, 0, -1, 1), Safe(slide.PanEnd.y, 0, -1, 1));
            Vector2 pan = Vector2.Lerp(a, b, t); RectTransform root = Panel.transform as RectTransform;
            Panel.IllustrationRoot.anchoredPosition = Vector2.Scale(pan, root.rect.size);
        }
        private static float Safe(float value, float fallback, float min, float max)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max); }
    }
}
