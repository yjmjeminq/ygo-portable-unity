using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private enum Page { SeriesSelect, Map, Field, Room, Mail, Ledger, Talk, Topics, DialogueLog, Shop, Gift, Duel, Bond, Prologue, Guide }

        private readonly GameAppController _app;
        private readonly TagForceWorldImages _images = new();
        private readonly RectTransform _screen, _body, _navigation;
        private readonly CanvasGroup _worldInput;
        private readonly Button _back;
        private readonly Text _header, _status;
        private const float WalkFrequency = 8f * 2f * Mathf.PI;
        private static readonly Vector2[] VisitorSlots =
        {
            new(.19f, .59f), new(.50f, .65f), new(.80f, .57f), new(.34f, .42f), new(.67f, .40f),
        };

        private readonly List<(RectTransform Actor, Action Action)> _interactions = new();
        private readonly List<(RectTransform Actor, RectTransform Art, Vector2 Center)> _visitors = new();
        private readonly List<string> _dialogueLog = new();
        private TagForceData _data;
        private TagForceManager _manager;
        private Page _page;
        private string _selectedId, _effect, _reply, _deckId;
        private TagForceBondEvent _bond;
        private bool _grandPrix;
        private int _deckLevel = 1;
        private Vector2 _position = new(.50f, .16f), _destination, _partnerPosition;
        private bool _moving, _manualWalk, _fieldMenu;
        private RectTransform _player, _partner, _playerSprite, _partnerSprite;
        private Text _fieldControls;
        private Action _onArrival;
        private Image _conversationShade;
        private RectTransform _leftStanding, _rightStanding;
        private CanvasGroup _dialoguePanel;
        private float _conversationProgress;
        private bool _closingConversation;
        private Text _dialogue, _heartGauge;
        private BreadSpin _breadSpin;

        private sealed class BreadSpin
        {
            internal TagForceItem Prize;
            internal uint Card;
            internal Text Caption;
            internal Image Icon;
            internal float Elapsed;
            internal int LastPreview = -1;
            internal bool Revealed;
        }
        private string _dialogueLine;
        private int[] _dialogueLetters;
        private float _dialogueProgress;
        private int _visibleDialogueLetters;

        private TagForceSave State => _manager.Progress;
        private TagForceSpot CurrentSpot => _manager.CurrentSpot;
        private TagForcePersona Selected => _data.Persona(_selectedId);
        internal GameObject Root => _screen.gameObject;
        internal bool CanWalk => _page == Page.Field && _player != null && _rpgDialogueCast == null && _rpgPendingRegion < 0;

        internal TagForceWorldUI(GameAppController app, Transform parent)
        {
            _app = app;
            _screen = DuelUiKit.FillRect("Screen_TagForceWorld", parent);
            _worldInput = _screen.gameObject.AddComponent<CanvasGroup>();
            Tint(_screen, new Color(.027f, .064f, .102f));
            var layout = Application.isMobilePlatform ? MobileSafeArea.Create(_screen) : _screen;
            _header = Label(Area(layout, "Header", .05f, .899f, .78f, .975f), "", 24, TextAnchor.MiddleLeft);
            _back = Btn(Area(layout, "Back", .82f, .908f, .95f, .967f), "뒤로", Back, sound: null);
            _body = Area(layout, "World", .05f, .174f, .95f, .895f);
            _status = Label(Area(layout, "Status", .05f, .112f, .95f, .168f), "", 17, TextAnchor.MiddleLeft);

            _navigation = Area(layout, "WorldNavigation", .05f, .027f, .95f, .101f);
            string[] labels = { "월드맵", "내 방", "PDA 메일", "인연 명부", "매점", "덱 편집", "시리즈 선택" };
            Action[] actions = { () => Show(Page.Map), () => Show(Page.Room), () => Show(Page.Mail),
                () => Show(Page.Ledger), () => Show(Page.Shop), _app.OpenTagForceDeckEditor, ShowSeriesSelect };
            for (int i = 0; i < labels.Length; i++)
            {
                float x = (float)i / labels.Length;
                Btn(Area(_navigation, labels[i], x, 0, (float)(i + 1) / labels.Length - .005f, 1), labels[i], actions[i]);
            }
            _rpgNavigation = BuildRpgNavigation(layout);
            Root.SetActive(false);
        }

        internal void Open(bool resume = false, bool restoreRpgPage = false)
        {
            Root.SetActive(true);
            _status.text = "";
            if (_rpgField) Show(restoreRpgPage ? _page : _rpgIntroOnOpen ? Page.Prologue : Page.Field);
            else if (resume && _data != null) Show(Page.Field);
            else ShowSeriesSelect();
        }

        internal void Close()
        {
            Root.SetActive(false);
            ClearBody();
            _images.Clear();
        }

        internal void Back()
        {
            if (_closingConversation || _rpgDialogueCast != null || _rpgPendingRegion >= 0) return;
            DuelAudio.PlayTagForceSfx(TagForceSfx.Cancel);
            if (_rpgField)
            {
                if (_page == Page.Field && _fieldMenu)
                {
                    ToggleFieldMenu(playSound: false);
                    return;
                }
                if (_page is Page.Prologue or Page.Guide)
                {
                    if (_rpgIntroDialogue?.ClosePopup() != true) FinishRpgIntro();
                }
                else if (_page == Page.Talk && _rpgChoosingCompanion)
                {
                    _rpgChoosingCompanion = false;
                    Show(Page.Talk);
                }
                else if (_page is Page.Map or Page.Talk or Page.Ledger) Show(Page.Field);
                else if (_page == Page.Duel) Show(Page.Talk);
                else Show(Page.Map);
                return;
            }
            if (_page == Page.SeriesSelect)
            {
                _app.ShowScreen(AppScreen.MainMenu);
                return;
            }
            if (_page == Page.Map) { ShowSeriesSelect(); return; }
            if (_page == Page.Talk && _dialoguePanel != null)
            {
                _closingConversation = true;
                _dialoguePanel.interactable = false;
                return;
            }
            Show(_page is Page.Topics or Page.DialogueLog or Page.Gift or Page.Bond or Page.Duel ? Page.Talk : Page.Map);
        }

        internal void ShowStatus(string message) => _status.text = message;

        private void ShowSeriesSelect()
        {
            _data = null;
            _manager = null;
            ShowStatus("");
            Show(Page.SeriesSelect);
        }

        private void OpenSeries(TagForceSeries series)
        {
            _data = TagForceData.Load(Application.streamingAssetsPath, series);
            if (_data == null)
            {
                ShowStatus("태그 포스 월드 데이터를 읽을 수 없습니다.");
                return;
            }
            _manager = new TagForceManager(_data);
            _selectedId = _effect = _reply = null;
            _bond = null;
            _grandPrix = false;
            _position = new(.50f, .16f);
            Show(Page.Field);
            ShowStatus("");
        }

        internal void ShowResult(TagForceDuel duel, bool won, string result, bool rewarded)
        {
            if (_rpgField)
            {
                ShowStatus(result);
                if (rewarded) DuelAudio.PlayTagForceReward();
                return;
            }
            _selectedId = duel.Persona.personaId;
            _status.text = result;
            _reply = won ? duel.Form.loseLine : duel.Form.winLine;
            var ending = rewarded && duel.GrandPrix && won && State.grandPrixRound == 0 ? duel.Partner?.grandPrixEnding : null;
            if (ending != null) _reply = PrintDialogue(ending);
            Show(Page.Field);
            if (!string.IsNullOrWhiteSpace(_reply))
                Label(Area(_body, "ResultDialogue", .13f, .77f, .88f, .96f), _reply, 19);
            if (rewarded) DuelAudio.PlayTagForceReward();
        }

        private void Show(Page page)
        {
            bool openConversation = page == Page.Talk && _page == Page.Field;
            _page = page;
            UpdateRpgWorldChrome();
            ClearBody();
            LayoutRpgField(_rpgField && page == Page.Field);
            _images.BeginPage();
            if (_rpgField)
            {
                ShowRpgPage(page);
                _images.Trim();
                return;
            }
            _rpgNavigation.gameObject.SetActive(false);
            _navigation.gameObject.SetActive(page is not (Page.SeriesSelect or Page.Talk));
            if (page == Page.SeriesSelect)
            {
                _header.text = "태그 포스 월드";
                DuelAudio.PlayTagForceBgm(false);
                BuildSeriesSelect();
                _images.Trim();
                return;
            }
            var state = State;
            DuelAudio.PlayTagForceBgm(state.time == TagForceData.TimeKeys.Length - 1);
            string partner = _data.Persona(State.partnerId)?.displayName ?? "없음";
            string[] weekdays = { "월", "화", "수", "목", "금", "토", "일" };
            var series = TagForceData.Series.First(entry => entry.Id == _data.seriesId);
            _header.text = $"{series.Title}  ·  {state.day}일차 ({weekdays[(state.day - 1) % 7]})  ·  {TagForceData.TimeNames[state.time]}  ·  " +
                $"{CollectionProgress.Balance:N0} DP  ·  파트너 {partner}";
            switch (page)
            {
                case Page.Map: BuildMap(); break;
                case Page.Field: BuildField(); break;
                case Page.Room: BuildRoom(); break;
                case Page.Mail: BuildMail(); break;
                case Page.Ledger: BuildLedger(); break;
                case Page.Talk: BuildTalk(openConversation); break;
                case Page.Topics: BuildTopics(); break;
                case Page.DialogueLog: BuildDialogueLog(); break;
                case Page.Gift: BuildGifts(); break;
                case Page.Duel: BuildDuelChoice(); break;
                case Page.Bond: BuildBond(); break;
                case Page.Shop: BuildShop(); break;
            }
            _images.Trim();
        }

        private void ClearBody()
        {
            ClearRpgDialogue();
            ClearRpgAtmosphere();
            ClearRpgExploration();
            ClearRpgResidentMotion();
            ClearRpgFieldHud();
            ClearRpgPresentation();
            _rpgResidents.Clear();
            _rpgNearbyScene = null;
            _rpgSceneSpace = _rpgViewport = null;
            _interactions.Clear();
            _visitors.Clear();
            _questMarkers.Clear();
            _rpgToast = null;
            _player = _partner = _playerSprite = _partnerSprite = null;
            _fieldControls = null;
            _conversationShade = null;
            _leftStanding = _rightStanding = null;
            _dialoguePanel = null;
            _closingConversation = false;
            _dialogue = _heartGauge = null;
            FinishBread();
            _dialogueLine = null;
            _dialogueLetters = null;
            _moving = _manualWalk = _fieldMenu = false;
            _onArrival = null;
            for (int i = _body.childCount - 1; i >= 0; i--)
            {
                GameObject child = _body.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        private bool Commit(TagForceSave next, int dp = 0, uint reward = 0, bool foil = false, uint gift = 0,
            bool playReward = true)
        {
            if (_manager.TrySave(next, dp, out string error, reward, foil, gift))
            {
                if (playReward && (dp > 0 || reward != 0)) DuelAudio.PlayTagForceReward();
                return true;
            }
            _status.text = error;
            return false;
        }

        private void GoTo(string spot, string invite = null, string mailId = null)
        {
            if (_data.Hub(spot) == null) return;
            var next = State.Copy();
            next.spotId = spot;
            if (invite != null)
            {
                next.inviteId = invite;
                next.inviteSpot = spot;
                if (!next.seenMail.Contains(mailId)) next.seenMail.Add(mailId);
            }
            if (!Commit(next)) return;
            _position = new(.50f, .16f);
            _reply = "";
            Show(Page.Field);
        }

        private void BuildSeriesSelect()
        {
            Tint(DuelUiKit.FillRect("SeriesBackground", _body), new Color(.06f, .11f, .16f));
            var series = TagForceData.Series;
            for (int i = 0; i < series.Length; i++)
            {
                var entry = series[i];
                float x = .038f + i % 3 * .317f;
                float y = i < 3 ? .535f : .09f;
                var tile = Area(_body, entry.AssetFolder, x, y, x + .29f, y + .365f);
                Btn(tile, $"{entry.Title}\n{entry.Region}", () => OpenSeries(entry), 26);
            }
        }

        private void BuildMap()
        {
            Backdrop(_data.assetConfig.worldMapPath, new Color(.14f, .31f, .37f));
            var spots = _data.Hubs;
            for (int i = 0; i < spots.Length; i++)
            {
                var spot = spots[i];
                float x = .035f + i % 3 * .319f;
                float y = spots.Length <= 3 ? .26f : i < 3 ? .535f : .065f;
                var tile = Area(_body, spot.spotId, x, y, x + .294f, y + .405f);
                Tint(tile, new Color(.031f, .124f, .169f, .97f));
                Label(Area(tile, "Spot", .04f, .78f, .96f, .97f), spot.name, 18, TextAnchor.MiddleLeft);
                var state = State;
                var present = _data.Visitors(state, spot.spotId);
                float col = .035f;
                foreach (var (persona, form) in present)
                {
                    var chip = Area(tile, persona.personaId, col, .29f, col + .176f, .76f);
                    Portrait(chip, form.sdSpritePath, form.formName);
                    string notices = persona.NextBond(state.Relationship(persona.personaId)) != null ? " !" : "";
                    if (_data.Mail(state).Any(entry => entry.Persona == persona &&
                        _data.MeetingSpot(state, persona, entry.Mail) == spot.spotId))
                        notices += " 메일";
                    Label(Area(tile, "Hearts", col, .215f, col + .176f, .34f),
                        $"{Hearts(persona)}{notices}", 12);
                    col += .188f;
                }
                Btn(Area(tile, "Visit", .04f, .039f, .96f, .205f), "이동", () => GoTo(spot.spotId));
            }
        }

        private void BuildField()
        {
            var spot = CurrentSpot;
            var floor = Backdrop(spot.mapPath, new Color(.14f, .24f, .25f));
            floor.raycastTarget = true;
            var tap = floor.gameObject.AddComponent<TagForceFloorTap>();
            tap.Click = point => MoveTo(point);
            Label(Area(_body, "SpotName", .02f, .88f, .60f, .99f), spot.name, 21, TextAnchor.MiddleLeft);
            Label(Area(_body, "SpotSubtitle", .02f, .83f, .68f, .885f), spot.subtitle, 15, TextAnchor.MiddleLeft);
            _fieldControls = Label(Area(_body, "FieldControls", .02f, .012f, .98f, .082f),
                "방향키 · 스틱 이동   선택 조사/대화   위쪽 버튼 메뉴", 15, TextAnchor.MiddleRight);
            if (_data.seriesId == TagForceData.GxId && spot.spotId == "spot_main_academy")
                Btn(Area(_body, "GrandPrix", .76f, .87f, .98f, .97f), "GX 그랑프리", BeginGrandPrix);

            int visitorIndex = 0;
            var state = State;
            foreach (var (persona, form) in _data.Visitors(state, spot.spotId))
            {
                var slot = VisitorSlots[visitorIndex++];
                var figure = Anchor(_body, persona.personaId, slot, new Vector2(123, 116));
                var face = Area(figure, "Face", .05f, .22f, .95f, 1f);
                bool hasArt = Portrait(face, form.sdSpritePath, form.formName);
                void OpenTalk() => MoveTo(figure.anchorMin, () => Talk(persona));
                BindPortrait(face, OpenTalk);
                _visitors.Add((figure, hasArt ? face : null, slot));
                Btn(Area(figure, "Talk", 0, 0, 1, .25f), form.formName, OpenTalk, 15);
                _interactions.Add((figure, () => Talk(persona)));
            }
            for (int i = 0; i < (spot.defaultPickups?.Length ?? 0); i++)
            {
                int index = i;
                string key = spot.spotId + ":" + i;
                if (State.pickups.Contains(key)) continue;
                var position = new Vector2(.24f + i * .255f, .245f + (i % 2) * .09f);
                var button = Anchor(_body, "Pickup" + i, position, new Vector2(82, 30));
                Btn(button, "조사", () => MoveTo(position, () => Pickup(spot, index)));
                _interactions.Add((button, () => Pickup(spot, index)));
            }

            var partner = _data.Persona(state.partnerId);
            if (partner != null)
            {
                var form = partner.DisplayForm(state);
                _partnerPosition = _position - new Vector2(.06f, 0);
                _partner = Anchor(_body, "Partner", _partnerPosition, new Vector2(72, 94));
                _partnerSprite = WalkerSprite(_partner, form.sdSpritePath, form.formName);
                if (_partnerSprite != null) BindPortrait(_partnerSprite, () => Talk(partner));
                Btn(Area(_body, "PartnerTalk", .02f, .085f, .24f, .145f), "파트너와 대화", () => Talk(partner), 15);
            }
            _player = Anchor(_body, "Player", _position, new Vector2(83, 106));
            _playerSprite = WalkerSprite(_player, TagForceData.PlayerSd, TagForceData.ProtagonistName);
        }

        private static void BindPortrait(RectTransform rect, Action action)
        {
            var target = rect.GetComponent<Image>();
            target.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                DuelAudio.PlayTagForceSfx(TagForceSfx.Confirm);
                action();
            });
            rect.gameObject.AddComponent<TagForceCursorSound>();
        }

        private RectTransform WalkerSprite(RectTransform actor, string path, string name, string outfitAtlas = null)
        {
            var visual = DuelUiKit.FillRect("Sprite", actor);
            if (_rpgField) return BuildRpgActorArt(visual, path, name, outfitAtlas);
            return Portrait(visual, path, name) ? visual : null;
        }

        private void Pickup(TagForceSpot spot, int index)
        {
            var next = State.Copy();
            string key = spot.spotId + ":" + index;
            if (next.pickups.Contains(key)) return;
            next.pickups.Add(key);
            string item = spot.defaultPickups[index];
            int dp = item switch { "dp_small" => 150, "dp_medium" => 350, "dp_large" => 600, _ => 0 };
            uint card = index == 2 && State.day % 3 == 0 ? CollectionProgress.TagForceBonusCard() : 0;
            if (dp == 0 && card == 0) next.ChangeItem(item, 1);
            if (Commit(next, dp, card, playReward: card == 0))
            {
                if (card != 0) DuelAudio.PlayTagForceSfx(TagForceSfx.CardDraw);
                else if (dp == 0) DuelAudio.PlayTagForceReward();
                ShowStatus(card != 0 ? $"{CardDatabase.GetName(card)} 획득" :
                    dp > 0 ? $"+{dp} DP" : $"{_data.Item(item)?.name ?? "아이템"} 획득");
                Show(Page.Field);
            }
        }

        private void MoveTo(Vector2 point, Action arrive = null, RectTransform approachActor = null)
        {
            if (!CanWalk || (_rpgField && _fieldMenu)) return;
            _manualWalk = false;
            _destination = new Vector2(Mathf.Clamp(point.x, .06f, .94f), Mathf.Clamp(point.y, .095f, _rpgField ? .455f : .77f));
            _moving = true;
            _onArrival = arrive;
            _rpgApproachActor = approachActor;
        }

        internal void Update(float delta, Vector2 padDirection = default)
        {
            UpdateBreadSpin(delta);
            UpdateConversation(delta);
            bool traveling = _rpgPendingRegion >= 0;
            if (_rpgField) UpdateRpgField(delta);
            if (traveling || !CanWalk) return;
            if (_rpgField && Input.GetKeyDown(KeyCode.M))
            {
                Show(Page.Map);
                return;
            }
            if (_rpgField && Input.GetKeyDown(KeyCode.Tab))
            {
                ToggleFieldMenu();
                return;
            }
            if (_rpgField && !_fieldMenu && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Z)))
            {
                ActivateRpgInteraction();
                return;
            }
            Vector2 movement = _fieldMenu ? Vector2.zero : padDirection;
            if (!_fieldMenu && movement == Vector2.zero)
            {
                movement = new Vector2(
                    (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) -
                        (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                    (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) -
                        (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
            }
            if (movement != Vector2.zero)
            {
                var direction = movement.normalized;
                if (_rpgField) direction.y *= _rpgBackdropAspect;
                MoveTo(_position + direction * .12f);
                _manualWalk = true;
            }
            else if (_manualWalk)
            {
                _manualWalk = _moving = false;
            }
            float time = Time.unscaledTime;
            AdvanceWalk(delta, time);
            if (CanWalk) AdvanceVisitors(time);
            if (_rpgField) RefreshRpgFieldPrompt(delta);
        }

        private void AdvanceWalk(float delta, float time)
        {
            Vector2 previous = _position;
            bool arrived = false;
            if (_moving)
            {
                _position = _rpgField ? AdvanceRpgWalk(delta) : Vector2.MoveTowards(_position, _destination, delta * .40f);
                arrived = _position == _destination;
                _moving = !arrived;
            }
            float phase = time * WalkFrequency;
            if (_rpgField)
            {
                PoseRpgWalker(_player, _playerSprite, _position, _position - previous, _moving);
                TrackRpgCamera(delta, horizontalTravel: _position.x - previous.x);
                FollowRpgPlayer(delta);
                UpdateRpgResidentMotion(delta);
                OrderRpgFieldActors();
                CheckRpgSceneEncounter(previous);
                if (!CanWalk || CheckRpgExit(previous)) return;
            }
            else PoseWalker(_player, _playerSprite, _position, _position - previous, _moving, phase);
            if (_partner != null)
            {
                Vector2 before = _partnerPosition;
                Vector2 follow = _position - before;
                if (follow.magnitude > .064f)
                    _partnerPosition = Vector2.MoveTowards(before, _position - follow.normalized * .064f, delta * .46f);
                Vector2 step = _partnerPosition - before;
                bool walking = step.sqrMagnitude > 1e-8f &&
                    (_moving || (_position - _partnerPosition).sqrMagnitude > .065f * .065f);
                PoseWalker(_partner, _partnerSprite, _partnerPosition, step, walking, phase + .8f);
            }
            if (!arrived) return;
            var action = _onArrival;
            _onArrival = null;
            _rpgApproachActor = null;
            action?.Invoke();
        }

        private void AdvanceVisitors(float time)
        {
            float phase = time * WalkFrequency;
            for (int i = 0; i < _visitors.Count; i++)
            {
                var (actor, art, center) = _visitors[i];
                float pace = time * (1.25f + .09f * i) + i * 1.14f;
                float heading = Mathf.Cos(pace);
                actor.anchorMin = actor.anchorMax = center + new Vector2(.022f * Mathf.Sin(pace), 0f);
                actor.anchoredPosition = Vector2.up * (Mathf.Sin(phase + i) * 2.4f * Mathf.Abs(heading));
                FaceDirection(art, heading);
            }
        }

        private static void PoseWalker(RectTransform actor, RectTransform sprite, Vector2 position, Vector2 step,
            bool walking, float phase)
        {
            actor.anchorMin = actor.anchorMax = position;
            actor.anchoredPosition = Vector2.up * (walking ? Mathf.Sin(phase) * 6f : 0f);
            FaceDirection(sprite, step.x);
        }

        private static void FaceDirection(RectTransform sprite, float horizontal)
        {
            if (sprite != null && Mathf.Abs(horizontal) > 1e-5f)
                sprite.localScale = new Vector3(horizontal < 0f ? -1f : 1f, 1f, 1f);
        }

        internal bool HandleFieldInput(DeckPadFrame frame)
        {
            if (_rpgPendingRegion >= 0) return true;
            if (!CanWalk) return false;
            if (frame.Search)
            {
                ToggleFieldMenu();
                return true;
            }
            if (_fieldMenu) return false;
            if (_rpgField && frame.Alternate && (_rpgCompanion != null || _rpgResidents.ContainsKey(RpgPartnerId)))
            {
                OpenRpgService(RpgPartnerId);
                return true;
            }
            if (!frame.Submit) return true;
            if (_rpgField)
            {
                ActivateRpgInteraction();
                return true;
            }
            var closest = _interactions.OrderBy(entry => Vector2.SqrMagnitude(entry.Actor.anchorMin - _position)).FirstOrDefault();
            if (closest.Actor == null) return true;
            DuelAudio.PlayTagForceSfx(TagForceSfx.Confirm);
            MoveTo(closest.Actor.anchorMin, closest.Action);
            return true;
        }

        private void ToggleFieldMenu(bool playSound = true)
        {
            if (!CanWalk) return;
            _fieldMenu = !_fieldMenu;
            if (playSound) DuelAudio.PlayTagForceSfx(TagForceSfx.Cursor, .65f);
            _manualWalk = _moving = false;
            _onArrival = null;
            _rpgApproachActor = null;
            if (_rpgField)
            {
                RefreshRpgFieldPrompt();
                EventSystem.current?.SetSelectedGameObject(_fieldMenu ? _rpgNavigation.Find("Map").gameObject : null);
            }
            else _fieldControls.text = _fieldMenu ? "위쪽 버튼으로 보행 복귀" :
                "방향키 · 스틱 이동   선택 조사/대화   위쪽 버튼 메뉴";
        }

        private void BuildRoom()
        {
            Backdrop(_data.assetConfig.myRoomPath, new Color(.37f, .225f, .22f));
            Label(Area(_body, "Room", .07f, .67f, .94f, .91f), "내 기숙사 방", 38, TextAnchor.MiddleLeft);
            Btn(Area(_body, "Rest", .10f, .3f, .53f, .51f), "침대에서 휴식하기", () =>
            {
                var next = State.Copy();
                next.Advance();
                if (!Commit(next)) return;
                ShowStatus($"시간이 지났습니다. 도착한 메일 {_data.Mail(State).Count()}통");
                Show(Page.Room);
            });
            Btn(Area(_body, "CheckAssets", .61f, .30f, .94f, .51f), "추가 에셋 다시 읽기", () =>
            {
                ClearBody();
                _images.Clear();
                Show(Page.Room);
                ShowStatus("PNG를 다시 불러왔습니다.");
            });
        }

        private void BuildMail()
        {
            var list = List("Mail", .01f, .07f, .99f, .95f);
            var state = State;
            foreach (var (persona, mail) in _data.Mail(state))
            {
                string spot = _data.MeetingSpot(state, persona, mail);
                Row(list, $"{(state.seenMail.Contains(mail.mailId) ? "" : "새 메일 · ")}{mail.subject} · {_data.Hub(spot).name}\n{mail.body}",
                    () => GoTo(spot, persona.personaId, mail.mailId), 76);
            }
            if (list.childCount == 0) Row(list, "이 시간대에 도착한 메일이 없습니다.", null);
        }

        private void BuildLedger()
        {
            var list = List("Ledger", .01f, .07f, .99f, .95f);
            foreach (var persona in _data.personas)
            {
                string schedule = string.Join(" · ", persona.forms.Select(form => form.formName + " " +
                    string.Join("/", form.activeTimes.Select(time => TagForceData.TimeNames[Array.IndexOf(TagForceData.TimeKeys, time)])) +
                    " " + string.Join("/", form.spots.Select(id => _data.Spot(id)?.name))));
                Row(list, $"{persona.displayName}  {Hearts(persona)}\n{schedule}\n선호 {_data.conversationTopics.FirstOrDefault(t => t.topicId == persona.favoriteTopic)?.name} · " +
                    $"{_data.Item(persona.favoriteGiftId)?.name}",
                    () =>
                    {
                        var progress = State.Relationship(persona.personaId);
                        var events = persona.bondEvents.Where(bond => progress.clearedBonds.Contains(bond.heartLevel));
                        string memory = string.Join("\n", events.Select(PrintDialogue));
                        if (persona.grandPrixEnding != null && (progress.wonGrandPrix || progress.Hearts == TagForceRelation.HeartCount))
                            memory += "\n" + PrintDialogue(persona.grandPrixEnding);
                        ShowStatus(string.IsNullOrWhiteSpace(memory) ? "아직 회상할 인연 이벤트가 없습니다." : "");
                        if (string.IsNullOrWhiteSpace(memory)) return;
                        ClearBody();
                        Row(List("Memory", .05f, .06f, .95f, .97f), memory, null, 360);
                    }, 88);
            }
        }

        private void Talk(TagForcePersona persona)
        {
            _dialogueLog.Clear();
            _selectedId = persona.personaId;
            _grandPrix = false;
            _bond = null;
            _effect = null;
            _reply = "";
            Show(Page.Talk);
        }

        private void BuildTalk(bool animate)
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Field); return; }
            var state = State;
            var form = persona.DisplayForm(state);
            var relationship = state.Relationship(persona.personaId);

            BuildField();
            foreach (var button in _body.GetComponentsInChildren<Button>()) button.interactable = false;
            var overlay = DuelUiKit.FillRect("Conversation", _body);
            overlay.gameObject.AddComponent<RectMask2D>();
            _conversationShade = DuelUiKit.AddImage(overlay.gameObject, DuelUiKit.WhiteSprite, Color.black);
            _leftStanding = Area(overlay, "Protagonist", .035f, .195f, .47f, 1f);
            _rightStanding = Area(overlay, "Guest", .53f, .195f, .965f, 1f);
            Portrait(_leftStanding, TagForceData.PlayerStanding, TagForceData.ProtagonistName);
            Portrait(_rightStanding, form.standingSpritePath, form.formName);

            var panel = Area(overlay, "Dialogue", .018f, .012f, .982f, .405f);
            _dialoguePanel = panel.gameObject.AddComponent<CanvasGroup>();
            Tint(panel, new Color(.029f, .084f, .118f, .98f));
            Tint(Area(panel, "TopRule", 0f, .982f, 1f, 1f), new Color(.30f, .75f, .77f));
            Label(Area(panel, "Name", .035f, .72f, .36f, .96f), form.formName, 25, TextAnchor.MiddleLeft);
            _heartGauge = Label(Area(panel, "Hearts", .37f, .73f, .61f, .95f),
                $"{Hearts(persona)}   {relationship.affinity:N0} / {TagForceRelation.MaxAffinity:N0} pt", 19,
                TextAnchor.MiddleRight);
            _heartGauge.color = new Color(1f, .70f, .80f);
            Label(Area(panel, "Title", .035f, .575f, .61f, .73f), form.formTitle, 15, TextAnchor.MiddleLeft);
            string line = string.IsNullOrEmpty(_reply) ? form.Greet(relationship.Hearts) : _reply;
            if (!string.IsNullOrWhiteSpace(line) && (_dialogueLog.Count == 0 || _dialogueLog[^1] != line))
            {
                if (_dialogueLog.Count == 16) _dialogueLog.RemoveAt(0);
                _dialogueLog.Add(line);
            }
            TypeDialogue(Area(panel, "DialogueText", .035f, .11f, .61f, .545f), line);

            var actions = List("DialogueActions", .65f, .06f, .978f, .91f, panel);
            Row(actions, "이야기하기", () => Show(Page.Topics), icon: "action/look");
            Row(actions, "샌드위치·선물 주기", () => Show(Page.Gift), icon: "action/set");
            Row(actions, "듀얼 신청", () => { _deckLevel = 1; Show(Page.Duel); },
                sound: TagForceSfx.DuelEncounter, icon: "action/attack");
            Row(actions, state.partnerId == persona.personaId ? "파트너 해제" : "파트너 신청",
                () => SetPartner(persona), sound: null, icon: "check");
            Row(actions, "대화 기록", () => Show(Page.DialogueLog));
            if (persona.NextBond(relationship) is { } pending)
                Row(actions, "인연 승급전", () => { _bond = pending; Show(Page.Bond); });
            if (relationship.masterReward || relationship.wonGrandPrix || relationship.Hearts == TagForceRelation.HeartCount)
                Row(actions, "파트너 마스터 덱 레시피 받기", () => CopyMasterDeck(persona));

            _conversationProgress = animate ? 0f : 1f;
            ApplyConversationPose();
        }

        private void UpdateConversation(float delta)
        {
            if (_dialoguePanel == null) return;
            if (_closingConversation || _conversationProgress < 1f)
            {
                _conversationProgress = Mathf.MoveTowards(_conversationProgress, _closingConversation ? 0f : 1f,
                    delta / (_closingConversation ? .22f : .32f));
                if (_closingConversation && _conversationProgress == 0f)
                {
                    Show(Page.Field);
                    return;
                }
                ApplyConversationPose();
            }
            if (!_closingConversation && _conversationProgress >= .55f) UpdateDialogue(delta);
        }

        private void ApplyConversationPose()
        {
            float visible = Mathf.SmoothStep(0f, 1f, _conversationProgress);
            float offset = (1f - visible) * _body.rect.width * .50f;
            _leftStanding.anchoredPosition = Vector2.left * offset;
            _rightStanding.anchoredPosition = Vector2.right * offset;
            _conversationShade.color = new Color(.017f, .037f, .055f, .65f * visible);
            _dialoguePanel.alpha = Mathf.InverseLerp(.23f, .90f, visible);
            _dialoguePanel.interactable = !_closingConversation && _conversationProgress >= .90f;
            ((RectTransform)_dialoguePanel.transform).anchoredPosition = Vector2.down * (1f - visible) * 64f;
        }

        private void BuildTopics()
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Field); return; }
            var list = List("Topics", .10f, .10f, .90f, .91f);
            Row(list, persona.displayName + "에게 이야기할 주제", null, 50);
            foreach (var topic in _data.conversationTopics)
                Row(list, topic.name, () => Discuss(persona, topic), 52);
        }

        private void BuildDialogueLog()
        {
            var list = List("DialogueLog", .10f, .10f, .90f, .91f);
            Row(list, Selected?.displayName + " · 대화 기록", null, 50);
            foreach (var line in _dialogueLog)
                Row(list, line, null, 64);
        }

        private void TypeDialogue(RectTransform area, string line)
        {
            StartTypingDialogue(Label(area, "", 18, TextAnchor.UpperLeft), line);
        }

        private void StartTypingDialogue(Text label, string line)
        {
            _dialogue = label;
            _dialogue.supportRichText = true;
            _dialogueLine = line ?? "";
            _dialogueLetters = StringInfo.ParseCombiningCharacters(_dialogueLine);
            _visibleDialogueLetters = 0;
            _dialogueProgress = 0f;
            PaintDialogue();
        }

        private void UpdateDialogue(float delta)
        {
            if (_dialogue == null || _visibleDialogueLetters == _dialogueLetters.Length) return;
            _dialogueProgress += delta * 32f;
            int before = _visibleDialogueLetters;
            _visibleDialogueLetters = Math.Min((int)_dialogueProgress, _dialogueLetters.Length);
            if (_visibleDialogueLetters == before) return;
            if (before == 0 || before / 3 != _visibleDialogueLetters / 3)
                DuelAudio.PlayTagForceSfx(TagForceSfx.DialogueBlip, .5f);
            PaintDialogue();
        }

        private void PaintDialogue()
        {
            int count = _visibleDialogueLetters;
            _dialogue.text = count == _dialogueLetters.Length ? _dialogueLine :
                $"{_dialogueLine[.._dialogueLetters[count]]}<color=#FFFFFF00>{_dialogueLine[_dialogueLetters[count]..]}</color>";
        }

        private void Discuss(TagForcePersona persona, TagForceTopic topic)
        {
            int before = State.Relationship(persona.personaId).affinity;
            if (!_manager.TryTalk(persona, topic, out var line, out uint reward, out var error))
            {
                ShowStatus(error);
                return;
            }
            _reply = line;
            Show(Page.Talk);
            ShowAffinityFeedback(persona, before, reward);
        }

        private void BuildGifts()
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Map); return; }
            var list = List("Gifts", .025f, .08f, .975f, .95f);
            foreach (var inventory in State.items.ToArray())
            {
                var item = _data.Item(inventory.id);
                if (item == null) continue;
                var row = Row(list, $"{item.name} · {inventory.count}개   {item.desc}", () =>
                    Gift(persona, item, 0), 56);
                Icon(row, item.iconPath);
            }
            foreach (var card in CardDatabase.AllCards.Where(card => CollectionProgress.GetOwnedCount(card.code) > 3)
                .OrderBy(card => card.name, StringComparer.CurrentCulture))
                Row(list, $"{card.name} · 여분 {CollectionProgress.GetOwnedCount(card.code) - 3}장",
                    () => Gift(persona, null, card.code));
            if (list.childCount == 0) Row(list, "선물할 빵, 물건 또는 여분 카드가 없습니다.", null);
        }

        private void Gift(TagForcePersona persona, TagForceItem item, uint card)
        {
            int before = State.Relationship(persona.personaId).affinity;
            if (!_manager.TryGive(persona, item, card, out var line, out uint reward, out var error))
            {
                ShowStatus(error);
                return;
            }
            _reply = line;
            Show(Page.Talk);
            ShowAffinityFeedback(persona, before, reward);
        }

        private void ShowAffinityFeedback(TagForcePersona persona, int before, uint reward)
        {
            int affinity = State.Relationship(persona.personaId).affinity;
            if (affinity != before)
            {
                DuelAudio.PlayTagForceSfx(affinity > before ? TagForceSfx.HeartUp : TagForceSfx.HeartDown);
                if (_heartGauge != null) UiEntranceMotion.Play(_heartGauge.gameObject, .48f, .55f);
            }
            if (reward != 0)
                ShowStatus($"마스터 달성 · 오버프레임 {CardDatabase.GetName(reward)}");
        }

        private void SetPartner(TagForcePersona persona)
        {
            bool wasPartner = State.partnerId == persona.personaId;
            if (!_manager.TrySetPartner(persona, out var line, out var error))
            {
                ShowStatus(error);
                return;
            }
            DuelAudio.PlayTagForceSfx(wasPartner ? TagForceSfx.Cancel :
                State.partnerId == persona.personaId ? TagForceSfx.PartnerAccept : TagForceSfx.HeartDown);
            _reply = line;
            Show(Page.Talk);
        }

        private void BuildBond()
        {
            var list = List("Bond", .04f, .05f, .96f, .97f);
            Row(list, PrintDialogue(_bond), null, 240);
            Row(list, _bond.choice.prompt, null, 52);
            foreach (var option in _bond.choice.options)
                Row(list, option.text, () =>
                {
                    _effect = option.effectType;
                    _reply = option.reply;
                    _deckLevel = 1;
                    Show(Page.Duel);
                }, 54, sound: TagForceSfx.DuelEncounter);
        }

        private void BuildDuelChoice()
        {
            var persona = Selected;
            if (persona == null) { Show(Page.Field); return; }
            var form = persona.DisplayForm(State);
            var partner = _data.Persona(State.partnerId);
            int unlocked = _grandPrix ? form.Decks.Count : Math.Min(form.Decks.Count, State.Relationship(persona.personaId).unlockedLevel);
            _deckLevel = _grandPrix ? unlocked : Math.Clamp(_deckLevel, 1, Math.Max(1, unlocked));
            string headline = (_grandPrix ? $"그랑프리 {State.grandPrixRound + 1}/3 라운드 · " : "") + form.formName;
            string passive = partner == null ? "" :
                $"파트너 · {partner.partnerSkillName}\n시작 LP +{partner.OpeningLifeBonus:N0} · 시작 패 {partner.OpeningHand}장" +
                (partner.DestinyLimit > 2000 ? $" · 데스티니 드로우 {partner.DestinyLimit:N0} LP" : "");
            Label(Area(_body, "Match", .02f, .74f, .98f, .985f), $"{headline}\n{form.duelStartLine}\n{_reply}\n{passive}", 20);
            var yourDecks = DeckRepository.UserDecks.Where(deck => GameAppController.CanStartWithDeck(deck, out _)).ToArray();
            var playerDeck = yourDecks.FirstOrDefault(d => d.id == _deckId) ??
                yourDecks.FirstOrDefault(d => d.id == CollectionProgress.LastDuelDeckId) ?? yourDecks.FirstOrDefault();
            var player = List("YourDeck", .02f, .15f, .49f, .72f);
            foreach (var deck in yourDecks)
                Row(player, $"{(deck == playerDeck ? "✓ " : "")}{deck.name}", () => { _deckId = deck.id; Show(Page.Duel); });
            if (yourDecks.Length == 0) Row(player, "사용 가능한 내 덱이 없습니다.", null);
            var rival = List("RivalDeck", .51f, .15f, .98f, .72f);
            for (int i = _grandPrix ? Math.Max(0, unlocked - 1) : 0; i < unlocked; i++)
            {
                int level = i + 1;
                Row(rival, $"{(level == _deckLevel ? "✓ " : "")}Lv.{level} · {form.Decks[i].title}",
                    () => { _deckLevel = level; Show(Page.Duel); });
            }
            var start = Btn(Area(_body, "Start", .35f, .015f, .65f, .135f), "듀얼 시작", () =>
            {
                uint support = partner != null ? CollectionProgress.TagForceAce(partner.DisplayForm(State)) : 0;
                _app.StartTagForceDuel(new TagForceDuel(_data.seriesId, persona, form, partner, form.Decks[_deckLevel - 1], _deckLevel,
                    _bond, _effect, _grandPrix, support), playerDeck);
            });
            start.interactable = playerDeck != null && unlocked != 0;
        }

        private void BeginGrandPrix()
        {
            var partner = _data.Persona(State.partnerId);
            if (partner == null || State.Relationship(partner.personaId).Hearts < 4)
            {
                ShowStatus("하트 4개 이상인 파트너의 동행이 필요합니다.");
                return;
            }
            var roster = _data.personas.Where(persona => persona != partner)
                .OrderByDescending(persona => persona.DisplayForm(State).Decks.Count).ToArray();
            if (roster.Length <= State.grandPrixRound) return;
            _selectedId = roster[State.grandPrixRound].personaId;
            _grandPrix = true;
            _bond = null;
            _reply = "";
            _effect = null;
            Show(Page.Duel);
        }

        private void CopyMasterDeck(TagForcePersona persona)
        {
            var preset = persona.DisplayForm(State).Decks.LastOrDefault();
            if (preset == null) return;
            var deck = new UserDeck
            {
                id = Guid.NewGuid().ToString("N"),
                name = persona.displayName + " 마스터 레시피",
                main = preset.main.ToList(),
                extra = preset.extra.ToList(),
                side = preset.side.ToList(),
            };
            try
            {
                DeckRepository.SaveUserDeck(deck);
                DeckRepository.UserDecks.Add(deck);
                _app.OpenTagForceDeckEditor();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                ShowStatus("덱 레시피를 저장할 수 없습니다.");
            }
        }

        private void BuildShop()
        {
            Backdrop(_data.assetConfig.breadShopPath, new Color(.41f, .25f, .19f));
            var shop = _data.drawBreadCatalog;
            if (shop == null)
            {
                Label(_body, "이 월드의 매점은 준비 중입니다.", 24);
                return;
            }
            Label(Area(_body, "ShopTitle", .05f, .74f, .95f, .97f),
                $"{shop.shopkeeperName}\n{shop.shopkeeperGreeting}\n남은 봉지 {12 - State.bags.Count}/12 · {shop.drawPriceDp} DP", 21);
            for (int i = 0; i < 12; i++)
            {
                int bag = i;
                float x = .06f + i % 6 * .149f;
                float y = i < 6 ? .473f : .252f;
                bool opened = State.bags.Contains(i);
                var button = Btn(Area(_body, "Bag" + i, x, y, x + .137f, y + .191f),
                    opened ? "개봉 완료" : $"빵 봉지 {i + 1}", () => DrawBread(bag), sound: null);
                button.interactable = !opened && CollectionProgress.Balance >= shop.drawPriceDp;
            }
            var gifts = List("ShopGifts", .055f, .025f, .945f, .231f);
            foreach (var item in _data.gifts)
                Icon(Row(gifts, $"{item.name} · {item.priceDp} DP", () =>
                {
                    var next = State.Copy();
                    next.ChangeItem(item.Id, 1);
                    if (Commit(next, -item.priceDp))
                    {
                        DuelAudio.PlayTagForceReward();
                        ShowStatus($"{item.name} 구입");
                        Show(Page.Shop);
                    }
                }), item.iconPath);
        }

        private void DrawBread(int bag)
        {
            var next = State.Copy();
            if (_breadSpin != null || next.bags.Contains(bag)) return;
            var shop = _data.drawBreadCatalog;
            int weight = shop.breads.Sum(item => Math.Max(0, item.weight));
            var bread = _data.DrawBread(UnityEngine.Random.Range(0, weight));
            next.bags.Add(bag);
            next.ChangeItem(bread.Id, 1);
            uint card = bread.bonusRandomCard ? CollectionProgress.TagForceBonusCard() : 0;
            if (!Commit(next, -shop.drawPriceDp, card, playReward: false)) return;

            foreach (var button in _body.GetComponentsInChildren<Button>()) button.interactable = false;
            var shade = DuelUiKit.FillRect("BreadRoulette", _body);
            DuelUiKit.AddImage(shade.gameObject, DuelUiKit.WhiteSprite, new Color(.022f, .039f, .053f, .75f));
            var reveal = Area(shade, "Prize", .24f, .28f, .76f, .73f);
            Tint(reveal, new Color(.075f, .18f, .24f));
            _breadSpin = new BreadSpin
            {
                Prize = bread,
                Card = card,
                Caption = Label(Area(reveal, "Caption", .31f, .12f, .94f, .89f), "", 24),
                Icon = DuelUiKit.AddImage(Area(reveal, "Icon", .04f, .13f, .29f, .86f).gameObject,
                    null, Color.clear, true, false),
            };
            DuelAudio.PlayTagForceSfx(TagForceSfx.SandwichRoulette);
            UpdateBreadSpin(0f);
        }

        private void UpdateBreadSpin(float delta)
        {
            var spin = _breadSpin;
            if (spin == null) return;
            spin.Elapsed += delta;
            if (spin.Elapsed >= .80f)
            {
                if (!spin.Revealed)
                {
                    spin.Revealed = true;
                    PresentBread(spin.Prize, BreadResult(spin));
                    DuelAudio.PlayTagForceSfx(TagForceSfx.SandwichWin);
                }
                if (spin.Elapsed >= 1.72f) Show(Page.Shop);
                return;
            }

            var breads = _data.drawBreadCatalog.breads;
            int preview = (int)(spin.Elapsed / .085f) % breads.Length;
            if (preview == spin.LastPreview) return;
            spin.LastPreview = preview;
            PresentBread(breads[preview], breads[preview].name);
        }

        private void PresentBread(TagForceItem item, string caption)
        {
            _breadSpin.Caption.text = caption;
            var sprite = _images.Get(item.iconPath);
            _breadSpin.Icon.sprite = sprite;
            _breadSpin.Icon.color = sprite != null ? Color.white : Color.clear;
        }

        private void FinishBread()
        {
            if (_breadSpin == null) return;
            ShowStatus(BreadResult(_breadSpin));
            _breadSpin = null;
        }

        private static string BreadResult(BreadSpin spin) => spin.Card == 0 ? $"{spin.Prize.name} 획득!" :
            $"{spin.Prize.name} · {CardDatabase.GetName(spin.Card)} 획득!";

        private string Hearts(TagForcePersona persona)
        {
            int count = State.Relationship(persona.personaId).Hearts;
            return new string('♥', count) + new string('♡', TagForceRelation.HeartCount - count);
        }

        private static string PrintDialogue(TagForceBondEvent story) =>
            story.title + "\n" + string.Join("\n", story.dialogues.Select(line => $"{line.speaker}  {line.text}"));

        private Image Backdrop(string path, Color fallback, Transform parent = null)
        {
            var sprite = _images.Get(path);
            return DuelUiKit.AddImage(DuelUiKit.FillRect("Scenery", parent ?? _body).gameObject, sprite,
                sprite == null ? fallback : Color.white, false, false);
        }

        private bool Portrait(RectTransform rect, string path, string personaName)
        {
            var sprite = _images.Get(path);
            if (sprite != null)
            {
                DuelUiKit.AddImage(rect.gameObject, sprite, Color.white, true, false);
                return true;
            }
            Tint(rect, new Color(.14f, .32f, .39f, .94f));
            Label(rect, personaName, 16);
            return false;
        }

        private void Icon(RectTransform root, string path)
        {
            var sprite = _images.Get(path);
            if (sprite == null) return;
            var icon = Area(root, "ItemArt", .016f, .07f, .107f, .93f);
            DuelUiKit.AddImage(icon.gameObject, sprite, Color.white, true, false);
            var label = root.GetComponentInChildren<Text>().rectTransform;
            label.anchorMin = new Vector2(.13f, label.anchorMin.y);
        }

        private RectTransform List(string name, float l, float b, float r, float t, Transform parent = null)
        {
            var viewport = Area(parent ?? _body, name, l, b, r, t);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            var items = DuelUiKit.CreateRect("Items", viewport, Vector2.up, Vector2.one, Vector2.zero, Vector2.zero);
            items.pivot = Vector2.up;
            items.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 4;
            items.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = items;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            return items;
        }

        private static RectTransform Row(Transform parent, string text, Action click, float height = 37,
            TagForceSfx? sound = TagForceSfx.Confirm, string icon = null)
        {
            var row = DuelUiKit.FillRect("Row", parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            if (click == null) Label(row, text, 17, TextAnchor.MiddleLeft);
            else
            {
                Btn(row, text, click, sound: sound);
                var sprite = icon == null ? null : DuelUiIcons.Find(icon);
                if (sprite != null)
                {
                    var art = Area(row, "ActionIcon", .025f, .12f, .13f, .88f);
                    DuelUiKit.AddImage(art.gameObject, sprite, Color.white, true, false);
                    var label = (RectTransform)row.Find("Label");
                    label.anchorMin = new Vector2(.14f, label.anchorMin.y);
                }
            }
            return row;
        }

        private static RectTransform Anchor(Transform parent, string name, Vector2 point, Vector2 size)
        {
            var rect = DuelUiKit.CreateRect(name, parent, point, point, Vector2.zero, Vector2.zero);
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Area(Transform parent, string name, float l, float b, float r, float t) =>
            DuelUiKit.CreateRect(name, parent, new(l, b), new(r, t), Vector2.zero, Vector2.zero);

        private static Button Btn(RectTransform rect, string text, Action action, int size = 17,
            TagForceSfx? sound = TagForceSfx.Confirm)
        {
            var background = DuelUiKit.AddImage(rect.gameObject, DuelUiKit.WhiteSprite, new Color(.12f, .31f, .39f));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                if (sound is { } cue) DuelAudio.PlayTagForceSfx(cue);
                action();
            });
            rect.gameObject.AddComponent<TagForceCursorSound>();
            Label(rect, text, size);
            return button;
        }

        private static Text Label(RectTransform rect, string text, int size, TextAnchor align = TextAnchor.MiddleCenter) =>
            DuelUiKit.AddFittedText(rect, "Label", text, size, Math.Max(10, size - 8), align,
                new Color(.95f, .97f, .93f), FontStyle.Bold);
        private static void Tint(RectTransform rect, Color color) =>
            DuelUiKit.AddImage(rect.gameObject, DuelUiKit.WhiteSprite, color, false, false);
    }

    internal sealed class TagForceCursorSound : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        public void OnSelect(BaseEventData eventData) => Play();
        public void OnPointerEnter(PointerEventData eventData) => Play();

        private void Play()
        {
            if (GetComponent<Button>().IsInteractable()) DuelAudio.PlayTagForceSfx(TagForceSfx.Cursor, .65f);
        }
    }

    internal sealed class TagForceFloorTap : MonoBehaviour, IPointerClickHandler
    {
        internal Action<Vector2> Click;
        public void OnPointerClick(PointerEventData eventData)
        {
            var field = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(field, eventData.position, eventData.pressEventCamera, out var point))
                Click?.Invoke(Rect.PointToNormalized(field.rect, point));
        }
    }

    internal sealed class TagForceWorldImages
    {
        private readonly Dictionary<string, Sprite> _loaded = new();
        private readonly Dictionary<string, Sprite[]> _slices = new();
        private readonly Dictionary<string, Sprite> _busts = new();
        private readonly HashSet<string> _visible = new();

        internal void BeginPage() => _visible.Clear();

        internal Sprite Get(string relative, bool readable = false)
        {
            if (string.IsNullOrWhiteSpace(relative) || !relative.StartsWith("TagForce/", StringComparison.Ordinal) ||
                !relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                relative.IndexOfAny(new[] { '\\', ':' }) >= 0 || relative.Split('/').Contains("..")) return null;
            _visible.Add(relative);
            if (_loaded.TryGetValue(relative, out var result)) return result;
            result = Read(Path.Combine(Application.persistentDataPath, relative), readable) ??
                Read(Path.Combine(Application.streamingAssetsPath, relative), readable);
            _loaded.Add(relative, result);
            return result;
        }

        internal Sprite[] GetSlices(string path, int columns, int rows, bool fixedCells = false)
        {
            var source = Get(path, readable: !fixedCells);
            if (source == null) return null;
            if (_slices.TryGetValue(path, out var clips)) return clips;
            var texture = source.texture;
            if (!fixedCells && !texture.isReadable) return null;

            try
            {
                Color32[] pixels = fixedCells ? null : texture.GetPixels32();
                var rectangles = new Rect[columns * rows];
                for (int row = 0; row < rows; row++)
                {
                    int bottom = texture.height * (rows - row - 1) / rows;
                    int top = texture.height * (rows - row) / rows;
                    for (int column = 0; column < columns; column++)
                    {
                        int left = texture.width * column / columns;
                        int right = texture.width * (column + 1) / columns;
                        if (fixedCells)
                        {
                            rectangles[row * columns + column] = new Rect(left, bottom, right - left, top - bottom);
                            continue;
                        }
                        int xMin = right, yMin = top, xMax = left - 1, yMax = bottom - 1;
                        for (int y = bottom; y < top; y++)
                        for (int x = left; x < right; x++)
                        {
                            if (pixels[y * texture.width + x].a < 28) continue;
                            xMin = Math.Min(xMin, x);
                            xMax = Math.Max(xMax, x);
                            yMin = Math.Min(yMin, y);
                            yMax = Math.Max(yMax, y);
                        }
                        if (xMin >= xMax || yMin >= yMax) return null;
                        rectangles[row * columns + column] = new Rect(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
                    }
                }
                clips = rectangles.Select(rect => Sprite.Create(texture, rect, new Vector2(.5f, .5f),
                    100f, 0, SpriteMeshType.FullRect)).ToArray();
                return clips;
            }
            finally
            {
                _slices.Add(path, clips);
                if (!fixedCells) texture.Apply(false, true);
            }
        }

        internal Sprite GetBust(string path)
        {
            var source = Get(path, readable: true);
            if (source == null) return null;
            if (!_busts.TryGetValue(path, out var bust))
            {
                _busts[path] = bust = RpgDialoguePortrait.Crop(source.texture, source.rect);
                if (source.texture.isReadable) source.texture.Apply(false, true);
            }
            return bust;
        }

        private static Sprite Read(string file, bool readable)
        {
            Texture2D texture = null;
            bool keep = false;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 20 * 1024 * 1024) return null;
                byte[] bytes = File.ReadAllBytes(file);
                if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
                    return null;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes, !readable)) return null;
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.50f, .50f));
                keep = sprite != null;
                return sprite;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning($"[TagForceWorld] {Path.GetFileName(file)}: {error.Message}");
                return null;
            }
            finally
            {
                if (!keep && texture != null) DestroyAsset(texture);
            }
        }

        internal void Clear()
        {
            _visible.Clear();
            Trim();
        }

        internal void Trim()
        {
            foreach (string path in _loaded.Keys.Where(path => !_visible.Contains(path)).ToArray())
            {
                var sprite = _loaded[path];
                _loaded.Remove(path);
                if (_slices.Remove(path, out var clips) && clips != null)
                    foreach (var clip in clips) DestroyAsset(clip);
                if (_busts.Remove(path, out var bust)) DestroyAsset(bust);
                if (sprite == null) continue;
                var texture = sprite.texture;
                DestroyAsset(sprite);
                DestroyAsset(texture);
            }
        }

        private static void DestroyAsset(UnityEngine.Object asset)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
            else UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
