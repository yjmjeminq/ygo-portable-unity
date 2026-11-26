using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class StoryModeUI
    {
        private RectTransform _rpgFaceoff;
        private CanvasGroup _rpgFaceoffGroup;
        private Action _rpgFaceoffFinish;
        private float _rpgFaceoffTime;

        internal void OpenFieldRecap(int index)
        {
            if (!FromRpgField || Script?.episodes == null || (uint)index >= Script.episodes.Length) return;
            var episode = Script.episodes[index];
            var lines = (episode.preDialogues ?? Array.Empty<StoryDialogueLine>())
                .Concat(episode.postWinDialogues ?? Array.Empty<StoryDialogueLine>())
                .Where(line => line != null).ToArray();
            if (lines.Length == 0)
                lines = new[] { new StoryDialogueLine { speaker = "내레이션", text = TitleOf(episode) } };
            _browser.interactable = _browser.blocksRaycasts = false;
            _browser.gameObject.SetActive(false);
            _app.RpgField.Root.SetActive(false);
            var region = TagForceWorldUI.RpgRegions[TagForceWorldUI.RegionForEpisode(index + 1)];
            _dialogue.Open("지난 이야기 · " + TitleOf(episode), Script.protagonist,
                DeckRepository.GetCharacter(episode.charId), lines, ReturnToEpisodes, rpg: true,
                rpgBackdrop: region.StoryBackdrop,
                preferredRpgBackdrop: TagForceWorldUI.LightingForEpisode(region, index + 1), rpgEpisode: index + 1);
        }

        private void PlayRpgFaceoff(StoryEpisodeData episode, Action finish)
        {
            CancelRpgFaceoff();
            _rpgFaceoffFinish = finish;
            _rpgFaceoffTime = 0f;
            _rpgFaceoff = DuelUiKit.FillRect("RpgBossFaceoff", Root.transform);
            _rpgFaceoffGroup = _rpgFaceoff.gameObject.AddComponent<CanvasGroup>();
            _rpgFaceoffGroup.alpha = 0f;
            DuelUiKit.AddImage(_rpgFaceoff.gameObject, DuelUiKit.WhiteSprite, new Color(.009f, .019f, .037f, .98f));
            var hero = DuelUiKit.CreateMenuPanel(_rpgFaceoff, "Kai", new Vector2(.12f, .28f), new Vector2(.40f, .76f));
            var boss = DuelUiKit.CreateMenuPanel(_rpgFaceoff, "Boss", new Vector2(.60f, .28f), new Vector2(.88f, .76f));
            RpgUiStyle.Panel(hero);
            RpgUiStyle.Panel(boss, accent: true);
            Portrait(hero, _app.RpgField?.RpgStandingPortrait(0) ?? DuelProfileArt.Player, "카이");
            Portrait(boss, _app.RpgField?.RpgStandingPortrait(episode.charId, RpgWardrobe.EpisodeNumber(episode)) ??
                DuelProfileArt.Opponent(DeckRepository.GetCharacter(episode.charId)), episode.opponentName);
            AddLabel(_rpgFaceoff, "Versus", .42f, .41f, .58f, .64f, 46, color: DuelUiKit.GoldAccent).text = "VS";
            AddLabel(_rpgFaceoff, "Match", .12f, .77f, .88f, .90f, 27, color: DuelUiKit.GoldAccent).text = TitleOf(episode);
            var start = AddButton(_rpgFaceoff, "Continue", "듀얼 시작", .39f, .13f, .61f, .24f, FinishRpgFaceoff, gold: true);
            RpgUiStyle.Control(start, primary: true);
            Focus(start);

            void Portrait(RectTransform card, Sprite art, string name)
            {
                if (art != null)
                {
                    var image = DuelUiKit.CreateRect("Portrait", card, new Vector2(.13f, .18f),
                        new Vector2(.87f, .91f), Vector2.zero, Vector2.zero);
                    DuelUiKit.AddImage(image.gameObject, art, Color.white, true, false);
                }
                var label = AddLabel(card, "Name", .04f, .028f, .96f, .18f, 25);
                RpgUiStyle.Type(label, strong: true);
                label.text = name;
            }
        }

        private void UpdateRpgFaceoff(float delta)
        {
            if (_rpgFaceoffFinish == null) return;
            _rpgFaceoffTime += delta;
            _rpgFaceoffGroup.alpha = Mathf.Clamp01(_rpgFaceoffTime / .23f);
            if (_rpgFaceoffTime >= 1.35f) FinishRpgFaceoff();
        }

        private void FinishRpgFaceoff()
        {
            var finish = _rpgFaceoffFinish;
            CancelRpgFaceoff();
            finish?.Invoke();
        }

        private void CancelRpgFaceoff()
        {
            _rpgFaceoffFinish = null;
            if (_rpgFaceoff == null) return;
            _rpgFaceoff.gameObject.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_rpgFaceoff.gameObject);
            else UnityEngine.Object.DestroyImmediate(_rpgFaceoff.gameObject);
            _rpgFaceoff = null;
            _rpgFaceoffGroup = null;
        }
    }
}
