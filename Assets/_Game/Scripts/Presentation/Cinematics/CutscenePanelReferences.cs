using UnityEngine;
using UnityEngine.UI;

namespace Spotlight.Presentation.Cinematics
{
    public sealed class CutscenePanelReferences : MonoBehaviour
    {
        public Image IllustrationA, IllustrationB;
        public RectTransform IllustrationRoot, TextPanel;
        public CanvasGroup RootGroup, TextGroup;
        public Text TitleText, BodyText, CounterText, ContinueText, HintText;
        public Button ContinueButton, SkipButton;

        public bool Validate(out string error)
        {
            bool valid = IllustrationA != null && IllustrationB != null && IllustrationRoot != null &&
                TextPanel != null && RootGroup != null && TextGroup != null && TitleText != null &&
                BodyText != null && CounterText != null && ContinueText != null && ContinueButton != null && SkipButton != null;
            valid = valid && RootGroup.transform == transform && TextGroup.transform.IsChildOf(transform);
            error = valid ? null : "图文过场界面引用不完整。"; return valid;
        }

        // 共用运行时与编辑器工厂，不额外创建 Canvas 或 EventSystem。
        public static CutscenePanelReferences CreateDefaultPanel(Transform parent, Font font = null)
        {
            if (font == null) font = GameView.CreateFont();
            RectTransform root = Rect(parent, "Opening Cutscene", Vector2.zero, Vector2.one);
            Image background = root.gameObject.AddComponent<Image>(); background.color = new Color(.025f, .04f, .075f); background.raycastTarget = true;
            root.gameObject.AddComponent<CutsceneBackdropGradient>();
            CutscenePanelReferences p = root.gameObject.AddComponent<CutscenePanelReferences>();
            p.RootGroup = root.gameObject.AddComponent<CanvasGroup>();
            p.IllustrationRoot = Rect(root, "Illustration", Vector2.zero, Vector2.one);
            p.IllustrationA = Picture(p.IllustrationRoot, "Image A"); p.IllustrationB = Picture(p.IllustrationRoot, "Image B");
            // 叠加暗色底与半透明条，让缺图页面仍有清晰的纵向色阶。
            RectTransform shade = Rect(root, "Lower Shade", new Vector2(0, 0), new Vector2(1, .48f));
            Image shadeImage = shade.gameObject.AddComponent<Image>(); shadeImage.color = new Color(.015f, .025f, .055f, .94f); shadeImage.raycastTarget = false;
            p.TextPanel = Rect(root, "Text Panel", new Vector2(.1f, .08f), new Vector2(.9f, .44f));
            p.TextGroup = p.TextPanel.gameObject.AddComponent<CanvasGroup>();
            p.TitleText = Label(p.TextPanel, "Title", font, 32, new Vector2(0, .79f), Vector2.one);
            p.BodyText = Label(p.TextPanel, "Body", font, 24, new Vector2(0, .18f), new Vector2(1, .76f));
            p.CounterText = Label(root, "Counter", font, 18, new Vector2(.1f, .94f), new Vector2(.3f, .98f));
            p.HintText = Label(p.TextPanel, "Hint", font, 16, Vector2.zero, new Vector2(.7f, .14f));
            p.ContinueButton = Control(root, "Continue", font, new Vector2(.76f, .025f), new Vector2(.9f, .085f), out p.ContinueText);
            Text skipLabel; p.SkipButton = Control(root, "Skip", font, new Vector2(.82f, .93f), new Vector2(.94f, .98f), out skipLabel); skipLabel.text = "跳过剧情";
            return p;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            RectTransform r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.anchorMin = min; r.anchorMax = max; r.offsetMin = r.offsetMax = Vector2.zero; return r;
        }
        private static Image Picture(Transform parent, string name)
        { Image i = Rect(parent, name, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>(); i.preserveAspect = true; i.raycastTarget = false; i.color = Color.clear; return i; }
        private static Text Label(Transform parent, string name, Font font, int size, Vector2 min, Vector2 max)
        {
            Text t = Rect(parent, name, min, max).gameObject.AddComponent<Text>(); t.font = font; t.fontSize = size;
            t.color = new Color(.93f, .94f, .98f); t.raycastTarget = false; t.supportRichText = false;
            t.alignment = TextAnchor.UpperLeft; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; return t;
        }
        private static Button Control(Transform parent, string name, Font font, Vector2 min, Vector2 max, out Text label)
        {
            RectTransform r = Rect(parent, name, min, max); Image i = r.gameObject.AddComponent<Image>(); i.color = new Color(.15f, .22f, .3f, .9f);
            Button b = r.gameObject.AddComponent<Button>(); b.targetGraphic = i;
            label = Label(r, "Label", font, 18, Vector2.zero, Vector2.one); label.alignment = TextAnchor.MiddleCenter; return b;
        }
    }
}
