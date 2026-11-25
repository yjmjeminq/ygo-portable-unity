using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal sealed partial class TagForceWorldUI
    {
        private const int RpgGarage = 0, RpgBridge = 6, RpgCrow = 10;
        private static readonly Vector2 RpgQuestCrowBench = new(.67f, .28f);
        private static readonly Vector2[] RpgExitPoints =
        {
            new(.075f, .20f), new(.925f, .20f), new(.12f, .455f), new(.88f, .455f),
        };

        private static readonly (int Region, int Exit, int Other, int Entrance)[] RpgPaths =
        {
            (0, 1, 1, 0), (0, 2, 3, 0), (1, 2, 2, 0), (1, 3, 5, 0),
            (1, 1, 6, 0), (3, 1, 4, 0), (6, 1, 7, 0), (7, 3, 8, 0),
        };

        private static readonly (string Name, Vector2 Position, string Description)[] RpgLandmarks =
        {
            ("정비 작업대", new(.71f, .24f), "듀얼 러너의 분해 도면과 부품함이 작업대에 놓여 있다."),
            ("벽의 표식", new(.66f, .27f), "벽에 그려진 작은 화살표는 밤에도 은신처 입구를 찾을 수 있게 표시한 것이다."),
            ("수용소 게시판", new(.58f, .34f), "교대 시간표 아래에 통신기가 끊겼다는 메모가 붙어 있다."),
            ("라디오", new(.67f, .29f), "낡은 라디오에서 포춘 컵 경기 결과가 흘러나온다."),
            ("경기 대진표", new(.62f, .29f), "대진표 아래에 선수들이 마지막으로 등록한 듀얼 러너 번호가 보인다."),
            ("희미한 흔적", new(.70f, .28f), "그을음 사이로 케이블이 끌려간 흔적이 남아 있다."),
            ("다리 난간", new(.69f, .27f), "난간 아래를 살피자 지나간 듀얼 러너의 타이어 자국이 보인다."),
            ("서킷 전광판", new(.64f, .32f), "마지막 주자의 기록이 아직 전광판에 남아 있다."),
            ("제어 단말기", new(.68f, .30f), "희미하게 켜진 단말기가 중단된 제어 기록을 반복해서 보여 준다."),
        };

        private readonly List<(int Slot, int Region, int Entry)> _rpgExits = new();
        private RectTransform _rpgFieldObjects, _rpgWalkInActor;
        private Text _rpgLandmarkLabel;
        private CanvasGroup _rpgWalkInChoices;
        private Button _rpgWalkInSelection;
        private Vector2 _rpgWalkInFrom;
        private float _rpgWalkInElapsed;
        private bool _rpgInspecting;

        private static RpgSideQuest RpgCouplerQuest => RpgSideQuest.Find(RpgSideQuest.GarageCouplerId);
        private static RpgQuestState RpgCouplerState => CollectionProgress.GetRpgQuestState(RpgSideQuest.GarageCouplerId);

        private static bool IsRpgQuestOpen(RpgSideQuest quest, int clears) =>
            (quest.Id != RpgSideQuest.GarageCouplerId || RpgRegions[RpgBridge].IsOpen(clears)) &&
            Array.Exists(RpgRegions, region => region.Id == quest.Region && region.IsOpen(clears));

        private void BuildRpgExploration(int cleared)
        {
            _rpgFieldObjects = DuelUiKit.FillRect("Exploration", _rpgSceneSpace);
            foreach (var (from, exit, to, entrance) in RpgPaths)
            {
                if (_rpgLocation == from) AddExit(exit, to, entrance);
                if (_rpgLocation == to) AddExit(entrance, from, exit);
            }

            var landmark = RpgLandmarks[_rpgLocation];
            var marker = Anchor(_rpgFieldObjects, "Landmark", landmark.Position, new Vector2(146f, 31f));
            void Inspect() => MoveTo(landmark.Position, OpenRpgLandmark);
            RpgButton(marker, landmark.Name, Inspect, 16);
            _rpgLandmarkLabel = marker.GetComponentInChildren<Text>();
            RegisterRpgInteraction(marker, $"{landmark.Name} · 조사", Inspect);

            void AddExit(int slot, int destination, int entry)
            {
                bool open = RpgRegions[destination].IsOpen(cleared);
                var gate = Anchor(_rpgFieldObjects, $"Exit_{destination}",
                    RpgExitPoints[slot] + new Vector2(0f, .047f), new Vector2(196f, 32f));
                string arrow = slot switch { 0 => "‹", 1 => "›", _ => "↑" };
                var button = RpgButton(gate, open ? $"{arrow}  {RpgRegions[destination].Name}" :
                    $"{RpgRegions[destination].Name} · {RpgRegions[destination].RequiredEpisode}화", () =>
                        MoveTo(RpgExitPoints[slot], () => FollowRpgExit(destination, entry)), 15);
                button.interactable = open;
                if (open) _rpgExits.Add((slot, destination, entry));
            }
        }

        private static Vector2 RpgExitDirection(int slot) =>
            slot == 0 ? Vector2.left : slot == 1 ? Vector2.right : Vector2.up;

        private void FollowRpgExit(int destination, int entry)
        {
            if (!CanWalk) return;
            var arrival = RpgExitPoints[entry] - RpgExitDirection(entry) * .105f;
            BeginRpgTravel(destination, arrival);
        }

        private bool CheckRpgExit(Vector2 previous)
        {
            var step = _position - previous;
            foreach (var (slot, destination, entry) in _rpgExits)
            {
                if (Vector2.Dot(step, RpgExitDirection(slot)) <= 0f) continue;
                var exit = RpgExitPoints[slot];
                bool reached = slot switch
                {
                    0 => _position.x <= exit.x && Mathf.Abs(_position.y - exit.y) <= .043f,
                    1 => _position.x >= exit.x && Mathf.Abs(_position.y - exit.y) <= .043f,
                    _ => _position.y >= exit.y && Mathf.Abs(_position.x - exit.x) <= .046f,
                };
                if (!reached) continue;
                FollowRpgExit(destination, entry);
                return true;
            }
            return false;
        }

        private bool RpgLandmarkIsNear =>
            RpgArtDirection(RpgLandmarks[_rpgLocation].Position - _position).sqrMagnitude <= .085f * .085f;

        private void OpenRpgLandmark()
        {
            if (!CanWalk) return;
            _rpgInspecting = true;
            Show(Page.Talk);
        }

        private void BuildRpgLandmarkTalk()
        {
            BeginRpgTalkBackdrop();
            var landmark = RpgLandmarks[_rpgLocation];
            var state = RpgCouplerState;
            var quest = RpgCouplerQuest;
            bool garage = _rpgLocation == RpgGarage, bridge = _rpgLocation == RpgBridge;
            string line = landmark.Description;
            var actions = new List<(string Id, string Label, Action Select)>();
            if (garage && IsRpgQuestOpen(quest, RpgClears))
            {
                switch (state)
                {
                    case RpgQuestState.Available:
                        line = quest.Greeting;
                        actions.Add(("AskCrow", "크로우에게 묻기", () =>
                        {
                            Show(Page.Field);
                            if (_rpgResidents.TryGetValue(RpgCrow, out var crow))
                                MoveTo(crow.anchorMin - new Vector2(0f, .06f), () => OpenRpgQuestTalk(
                                    RpgSideQuest.Find("garage-tools")));
                        }));
                        actions.Add(("ReadRunnerLog", "주행 일지 읽기", () => AdvanceRpgQuest(
                            quest, RpgQuestState.Available, Page.Talk, "수첩에 새 의뢰를 기록했습니다.")));
                        break;
                    case RpgQuestState.Active:
                        line = "주행 일지에 마지막 정차 지점이 적혀 있다. 다이달로스 브리지의 난간 쪽부터 살펴보자.";
                        break;
                    case RpgQuestState.Ready:
                        line = "그 부품 맞아. 이리 줘, 바로 끼워 볼게.";
                        actions.Add(("FitCoupler", "커플러 건네기", () => AdvanceRpgQuest(
                            quest, RpgQuestState.Ready, Page.Talk, null)));
                        break;
                    case RpgQuestState.Complete:
                        line = "새 커플러를 단 러너의 엔진이 일정하게 돌아간다. 작업대 위에는 크로우가 남긴 감사 메모가 있다.";
                        break;
                }
            }
            else if (bridge && state == RpgQuestState.Active)
            {
                line = "난간에 걸린 정비용 끈 끝에서 점화 커플러가 반짝인다.";
                actions.Add(("RecoverCoupler", "부품 회수", () => AdvanceRpgQuest(
                    quest, RpgQuestState.Active, Page.Talk, "점화 커플러 확보 · 정비 작업대로 돌아가세요.")));
            }
            else if (bridge && state >= RpgQuestState.Ready)
                line = "커플러가 걸려 있던 난간에는 정비용 끈 자국만 남아 있다.";

            if (garage) actions.Add(("WorkbenchMenu", "작업대 메뉴", () => { _rpgWorkbenchServices = true; Show(Page.Talk); }));
            actions.Add(("Leave", "돌아가기", () => Show(Page.Field)));
            bool talkingToCrow = garage && state == RpgQuestState.Ready;
            var panel = BuildRpgConversation(talkingToCrow ? "크로우 호건" : landmark.Name, line, out _,
                talkingToCrow ? RpgCrow : (int?)null);
            var first = AddRpgConversationActions(panel, actions);
            if (garage && state == RpgQuestState.Ready && _rpgResidents.TryGetValue(RpgCrow, out _rpgWalkInActor))
            {
                _rpgWalkInFrom = _rpgWalkInActor.anchorMin;
                _rpgWalkInElapsed = 0f;
                _rpgWalkInActor.Find("ResidentMarker")?.gameObject.SetActive(false);
                _rpgWalkInActor.Find("QuestBadge")?.gameObject.SetActive(false);
                if (!panel.TryGetComponent(out _rpgWalkInChoices))
                    _rpgWalkInChoices = panel.gameObject.AddComponent<CanvasGroup>();
                _rpgWalkInChoices.interactable = _rpgWalkInChoices.blocksRaycasts = false;
                _rpgWalkInSelection = first;
            }
        }

        private void UpdateRpgExploration(float delta)
        {
            if (_rpgLandmarkLabel != null)
            {
                var name = RpgLandmarks[_rpgLocation].Name;
                string caption = CanWalk && RpgLandmarkIsNear ? $"조사 · {name}" : name;
                if (_rpgLandmarkLabel.text != caption) _rpgLandmarkLabel.text = caption;
            }
            if (_rpgWalkInActor == null) return;
            _rpgWalkInElapsed += delta;
            float progress = Mathf.Clamp01(_rpgWalkInElapsed / .82f);
            var position = Vector2.Lerp(_rpgWalkInFrom, RpgQuestCrowBench, Mathf.SmoothStep(0f, 1f, progress));
            PoseRpgWalker(_rpgWalkInActor, _rpgWalkInActor.Find("SD") as RectTransform, position,
                position - _rpgWalkInActor.anchorMin, progress < 1f, progress >= 1f ? _position - position : Vector2.zero);
            TrackRpgCamera(delta, Vector2.Lerp(_position, position, .32f));
            if (progress < 1f) return;
            _rpgWalkInChoices.interactable = _rpgWalkInChoices.blocksRaycasts = true;
            EventSystem.current?.SetSelectedGameObject(_rpgWalkInSelection.gameObject);
            _rpgWalkInActor = null;
        }

        private void ClearRpgExploration()
        {
            _rpgExits.Clear();
            _rpgFieldObjects = _rpgWalkInActor = null;
            _rpgLandmarkLabel = null;
            _rpgWalkInChoices = null;
            _rpgWalkInSelection = null;
        }
    }
}
