using UnityEngine;
using UnityEngine.UI;

namespace Spotlight.Presentation
{
    [CreateAssetMenu(menuName="聚光灯/美术/UI 主题")]
    public sealed class ThirdPartyUiThemeSO : ScriptableObject
    {
        public Sprite DayBackground, NightBackground;
        public Material WorldSpriteMaterial;
        public Sprite PortraitNeutral, PortraitCalm, PortraitSmile, PortraitAngry, PortraitCrying;
        public Sprite[] ButtonNormal = new Sprite[4], ButtonPressed = new Sprite[4];

        public void ApplyButton(Button button, int style = -1)
        {
            if(button==null || ButtonNormal==null || ButtonNormal.Length==0)return;
            Image image=button.targetGraphic as Image;if(image==null)image=button.GetComponent<Image>();
            if(image==null)return;
            int index=style<0?System.Array.IndexOf(ButtonNormal,image.sprite):style%ButtonNormal.Length;
            if(index<0)index=0;
            Sprite normal=ButtonNormal[index];if(normal==null)return;
            image.sprite=normal;image.type=Image.Type.Sliced;image.color=Color.white;button.targetGraphic=image;
            button.transition=Selectable.Transition.SpriteSwap;
            SpriteState state=button.spriteState;
            state.highlightedSprite=normal;state.selectedSprite=normal;
            state.pressedSprite=ButtonPressed!=null&&index<ButtonPressed.Length?ButtonPressed[index]:normal;
            state.disabledSprite=normal;button.spriteState=state;
            bool hasElementIcon=false;
            foreach(Transform child in button.GetComponentsInChildren<Transform>(true))
                if(child!=button.transform&&child.name.StartsWith("ElementIcon",System.StringComparison.Ordinal)){hasElementIcon=true;break;}
            foreach(Text label in button.GetComponentsInChildren<Text>(true))
            {
                label.color=index==0?new Color(1f,.97f,.86f):new Color(.16f,.12f,.09f,1f);
                if(hasElementIcon)continue;
                Fill(label.rectTransform,Vector2.zero,Vector2.one);label.rectTransform.offsetMin=new Vector2(10,3);label.rectTransform.offsetMax=new Vector2(-10,-3);
            }
        }
        public void ApplyState(Button button, bool selected = false)
        {
            if(button==null||button.targetGraphic==null)return;
            button.targetGraphic.color=!button.IsInteractable()?new Color(.55f,.55f,.55f,.6f):selected?new Color(.65f,.9f,1f,1f):Color.white;
        }
        public void ApplyButtons(Transform root)
        { int i=0;foreach(Button button in root.GetComponentsInChildren<Button>(true))ApplyButton(button,i++); }

