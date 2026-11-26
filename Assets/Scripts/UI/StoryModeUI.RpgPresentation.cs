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
        private RectTransform _rpgChallengerBanner, _rpgBossBanner;
        private Image _rpgFaceoffFlash;
        private Action _rpgFaceoffFinish;
        private float _rpgFaceoffTime;
        internal const float RpgFaceoffDuration = 1.12f;

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

        private void PlayRpgFaceoff(StoryEpisodeData episode, string deckTitle, Action finish)
        {
            CancelRpgFaceoff();
            _rpgFaceoffFinish = finish;
            _rpgFaceoffTime = 0f;
            _rpgFaceoff = DuelUiKit.FillRect("RpgBossFaceoff", Root.transform);
            _rpgFaceoffGroup = _rpgFaceoff.gameObject.AddComponent<CanvasGroup>();
            DuelUiKit.AddImage(_rpgFaceoff.gameObject, DuelUiKit.WhiteSprite, new Color(.009f, .019f, .037f, .98f));
            var backdrop = DuelUiKit.FillRect("Split", _rpgFaceoff).gameObject.AddComponent<RpgFaceoffSplit>();
            backdrop.raycastTarget = false;
            var content = Application.isMobilePlatform ? MobileSafeArea.Create(_rpgFaceoff) : _rpgFaceoff;
            _rpgChallengerBanner = Region(content, "Kai", .042f, .21f, .494f, .92f);
            _rpgBossBanner = Region(content, "Boss", .506f, .21f, .958f, .92f);
            Portrait(_rpgChallengerBanner, _app.RpgField?.RpgStandingPortrait(0) ?? DuelProfileArt.Player, "카이");
            Portrait(_rpgBossBanner, _app.RpgField?.RpgStandingPortrait(episode.charId, RpgWardrobe.EpisodeNumber(episode)) ??
                DuelProfileArt.Opponent(DeckRepository.GetCharacter(episode.charId)), episode.opponentName);
            var versus = AddLabel(content, "Versus", .416f, .39f, .586f, .66f, 96, color: RpgUiStyle.Gold);
            RpgUiStyle.Display(versus);
            versus.text = "VS";
            var panel = Region(content, "MatchTitle", .15f, .065f, .85f, .18f);
            RpgUiStyle.Dialogue(panel);
            var match = AddLabel(panel, "DeckTitle", .039f, .10f, .961f, .90f, 32, color: Color.white);
            RpgUiStyle.Display(match);
            match.text = string.IsNullOrWhiteSpace(deckTitle) ? TitleOf(episode) : deckTitle;
            _rpgFaceoffFlash = DuelUiKit.AddImage(DuelUiKit.FillRect("Flash", _rpgFaceoff).gameObject,
                DuelUiKit.WhiteSprite, Color.clear, false, false);
            DuelAudio.PlayTagForceSfx(TagForceSfx.DuelEncounter);
            PaintRpgFaceoff();

            void Portrait(RectTransform card, Sprite art, string name)
            {
                if (art != null)
                {
                    var image = DuelUiKit.CreateRect("Portrait", card, new Vector2(.04f, .145f),
                        new Vector2(.96f, .99f), Vector2.zero, Vector2.zero);
                    DuelUiKit.AddImage(image.gameObject, art, Color.white, true, false);
                }
                var plate = Region(card, "NamePlate", .12f, .016f, .88f, .155f);
                RpgUiStyle.Speaker(plate);
                var label = AddLabel(plate, "Name", .055f, .07f, .945f, .93f, 34);
                RpgUiStyle.Display(label);
                label.text = name;
            }
        }

        private void UpdateRpgFaceoff(float delta)
        {
            if (_rpgFaceoffFinish == null) return;
            _rpgFaceoffTime += delta;
            if (_rpgFaceoffTime >= RpgFaceoffDuration) FinishRpgFaceoff();
            else PaintRpgFaceoff();
        }

        private void PaintRpgFaceoff()
        {
            float settle = 1f - Mathf.SmoothStep(0f, 1f, _rpgFaceoffTime / .19f);
            _rpgChallengerBanner.anchoredPosition = Vector2.left * (148f * settle);
            _rpgBossBanner.anchoredPosition = Vector2.right * (148f * settle);
            _rpgFaceoffGroup.alpha = Mathf.Lerp(1f, .86f, Mathf.InverseLerp(.99f, RpgFaceoffDuration, _rpgFaceoffTime));
            _rpgFaceoffFlash.color = new Color(.92f, .97f, 1f, .82f * Mathf.Clamp01(1f - _rpgFaceoffTime / .19f));
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
            _rpgChallengerBanner = _rpgBossBanner = null;
            _rpgFaceoffFlash = null;
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class RpgFaceoffSplit : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            var left = new Color32(20, 46, 72, 255);
            var right = new Color32(65, 24, 46, 255);
            var cool = new Color32(26, 96, 121, 255);
            var warm = new Color32(136, 45, 61, 255);
            var slash = new Color32(225, 171, 100, 255);
            Quad(new(0f, 0f), new(.42f, 0f), new(.60f, 1f), new(0f, 1f), left, cool);
            Quad(new(.42f, 0f), new(1f, 0f), new(1f, 1f), new(.60f, 1f), right, warm);
            Quad(new(.415f, 0f), new(.424f, 0f), new(.604f, 1f), new(.595f, 1f), slash, slash);

            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 bottom, Color32 top)
            {
                int i = mesh.currentVertCount;
                mesh.AddVert(rect.min + Vector2.Scale(a, rect.size), bottom, Vector2.zero);
                mesh.AddVert(rect.min + Vector2.Scale(b, rect.size), bottom, Vector2.zero);
                mesh.AddVert(rect.min + Vector2.Scale(c, rect.size), top, Vector2.zero);
                mesh.AddVert(rect.min + Vector2.Scale(d, rect.size), top, Vector2.zero);
                mesh.AddTriangle(i, i + 1, i + 2);
                mesh.AddTriangle(i, i + 2, i + 3);
            }
        }
    }
}
