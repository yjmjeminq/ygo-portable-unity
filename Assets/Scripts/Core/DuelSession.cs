using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace YGOPortable.Core
{
    public class LiveCardState
    {
        public uint code;
        public uint alias;
        public byte controller;
        public uint location;
        public uint sequence;
        public uint position;
        public uint type;
        public int attack;
        public int defense;
        public int baseAttack;
        public int baseDefense;
        public uint level;
        public uint rank;
        public uint attribute;
        public ulong race;
        public uint lscale;
        public uint rscale;
        public uint link;
        public uint linkMarker;
        public byte owner;
        public bool isPublic;
        public List<uint> overlayCodes = new List<uint>();

        public bool IsFaceUp => (position & YgoConstants.POS_FACEUP) != 0;
        public bool IsDefense => (position & YgoConstants.POS_DEFENSE) != 0;
        public bool IsAttack => (position & YgoConstants.POS_ATTACK) != 0;
    }

    public class CardMoveEvent
    {
        public uint code;
        public byte fromCon;
        public uint fromLoc;
        public uint fromSeq;
        public uint fromPos;
        public byte toCon;
        public uint toLoc;
        public uint toSeq;
        public uint toPos;
        public uint reason;
    }

    public class ChainLinkInfo
    {
        public int chainNumber;
        public uint code;
        public byte triggerPlayer;
        public uint triggerLoc;
        public uint triggerSeq;
        public ulong description;
        public bool negated;
    }

    public class SelectableCardItem
    {
        public uint code;
        public byte controller;
        public uint location;
        public uint sequence;
        public uint position;
        public ulong description;
        public byte mode;
        public bool directAttackable;
        public byte releaseParam;
        public uint sumParam;
        public uint currentCounter;
    }

    public class DuelPrompt
    {
        public byte messageType;
        public byte playerId;

        // IdleCmd & BattleCmd
        public List<SelectableCardItem> summonable = new List<SelectableCardItem>();
        public List<SelectableCardItem> spSummonable = new List<SelectableCardItem>();
        public List<SelectableCardItem> repositionable = new List<SelectableCardItem>();
        public List<SelectableCardItem> mSetable = new List<SelectableCardItem>();
        public List<SelectableCardItem> sSetable = new List<SelectableCardItem>();
        public List<SelectableCardItem> activatable = new List<SelectableCardItem>();
        public List<SelectableCardItem> attackable = new List<SelectableCardItem>();
        public bool canBp, canEp, canM2, canShuffle;

        // YesNo / EffectYN
        public uint promptCardCode;
        public byte promptCon;
        public uint promptLoc;
        public uint promptSeq;
        public ulong promptDesc;

        // SelectOption / AnnounceNumber / AnnounceCard
        public List<ulong> options = new List<ulong>();

        // SelectCard / Unselect / Tribute / Sum / Sort / Counter
        public bool cancelable;
        public bool finishable;
        public bool forced;
        public byte speCount;
        public bool IsTriggerChain => speCount == 0x7f;
        public bool isChainSort;
        public bool sumExactCountZeroAny;
        public uint min;
        public uint max;
        public uint acc;
        public ushort counterType;
        public uint placeFlag;
        public byte allowedPositions;
        public ulong announceMask;
        public List<SelectableCardItem> mustSelect = new List<SelectableCardItem>();
        public List<SelectableCardItem> candidates = new List<SelectableCardItem>();
        public List<SelectableCardItem> unselectCandidates = new List<SelectableCardItem>();
    }

    public class PlayerFieldSnapshot
    {
        public int lp = 8000;
        public LiveCardState[] mzone = new LiveCardState[7]; // 0..4 main, 5 EMZ-L, 6 EMZ-R
        public LiveCardState[] szone = new LiveCardState[8]; // 0..4 s/t, 5 field, 6 pL, 7 pR
        public List<LiveCardState> hand = new List<LiveCardState>();
        public List<LiveCardState> grave = new List<LiveCardState>();
        public List<LiveCardState> banished = new List<LiveCardState>();
        public List<LiveCardState> extra = new List<LiveCardState>();
        public int deckCount;
    }

    public class DuelSession : IDisposable
    {
        public IntPtr NativePtr { get; private set; }
        public int TurnNumber { get; private set; }
        public int TurnPlayer { get; private set; }
        public int CurrentPhase { get; private set; }
        public uint FieldDisabledMask { get; private set; }
        public bool IsFinished { get; private set; }
        public int Winner { get; private set; } = -1; // 0=P0, 1=P1, 2=Draw
        public byte WinReason { get; private set; }
        public int RetryCount { get; private set; }
        public ulong LastHintMsg { get; private set; }
        public uint AttackerCon, AttackerLoc, AttackerSeq, TargetCon, TargetLoc, TargetSeq;
        public bool HasActiveAttack { get; private set; }

        public PlayerFieldSnapshot[] Players { get; } = { new PlayerFieldSnapshot(), new PlayerFieldSnapshot() };
        public List<ChainLinkInfo> CurrentChain { get; } = new List<ChainLinkInfo>();
        internal ChainLinkInfo SelectionChainLink => _resolvingChainLink ?? (CurrentChain.Count == 1 ? CurrentChain[0] : null);
        public List<(int chainNumber, uint code)> SolvedChainLinks { get; } = new List<(int, uint)>();
        public List<string> Logs { get; } = new List<string>();
        public DuelPrompt ActivePrompt { get; private set; }
        private ChainLinkInfo _resolvingChainLink;
        private readonly HashSet<(byte Player, uint Location, uint Sequence, uint Code)> _confirmedCards =
            new HashSet<(byte, uint, uint, uint)>();

        public event Action<string> OnLogAdded;

        public int HumanPlayerIndex { get; private set; } = 0;
        public int AiPlayerIndex => 1 - HumanPlayerIndex;

        public void PreparePlayerIndices(bool p0GoesFirst)
        {
            HumanPlayerIndex = p0GoesFirst ? 0 : 1;
        }

        public event Action<CardMoveEvent> OnCardMoved;
        public event Action<byte, IReadOnlyList<uint>> OnCardsDrawn;
        public event Action<byte, int, int> OnLifePointsChanged;
        public event Action<int, int> OnTurnStarted;
        public event Action<int, int> OnPhaseChanged;
        public event Action<ChainLinkInfo> OnChainActivated;
        public event Action<ChainLinkInfo> OnChainResolving;
        public event Action<byte, uint, uint, byte, uint, uint> OnAttackDeclared;
        public event Action<byte, uint, uint, byte, uint, uint> OnBattleResolved;
        public event Action<byte, IReadOnlyList<bool>> OnCoinTossed;
        public event Action<byte, IReadOnlyList<int>> OnDiceRolled;

        public bool StartNewDuel(
            IReadOnlyList<uint> p0Main, IReadOnlyList<uint> p0Extra,
            IReadOnlyList<uint> p1Main, IReadOnlyList<uint> p1Extra,
            bool p0GoesFirst = true, int startingLp = 8000, ulong[] seed = null)
        {
            Dispose();
            IsFinished = false;
            Winner = -1;
            TurnNumber = 0;
            CurrentPhase = YgoConstants.PHASE_DRAW;
            RetryCount = 0;
            LastHintMsg = 0;
            ActivePrompt = null;
            CurrentChain.Clear();
            _resolvingChainLink = null;
            SolvedChainLinks.Clear();
            Logs.Clear();
            _confirmedCards.Clear();

            HumanPlayerIndex = p0GoesFirst ? 0 : 1;

            if (seed == null || seed.Length < 4)
            {
                seed = new ulong[4];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    byte[] bytes = new byte[32];
                    rng.GetBytes(bytes);
                    Buffer.BlockCopy(bytes, 0, seed, 0, 32);
                }
            }

            NativePtr = YgoNativeBridge.CreateDuel(
                seed, YgoConstants.DUEL_MODE_MR5,
                startingLp, 5, 1,
                startingLp, 5, 1);

            if (NativePtr == IntPtr.Zero) return false;
            if (YgoNativeBridge.YGO_LoadBaseScripts(NativePtr) == 0)
            {
                Debug.LogError("[DuelSession] Failed to load base scripts.");
                Dispose();
                return false;
            }

            Players[0].lp = startingLp;
            Players[1].lp = startingLp;

            var turn0Main = p0GoesFirst ? p0Main : p1Main;
            var turn0Extra = p0GoesFirst ? p0Extra : p1Extra;
            var turn1Main = p0GoesFirst ? p1Main : p0Main;
            var turn1Extra = p0GoesFirst ? p1Extra : p0Extra;

            // Reuse the duel seed for deck order so a reported match can be replayed.
            uint shuffleSeed = 2166136261;
            for (int i = 0; i < 4; i++)
            {
                shuffleSeed = unchecked((shuffleSeed ^ (uint)seed[i]) * 16777619);
                shuffleSeed = unchecked((shuffleSeed ^ (uint)(seed[i] >> 32)) * 16777619);
            }
            var shuffle = new System.Random(unchecked((int)shuffleSeed));
            LoadDeckForPlayer(0, turn0Main, turn0Extra, shuffle);
            LoadDeckForPlayer(1, turn1Main, turn1Extra, shuffle);

            YgoNativeBridge.YGO_StartDuel(NativePtr);
            AddLog($"듀얼 개시! ({(p0GoesFirst ? "플레이어 선공" : "AI 선공")}) — 덱 무작위 셔플 완료");
            StepUntilPrompt();
            return true;
        }

        private void LoadDeckForPlayer(byte playerId, IReadOnlyList<uint> main, IReadOnlyList<uint> extra, System.Random shuffle)
        {
            if (main != null && main.Count > 0)
            {
                var shuffled = new List<uint>(main);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = shuffle.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }
                for (int i = shuffled.Count - 1; i >= 0; i--)
                {
                    YgoNativeBridge.YGO_AddCard(NativePtr, playerId, 0, shuffled[i],
                        playerId, YgoConstants.LOCATION_DECK, 0, YgoConstants.POS_FACEDOWN_DEFENSE);
                }
            }
            if (extra != null)
            {
                for (int i = 0; i < extra.Count; i++)
                {
                    YgoNativeBridge.YGO_AddCard(NativePtr, playerId, 0, extra[i],
                        playerId, YgoConstants.LOCATION_EXTRA, 0, YgoConstants.POS_FACEDOWN_DEFENSE);
                }
            }
        }

        public void SubmitResponse(byte[] buffer)
        {
            if (NativePtr == IntPtr.Zero || IsFinished) return;
            var prevPrompt = ActivePrompt;
            int prevRetry = RetryCount;
            ActivePrompt = null;
            YgoNativeBridge.SetResponse(NativePtr, buffer);
            StepUntilPrompt();
            if (!IsFinished && ActivePrompt == null && RetryCount > prevRetry && prevPrompt != null)
            {
                ActivePrompt = prevPrompt;
            }
        }

        public void SubmitResponseInt(int val)
        {
            SubmitResponse(BitConverter.GetBytes(val));
        }

        public void Surrender(int losingPlayer)
        {
            IsFinished = true;
            Winner = 1 - losingPlayer;
            WinReason = 0;
            ActivePrompt = null;
            AddLog(losingPlayer == HumanPlayerIndex ? "플레이어가 항복했습니다." : "상대가 항복했습니다.");
        }

        public void StepUntilPrompt()
        {
            if (NativePtr == IntPtr.Zero || IsFinished) return;

            int guard = 0;
            while (!IsFinished && guard < 256)
            {
                guard++;
                int status = YgoNativeBridge.YGO_Process(NativePtr);
                byte[] msgBytes = YgoNativeBridge.GetMessageBuffer(NativePtr);
                if (msgBytes.Length > 0)
                {
                    ParseMessages(msgBytes);
                }

                if (status == YgoConstants.OCG_DUEL_STATUS_END)
                {
                    IsFinished = true;
                    break;
                }
                if (status == YgoConstants.OCG_DUEL_STATUS_AWAITING)
                {
                    var prompt = ActivePrompt;
                    if (prompt == null || prompt.messageType != YgoConstants.MSG_SELECT_CHAIN ||
                        prompt.forced || prompt.candidates.Count != 0)
                        break;

                    // The core requests a pass even when no optional chain effects are available.
                    ActivePrompt = null;
                    YgoNativeBridge.SetResponseInt(NativePtr, -1);
                }
            }

            RefreshZonesFromCore();
        }

        private void ParseMessages(byte[] buf)
        {
            using (var ms = new MemoryStream(buf))
            using (var br = new BinaryReader(ms))
            {
                while (ms.Position + 4 <= ms.Length)
                {
                    uint sz = br.ReadUInt32();
                    if (sz == 0 || ms.Position + sz > ms.Length) break;
                    long msgEnd = ms.Position + sz;
                    byte msgType = br.ReadByte();
                    HandleMessage(msgType, br, msgEnd);
                    ms.Position = msgEnd;
                }
            }
        }

        private static SelectableCardItem ReadLocIntoItem(BinaryReader br, uint code)
        {
            return new SelectableCardItem
            {
                code = code,
                controller = br.ReadByte(),
                location = br.ReadByte(),
                sequence = br.ReadUInt32(),
                position = br.ReadUInt32(),
            };
        }

        internal uint GetVisibleSelectionCode(SelectableCardItem item)
        {
            if (item.code == 0 || item.controller == HumanPlayerIndex || item.location == 0 ||
                item.location == YgoConstants.LOCATION_GRAVE || (item.location & YgoConstants.LOCATION_OVERLAY) != 0 ||
                _confirmedCards.Contains((item.controller, item.location, item.sequence, item.code)))
                return item.code;

            if (item.controller >= Players.Length) return 0;
            var player = Players[item.controller];
            IReadOnlyList<LiveCardState> cards = item.location switch
            {
                YgoConstants.LOCATION_HAND => player.hand,
                YgoConstants.LOCATION_MZONE => player.mzone,
                YgoConstants.LOCATION_SZONE => player.szone,
                YgoConstants.LOCATION_EXTRA => player.extra,
                YgoConstants.LOCATION_REMOVED => player.banished,
                _ => null,
            };
            var card = cards != null && item.sequence < cards.Count ? cards[(int)item.sequence] : null;
            if (card?.isPublic == true) return item.code;
            if (item.location == YgoConstants.LOCATION_HAND) return 0;
            uint position = item.position != 0 ? item.position : card?.position ?? 0;
            return (position & YgoConstants.POS_FACEUP) != 0 ? item.code : 0;
        }

        private void NotifySwapMove(SelectableCardItem card, SelectableCardItem destination)
        {
            OnCardMoved?.Invoke(new CardMoveEvent
            {
                code = card.code,
                fromCon = card.controller, fromLoc = card.location, fromSeq = card.sequence, fromPos = card.position,
                toCon = destination.controller, toLoc = destination.location, toSeq = destination.sequence, toPos = card.position,
            });
        }

        private void HandleMessage(byte msgType, BinaryReader br, long msgEnd)
        {
            switch (msgType)
            {
                case YgoConstants.MSG_RETRY:
                    RetryCount++;
                    AddLog("[경고] 엔진이 응답 재요청(MSG_RETRY)을 반환했습니다.");
                    break;

                case YgoConstants.MSG_HINT:
                {
                    byte htype = br.ReadByte();
                    byte player = br.ReadByte();
                    ulong desc = br.ReadUInt64();
                    if (htype == YgoConstants.HINT_SELECTMSG)
                    {
                        LastHintMsg = desc;
                    }
                    else if (htype == YgoConstants.HINT_MESSAGE)
                    {
                        string message = desc > 499 ? CardDatabase.ResolveDescription(desc) : null;
                        if (message != null) AddLog(message);
                    }
                    break;
                }

                case YgoConstants.MSG_WIN:
                {
                    byte winp = br.ReadByte();
                    byte reason = br.ReadByte();
                    IsFinished = true;
                    Winner = winp <= 1 ? winp : 2;
                    WinReason = reason;
                    ActivePrompt = null;
                    string who = Winner == 2 ? "무승부" : (Winner == HumanPlayerIndex ? "플레이어 승리!" : "AI 승리!");
                    AddLog($"듀얼 종료: {who}");
                    break;
                }

                case YgoConstants.MSG_NEW_TURN:
                {
                    _confirmedCards.Clear();
                    TurnPlayer = br.ReadByte();
                    TurnNumber++;
                    HasActiveAttack = false;
                    string who = TurnPlayer == HumanPlayerIndex ? "내 턴" : "상대 턴";
                    AddLog($"=== 턴 {TurnNumber}: {who} ===");
                    OnTurnStarted?.Invoke(TurnNumber, TurnPlayer);
                    break;
                }

                case YgoConstants.MSG_NEW_PHASE:
                {
                    CurrentPhase = br.ReadUInt16();
                    HasActiveAttack = false;
                    AddLog($"[{YgoConstants.GetPhaseNameKR(CurrentPhase)}]");
                    OnPhaseChanged?.Invoke(CurrentPhase, TurnPlayer);
                    break;
                }

                case YgoConstants.MSG_DRAW:
                {
                    byte p = br.ReadByte();
                    if (_confirmedCards.Count != 0)
                        _confirmedCards.RemoveWhere(c => c.Player == p &&
                            (c.Location == YgoConstants.LOCATION_HAND || c.Location == YgoConstants.LOCATION_DECK));
                    uint cnt = br.ReadUInt32();
                    string who = p == HumanPlayerIndex ? "플레이어" : "상대";
                    AddLog($"{who} 드로우 {cnt}장");
                    if (OnCardsDrawn != null && cnt <= (msgEnd - br.BaseStream.Position) / 8)
                    {
                        var codes = new uint[cnt];
                        for (int i = 0; i < codes.Length; i++)
                        {
                            uint code = br.ReadUInt32();
                            uint position = br.ReadUInt32();
                            if (p == HumanPlayerIndex || (position & YgoConstants.POS_FACEUP) != 0)
                                codes[i] = code;
                        }
                        OnCardsDrawn.Invoke(p, codes);
                    }
                    break;
                }

                case YgoConstants.MSG_DAMAGE:
                {
                    byte p = br.ReadByte();
                    uint amt = br.ReadUInt32();
                    Players[p].lp = Math.Max(0, Players[p].lp - (int)amt);
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} 데미지 -{amt} LP (남은 LP: {Players[p].lp})");
                    OnLifePointsChanged?.Invoke(p, -(int)amt, Players[p].lp);
                    break;
                }

                case YgoConstants.MSG_RECOVER:
                {
                    byte p = br.ReadByte();
                    uint amt = br.ReadUInt32();
                    Players[p].lp += (int)amt;
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} 회복 +{amt} LP (LP: {Players[p].lp})");
                    OnLifePointsChanged?.Invoke(p, (int)amt, Players[p].lp);
                    break;
                }

                case YgoConstants.MSG_PAY_LPCOST:
                {
                    byte p = br.ReadByte();
                    uint amt = br.ReadUInt32();
                    Players[p].lp = Math.Max(0, Players[p].lp - (int)amt);
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} LP 코스트 -{amt} (LP: {Players[p].lp})");
                    OnLifePointsChanged?.Invoke(p, -(int)amt, Players[p].lp);
                    break;
                }

                case YgoConstants.MSG_LPUPDATE:
                {
                    byte p = br.ReadByte();
                    int nlp = br.ReadInt32();
                    int previous = Players[p].lp;
                    Players[p].lp = Math.Max(0, nlp);
                    if (Players[p].lp != previous)
                        OnLifePointsChanged?.Invoke(p, Players[p].lp - previous, Players[p].lp);
                    break;
                }

                case YgoConstants.MSG_SUMMONING:
                {
                    uint code = br.ReadUInt32();
                    br.ReadBytes(10);
                    AddLog($"일반 소환: [{CardDatabase.GetName(code)}]");
                    break;
                }

                case YgoConstants.MSG_SPSUMMONING:
                {
                    uint code = br.ReadUInt32();
                    br.ReadBytes(10);
                    AddLog(code != 0 ? $"특수 소환: [{CardDatabase.GetName(code)}]" : "뒷면 표시 특수 소환");
                    break;
                }

                case YgoConstants.MSG_FLIPSUMMONING:
                {
                    uint code = br.ReadUInt32();
                    byte controller = br.ReadByte();
                    byte location = br.ReadByte();
                    uint sequence = br.ReadUInt32();
                    uint position = br.ReadUInt32();
                    uint previous = sequence < Players[controller].mzone.Length
                        ? Players[controller].mzone[sequence]?.position ?? YgoConstants.POS_FACEDOWN_DEFENSE
                        : YgoConstants.POS_FACEDOWN_DEFENSE;
                    AddLog($"반전 소환: [{CardDatabase.GetName(code)}]");
                    OnCardMoved?.Invoke(new CardMoveEvent
                    {
                        code = code,
                        fromCon = controller, fromLoc = location, fromSeq = sequence, fromPos = previous,
                        toCon = controller, toLoc = location, toSeq = sequence, toPos = position,
                    });
                    break;
                }

                case YgoConstants.MSG_SET:
                {
                    uint code = br.ReadUInt32();
                    byte con = br.ReadByte();
                    byte loc = br.ReadByte();
                    AddLog($"{(con == HumanPlayerIndex ? "플레이어" : "상대")} {(loc == YgoConstants.LOCATION_MZONE ? "몬스터" : "마법/함정")} 세트");
                    break;
                }

                case YgoConstants.MSG_MOVE:
                {
                    uint code = br.ReadUInt32();
                    byte fCon = br.ReadByte();
                    byte fLoc = br.ReadByte();
                    uint fSeq = br.ReadUInt32();
                    uint fPos = br.ReadUInt32();
                    byte tCon = br.ReadByte();
                    byte tLoc = br.ReadByte();
                    uint tSeq = br.ReadUInt32();
                    uint tPos = br.ReadUInt32();
                    uint reason = (br.BaseStream.Position < msgEnd) ? br.ReadUInt32() : 0;
                    _confirmedCards.Remove((fCon, fLoc, fSeq, code));

                    var ev = new CardMoveEvent
                    {
                        code = code,
                        fromCon = fCon, fromLoc = fLoc, fromSeq = fSeq, fromPos = fPos,
                        toCon = tCon, toLoc = tLoc, toSeq = tSeq, toPos = tPos,
                        reason = reason,
                    };
                    OnCardMoved?.Invoke(ev);
                    break;
                }

                case YgoConstants.MSG_SWAP:
                {
                    var first = ReadLocIntoItem(br, br.ReadUInt32());
                    var second = ReadLocIntoItem(br, br.ReadUInt32());
                    _confirmedCards.Remove((first.controller, first.location, first.sequence, first.code));
                    _confirmedCards.Remove((second.controller, second.location, second.sequence, second.code));
                    NotifySwapMove(first, second);
                    NotifySwapMove(second, first);
                    break;
                }

                case YgoConstants.MSG_POS_CHANGE:
                {
                    uint code = br.ReadUInt32();
                    byte controller = br.ReadByte();
                    byte location = br.ReadByte();
                    uint sequence = br.ReadByte();
                    uint previous = br.ReadByte();
                    uint position = br.ReadByte();
                    OnCardMoved?.Invoke(new CardMoveEvent
                    {
                        code = code,
                        fromCon = controller, fromLoc = location, fromSeq = sequence, fromPos = previous,
                        toCon = controller, toLoc = location, toSeq = sequence, toPos = position,
                    });
                    break;
                }

                case YgoConstants.MSG_CONFIRM_CARDS:
                case YgoConstants.MSG_CONFIRM_DECKTOP:
                case YgoConstants.MSG_CONFIRM_EXTRATOP:
                {
                    byte recipient = br.ReadByte();
                    bool shownToHuman = msgType != YgoConstants.MSG_CONFIRM_CARDS || recipient == HumanPlayerIndex;
                    uint cnt = br.ReadUInt32();
                    var names = new List<string>((int)Math.Min(cnt, 4));
                    int visibleCount = 0;
                    for (uint i = 0; i < cnt; i++)
                    {
                        uint code = br.ReadUInt32();
                        byte controller = br.ReadByte();
                        uint location = br.ReadByte();
                        uint sequence = br.ReadUInt32();
                        if (code == 0 || (!shownToHuman && controller != HumanPlayerIndex)) continue;
                        if (shownToHuman && controller != HumanPlayerIndex)
                            _confirmedCards.Add((controller, location, sequence, code));
                        visibleCount++;
                        if (names.Count < 4)
                            names.Add($"[{CardDatabase.GetName(code)}]");
                    }
                    if (names.Count > 0)
                    {
                        string prefix = msgType switch
                        {
                            YgoConstants.MSG_CONFIRM_DECKTOP => "덱 위 확인",
                            YgoConstants.MSG_CONFIRM_EXTRATOP => "엑스트라 덱 확인",
                            _ => "카드 확인",
                        };
                        string suffix = visibleCount > names.Count ? $" 외 {visibleCount - names.Count}장" : "";
                        AddLog($"{prefix}: {string.Join(", ", names)}{suffix}");
                    }
                    break;
                }

                case YgoConstants.MSG_SHUFFLE_HAND:
                case YgoConstants.MSG_SHUFFLE_DECK:
                case YgoConstants.MSG_SHUFFLE_EXTRA:
                {
                    byte player = br.ReadByte();
                    uint location = msgType == YgoConstants.MSG_SHUFFLE_HAND ? YgoConstants.LOCATION_HAND
                        : msgType == YgoConstants.MSG_SHUFFLE_DECK ? YgoConstants.LOCATION_DECK : YgoConstants.LOCATION_EXTRA;
                    if (_confirmedCards.Count != 0)
                        _confirmedCards.RemoveWhere(c => c.Player == player && c.Location == location);
                    break;
                }

                case YgoConstants.MSG_SHUFFLE_SET_CARD:
                {
                    uint location = br.ReadByte();
                    if (_confirmedCards.Count != 0)
                        _confirmedCards.RemoveWhere(c => c.Location == location);
                    break;
                }

                case YgoConstants.MSG_CHAINING:
                {
                    uint code = br.ReadUInt32();
                    br.ReadBytes(10);
                    byte tCon = br.ReadByte();
                    byte tLoc = br.ReadByte();
                    uint tSeq = br.ReadUInt32();
                    ulong desc = br.ReadUInt64();
                    int chainNo = (int)br.ReadUInt32();
                    var link = new ChainLinkInfo
                    {
                        chainNumber = chainNo,
                        code = code,
                        triggerPlayer = tCon,
                        triggerLoc = tLoc,
                        triggerSeq = tSeq,
                        description = desc,
                    };
                    CurrentChain.Add(link);
                    AddLog($"체인 {chainNo}: [{CardDatabase.GetName(code)}] 발동");
                    OnChainActivated?.Invoke(link);
                    break;
                }

                case YgoConstants.MSG_CHAIN_SOLVING:
                {
                    byte idx = br.ReadByte();
                    _resolvingChainLink = null;
                    if (idx > 0 && idx <= CurrentChain.Count)
                    {
                        var link = CurrentChain[idx - 1];
                        _resolvingChainLink = link;
                        SolvedChainLinks.Add((idx, link.code));
                        AddLog($"체인 {idx} 처리: [{CardDatabase.GetName(link.code)}]");
                        OnChainResolving?.Invoke(link);
                    }
                    break;
                }

                case YgoConstants.MSG_CHAIN_SOLVED:
                    _resolvingChainLink = null;
                    break;

                case YgoConstants.MSG_CHAIN_NEGATED:
                case YgoConstants.MSG_CHAIN_DISABLED:
                {
                    byte idx = br.ReadByte();
                    if (idx > 0 && idx <= CurrentChain.Count)
                        CurrentChain[idx - 1].negated = true;
                    AddLog($"체인 {idx} 무효화");
                    break;
                }

                case YgoConstants.MSG_CHAIN_END:
                    CurrentChain.Clear();
                    _resolvingChainLink = null;
                    _confirmedCards.Clear();
                    break;

                case YgoConstants.MSG_ATTACK:
                {
                    AttackerCon = br.ReadByte();
                    AttackerLoc = br.ReadByte();
                    AttackerSeq = br.ReadUInt32();
                    br.ReadUInt32();
                    TargetCon = br.ReadByte();
                    TargetLoc = br.ReadByte();
                    TargetSeq = br.ReadUInt32();
                    br.ReadUInt32();
                    HasActiveAttack = true;
                    AddLog(TargetLoc == 0 ? "직접 공격 선언!" : "몬스터 공격 선언!");
                    OnAttackDeclared?.Invoke((byte)AttackerCon, AttackerLoc, AttackerSeq, (byte)TargetCon, TargetLoc, TargetSeq);
                    break;
                }

                case YgoConstants.MSG_BATTLE:
                {
                    if (OnBattleResolved == null) break;
                    byte attacker = br.ReadByte();
                    uint attackLocation = br.ReadByte();
                    uint attackSequence = br.ReadUInt32();
                    br.ReadBytes(13); // position, attack, defense, destroyed flag
                    byte target = br.ReadByte();
                    uint targetLocation = br.ReadByte();
                    uint targetSequence = br.ReadUInt32();
                    OnBattleResolved.Invoke(attacker, attackLocation, attackSequence, target, targetLocation, targetSequence);
                    break;
                }

                case YgoConstants.MSG_ATTACK_DISABLED:
                case YgoConstants.MSG_DAMAGE_STEP_END:
                    HasActiveAttack = false;
                    break;

                case YgoConstants.MSG_FIELD_DISABLED:
                    FieldDisabledMask = br.ReadUInt32();
                    break;

                case YgoConstants.MSG_SELECT_BATTLECMD:
                {
                    var p = new DuelPrompt { messageType = msgType, playerId = br.ReadByte() };
                    uint actCnt = br.ReadUInt32();
                    for (uint i = 0; i < actCnt; i++)
                    {
                        p.activatable.Add(new SelectableCardItem
                        {
                            code = br.ReadUInt32(),
                            controller = br.ReadByte(),
                            location = br.ReadByte(),
                            sequence = br.ReadUInt32(),
                            description = br.ReadUInt64(),
                            mode = br.ReadByte(),
                        });
                    }
                    uint atkCnt = br.ReadUInt32();
                    for (uint i = 0; i < atkCnt; i++)
                    {
                        p.attackable.Add(new SelectableCardItem
                        {
                            code = br.ReadUInt32(),
                            controller = br.ReadByte(),
                            location = br.ReadByte(),
                            sequence = br.ReadByte(), // Note: playerop.cpp writes uint8 here!
                            directAttackable = br.ReadByte() != 0,
                        });
                    }
                    p.canM2 = br.ReadByte() != 0;
                    p.canEp = br.ReadByte() != 0;
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_IDLECMD:
                {
                    var p = new DuelPrompt { messageType = msgType, playerId = br.ReadByte() };
                    uint sumCnt = br.ReadUInt32();
                    for (uint i = 0; i < sumCnt; i++)
                        p.summonable.Add(new SelectableCardItem { code = br.ReadUInt32(), controller = br.ReadByte(), location = br.ReadByte(), sequence = br.ReadUInt32() });
                    uint spCnt = br.ReadUInt32();
                    for (uint i = 0; i < spCnt; i++)
                        p.spSummonable.Add(new SelectableCardItem { code = br.ReadUInt32(), controller = br.ReadByte(), location = br.ReadByte(), sequence = br.ReadUInt32() });
                    uint posCnt = br.ReadUInt32();
                    for (uint i = 0; i < posCnt; i++)
                        p.repositionable.Add(new SelectableCardItem { code = br.ReadUInt32(), controller = br.ReadByte(), location = br.ReadByte(), sequence = br.ReadByte() }); // uint8 in playerop.cpp!
                    uint msetCnt = br.ReadUInt32();
                    for (uint i = 0; i < msetCnt; i++)
                        p.mSetable.Add(new SelectableCardItem { code = br.ReadUInt32(), controller = br.ReadByte(), location = br.ReadByte(), sequence = br.ReadUInt32() });
                    uint ssetCnt = br.ReadUInt32();
                    for (uint i = 0; i < ssetCnt; i++)
                        p.sSetable.Add(new SelectableCardItem { code = br.ReadUInt32(), controller = br.ReadByte(), location = br.ReadByte(), sequence = br.ReadUInt32() });
                    uint actCnt = br.ReadUInt32();
                    for (uint i = 0; i < actCnt; i++)
                        p.activatable.Add(new SelectableCardItem { code = br.ReadUInt32(), controller = br.ReadByte(), location = br.ReadByte(), sequence = br.ReadUInt32(), description = br.ReadUInt64(), mode = br.ReadByte() });
                    p.canBp = br.ReadByte() != 0;
                    p.canEp = br.ReadByte() != 0;
                    p.canShuffle = br.ReadByte() != 0;
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_EFFECTYN:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        promptCardCode = br.ReadUInt32(),
                        promptCon = br.ReadByte(),
                        promptLoc = br.ReadByte(),
                        promptSeq = br.ReadUInt32(),
                    };
                    br.ReadUInt32(); // pos
                    p.promptDesc = br.ReadUInt64();
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_YESNO:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        promptDesc = br.ReadUInt64(),
                    };
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_OPTION:
                case YgoConstants.MSG_ANNOUNCE_CARD:
                case YgoConstants.MSG_ANNOUNCE_NUMBER:
                {
                    var p = new DuelPrompt { messageType = msgType, playerId = br.ReadByte() };
                    byte cnt = br.ReadByte();
                    for (int i = 0; i < cnt; i++) p.options.Add(br.ReadUInt64());
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_CARD:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        cancelable = br.ReadByte() != 0,
                        min = br.ReadUInt32(),
                        max = br.ReadUInt32(),
                    };
                    uint cnt = br.ReadUInt32();
                    for (uint i = 0; i < cnt; i++)
                        p.candidates.Add(ReadLocIntoItem(br, br.ReadUInt32()));
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_UNSELECT_CARD:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        finishable = br.ReadByte() != 0,
                        cancelable = br.ReadByte() != 0,
                        min = br.ReadUInt32(),
                        max = br.ReadUInt32(),
                    };
                    uint selCnt = br.ReadUInt32();
                    for (uint i = 0; i < selCnt; i++)
                        p.candidates.Add(ReadLocIntoItem(br, br.ReadUInt32()));
                    uint unselCnt = br.ReadUInt32();
                    for (uint i = 0; i < unselCnt; i++)
                        p.unselectCandidates.Add(ReadLocIntoItem(br, br.ReadUInt32()));
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_CHAIN:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                    };
                    p.speCount = br.ReadByte(); // 0x7f when SEGOC trigger chain (mandatory/optional triggers)
                    p.forced = br.ReadByte() != 0;
                    br.ReadUInt32(); // hint_timing self
                    br.ReadUInt32(); // hint_timing opp
                    uint cnt = br.ReadUInt32();
                    for (uint i = 0; i < cnt; i++)
                    {
                        var item = ReadLocIntoItem(br, br.ReadUInt32());
                        item.description = br.ReadUInt64();
                        item.mode = br.ReadByte();
                        p.candidates.Add(item);
                    }
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_PLACE:
                case YgoConstants.MSG_SELECT_DISFIELD:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        min = br.ReadByte(),
                        placeFlag = br.ReadUInt32(),
                    };
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_POSITION:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        promptCardCode = br.ReadUInt32(),
                        allowedPositions = br.ReadByte(),
                    };
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_TRIBUTE:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        cancelable = br.ReadByte() != 0,
                        min = br.ReadUInt32(),
                        max = br.ReadUInt32(),
                    };
                    uint cnt = br.ReadUInt32();
                    for (uint i = 0; i < cnt; i++)
                    {
                        p.candidates.Add(new SelectableCardItem
                        {
                            code = br.ReadUInt32(),
                            controller = br.ReadByte(),
                            location = br.ReadByte(),
                            sequence = br.ReadUInt32(),
                            releaseParam = br.ReadByte(),
                        });
                    }
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_COUNTER:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        counterType = br.ReadUInt16(),
                        min = br.ReadUInt16(),
                    };
                    uint cnt = br.ReadUInt32();
                    for (uint i = 0; i < cnt; i++)
                    {
                        p.candidates.Add(new SelectableCardItem
                        {
                            code = br.ReadUInt32(),
                            controller = br.ReadByte(),
                            location = br.ReadByte(),
                            sequence = br.ReadByte(),
                            currentCounter = br.ReadUInt16(),
                        });
                    }
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SELECT_SUM:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        sumExactCountZeroAny = br.ReadByte() != 0,
                        acc = br.ReadUInt32(),
                        min = br.ReadUInt32(),
                        max = br.ReadUInt32(),
                    };
                    uint mCnt = br.ReadUInt32();
                    for (uint i = 0; i < mCnt; i++)
                    {
                        var item = ReadLocIntoItem(br, br.ReadUInt32());
                        item.sumParam = br.ReadUInt32();
                        p.mustSelect.Add(item);
                    }
                    uint cCnt = br.ReadUInt32();
                    for (uint i = 0; i < cCnt; i++)
                    {
                        var item = ReadLocIntoItem(br, br.ReadUInt32());
                        item.sumParam = br.ReadUInt32();
                        p.candidates.Add(item);
                    }
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_SORT_CHAIN:
                case YgoConstants.MSG_SORT_CARD:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        isChainSort = msgType == YgoConstants.MSG_SORT_CHAIN,
                    };
                    uint cnt = br.ReadUInt32();
                    for (uint i = 0; i < cnt; i++)
                    {
                        p.candidates.Add(new SelectableCardItem
                        {
                            code = br.ReadUInt32(),
                            controller = br.ReadByte(),
                            location = br.ReadUInt32(),
                            sequence = br.ReadUInt32(),
                        });
                    }
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_ANNOUNCE_RACE:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        min = br.ReadByte(),
                        announceMask = br.ReadUInt64(),
                    };
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_ANNOUNCE_ATTRIB:
                {
                    var p = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                        min = br.ReadByte(),
                        announceMask = br.ReadUInt32(),
                    };
                    ActivePrompt = p;
                    break;
                }

                case YgoConstants.MSG_ROCK_PAPER_SCISSORS:
                {
                    ActivePrompt = new DuelPrompt
                    {
                        messageType = msgType,
                        playerId = br.ReadByte(),
                    };
                    break;
                }

                case YgoConstants.MSG_HAND_RES:
                {
                    byte res = br.ReadByte();
                    int h0 = res & 3;
                    int h1 = (res >> 2) & 3;
                    AddLog($"가위바위보 결과: P0({FormatRpsKR(h0)}) vs P1({FormatRpsKR(h1)})");
                    break;
                }

                case YgoConstants.MSG_EQUIP:
                {
                    byte eCon = br.ReadByte();
                    br.ReadByte(); // equip location
                    uint eSeq = br.ReadUInt32();
                    br.ReadUInt32(); // equip position
                    byte tCon = br.ReadByte();
                    br.ReadByte(); // target location
                    uint tSeq = br.ReadUInt32();
                    br.ReadUInt32(); // target position
                    AddLog($"장착: {(eCon == HumanPlayerIndex ? "내" : "상대")} 마함[{eSeq + 1}] -> {(tCon == HumanPlayerIndex ? "내" : "상대")} 몬스터[{tSeq + 1}]");
                    break;
                }

                case YgoConstants.MSG_TOSS_COIN:
                {
                    byte p = br.ReadByte();
                    byte cnt = br.ReadByte();
                    var coins = new List<string>(cnt);
                    var coinBools = new List<bool>(cnt);
                    for (int i = 0; i < cnt; i++)
                    {
                        byte v = br.ReadByte();
                        coins.Add(v == 1 ? "앞면" : "뒷면");
                        coinBools.Add(v == 1);
                    }
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} 코인 토스: {string.Join(", ", coins)}");
                    OnCoinTossed?.Invoke(p, coinBools);
                    break;
                }

                case YgoConstants.MSG_TOSS_DICE:
                {
                    byte p = br.ReadByte();
                    byte cnt = br.ReadByte();
                    var dice = new List<string>(cnt);
                    var diceInts = new List<int>(cnt);
                    for (int i = 0; i < cnt; i++)
                    {
                        byte v = br.ReadByte();
                        dice.Add(v.ToString());
                        diceInts.Add(v);
                    }
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} 주사위: {string.Join(", ", dice)}");
                    OnDiceRolled?.Invoke(p, diceInts);
                    break;
                }
            }
        }

        private static string FormatRpsKR(int hand)
        {
            switch (hand)
            {
                case 1: return "가위";
                case 2: return "바위";
                case 3: return "보";
                default: return "?";
            }
        }

        public void RefreshZonesFromCore()
        {
            if (NativePtr == IntPtr.Zero) return;

            byte[] fb = YgoNativeBridge.QueryFieldBytes(NativePtr);
            if (fb.Length >= 12)
            {
                int pos = 4; // duel_options
                for (int p = 0; p < 2; p++)
                {
                    Players[p].lp = BitConverter.ToInt32(fb, pos); pos += 4;
                    for (int m = 0; m < 7; m++)
                    {
                        byte occ = fb[pos++];
                        if (occ != 0) pos += 5; // pos(1) + xyzCount(4)
                    }
                    for (int s = 0; s < 8; s++)
                    {
                        byte occ = fb[pos++];
                        if (occ != 0) pos += 5;
                    }
                    Players[p].deckCount = BitConverter.ToInt32(fb, pos); pos += 20; // main, hand, grave, remove, extra
                    pos += 4; // extra_p_count
                }
            }

            for (byte p = 0; p < 2; p++)
            {
                Array.Clear(Players[p].mzone, 0, Players[p].mzone.Length);
                var mList = QueryLocationCards(p, YgoConstants.LOCATION_MZONE);
                for (int i = 0; i < mList.Count && i < 7; i++) Players[p].mzone[i] = mList[i];

                Array.Clear(Players[p].szone, 0, Players[p].szone.Length);
                var sList = QueryLocationCards(p, YgoConstants.LOCATION_SZONE);
                for (int i = 0; i < sList.Count && i < 8; i++) Players[p].szone[i] = sList[i];

                Players[p].hand = QueryLocationCards(p, YgoConstants.LOCATION_HAND, keepNulls: false);
                Players[p].grave = QueryLocationCards(p, YgoConstants.LOCATION_GRAVE, keepNulls: false);
                Players[p].banished = QueryLocationCards(p, YgoConstants.LOCATION_REMOVED, keepNulls: false);
                Players[p].extra = QueryLocationCards(p, YgoConstants.LOCATION_EXTRA, keepNulls: false);
            }
        }

        private List<LiveCardState> QueryLocationCards(byte playerId, uint loc, bool keepNulls = true)
        {
            var result = new List<LiveCardState>();
            byte[] raw = YgoNativeBridge.QueryLocationBytes(NativePtr, YgoConstants.QUERY_DEFAULT, playerId, loc);
            if (raw.Length < 4) return result;

            int pos = 4; // uint32 total payload bytes
            int seqIdx = 0;
            while (pos + 2 <= raw.Length)
            {
                short headerLen = BitConverter.ToInt16(raw, pos);
                pos += 2;
                if (headerLen == 0)
                {
                    if (keepNulls) result.Add(null);
                    seqIdx++;
                    continue;
                }
                // card::get_infos writes blocks until QUERY_END (block length 4 + query 0x80000000)
                var card = new LiveCardState
                {
                    controller = playerId,
                    location = loc,
                    sequence = (uint)seqIdx,
                };
                // First block was ALREADY read into headerLen! Rewind 2 bytes to parse block loop cleanly
                pos -= 2;
                while (pos + 2 <= raw.Length)
                {
                    ushort blkSize = BitConverter.ToUInt16(raw, pos);
                    pos += 2;
                    if (blkSize == 0 || pos + blkSize > raw.Length) break;
                    int blkStart = pos;
                    uint qflag = BitConverter.ToUInt32(raw, pos);
                    if (qflag == YgoConstants.QUERY_END)
                    {
                        pos = blkStart + blkSize;
                        break;
                    }
                    int valPos = pos + 4;
                    switch (qflag)
                    {
                        case YgoConstants.QUERY_CODE: card.code = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_POSITION: card.position = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_ALIAS: card.alias = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_TYPE: card.type = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_LEVEL: card.level = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_RANK: card.rank = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_ATTRIBUTE: card.attribute = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_RACE: card.race = BitConverter.ToUInt64(raw, valPos); break;
                        case YgoConstants.QUERY_ATTACK: card.attack = BitConverter.ToInt32(raw, valPos); break;
                        case YgoConstants.QUERY_DEFENSE: card.defense = BitConverter.ToInt32(raw, valPos); break;
                        case YgoConstants.QUERY_BASE_ATTACK: card.baseAttack = BitConverter.ToInt32(raw, valPos); break;
                        case YgoConstants.QUERY_BASE_DEFENSE: card.baseDefense = BitConverter.ToInt32(raw, valPos); break;
                        case YgoConstants.QUERY_OWNER: card.owner = raw[valPos]; break;
                        case YgoConstants.QUERY_IS_PUBLIC: card.isPublic = raw[valPos] != 0; break;
                        case YgoConstants.QUERY_LSCALE: card.lscale = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_RSCALE: card.rscale = BitConverter.ToUInt32(raw, valPos); break;
                        case YgoConstants.QUERY_LINK:
                            card.link = BitConverter.ToUInt32(raw, valPos);
                            card.linkMarker = BitConverter.ToUInt32(raw, valPos + 4);
                            break;
                        case YgoConstants.QUERY_OVERLAY_CARD:
                            uint oc = BitConverter.ToUInt32(raw, valPos);
                            for (uint oi = 0; oi < oc; oi++)
                                card.overlayCodes.Add(BitConverter.ToUInt32(raw, valPos + 4 + (int)oi * 4));
                            break;
                    }
                    pos = blkStart + blkSize;
                }
                result.Add(card);
                seqIdx++;
            }
            return result;
        }

        private void AddLog(string line)
        {
            if (Logs.Count > 250) Logs.RemoveAt(0);
            Logs.Add(line);
            OnLogAdded?.Invoke(line);
        }

        public void Dispose()
        {
            if (NativePtr != IntPtr.Zero)
            {
                YgoNativeBridge.YGO_DestroyDuel(NativePtr);
                NativePtr = IntPtr.Zero;
            }
        }
    }
}
