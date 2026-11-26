using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private const string ResortOutfit = CollectionProgress.RpgResortSkin;
        private const string AutoOutfit = CollectionProgress.RpgAutoOutfit;
        private RectTransform _rpgSummerScene;
        private int _rpgWardrobeCharacter;
        private bool _rpgWardrobeFromShop;

        private void OpenRpgResortWardrobe(bool fromShop)
        {
            _rpgWardrobeFromShop = fromShop;
            _rpgWardrobeCharacter = CollectionProgress.RpgResortCharacters[0].Id;
            ShowStatus("");
            Show(Page.ResortWardrobe);
        }

        private void BuildRpgResortWardrobe()
        {
            BuildRpgMenuBackdrop();
            var character = CollectionProgress.RpgResortCharacters.First(entry => entry.Id == _rpgWardrobeCharacter);
            int id = character.Id;
            bool available = RpgWardrobe.HasResortAssets(id);
            bool unlocked = CollectionProgress.RpgResortUnlocked(id);
            bool equipped = CollectionProgress.RpgEquippedOutfit(id) == ResortOutfit && available;
            const float bottom = .17f;
            var list = Area(_body, "WardrobeCharacters", .017f, bottom, .257f, .949f);
            RpgUiStyle.Panel(list);
            HudLabel(Area(list, "Title", .065f, .84f, .94f, .98f),
                _rpgWardrobeFromShop ? $"의상 매점 · {CollectionProgress.Balance:N0} DP" : "의상 변경", 24, Color.white,
                align: TextAnchor.MiddleCenter, heading: true);
            Button focus = null;
            for (int i = 0; i < CollectionProgress.RpgResortCharacters.Count; i++)
            {
                var entry = CollectionProgress.RpgResortCharacters[i];
                float top = .805f - .151f * i;
                var button = RpgButton(Area(list, $"Character_{entry.Id}", .075f, top - .111f, .925f, top),
                    entry.Name, () =>
                    {
                        _rpgWardrobeCharacter = entry.Id;
                        ShowStatus("");
                        Show(Page.ResortWardrobe);
                    }, primary: entry.Id == id);
                if (entry.Id == id) focus = button;
            }

            string automatic = RpgWardrobe.AutomaticForField(id, _rpgLocation, RpgClears + 1);
            var normal = OutfitCard("Auto", "기본 의상 (자동)", automatic, !equipped, .278f, .622f);
            var resort = OutfitCard("Resort", "여름 리조트", ResortOutfit, equipped, .644f, .987f);
            if (!_rpgWardrobeFromShop)
                RpgButton(Area(normal, "Equip", .10f, .041f, .90f, .142f), equipped ? "자동으로 변경" : "사용 중",
                    () => Change(AutoOutfit)).interactable = equipped;

            string resortAction = !available ? "의상 데이터 없음" : _rpgWardrobeFromShop
                ? unlocked ? "해금 완료" : $"{CollectionProgress.RpgResortPrice:N0} DP로 구매"
                : equipped ? "사용 중" : unlocked ? "장착" : "매점 구매 또는 엔딩 후 해금";
            var action = RpgButton(Area(resort, "Action", .10f, .041f, .90f, .142f), resortAction,
                () => Change(ResortOutfit), primary: true);
            action.interactable = available && (_rpgWardrobeFromShop ? !unlocked : unlocked && !equipped);
            RpgButton(Area(_body, "WardrobeBack", .378f, .023f, .622f, .128f), "돌아가기", Back);
            DeckPadUi.LimitKeyboardNavigation(_body);
            EventSystem.current?.SetSelectedGameObject(focus?.gameObject);

            RectTransform OutfitCard(string key, string title, string outfit, bool selected, float left, float right)
            {
                var card = Area(_body, key, left, bottom, right, .949f);
                RpgUiStyle.Panel(card, accent: selected);
                HudLabel(Area(card, "Heading", .075f, .832f, .925f, .978f), title, 23,
                    selected ? RpgUiStyle.Gold : Color.white, align: TextAnchor.MiddleCenter, heading: true);
                var bust = outfit == null
                    ? _images.GetBust(RpgArt + RpgWardrobe.PortraitStem(id, null) + ".png")
                    : _images.Get(RpgArt + RpgWardrobe.Preview(id, outfit));
                var preview = Area(card, "Portrait", .078f, .17f, .786f, .83f);
                if (bust != null) DuelUiKit.AddImage(preview.gameObject, bust, Color.white, true, false);
                else HudLabel(preview, character.Name, 21, Color.white, align: TextAnchor.MiddleCenter);
                var atlas = outfit == null ? null : _images.GetSlices(RpgArt + RpgWardrobe.WalkAtlas(id, outfit), 3, 3, fixedCells: true);
                var sd = atlas?[3] ?? (outfit == null ? _images.Get(RpgArt + $"Characters_SD/sd_{id}.png") : null);
                if (sd != null)
                    DuelUiKit.AddImage(Area(card, "FieldPreview", .747f, .196f, .974f, .481f).gameObject, sd, Color.white, true, false);
                return card;
            }

            void Change(string outfit)
            {
                bool saved = _rpgWardrobeFromShop ? CollectionProgress.TryBuyRpgResort(id, out string error) :
                    CollectionProgress.TryEquipRpgOutfit(id, outfit, out error);
                if (!saved) { ShowStatus(error); return; }
                if (_rpgWardrobeFromShop) DuelAudio.PlayTagForceReward();
                Show(Page.ResortWardrobe);
                ShowStatus(_rpgWardrobeFromShop ? "의상을 해금했습니다. 정비소 작업대에서 장착할 수 있습니다." :
                    outfit == ResortOutfit ? "여름 리조트 의상을 장착했습니다." : "기본 의상으로 변경했습니다.");
            }
        }

        private void BuildRpgSummerEpilogue()
        {
            if (!CollectionProgress.RpgEndingCleared) { Show(Page.Talk); return; }
            _rpgSummerScene = DuelUiKit.FillRect("SummerEpilogue", _screen);
            _rpgIntroDialogue = new StoryModeUI.StoryDialogueView(_rpgSummerScene);
            var lines = new[]
            {
                new StoryDialogueLine { speaker = "내레이션", cutscene = "cg_summer_beach_aki",
                    text = "싸움이 끝나고 맞은 첫 여름. 해변에 파도 소리가 울렸다." },
                new StoryDialogueLine { speaker = "이자요이 아키", cutscene = "cg_summer_beach_aki",
                    text = "카이, 천천히 와. 이제 우릴 재촉할 사람은 없잖아." },
                new StoryDialogueLine { speaker = "셰리 르블랑", cutscene = "cg_summer_beach_resort_group",
                    text = "모처럼 다 함께 쉬는 날이네요. 먼저 한잔해요." },
                new StoryDialogueLine { speaker = "칼리 나기사", cutscene = "cg_summer_beach_resort_group",
                    text = "오늘은 마감도 잊을래요! 미스티 씨, 루카, 이쪽이에요." },
                new StoryDialogueLine { speaker = "루카", cutscene = "cg_summer_beach_group",
                    text = "카이! 여기서 우리 사진 한 장만 찍어 줘!" },
                new StoryDialogueLine { speaker = "내레이션", cutscene = "cg_summer_beach_group",
                    text = "셔터 소리와 함께 그날의 웃음이 사진에 담겼다." },
            };
            void ReturnToWorkbench() => Show(Page.Talk);
            _rpgIntroDialogue.Open("네오 도미노의 여름", "카이", DeckRepository.GetCharacter(1), lines,
                ReturnToWorkbench, rpg: true, onSkip: ReturnToWorkbench, rpgEpisode: 50,
                rpgBackdrop: "Maps/spot_poppo_time.png");
            _rpgIntroDialogue.SetBranch(lines.Length - 1, () => _rpgIntroDialogue.ShowChoices(
                () => "몇 해 뒤, 누구와 다시 바다를 찾을까?",
                new StoryModeUI.StoryChoice("이자요이 아키", () => OpenRpgSummerRomance(withAki: true)),
                new StoryModeUI.StoryChoice("셰리 르블랑", () => OpenRpgSummerRomance(withAki: false)),
                new StoryModeUI.StoryChoice("정비소로 돌아가기", ReturnToWorkbench)));
        }

        private void OpenRpgSummerRomance(bool withAki)
        {
            int id = withAki ? 1 : 32;
            string speaker = withAki ? "이자요이 아키" : "셰리 르블랑";
            string title = withAki ? "아키 · 파도가 잦아들 때" : "셰리 · 여름의 마지막 불빛";
            string nightScene = withAki ? "cg_summer_romance_aki" : "cg_summer_romance_sherry";
            var lines = new[]
            {
                new StoryDialogueLine { speaker = "내레이션", text = "몇 해 뒤. 성인이 된 두 사람은 다시 이 바다를 찾았다." },
                new StoryDialogueLine { speaker = speaker, emotion = "smile", text = withAki
                    ? "그날 우리 사진, 아직 정비소 책상 위에 있어."
                    : "그날 사진을 액자에 넣었어요. 볼 때마다 여기가 생각났죠." },
                new StoryDialogueLine { speaker = "카이", text = withAki
                    ? "아키, 오늘 밤도 네 곁에 있고 싶어. 그래도 될까?"
                    : "셰리, 오늘 밤은 좀 더 함께 있어도 될까요?" },
                new StoryDialogueLine { speaker = speaker, emotion = withAki ? "blush" : "smile", text = withAki
                    ? "응. 오늘은 나도 네가 곁에 있었으면 해."
                    : "좋아요. 저도 당신과 더 있고 싶었어요." },
                new StoryDialogueLine { speaker = "내레이션", cutscene = nightScene, text = withAki
                    ? "아키가 카이의 손을 잡고 어깨에 살며시 기댔다."
                    : "셰리가 카이의 손을 잡았다. 눈이 마주치자 둘 다 웃었다." },
                new StoryDialogueLine { speaker = speaker, cutscene = nightScene, text = withAki
                    ? "파도 소리가 좋다. 이 밤은 오래 기억할 것 같아."
                    : "내일도 같이 바다를 볼까요? 이번엔 아침 일찍요." },
                new StoryDialogueLine { speaker = speaker, cutscene = "cg_summer_sea_morning", text = withAki
                    ? "다음엔 사진보다 먼저 우리 둘이 갈 곳부터 정하자."
                    : "다음 여름 계획은 같이 세워요. 이번엔 당신이 먼저 골라요." },
            };
            void ReturnToWorkbench() => Show(Page.Talk);
            _rpgIntroDialogue.Open(title, "카이", DeckRepository.GetCharacter(id), lines, ReturnToWorkbench,
                rpg: true, onSkip: ReturnToWorkbench, rpgEpisode: 50, compactRpgDialogue: true,
                rpgBackdrop: "Cutscenes/cg_summer_guest_room_night.jpg");
        }
    }
}
