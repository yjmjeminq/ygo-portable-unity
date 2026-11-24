using System;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    public partial class DuelFieldUI
    {
        private GameObject _storyCrisisControls;
        private Button _storyCrisisButton;
        private Button _storyDrawButton;
        private Text _storyCrisisLabel;

        private void BuildStoryCrisisControls(Transform root)
        {
            var panel = DuelUiKit.CreateRect("StoryCrisis", root,
                new Vector2(_mobileLayout ? .734f : .811f, .231f), new Vector2(.985f, .373f),
                Vector2.zero, Vector2.zero);
            _storyCrisisControls = panel.gameObject;
            _storyCrisisButton = AddControl("Activate", "", .52f, 1f, ActivateStoryCrisis, gold: true);
            _storyCrisisLabel = _storyCrisisButton.GetComponentInChildren<Text>();
            _storyDrawButton = AddControl("DrawNormally", "일반 드로우", 0f, .43f, SkipStoryCrisis);
            _storyCrisisControls.SetActive(false);

            Button AddControl(string name, string label, float bottom, float top, Action action, bool gold = false)
            {
                var slot = DuelUiKit.CreateRect(name, panel, new Vector2(0f, bottom), new Vector2(1f, top),
                    Vector2.zero, Vector2.zero);
                return DuelUiKit.CreateButton(slot, "Button", label, action, 19, gold);
            }
        }

        private void RefreshStoryCrisisControls()
        {
            var skill = _session?.PendingStoryCrisis ?? StoryCrisisSkill.None;
            bool visible = CanAcceptPlayerAction && skill != StoryCrisisSkill.None;
            _storyCrisisControls.SetActive(visible);
            if (visible)
                _storyCrisisLabel.text = skill == StoryCrisisSkill.ShiningDraw ? "✧ 샤이닝 드로우" : "⚡ 스톰 액세스";
        }

        private void SkipStoryCrisis()
        {
            if (_session?.PendingStoryCrisis is StoryCrisisSkill.ShiningDraw or StoryCrisisSkill.StormAccess)
                ExecuteActionInt(0);
        }

        private void ActivateStoryCrisis()
        {
            if (!CanAcceptPlayerAction || IsBrowsingZone || IsReadingCardDetails) return;
            var session = _session;
            var prompt = session.ActivePrompt;
            if (session.PendingStoryCrisis == StoryCrisisSkill.StormAccess)
            {
                ExecuteActionInt(1);
                return;
            }
            var choices = session.ShiningDrawChoices();
            if (choices.Count == 0) return;
            bool CanChoose() => _session == session && ReferenceEquals(session.ActivePrompt, prompt) && CanAcceptPlayerAction;
            _modals.ShowZoneViewer("샤이닝 드로우 · 뽑을 카드 선택", choices, newestFirst: false,
                hasCardActions: _ => CanChoose(),
                onOpenCardActions: card => { if (CanChoose()) ExecuteActionInt((int)card.sequence + 1); },
                canUseCardActions: CanChoose, actionsOnly: true, actionLabel: "이 카드 드로우");
        }
    }
}
