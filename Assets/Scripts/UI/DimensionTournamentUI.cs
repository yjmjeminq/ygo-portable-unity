using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed class DimensionTournamentUI
    {
        private static readonly Color Panel = new(.035f, .079f, .145f, .98f);
        private readonly GameAppController _app;
        private readonly StoryModeUI.StoryDialogueView _dialogue;
        private readonly CanvasGroup _browser;
        private readonly RectTransform _bracket;
        private readonly Button _eight;
        private readonly Button _sixteen;
        private readonly Button _next;
        private readonly Button _back;
        private readonly Text _status;
        private readonly Text _fame;
        private readonly Dropdown _deck;
        private readonly GameObject _trophy;
        private readonly List<UserDeck> _decks = new();
        private readonly Dictionary<int, int> _aiResults = new();
        private DimensionTournament _run;
        private TournamentMatchSimulation _simulation;
        private int _simulationNode;
        private bool _saveFailed;
        private string _deckId;

        internal GameObject Root { get; }
        internal GameObject ActiveRoot => _dialogue.IsOpen ? _dialogue.ActiveRoot : Root;

        internal DimensionTournamentUI(GameAppController app, Transform parent)
        {
            _app = app;
            var root = LotdUiKit.FillRect("DimensionTournament", parent);
            Root = root.gameObject;
            LotdUiKit.AddImage(Root, LotdUiKit.WhiteSprite, new Color(.016f, .035f, .073f, 1f), false, false);
            var content = LotdUiKit.FillRect("BracketView", root);
            _browser = content.gameObject.AddComponent<CanvasGroup>();
            var area = Application.isMobilePlatform ? MobileSafeArea.Create(content) : content;
            AddLabel(area, "Title", "시공 초월 차원 대항전", .04f, .903f, .69f, .978f, 32, LotdUiKit.GoldAccent);
            _back = AddButton(area, "Back", "스토리로", .834f, .9f, .968f, .973f, Back);
            _fame = AddLabel(area, "HallOfFame", "", .045f, .839f, .955f, .898f, 22, LotdUiKit.TextPrimary);
            var board = LotdUiKit.CreateMenuPanel(area, "Bracket", new Vector2(.033f, .228f), new Vector2(.966f, .83f));
            _bracket = LotdUiKit.FillRect("Draw", board);
            _status = AddLabel(area, "Status", "", .044f, .158f, .957f, .22f, 18, LotdUiKit.GoldAccent);
            _eight = AddButton(area, "Eight", "8강 참가", .041f, .049f, .195f, .144f, () => Begin(8));
            _sixteen = AddButton(area, "Sixteen", "16강 참가", .21f, .049f, .364f, .144f, () => Begin(16));
            var deck = Region(area, "PlayerDeck", .385f, .049f, .732f, .144f);
            _deck = LotdUiKit.CreateLotdDropdown(deck, "SelectDeck", Array.Empty<string>(), index =>
            {
                if (index >= 0 && index < _decks.Count) _deckId = _decks[index].id;
            }, dynamicChoices: true);
            _next = AddButton(area, "NextRound", "다음 경기", .75f, .049f, .958f, .144f, IntroduceRound, true);
            _dialogue = new StoryModeUI.StoryDialogueView(root);
            var trophy = Region(root, "ChampionTrophy", .76f, .60f, .918f, .8f);
            _trophy = trophy.gameObject;
            LotdUiKit.AddImage(trophy.gameObject, LotdUiKit.WhiteSprite, new Color(.20f, .126f, .040f, .97f), false, false);
            var crown = Region(trophy, "Crown", .21f, .31f, .79f, .94f);
            LotdUiKit.AddImage(crown.gameObject, DuelVisualAssets.WinnerCrown, LotdUiKit.GoldAccent, true, false).enabled =
                DuelVisualAssets.WinnerCrown != null;
            AddLabel(trophy, "Champion", "우승 트로피", .04f, .05f, .96f, .31f, 20, LotdUiKit.GoldAccent);
            Close();
        }

        internal void Open()
        {
            Root.SetActive(true);
            CloseDialogue();
            _run = CollectionProgress.Tournament;
            _saveFailed = false;
            LoadDecks();
            Render();
            Focus(_next.interactable ? _next : _eight.interactable ? _eight : _back);
        }

        internal void Close()
        {
            _simulation?.Dispose();
            _simulation = null;
            _aiResults.Clear();
            _deck.Hide();
            CloseDialogue();
            Root.SetActive(false);
        }

        internal void Back()
        {
            if (_dialogue.IsOpen)
            {
                if (_dialogue.ClosePopup()) return;
                CloseDialogue();
                Focus(_next.interactable ? _next : _back);
                return;
            }
            _app.ShowScreen(AppScreen.StoryMode);
        }

        internal void ShowResult(OpponentCharacter opponent, int slots, bool won, bool draw,
            (int Dp, int Dust, bool Champion) prize, string error)
        {
            string outcome = won ? "승리" : draw ? "무승부 · 탈락" : "탈락";
            string result = !string.IsNullOrWhiteSpace(error) ? error :
                won ? $"+{prize.Dp:N0} DP" : "이번 대진이 종료되었습니다.";
            if (prize.Champion) result = $"우승 · {result} · 가루 +{prize.Dust:N0}\n시크릿 팩 무료 10팩권 1장";
            string response = won ? VictoryLine(opponent.id, slots) : "이번 대진의 승자는 정해졌어. 그래도 네 다음 도전을 기다리겠다.";
            var lines = new[]
            {
                new StoryDialogueLine { speaker = opponent.name, text = response },
                new StoryDialogueLine { speaker = "주인공", text = prize.Champion
                    ? "여섯 차원의 듀얼리스트와 겨뤄 얻은 트로피야. 오늘의 한 수들을 기억할게." :
                    won ? "다음 상대가 누구든, 이번 경기에서 배운 수를 가져갈게." : "다음에는 이 결과를 바꿔 보겠어." },
            };
            OpenDialogue(opponent, $"{DimensionTournament.RoundTitle(slots)} · {outcome}", lines, () =>
            {
                if (prize.Champion)
                {
                    _trophy.SetActive(true);
                    UiEntranceMotion.Play(_trophy, .65f, .35f);
                    if (Application.isPlaying) LotdAudio.PlayWin();
                }
                _dialogue.ShowChoices(() => result, new StoryModeUI.StoryChoice("대진표로", Back));
            });
        }

        internal void Update(float delta)
        {
            _dialogue.Update(delta);
            if (_dialogue.IsOpen || _run?.Active != true || _saveFailed) return;
            var remaining = _run.PendingAiMatches().Where(node => !_aiResults.ContainsKey(node)).ToArray();
            if (_simulation == null && remaining.Length == 0)
            {
                if (_aiResults.Count == 0) return;
                if (CollectionProgress.TrySaveTournamentAi(_run.seed, _run.round, _aiResults, out string error))
                {
                    _run = CollectionProgress.Tournament;
                    _aiResults.Clear();
                    Render();
                }
                else
                {
                    _saveFailed = true;
                    _status.text = error;
                }
                return;
            }
            if (_simulation == null)
            {
                _simulationNode = remaining[0];
                _simulation = new TournamentMatchSimulation(_run.bracket[_simulationNode * 2], _run.bracket[_simulationNode * 2 + 1],
                    _run.seed, _simulationNode, _run.PlayerNode);
            }
            _status.text = $"AI 경기 진행 중 · {_aiResults.Count} / {_run.PendingAiMatches().Count()}";
            var timer = Stopwatch.StartNew();
            do { _simulation.Step(); } while (!_simulation.Finished && timer.Elapsed.TotalMilliseconds < 4);
            if (!_simulation.Finished) return;
            _aiResults[_simulationNode] = _simulation.Winner;
            _simulation.Dispose();
            _simulation = null;
            DrawBracket();
        }

        private void Begin(int size)
        {
            if (!CollectionProgress.TryStartTournament(size, out string error))
            {
                _status.text = error;
                return;
            }
            _run = CollectionProgress.Tournament;
            _saveFailed = false;
            Render();
        }

        private void LoadDecks()
        {
            _decks.Clear();
            _decks.AddRange(DeckRepository.UserDecks.Where(deck => GameAppController.CanStartWithDeck(deck, out _)));
            int selection = _decks.FindIndex(deck => deck.id == (_deckId ?? CollectionProgress.LastDuelDeckId));
            _deck.ClearOptions();
            _deck.AddOptions(_decks.Count == 0 ? new List<string> { "사용 가능한 덱 없음" } : _decks.Select(deck => deck.name).ToList());
            _deck.SetValueWithoutNotify(Mathf.Max(0, selection));
            _deck.RefreshShownValue();
        }

        private void IntroduceRound()
        {
            if (!_next.interactable || _run?.Active != true) return;
            var run = _run;
            var opponent = DeckRepository.GetCharacter(run.Opponent);
            var deck = DimensionTournament.HighestDeck(run.Opponent);
            var opening = DimensionTournament.Opening(run.PlayerNode, deck);
            string ace = opening.FieldCard == 0 ? "" : $"\n초기 에이스 · {CardDatabase.GetName(opening.FieldCard)}";
            var lines = new[]
            {
                new StoryDialogueLine { speaker = opponent.name, text = EntranceLine(opponent.id, run.PlayerNode) },
                new StoryDialogueLine { speaker = "주인공", text = "좋아. 지금까지 준비한 덱으로 정면에서 겨뤄 보겠어." },
            };
            OpenDialogue(opponent, $"{DimensionTournament.RoundTitle(run.PlayerNode)} · {opponent.name}", lines, () =>
                _dialogue.ShowChoices(() => $"상대 LP {opening.Life:N0} · 패 {opening.Hand}장{ace}",
                    new("경기 시작", () => _app.StartTournamentDuel(run, _decks[_deck.value])),
                    new("대진표로", Back)));
        }

        private void OpenDialogue(OpponentCharacter opponent, string title, StoryDialogueLine[] lines, Action onFinish)
        {
            _browser.blocksRaycasts = _browser.interactable = false;
            _dialogue.Open(title, "차원 대항전 참가자", LotdUiKit.LoadOpponentPortrait(opponent), lines, onFinish);
        }

        private void CloseDialogue()
        {
            _trophy.SetActive(false);
            _dialogue.Close();
            _browser.blocksRaycasts = _browser.interactable = true;
        }

        internal void ShowStatus(string message) => _status.text = message;

        private void Render()
        {
            bool active = _run?.Active == true;
            bool unlocked = DimensionTournament.Unlocked;
            _eight.interactable = _sixteen.interactable = unlocked && !active;
            _deck.interactable = _decks.Count > 0;
            _next.interactable = active && _run.Opponent > 0 && !_run.PendingAiMatches().Any() && _decks.Count > 0;
            _fame.text = $"명예의 전당 · 누적 우승 {CollectionProgress.TournamentWins:N0}회";
            _status.text = !unlocked ? "아무 시리즈의 50화를 클리어하면 대항전이 열립니다." :
                _run == null ? "8강은 3연승, 16강은 4연승으로 우승합니다." :
                _run.Champion ? "우승" : _run.lost ? "이번 대진에서 탈락했습니다." :
                _decks.Count == 0 ? DuelDeckAvailability.ExplainEmptyList(_deckId) :
                $"{DimensionTournament.RoundTitle(_run.PlayerNode)} · 다음 상대 {Name(_run.Opponent)}";
            DrawBracket();
        }

        private void DrawBracket()
        {
            for (int i = _bracket.childCount - 1; i >= 0; i--)
            {
                var child = _bracket.GetChild(i);
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child.gameObject);
                else UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
            if (_run == null)
            {
                AddLabel(_bracket, "Empty", "DM · GX · 5D’s · ZEXAL · ARC-V · VRAINS", .08f, .4f, .92f, .6f, 29, LotdUiKit.TextPrimary);
                return;
            }

            float column = .924f / (_run.RoundCount + 1);
            for (int level = 0; level <= _run.RoundCount; level++)
            {
                int slots = _run.size >> level;
                float left = .038f + level * column;
                float right = left + column * .88f;
                AddLabel(_bracket, "Round", DimensionTournament.RoundTitle(slots), left, .886f, right, .958f, 19, LotdUiKit.GoldAccent);
                for (int row = 0; row < slots; row++)
                {
                    int node = slots + row;
                    float step = .797f / slots;
                    float center = .868f - (row + .5f) * step;
                    float half = Mathf.Min(.038f, step * .41f);
                    if (slots > 1)
                    {
                        float parent = .868f - (row / 2 + .5f) * step * 2f;
                        float pivot = right + column * .06f;
                        Line(right, center - .0015f, pivot, center + .0015f);
                        Line(pivot - .0007f, Mathf.Min(center, parent), pivot + .0007f, Mathf.Max(center, parent));
                        Line(pivot, parent - .0015f, left + column, parent + .0015f);
                    }
                    int entry = _aiResults.GetValueOrDefault(node, _run.bracket[node]);
                    bool nextOpponent = _run.Active && node == (_run.PlayerNode ^ 1);
                    var cell = Region(_bracket, $"Match_{node}", left, center - half, right, center + half);
                    LotdUiKit.AddImage(cell.gameObject, LotdUiKit.WhiteSprite,
                        entry == DimensionTournament.Player ? new Color(.36f, .23f, .078f) :
                        nextOpponent ? new Color(.28f, .086f, .098f) : Panel, false, false);
                    string name = entry == DimensionTournament.Player && node == 1 ? "나 · 우승" : Name(entry);
                    LotdUiKit.AddFittedText(cell, "Name", name, 19, 10, TextAnchor.MiddleCenter,
                        entry == DimensionTournament.Player ? LotdUiKit.GoldAccent : LotdUiKit.TextPrimary);
                }
            }

            void Line(float left, float bottom, float right, float top) =>
                LotdUiKit.AddImage(Region(_bracket, "Connector", left, bottom, right, top).gameObject,
                    LotdUiKit.WhiteSprite, new Color(.25f, .38f, .52f), false, false);
        }

        private static string EntranceLine(int opponent, int slots) => opponent switch
        {
            104 => "차원이 달라도 듀얼을 믿는 마음은 같을 거다. 네 덱의 대답을 보여 다오.",
            94 => "이 무대까지 왔으면 변명은 필요 없겠군. 네 최고의 카드로 덤벼라.",
            119 => "여섯 차원에서 모인 대진이라니 재밌겠어! 나부터 전력으로 간다!",
            35 => "이 경기에서 네 덱이 이어 온 길을 보고 싶어. 서로 마지막까지 달려 보자.",
            143 => "네가 여기까지 온 기록은 확인했다. 승부는 지금부터다.",
            _ => slots == 2 ? "여기서 여섯 차원의 우승자가 정해진다. 시작부터 내 에이스를 마주해라." :
                "지금까지 올라온 실력을 보여 줘. 나도 이 대진을 양보하지 않겠다.",
        };

        private static string VictoryLine(int opponent, int slots) => opponent switch
        {
            104 => "좋은 듀얼이었다. 네가 카드와 함께 걸어온 길을 볼 수 있었어.",
            94 => "졌다는 사실은 인정한다. 하지만 다음 대진에서도 같은 결과라 생각하진 마라.",
            119 => "정말 신나는 승부였어! 다음에도 꼭 한 판 더 하자.",
            35 => "네 동료 카드들이 끝까지 네 편이었어. 그 믿음을 다음 경기에도 가져가.",
            143 => "네 승리다. 기록에 남겨 둘 가치가 있는 경기였어.",
            _ => slots == 2 ? "결승에 어울리는 승부였어. 그 트로피는 네 것이다." :
                "결정적인 한 수는 네 것이었어. 다음 상대에게도 그 수를 보여 줘.",
        };

        private static string Name(int id) => id == DimensionTournament.Player ? "나" :
            DeckRepository.GetCharacter(id)?.name ?? "미정";
        private static void Focus(Selectable control) => EventSystem.current?.SetSelectedGameObject(control.gameObject);
        private static RectTransform Region(Transform parent, string name, float left, float bottom, float right, float top) =>
            LotdUiKit.CreateRect(name, parent, new Vector2(left, bottom), new Vector2(right, top), Vector2.zero, Vector2.zero);
        private static Text AddLabel(Transform parent, string name, string text, float left, float bottom, float right, float top,
            int size, Color color) => LotdUiKit.AddFittedText(Region(parent, name, left, bottom, right, top),
                "Text", text, size, 11, TextAnchor.MiddleCenter, color);
        private static Button AddButton(Transform parent, string name, string text, float left, float bottom, float right, float top,
            Action action, bool gold = false) => LotdUiKit.CreateLotdButton(Region(parent, name, left, bottom, right, top),
                "Button", text, action, 20, gold);
    }
}
