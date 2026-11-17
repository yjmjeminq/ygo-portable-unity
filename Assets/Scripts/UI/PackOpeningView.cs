using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class PackOpeningView
    {
        private const float DealTime = .23f;
        private const float DealGap = .065f;
        private const float FlipTime = .32f;
        private const float FlipGap = .145f;

        private sealed class Pull
        {
            internal RectTransform Slot;
            internal CanvasGroup Group;
            internal RectTransform Face;
            internal CompositeCardView Card;
            internal CompositeCardView Reflection;
            internal Button Button;
            internal Text Name;
            internal Text RarityLabel;
            internal Image Shine;
            internal uint Code;
            internal CardRarity Rarity;
            internal float FlipAge;
            internal float Lift;
            internal bool Flipping;
            internal bool FaceUp;
            internal bool Highlighted;
        }

        private readonly GameObject _root;
        private readonly GameObject _opening;
        private readonly GameObject _results;
        private readonly PackOpeningMotion _motion;
        private readonly PackOpeningPlayback _playback;
        private readonly Text _openingTitle;
        private readonly Text _resultTitle;
        private readonly Text _openingPage;
        private readonly Text _resultPage;
        private readonly Button _skip;
        private readonly Button _skipBatch;
        private readonly Button _all;
        private readonly Button _exit;
        private readonly Button _skipResult;
        private readonly Button _previous;
        private readonly Text _allLabel;
        private readonly Text _exitLabel;
        private readonly Text _summary;
        private readonly Text _selected;
        private readonly Text _controls;
        private readonly Pull[] _pulls = new Pull[CollectionProgress.CardsPerPack];
        private readonly Action _close;
        private IReadOnlyList<IReadOnlyList<uint>> _packs;
        private bool[] _opened;
        private Sprite _cover;
        private int _packIndex;
        private int _count;
        private int _revealed;
        private int _inspected;
        private float _dealAge;
        private bool _allQueued;
        private bool _quickBatch;

        internal bool IsOpen => _root.activeSelf;
        internal GameObject ActiveRoot => _opening.activeSelf ? _opening : _results;

        internal PackOpeningView(Transform parent, Action close)
        {
            _close = close;
            var root = LotdUiKit.FillRect("Screen_PackOpening", parent);
            _root = root.gameObject;
            var opening = LotdUiKit.FillRect("Opening", root);
            _opening = opening.gameObject;
            PackOpeningStage.Backdrop(opening);
            _motion = LotdUiKit.FillRect("Motion", opening).gameObject.AddComponent<PackOpeningMotion>();
            _openingTitle = Label(opening, "Title", "", .18f, .88f, .82f, .965f, 31, PackShopStyle.TextPrimary);
            _openingPage = Label(opening, "Page", "", .02f, .89f, .16f, .967f, 28, PackShopStyle.TextPrimary);
            _skip = ButtonAt(opening, "Skip", "건너뛰기", .84f, .04f, .967f, .099f, Back);
            _skipBatch = ButtonAt(opening, "SkipBatch", "전체 빠른 공개", .655f, .04f, .826f, .099f, SkipBatch);

            var results = LotdUiKit.FillRect("Results", root);
            _results = results.gameObject;
            PackOpeningStage.Backdrop(results);
            _resultTitle = Label(results, "Title", "", .19f, .875f, .81f, .957f, 32, PackShopStyle.TextPrimary);
            _resultPage = Label(results, "Page", "", .02f, .89f, .16f, .967f, 28, PackShopStyle.TextPrimary);
            _previous = ButtonAt(results, "Previous", "이전 팩", .034f, .048f, .165f, .112f, () => ChangePage(-1));
            _exit = ButtonAt(results, "Exit", "", .405f, .086f, .595f, .152f, Back, light: true);
            _skipResult = ButtonAt(results, "Skip", "건너뛰기", .84f, .04f, .967f, .099f, Back);
            _exitLabel = _exit.GetComponentInChildren<Text>();

            var spread = Slot(results, "Cards", .048f, .37f, .952f, .686f);
            for (int i = 0; i < _pulls.Length; i++)
                _pulls[i] = BuildPull(spread, i);
            PackOpeningStage.FloorFade(results, .09f, .375f);
            _exit.transform.parent.SetAsLastSibling();
            _previous.transform.parent.SetAsLastSibling();
            _skipResult.transform.parent.SetAsLastSibling();
            _summary = Label(results, "Summary", "", .20f, .206f, .80f, .253f, 19, PackShopStyle.TextMuted);
            _selected = Label(results, "Selected", "", .16f, .149f, .84f, .204f, 22, PackShopStyle.TextPrimary);
            _all = ButtonAt(results, "RevealAll", "모두 공개", .405f, .086f, .595f, .152f, RevealAll, light: true);
            _allLabel = _all.GetComponentInChildren<Text>();
            _controls = Label(results, "Controls", "", .20f, .017f, .8f, .066f, 16, PackShopStyle.TextMuted);
            _playback = results.gameObject.AddComponent<PackOpeningPlayback>();
            _playback.Step = AdvanceResults;

            Hide();
        }

        internal void Show(Sprite cover, string name, IReadOnlyList<IReadOnlyList<uint>> packs)
        {
            Hide();
            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
            _openingTitle.text = name;
            _resultTitle.text = name;
            _packs = packs;
            _opened = new bool[packs.Count];
            _cover = cover;
            _quickBatch = false;
            StartPack(0);
        }

        private void StartPack(int index)
        {
            _motion.Stop();
            _playback.enabled = false;
            _packIndex = index;
            var cards = _packs[index];
            _count = cards.Count;
            _revealed = 0;
            _inspected = -1;
            _allQueued = false;
            _dealAge = 0f;

            var highest = CardRarity.Normal;
            for (int i = 0; i < _count; i++)
            {
                var pull = _pulls[i];
                pull.Code = cards[i];
                pull.Rarity = CardRarityCatalog.GetRarity(pull.Code);
                if (pull.Rarity > highest) highest = pull.Rarity;
            }
            string page = $"{_packIndex + 1} / {_packs.Count}";
            _openingPage.text = _resultPage.text = page;
            if (_quickBatch || _opened[_packIndex])
            {
                ShowResults();
                FinishResults();
                return;
            }

            _results.SetActive(false);
            _opening.SetActive(true);
            _skipBatch.gameObject.SetActive(_packs.Count > 1);
            _motion.Initialize(_cover, highest, ShowResults);
            Focus(_skip);
        }

        internal void Hide()
        {
            _motion.Stop();
            _playback.enabled = false;
            _root.SetActive(false);
            _opening.SetActive(false);
            _results.SetActive(false);
            foreach (var pull in _pulls) SetFace(pull, 0, true);
            _packs = null;
            _opened = null;
            _cover = null;
        }

        internal void Back()
        {
            if (!IsOpen) return;
            if (_opening.activeSelf) _motion.Skip();
            else if (_revealed < _count) FinishResults();
            else if (_packIndex + 1 < _packs.Count) ChangePage(1);
            else _close();
        }

        internal void ChangePage(int delta)
        {
            if (!IsOpen || _opening.activeSelf || _revealed < _count) return;
            int next = Mathf.Clamp(_packIndex + delta, 0, _packs.Count - 1);
            if (next != _packIndex) StartPack(next);
        }

        private void SkipBatch()
        {
            if (!_opening.activeInHierarchy || !_motion.IsPlaying) return;
            _quickBatch = true;
            _motion.Skip();
            FinishResults();
        }

        private Pull BuildPull(Transform parent, int index)
        {
            float stride = 1f / _pulls.Length;
            var slot = Slot(parent, $"Card_{index}", index * stride + .006f, 0f, (index + 1) * stride - .006f, 1f);
            var group = slot.gameObject.AddComponent<CanvasGroup>();
            var buttonBounds = Slot(slot, "Select", 0f, .015f, 1f, .94f);
            var clickArea = LotdUiKit.AddImage(buttonBounds.gameObject, LotdUiKit.WhiteSprite, Color.clear);
            var button = buttonBounds.gameObject.AddComponent<Button>();
            button.targetGraphic = clickArea;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SelectCard(index));

            var face = LotdUiKit.FillRect("Face", buttonBounds);
            var shineRect = LotdUiKit.FillRect("Shine", face);
            shineRect.anchorMin = new Vector2(.03f, .018f);
            shineRect.anchorMax = new Vector2(.97f, .982f);
            var shine = LotdUiKit.AddImage(shineRect.gameObject, LotdUiKit.WhiteSprite, Color.clear, false, false);
            var card = CompositeCardView.Create(face, "View", detailedOverlay: true);
            var mirror = Slot(card.transform.Find("AspectBox"), "Reflection", 0f, -1f, 1f, 0f);
            mirror.localScale = new Vector3(1f, -1f, 1f);
            mirror.gameObject.AddComponent<CanvasGroup>().alpha = .35f;
            var reflection = CompositeCardView.Create(mirror, "View");
            // The tint overlays the card front briefly at the midpoint of the flip.
            shineRect.SetAsLastSibling();

            var pull = new Pull
            {
                Slot = slot,
                Group = group,
                Face = face,
                Card = card,
                Reflection = reflection,
                Button = button,
                Name = Label(slot, "Name", "", 0f, -.23f, 1f, -.105f, 15, PackShopStyle.TextPrimary),
                RarityLabel = ShopRarityBadge.Create(slot, new Vector2(.74f, .927f), new Vector2(.987f, 1.013f), 15),
                Shine = shine,
            };
            var focus = button.gameObject.AddComponent<StarterRowFocus>();
            focus.PreviewOnHover = true;
            focus.ShowPreview = () =>
            {
                if (pull.Button.IsInteractable()) Inspect(index);
            };
            focus.ShowHighlight = highlighted =>
            {
                if (highlighted == pull.Highlighted) return;
                pull.Highlighted = highlighted;
                if (_results.activeInHierarchy) _playback.enabled = true;
            };
            return pull;
        }

        private void ShowResults()
        {
            _opening.SetActive(false);
            _results.SetActive(true);
            for (int i = 0; i < _pulls.Length; i++)
            {
                var pull = _pulls[i];
                pull.Slot.gameObject.SetActive(i < _count);
                if (i >= _count) continue;
                pull.FaceUp = pull.Flipping = false;
                pull.FlipAge = 0f;
                pull.Lift = 0f;
                pull.Face.localScale = Vector3.one;
                pull.Shine.color = Color.clear;
                SetFace(pull, pull.Code, true);
                pull.RarityLabel.transform.parent.gameObject.SetActive(false);
                pull.Name.text = "";
            }
            if (Application.isPlaying) LotdAudio.PlayDraw();
            RefreshStatus();
            AdvanceResults(0f);
            Focus(_all);
        }

        private void SelectCard(int index)
        {
            var pull = _pulls[index];
            if (!_results.activeInHierarchy || !pull.Button.IsInteractable()) return;
            Inspect(index);
            if (pull.FaceUp || (pull.Flipping && pull.FlipAge >= 0f)) return;
            pull.Flipping = true;
            pull.FlipAge = 0f;
            _playback.enabled = true;
        }

        private void RevealAll()
        {
            if (!_results.activeInHierarchy || _revealed >= _count) return;
            if (_allQueued)
            {
                FinishResults();
                return;
            }
            _allQueued = true;
            float delay = 0f;
            for (int i = 0; i < _count; i++)
            {
                var pull = _pulls[i];
                if (pull.FaceUp || pull.Flipping) continue;
                delay = Mathf.Max(delay, DealTime + i * DealGap - _dealAge);
                pull.Flipping = true;
                pull.FlipAge = -delay;
                delay += FlipGap;
            }
            _allLabel.text = "즉시 공개";
            _playback.enabled = true;
        }

        private void FinishResults()
        {
            for (int i = 0; i < _count; i++)
            {
                var pull = _pulls[i];
                RevealCard(pull, playSound: false);
                pull.Flipping = false;
                pull.Face.localScale = Vector3.one;
                pull.Shine.color = Color.clear;
            }
            _dealAge = DealTime + DealGap * _count;
            AdvanceResults(0f);
            RefreshStatus();
            Focus(_exit);
        }

        internal void AdvanceResults(float delta)
        {
            _dealAge += delta;
            bool motionActive = _dealAge < DealTime + DealGap * (_count - 1);
            for (int i = 0; i < _count; i++)
            {
                var pull = _pulls[i];
                float t = Mathf.Clamp01((_dealAge - i * DealGap) / DealTime);
                float ease = 1f - (1f - t) * (1f - t);
                pull.Group.alpha = ease;
                bool arrived = t >= 1f;
                if (arrived && !pull.Button.interactable && pull.Highlighted) Inspect(i);
                pull.Button.interactable = arrived;
                float lift = pull.Highlighted && pull.Button.interactable ? 1f : 0f;
                pull.Lift = Mathf.MoveTowards(pull.Lift, lift, delta / .13f);
                pull.Slot.anchoredPosition = Vector2.up *
                    (6f * Mathf.SmoothStep(0f, 1f, pull.Lift) - 36f * (1f - ease));
                motionActive |= pull.Lift != lift;
                if (pull.Flipping)
                {
                    UpdateFlip(pull, delta);
                    motionActive |= pull.Flipping;
                }
            }
            _playback.enabled = motionActive;
        }

        private void UpdateFlip(Pull pull, float delta)
        {
            pull.FlipAge += delta;
            if (pull.FlipAge < 0f) return;
            float t = Mathf.Clamp01(pull.FlipAge / FlipTime);
            float width = Mathf.Max(.025f, Mathf.Abs(Mathf.Cos(t * Mathf.PI)));
            pull.Face.localScale = new Vector3(width, 1f + .055f * Mathf.Sin(t * Mathf.PI), 1f);
            if (t >= .5f) RevealCard(pull, playSound: true);

            Color foil = pull.Rarity == CardRarity.UltraRare ? Color.HSVToRGB(t, .68f, 1f) : CardRarityStyle.GetColor(pull.Rarity);
            foil.a = Mathf.Sin(t * Mathf.PI) * (pull.Rarity >= CardRarity.SuperRare ? .68f : .25f);
            pull.Shine.color = foil;
            if (t >= 1f)
            {
                pull.Flipping = false;
                pull.Face.localScale = Vector3.one;
                pull.Shine.color = Color.clear;
            }
        }

        private void RevealCard(Pull pull, bool playSound)
        {
            if (pull.FaceUp) return;
            pull.FaceUp = true;
            _revealed++;
            SetFace(pull, pull.Code, false);
            ShopRarityBadge.Update(pull.RarityLabel, pull.Code);
            pull.Name.text = CardDatabase.GetName(pull.Code);
            if (playSound && Application.isPlaying)
            {
                if (pull.Rarity == CardRarity.UltraRare) LotdAudio.PlayWin();
                else if (pull.Rarity == CardRarity.SuperRare) LotdAudio.PlayChain();
                else LotdAudio.PlayTick();
            }
            RefreshStatus();
        }

        private static void SetFace(Pull pull, uint code, bool faceDown)
        {
            pull.Card.SetCard(code, faceDown);
            pull.Reflection.SetCard(code, faceDown);
        }

        private void RefreshStatus()
        {
            bool finished = _revealed == _count;
            if (finished) _opened[_packIndex] = true;
            _previous.gameObject.SetActive(_packIndex > 0);
            _previous.interactable = finished;
            _summary.text = finished ? $"카드 {_count}장 공개 완료" : $"공개 {_revealed} / {_count}";
            _exitLabel.text = _packIndex + 1 < _packs.Count ? "다음 팩" : "상점으로";
            _controls.text = finished
                ? _packs.Count > 1 ? "방향키 카드 선택   LB/RB 팩 이동" : "방향키 카드 선택   B 상점으로"
                : "방향키 카드 선택   A 공개   B 건너뛰기";
            _all.gameObject.SetActive(!finished);
            _exit.gameObject.SetActive(finished);
            _skipResult.gameObject.SetActive(!finished);
            _allLabel.text = _allQueued ? "즉시 공개" : "모두 공개";
            var focus = EventSystem.current?.currentSelectedGameObject;
            if (finished && (focus == _all.gameObject || focus == _skipResult.gameObject))
                Focus(_exit);
            Inspect(_inspected);
        }

        private void Inspect(int index)
        {
            _inspected = index;
            var pull = index >= 0 ? _pulls[index] : null;
            _selected.text = pull?.FaceUp == true
                ? $"{pull.Name.text} · {CardRarityCatalog.GetLabel(pull.Rarity)} · 보유 {CollectionProgress.GetOwnedCount(pull.Code)}장"
                : _revealed < _count ? "카드를 선택해 뒤집으세요" : "";
        }

        private static void Focus(Selectable control) =>
            EventSystem.current?.SetSelectedGameObject(control.gameObject);

        private static RectTransform Slot(Transform parent, string name, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);

        private static Text Label(Transform parent, string name, string text, float left, float bottom, float right, float top,
            int size, Color color) =>
            LotdUiKit.AddFittedText(Slot(parent, name, left, bottom, right, top), "Text", text, size,
                Mathf.Max(11, size - 8), TextAnchor.MiddleCenter, color, FontStyle.Bold);

        private static Button ButtonAt(Transform parent, string name, string text, float left, float bottom, float right, float top,
            Action click, bool light = false) =>
            PackShopStyle.Button(Slot(parent, name, left, bottom, right, top), "Button", text, click, 19,
                kind: light ? PackShopStyle.ButtonKind.OpeningAction : PackShopStyle.ButtonKind.OpeningSkip);
    }

    internal sealed class PackOpeningPlayback : MonoBehaviour
    {
        internal Action<float> Step { get; set; }

        private void Update() => Step?.Invoke(Time.unscaledDeltaTime);
    }
}