        public void ApplyMenu(MainMenuPanelReferences menu)
        {
            if(menu==null)return;
            Fill(menu.Root,new Vector2(0,0),new Vector2(1,1));
            Image backdrop=Art(menu.Root,"ThirdPartyMenuBackground",DayBackground,new Vector2(0,0),new Vector2(1,1),false);
            backdrop.transform.SetAsFirstSibling();
            Image plate=Art(menu.Root,"ThirdPartyMenuPlate",null,new Vector2(.035f,.09f),new Vector2(.46f,.92f),false);
            plate.color=new Color(.08f,.12f,.13f,.86f);plate.transform.SetSiblingIndex(1);
            Art(menu.Root,"ThirdPartyPortrait",PortraitNeutral,new Vector2(.48f,0),new Vector2(.99f,.98f),true).transform.SetSiblingIndex(2);
            Fill(menu.TitleText.rectTransform,new Vector2(.07f,.70f),new Vector2(.43f,.87f));
            menu.TitleText.fontSize=54;menu.TitleText.color=new Color(1,.95f,.78f);menu.TitleText.alignment=TextAnchor.MiddleLeft;
            Fill(menu.HintText.rectTransform,new Vector2(.07f,.12f),new Vector2(.43f,.25f));menu.HintText.color=Color.white;
            Button[] buttons={menu.StoryButton,menu.ContinueButton,menu.EndlessButton};
            for(int i=0;i<buttons.Length;i++) {Fill((RectTransform)buttons[i].transform,new Vector2(.07f+i*.018f,.57f-i*.13f),new Vector2(.40f+i*.018f,.66f-i*.13f));ApplyButton(buttons[i],i);}
        }
        // Reserve a narrow illustration column without overlapping existing text or controls.
        public void ApplyDialogue(RectTransform root, Sprite portrait)
        {
            if(root==null||portrait==null)return;
            ApplyButtons(root);
            TutorialPanelReferences tutorial=root.GetComponent<TutorialPanelReferences>();
            DayEventPanelReferences day=root.GetComponent<DayEventPanelReferences>();
            NightResultPanelReferences night=root.GetComponent<NightResultPanelReferences>();
            RunEndPanelReferences end=root.GetComponent<RunEndPanelReferences>();
            Text heading=tutorial!=null?tutorial.TitleText:day!=null?day.TitleText:night!=null?night.TitleText:end!=null?end.TitleText:null;
            RectTransform artRoot=heading!=null?heading.transform.parent as RectTransform:root;
            Image card=artRoot!=null?artRoot.GetComponent<Image>():null;
            if(card!=null&&artRoot!=root)card.color=new Color(.065f,.115f,.12f,1f);
            Color cream=new Color(1f,.97f,.86f,1f);
            if(heading!=null)heading.color=cream;
            if(tutorial!=null&&tutorial.BodyText!=null)tutorial.BodyText.color=cream;
            if(day!=null&&day.BodyText!=null)day.BodyText.color=cream;
            if(night!=null){if(night.SummaryText!=null)night.SummaryText.color=cream;if(night.HintText!=null)night.HintText.color=cream;}
            if(end!=null){if(end.SummaryText!=null)end.SummaryText.color=cream;if(end.HintText!=null)end.HintText.color=cream;}
            if(heading!=null)Fill(heading.rectTransform,new Vector2(.30f,.80f),new Vector2(.94f,.93f));
            if(tutorial!=null)
            {
                ScrollRect scroll=tutorial.BodyText.GetComponentInParent<ScrollRect>();
                Fill(scroll!=null?(RectTransform)scroll.transform:tutorial.BodyText.rectTransform,new Vector2(.30f,.25f),new Vector2(.94f,.77f));
                if(scroll!=null)ResponsiveScroll(scroll);
                Fill((RectTransform)tutorial.ConfirmButton.transform,new Vector2(.48f,.09f),new Vector2(.80f,.18f));
            }
            if(day!=null)
            {
                Fill((RectTransform)day.BodyScroll.transform,new Vector2(.30f,.43f),new Vector2(.94f,.77f));ResponsiveScroll(day.BodyScroll);
                Fill(day.OptionsRoot,new Vector2(.30f,.08f),new Vector2(.94f,.40f));
            }
            if(night!=null)
            {
                Fill(night.SummaryText.rectTransform,new Vector2(.30f,.28f),new Vector2(.94f,.77f));
                Fill(night.HintText.rectTransform,new Vector2(.30f,.16f),new Vector2(.94f,.26f));
                Fill((RectTransform)night.NextDayButton.transform,new Vector2(.30f,.04f),new Vector2(.61f,.14f));
                Fill((RectTransform)night.ReturnMenuButton.transform,new Vector2(.64f,.04f),new Vector2(.95f,.14f));
            }
            if(end!=null)
            {
                Fill(end.SummaryText.rectTransform,new Vector2(.30f,.28f),new Vector2(.94f,.77f));
                Fill(end.HintText.rectTransform,new Vector2(.30f,.16f),new Vector2(.94f,.26f));
                Fill((RectTransform)end.RetryButton.transform,new Vector2(.30f,.04f),new Vector2(.61f,.14f));
                Fill((RectTransform)end.ContinueEndlessButton.transform,new Vector2(.30f,.04f),new Vector2(.61f,.14f));
                Fill((RectTransform)end.ReturnMenuButton.transform,new Vector2(.64f,.04f),new Vector2(.95f,.14f));
            }
            Art(artRoot,"ThirdPartyPortrait",portrait,new Vector2(.015f,.08f),new Vector2(.265f,.90f),true);
        }
        private static void ResponsiveScroll(ScrollRect scroll)
        {
            if(scroll.content==null)return;
            RectTransform content=scroll.content;
            content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;
            content.pivot=new Vector2(.5f,1);content.anchoredPosition=new Vector2(0,content.anchoredPosition.y);
            content.sizeDelta=new Vector2(0,content.sizeDelta.y);
            foreach(Text text in content.GetComponentsInChildren<Text>(true))
            {
                RectTransform rect=text.rectTransform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;
                rect.anchoredPosition=new Vector2(0,rect.anchoredPosition.y);rect.sizeDelta=new Vector2(0,rect.sizeDelta.y);
            }
        }
        private static Image Art(RectTransform root,string name,Sprite sprite,Vector2 min,Vector2 max,bool preserve)
        {
            Transform existing=root.Find(name);
            Image image=existing==null?new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image)).GetComponent<Image>():existing.GetComponent<Image>();
            image.transform.SetParent(root,false);Fill(image.rectTransform,min,max);image.sprite=sprite;image.preserveAspect=preserve;image.raycastTarget=false;return image;
        }
        private static void Fill(RectTransform rect,Vector2 min,Vector2 max)
        {rect.anchorMin=min;rect.anchorMax=max;rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;}
    }
}
