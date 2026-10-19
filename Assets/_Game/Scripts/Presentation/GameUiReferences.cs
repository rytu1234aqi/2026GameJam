using UnityEngine;
using UnityEngine.UI;

namespace Spotlight.Presentation
{
    /// <summary>Editable UI references. GameView owns the listeners and all gameplay interaction.</summary>
    public sealed class GameUiReferences : MonoBehaviour
    {
        public Canvas Canvas;
        public ThirdPartyUiThemeSO ArtTheme;
        public TutorialPanelReferences TutorialPanelPrefab;
        public DayEventPanelReferences DayEventPanelPrefab;
        public NightResultPanelReferences NightResultPanelPrefab;
        public RunEndPanelReferences RunEndPanelPrefab;
        [Tooltip("可选；未绑定时使用兼容菜单。")] public MainMenuPanelReferences MainMenuPanelPrefab;
        [Tooltip("可选；只显示当前局已接收的黎明生产事件。")] public Text ProductionSummaryText;
        public Text TitleText, SpringHpText, WaveText, SkillText, GoldText, HintText, SelectionText;
        public Text[] HandTexts = new Text[6];
        public Text[] ItemTexts = new Text[2];
        public Button[] ElementButtons = new Button[6];
        public Button[] ItemButtons = new Button[2];
        public Button CancelSelectionButton, PauseButton, NormalSpeedButton, DoubleSpeedButton;
        public RectTransform ActionsRoot, ModalRoot;
        public Text DayText, PhaseText, SpeedText, PauseButtonText;
        public Slider SpringHpBar;

        public bool Validate(out string error)
        {
            if (Canvas == null) return Missing("Canvas", out error);
            if (TitleText == null) return Missing("TitleText", out error);
            if (SpringHpText == null) return Missing("SpringHpText", out error);
            if (WaveText == null) return Missing("WaveText", out error);
            if (SkillText == null) return Missing("SkillText", out error);
            if (GoldText == null) return Missing("GoldText", out error);
            if (HintText == null) return Missing("HintText", out error);
            if (SelectionText == null) return Missing("SelectionText", out error);
            if (CancelSelectionButton == null) return Missing("CancelSelectionButton", out error);
            if (PauseButton == null) return Missing("PauseButton", out error);
            if (NormalSpeedButton == null) return Missing("NormalSpeedButton", out error);
            if (DoubleSpeedButton == null) return Missing("DoubleSpeedButton", out error);
            if (ActionsRoot == null) return Missing("ActionsRoot", out error);
            if (ModalRoot == null) return Missing("ModalRoot", out error);
            if (!ValidateArray(HandTexts, 6, "HandTexts", out error)) return false;
            if (!ValidateArray(ItemTexts, 2, "ItemTexts", out error)) return false;
            if (!ValidateArray(ElementButtons, 6, "ElementButtons", out error)) return false;
            if (!ValidateArray(ItemButtons, 2, "ItemButtons", out error)) return false;
            System.Collections.Generic.HashSet<Button> buttons = new System.Collections.Generic.HashSet<Button>();
            foreach (Button button in ElementButtons) if (!buttons.Add(button)) return Duplicate(out error);
            foreach (Button button in ItemButtons) if (!buttons.Add(button)) return Duplicate(out error);
            foreach (Button button in new Button[] { CancelSelectionButton, PauseButton, NormalSpeedButton, DoubleSpeedButton })
                if (!buttons.Add(button)) return Duplicate(out error);
            if(!Canvas.transform.IsChildOf(transform)){error="Canvas 必须属于此 UI Prefab。";return false;}
            System.Collections.Generic.List<Component> references=new System.Collections.Generic.List<Component>
            {TitleText,SpringHpText,WaveText,SkillText,GoldText,HintText,SelectionText,CancelSelectionButton,PauseButton,NormalSpeedButton,DoubleSpeedButton,ActionsRoot,ModalRoot};
            references.AddRange(HandTexts);references.AddRange(ItemTexts);references.AddRange(ElementButtons);references.AddRange(ItemButtons);
            references.Add(ProductionSummaryText);references.Add(DayText);references.Add(PhaseText);references.Add(SpeedText);references.Add(PauseButtonText);references.Add(SpringHpBar);
            foreach(Component reference in references)if(reference!=null&&!reference.transform.IsChildOf(Canvas.transform))
            {error="UI 引用必须属于 Prefab Canvas："+reference.name;return false;}
            error = null;
            return true;
        }
        private static bool ValidateArray<T>(T[] values, int count, string name, out string error) where T : Object
        {
            if (values == null || values.Length != count) { error = name + " 必须包含 " + count + " 个引用。"; return false; }
            for (int i = 0; i < count; i++) if (values[i] == null) return Missing(name + "[" + i + "]", out error);
            error = null;
            return true;
        }
        private static bool Missing(string name, out string error) { error = "UI 引用未绑定：" + name; return false; }
        private static bool Duplicate(out string error) { error = "UI 按钮引用重复，请为每项绑定独立按钮。"; return false; }
    }
}
