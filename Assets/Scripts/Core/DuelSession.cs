using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    public readonly struct DuelOpening
    {
        internal readonly int Life;
        internal readonly int Hand;
        internal readonly uint FieldCard;
        internal readonly int DestinyLimit;
        internal readonly int DestinyUses;
        internal readonly uint DeckMonster;

        internal DuelOpening(int life, int hand, uint fieldCard, int destinyLimit = 2000, int destinyUses = 1, uint deckMonster = 0)
        {
            Life = life;
            Hand = hand;
            FieldCard = fieldCard;
            DestinyLimit = destinyLimit;
            DestinyUses = destinyUses;
            DeckMonster = deckMonster;
        }
    }

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
        public bool effectsDisabled;
        public List<uint> overlayCodes = new List<uint>();
        internal (byte Controller, uint Sequence)? EquipTarget;
        internal DeckCardAppearance? Appearance;
        internal (ushort Type, ushort Count)[] Counters = Array.Empty<(ushort, ushort)>();

        public uint counterCount
        {
            get
            {
                uint total = 0;
                foreach (var counter in Counters) total += counter.Count;
                return total;
            }
        }

        public bool IsFaceUp => (position & YgoConstants.POS_FACEUP) != 0;
        public bool IsDefense => (position & YgoConstants.POS_DEFENSE) != 0;
        public bool IsAttack => (position & YgoConstants.POS_ATTACK) != 0;
        internal bool IsPendulumScale => YgoConstants.IsPendulumZone(location, sequence) && YgoConstants.IsPendulumScale(type);
        internal uint CurrentNameCode => alias != 0 ? alias : code;
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
        internal uint? summonType;
        internal uint? ReturnEffectId;
        internal uint? TemporaryBanishCardId;
        internal LiveCardState SourceReadout;
        internal LiveCardState DestinationReadout;
        internal DeckCardAppearance? Appearance;
        internal object PresentationGroup;
        internal bool ChangesPositionInPlace =>
            fromCon == toCon && fromLoc == toLoc && fromSeq == toSeq && fromPos != toPos &&
            (fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE);
        internal uint MaterialMethod => (reason & YgoConstants.REASON_MATERIAL) == 0
            ? 0 : reason & YgoConstants.MATERIAL_METHOD_MASK;
        internal bool TemporarilyBanished => (reason & YgoConstants.REASON_TEMPORARY) != 0 &&
            (toLoc is 0 or YgoConstants.LOCATION_REMOVED);
    }

    internal enum BattleStatSource : byte { Attack = 1, Defense, Effect }
    internal enum LifePointChangeKind : byte { Damage, BattleDamage, EffectDamage, Recovery, Payment, Adjustment }

    internal readonly struct BattleComparisonValue
    {
        internal readonly BattleStatSource Source;
        internal readonly uint Value;

        internal BattleComparisonValue(BattleStatSource source, uint value)
        {
            Source = source;
            Value = value;
        }

        internal static (BattleComparisonValue? Attacker, BattleComparisonValue? Defender) ReadMessage(BinaryReader reader, long end)
        {
            // The project adds a versioned custom message just before the engine's normal MSG_BATTLE.
            if (end - reader.BaseStream.Position != 14 || reader.ReadUInt32() != 0x31424759) return default; // YGB1
            return (ReadValue(reader), ReadValue(reader));
        }

        private static BattleComparisonValue? ReadValue(BinaryReader reader)
        {
            var source = (BattleStatSource)reader.ReadByte();
            uint value = reader.ReadUInt32();
            return source is BattleStatSource.Attack or BattleStatSource.Defense or BattleStatSource.Effect
                ? new BattleComparisonValue(source, value) : null;
        }
    }

    internal readonly struct BattleCardStats
    {
        internal readonly byte Controller;
        internal readonly uint Location, Sequence, Position;
        internal readonly int Attack, Defense;
        internal readonly BattleComparisonValue? ComparedValue;
        internal bool IsFaceUp => Location != 0 && (Position & YgoConstants.POS_FACEUP) != 0;
        internal bool IsDefense => (Position & YgoConstants.POS_DEFENSE) != 0;

        internal BattleCardStats(byte controller, uint location, uint sequence, uint position, int attack, int defense,
            BattleComparisonValue? comparedValue = null)
        {
            Controller = controller;
            Location = location;
            Sequence = sequence;
            Position = position;
            Attack = attack;
            Defense = defense;
            ComparedValue = comparedValue;
        }

        internal static BattleCardStats Read(BinaryReader reader, BattleComparisonValue? comparedValue)
        {
            var stats = new BattleCardStats(reader.ReadByte(), reader.ReadByte(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadInt32(), reader.ReadInt32(), comparedValue);
            reader.ReadByte(); // Battle destruction flag.
            return stats;
        }
    }

    public class ChainLinkInfo
    {
        internal DeckCardAppearance? Appearance;
        public int chainNumber;
        public uint code;
        public byte triggerPlayer;
        public uint triggerLoc;
        public uint triggerSeq;
        public ulong description;
        public bool negated;
        internal bool ActivationNegated;
        internal bool IsCardActivation;
        internal bool HasOncePerTurnActivationLimit;
        internal bool AppliedColumnNegation;
        internal (byte Controller, uint Location, uint Sequence) ActivationSource;
        internal byte? NegatedGraveController;
        internal bool DestroyedCard;
        internal bool Resolved;
        internal LiveCardState ActivationReadout;
        internal LiveCardState ResolutionReadout;
        internal (byte Controller, uint Location, uint Sequence)? PublicFieldSource;
    }

    [Serializable]
    public class SelectableCardItem
    {
        public uint code;
        public byte controller;
        public uint location;
        public uint sequence;
        public uint position;
        public ulong description;
        public byte mode;
        public bool isTriggeredEffect;
        public bool isEventSpecificEffect;
        public bool directAttackable;
        public byte releaseParam;
        public uint sumParam;
        public uint currentCounter;
    }

    public enum ChainConfirmationMode
    {
        Auto,
        Off,
        On,
    }

    [Serializable]
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
        public uint promptPosition;
        public ulong promptDesc;
        public ulong promptEffectDesc;
        // The core uses 0 and 221 for optional triggers. Lua and rule questions carry the description to display.
        internal bool IsTriggerActivationConfirmation =>
            messageType == YgoConstants.MSG_SELECT_EFFECTYN && promptDesc is 0 or 221;
        internal bool IsDirectAttackChoice =>
            (messageType is YgoConstants.MSG_SELECT_YESNO or YgoConstants.MSG_SELECT_EFFECTYN) &&
            promptDesc == YgoConstants.DESC_DIRECT_ATTACK;
        internal bool IsAttackReplayChoice =>
            messageType == YgoConstants.MSG_SELECT_YESNO && promptDesc == YgoConstants.DESC_ATTACK_REPLAY;
        internal bool IsAttackDecision => IsDirectAttackChoice || IsAttackReplayChoice;

        internal SelectableCardItem ReadEffectConfirmationSource() => new SelectableCardItem
        {
            code = promptCardCode,
            controller = promptCon,
            location = promptLoc,
            sequence = promptSeq,
            position = promptPosition,
            description = promptEffectDesc != 0 ? promptEffectDesc : promptDesc,
        };

        // SelectOption / AnnounceNumber / AnnounceCard
        public List<ulong> options = new List<ulong>();

        // SelectCard / Unselect / Tribute / Sum / Sort / Counter
        public bool cancelable;
        public bool finishable;
        public bool forced;
        public byte speCount;
        public bool IsTriggerChain => speCount == 0x7f;
        public uint hintTimingSelf;
        public uint hintTimingOther;
        internal bool chainTriggerInfoMissing;
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
        internal ulong SelectionHint { get; set; }
        internal bool IsAttackTargetSelection =>
            messageType == YgoConstants.MSG_SELECT_CARD && SelectionHint == YgoConstants.HINTMSG_ATTACKTARGET &&
            min == 1 && max == 1 && candidates.Count > 0;
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
        internal int FieldZoneVersion { get; set; }
    }

    public partial class DuelSession : IDisposable
    {
        private const byte TriggeredEffectFlag = 1 << 0;
        private const byte EventSpecificEffectFlag = 1 << 1;
        private const int MaxReplayResponses = 768;

        private readonly bool _recordPlanningReplay;
        private readonly LocalDuelReplayRecorder _playerReplay;
        private int _automaticReplayResponse, _automaticReplayRetry;
        private uint[][] _startingMainDecks;
        private int[] _startingExtraCounts;
        private PlanningReplay _planningReplay;
        private bool _loadingPlanningReplay;
        private int _speculatingPlayer = -1;
        private readonly List<uint> _selectedFromDeck = new List<uint>();
        private ChainLinkInfo _deckSelectionEffect;
        private ulong _deckSelectionHint;
        private int _planningTurnLimit = -1;
        internal bool SpeculationUncertain { get; private set; }
        private bool SpeculationPassedTurnLimit => _planningTurnLimit >= 0 && TurnNumber > _planningTurnLimit;

        private sealed class PlanningReplay
        {
            internal uint[] firstDeck, firstExtra, secondDeck, secondExtra;
            internal ulong[] seed;
            internal bool first;
            internal int startingLp;
            internal DuelOpening? opponentOpening;
            internal DuelOpening? playerOpening;
            internal UserDeck destinyDeck;
            internal uint[] opponentDestiny;
            internal readonly List<(byte message, byte player, byte[] bytes)> responses = new();
        }

        public DuelSession() { }
        internal DuelSession(bool recordPlanningReplay, LocalDuelReplayRecorder playerReplay = null)
        {
            _recordPlanningReplay = recordPlanningReplay;
            _playerReplay = playerReplay;
        }

        public IntPtr NativePtr { get; private set; }
        public int TurnNumber { get; private set; }
        public int TurnPlayer { get; private set; }
        internal int TurnInterruptions { get; private set; }
        internal IReadOnlyList<uint> StartingMainDeck(byte player) => _startingMainDecks?[player] ?? Array.Empty<uint>();
        internal int StartingExtraCount(byte player) => _startingExtraCounts?[player] ?? 0;
        public int CurrentPhase { get; private set; }
        public uint FieldDisabledMask { get; private set; }
        public bool IsFinished { get; private set; }
        public int Winner { get; private set; } = -1; // 0=P0, 1=P1, 2=Draw
        public byte WinReason { get; private set; }
        public int RetryCount { get; private set; }
        public ulong LastHintMsg { get; private set; }
        public uint AttackerCon, AttackerLoc, AttackerSeq, TargetCon, TargetLoc, TargetSeq;
        public bool HasActiveAttack { get; private set; }
        private SelectableCardItem _chosenAttacker;

        public PlayerFieldSnapshot[] Players { get; } = { new PlayerFieldSnapshot(), new PlayerFieldSnapshot() };
        public List<ChainLinkInfo> CurrentChain { get; } = new List<ChainLinkInfo>();
        private HashSet<(byte Player, ulong Description)> _activatedMonsterEffects;
        private HashSet<(byte Player, uint Code)> _usedSharedTurnEffects;
        private HashSet<(byte Player, uint Code)> _usedSharedTurnActivations;

        internal bool HasUsedSharedTurnEffect(byte player, uint code) =>
            _usedSharedTurnEffects?.Contains((player, CardDatabase.GetDeckPrintingKey(code))) == true;

        internal bool HasUsedSharedTurnActivation(byte player, uint code) =>
            _usedSharedTurnActivations?.Contains((player, CardDatabase.GetDeckPrintingKey(code))) == true;

        private static bool HasSharedOncePerTurnText(CardInfo card)
        {
            string effect = card?.desc;
            return effect != null && effect.Contains("이 카드명의 효과는 1턴에 1번밖에 사용할 수 없다");
        }
        internal const uint InfiniteImpermanenceCode = 10045474;
        private const uint BaronneCode = 84815190;
        private const ulong BaronneNegation = ((ulong)BaronneCode << 20) | 1;
        private readonly HashSet<(byte Controller, uint Sequence)> _spentBaronneNegates = new();
        private readonly int[] _summonedMonsters = new int[2];
        private readonly HashSet<(byte Controller, uint Sequence)> _pendingSpecialSummons = new();
        internal int SummonedMonstersThisTurn(byte player) => _summonedMonsters[player];
        internal bool HasSpentBaronneNegate(LiveCardState card) => _spentBaronneNegates.Contains((card.controller, card.sequence));
        private HashSet<(byte FieldController, byte BanishController, uint Code, uint? CardId)> _temporaryAttackers;
        internal bool HasActivatedMonsterEffect(byte player, ulong description) =>
            _activatedMonsterEffects?.Contains((player, description)) == true;
        internal int PotentialReturningAttack(int player)
        {
            if (_temporaryAttackers == null) return 0;
            int attack = 0;
            foreach (var card in _temporaryAttackers)
            {
                if (card.FieldController != player) continue;
                foreach (var banished in Players[card.BanishController].banished)
                    if (banished.IsFaceUp && banished.code == card.Code)
                        attack = Math.Max(attack, banished.attack);
            }
            return attack;
        }
        internal ChainLinkInfo ResolvingChainLink => _resolvingChainLink;
        internal ChainLinkInfo SelectionChainLink => _resolvingChainLink ?? (CurrentChain.Count == 1 ? CurrentChain[0] : null);
        public List<(int chainNumber, uint code)> SolvedChainLinks { get; } = new List<(int, uint)>();
        public List<string> Logs { get; } = new List<string>();
        public DuelPrompt ActivePrompt { get; private set; }
        private ChainLinkInfo _resolvingChainLink;
        private (byte player, ulong hint)? _pendingSelectionHint;
        private Dictionary<(byte Controller, uint Location, uint Sequence), LiveCardState> _fieldReadoutBaseline;
        private bool _awaitingFieldArrivalCompletion;
        private const uint DestructionGroupTag = 0x31444759; // YGD1
        private const uint MaterialGroupTag = 0x314D4759; // YGM1
        private const uint PositionGroupTag = 0x31504759; // YGP1
        private const uint TemporaryBanishGroupTag = 0x31544759; // YGT1
        private const uint FieldReturnTag = 0x31524759; // YGR1
        private const uint SwapMoveTag = 0x31534759; // YGS1
        private readonly Stack<(uint Tag, object Key)> _movementGroups = new();
        private readonly HashSet<(byte Player, uint Location, uint Sequence, uint Code)> _confirmedCards =
            new HashSet<(byte, uint, uint, uint)>();
        private readonly Dictionary<uint, int>[] _knownOpponentHands = new Dictionary<uint, int>[2];
        private readonly (uint Code, int DeckCount)[] _observedDeckTop = new (uint, int)[2];
        private (byte Player, int Remaining) _observedTopSort;

        public event Action<string> OnLogAdded;

        public int HumanPlayerIndex { get; private set; } = 0;
        public int AiPlayerIndex => 1 - HumanPlayerIndex;

        public void PreparePlayerIndices(bool p0GoesFirst)
        {
            HumanPlayerIndex = p0GoesFirst ? 0 : 1;
        }

        public event Action<CardMoveEvent> OnCardMoved;
        internal event Action<CardMoveEvent, CardMoveEvent> OnCardLocationsChanged;
        internal event Action OnFieldArrivalBoundary;
        public event Action<byte, IReadOnlyList<uint>> OnCardsDrawn;
        internal event Action<byte, IReadOnlyList<uint>, IReadOnlyList<DeckCardAppearance?>> OnStyledCardsDrawn;
        public event Action<byte, int, int> OnLifePointsChanged;
        internal event Action<byte, int, int, LifePointChangeKind> OnLifePointChange;
        public event Action<int, int> OnTurnStarted;
        public event Action<int, int> OnPhaseChanged;
        public event Action<ChainLinkInfo> OnChainActivated;
        internal event Action<(byte Controller, uint Location, uint Sequence)?, IReadOnlyList<(byte Controller, uint Location, uint Sequence)>> OnCardsTargeted;
        public event Action<ChainLinkInfo> OnChainResolving;
        internal event Action<ChainLinkInfo> OnChainResolved;
        internal event Action<IReadOnlyList<(LiveCardState Before, LiveCardState After)>> OnFieldReadoutsChanged;
        internal event Action<LiveCardState, LiveCardState, IReadOnlyList<(LiveCardState Before, LiveCardState After)>> OnCardEquipped;
        private bool TracksFieldReadouts => OnFieldReadoutsChanged != null || OnCardEquipped != null;
        public event Action<byte, uint, uint, byte, uint, uint> OnAttackDeclared;
        internal event Action<BattleCardStats, BattleCardStats> OnBattleResolved;
        public event Action<byte, IReadOnlyList<bool>> OnCoinTossed;
        public event Action<byte, IReadOnlyList<int>> OnDiceRolled;

        private const uint CardIdentityTag = 0x31494759;
        private readonly Dictionary<uint, DeckCardAppearance> _deckAppearances = new();
        internal bool UsesDeckAppearances { get; private set; }

        public bool StartNewDuel(
            IReadOnlyList<uint> p0Main, IReadOnlyList<uint> p0Extra,
            IReadOnlyList<uint> p1Main, IReadOnlyList<uint> p1Extra,
            bool p0GoesFirst = true, int startingLp = 8000, ulong[] seed = null, UserDeck playerDeckAppearance = null,
            DuelOpening? opponentOpening = null, IReadOnlyList<uint> opponentDestiny = null, DuelOpening? playerOpening = null)
        {
            Dispose();
            UsesDeckAppearances = playerDeckAppearance != null && !_loadingPlanningReplay;
            _deckAppearances.Clear();
            ResetDestinyDraw();
            _playerDestinyLimit = playerOpening?.DestinyLimit ?? 2000;
            _playerDestinyUses = playerOpening?.DestinyUses ?? 1;
            IsFinished = false;
            Winner = -1;
            TurnNumber = 0;
            TurnInterruptions = 0;
            CurrentPhase = YgoConstants.PHASE_DRAW;
            RetryCount = 0;
            _automaticReplayResponse = 0;
            LastHintMsg = 0;
            _pendingSelectionHint = null;
            ClearAttack();
            ActivePrompt = null;
            CurrentChain.Clear();
            _activatedMonsterEffects?.Clear();
            _usedSharedTurnEffects?.Clear();
            _usedSharedTurnActivations?.Clear();
            _spentBaronneNegates.Clear();
            ResetSummonTracking();
            _temporaryAttackers?.Clear();
            _resolvingChainLink = null;
            SolvedChainLinks.Clear();
            Logs.Clear();
            _confirmedCards.Clear();
            Array.Clear(_knownOpponentHands, 0, _knownOpponentHands.Length);
            Array.Clear(_observedDeckTop, 0, _observedDeckTop.Length);
            _observedTopSort = default;

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

            int bossLife = opponentOpening?.Life ?? startingLp;
            int bossHand = opponentOpening?.Hand ?? 5;
            int playerLife = playerOpening?.Life ?? startingLp;
            int playerHand = playerOpening?.Hand ?? 5;
            int firstLife = p0GoesFirst ? playerLife : bossLife;
            int secondLife = p0GoesFirst ? bossLife : playerLife;
            NativePtr = YgoNativeBridge.CreateDuel(seed, YgoConstants.DUEL_MODE_MR5,
                firstLife, p0GoesFirst ? playerHand : bossHand, 1,
                secondLife, p0GoesFirst ? bossHand : playerHand, 1);

            if (NativePtr == IntPtr.Zero) return false;
            if (YgoNativeBridge.YGO_LoadBaseScripts(NativePtr) == 0)
            {
                Debug.LogError("[DuelSession] Failed to load base scripts.");
                Dispose();
                return false;
            }

            Players[0].lp = firstLife;
            Players[1].lp = secondLife;
            foreach (var player in Players) player.FieldZoneVersion = 0;

            var turn0Main = p0GoesFirst ? p0Main : p1Main;
            var turn0Extra = p0GoesFirst ? p0Extra : p1Extra;
            var turn1Main = p0GoesFirst ? p1Main : p0Main;
            var turn1Extra = p0GoesFirst ? p1Extra : p0Extra;
            // Planning replays already own the immutable starting lists.
            var firstMain = _loadingPlanningReplay && turn0Main is uint[] savedFirst ? savedFirst : CopyDeck(turn0Main);
            var secondMain = _loadingPlanningReplay && turn1Main is uint[] savedSecond ? savedSecond : CopyDeck(turn1Main);
            _startingMainDecks = new[] { firstMain, secondMain };
            _startingExtraCounts = new[] { turn0Extra?.Count ?? 0, turn1Extra?.Count ?? 0 };

            // Reuse the duel seed for deck order so a reported match can be replayed.
            uint shuffleSeed = 2166136261;
            for (int i = 0; i < 4; i++)
            {
                shuffleSeed = unchecked((shuffleSeed ^ (uint)seed[i]) * 16777619);
                shuffleSeed = unchecked((shuffleSeed ^ (uint)(seed[i] >> 32)) * 16777619);
            }
            var shuffle = new System.Random(unchecked((int)shuffleSeed));
            _destinyRandom = new System.Random(unchecked((int)(shuffleSeed ^ 0xD571D2A7)));
            uint deckMonster = opponentOpening?.DeckMonster ?? 0;
            var mainWithoutMonster = RemoveOpeningCard(p0GoesFirst ? secondMain : firstMain, deckMonster);
            var extraWithoutMonster = mainWithoutMonster == null ? RemoveOpeningCard(p1Extra, deckMonster) : null;
            LoadDeckForPlayer(0, p0GoesFirst ? firstMain : mainWithoutMonster ?? firstMain,
                p0GoesFirst ? turn0Extra : extraWithoutMonster ?? turn0Extra, shuffle,
                p0GoesFirst ? playerDeckAppearance : null, p0GoesFirst ? null : opponentDestiny);
            LoadDeckForPlayer(1, p0GoesFirst ? mainWithoutMonster ?? secondMain : secondMain,
                p0GoesFirst ? extraWithoutMonster ?? turn1Extra : turn1Extra, shuffle,
                p0GoesFirst ? null : playerDeckAppearance, p0GoesFirst ? opponentDestiny : null);
            if (opponentOpening is { FieldCard: > 0 } opening) PlaceOpeningCard((byte)AiPlayerIndex, opening.FieldCard);
            if (mainWithoutMonster != null || extraWithoutMonster != null)
                PlaceOpeningCard((byte)AiPlayerIndex, deckMonster, opponentOpening.Value.FieldCard == 0 ? 2u : 1u);

            YgoNativeBridge.YGO_StartDuel(NativePtr);
            if (_recordPlanningReplay)
            {
                _planningReplay = new PlanningReplay
                {
                    first = p0GoesFirst,
                    startingLp = startingLp,
                    opponentOpening = opponentOpening,
                    playerOpening = playerOpening,
                    destinyDeck = _destinyCards[HumanPlayerIndex].Count == 0 ? null : CollectionProgress.CopyDeck(playerDeckAppearance),
                    opponentDestiny = CopyDeck(opponentDestiny),
                    seed = (ulong[])seed.Clone(),
                    firstDeck = p0GoesFirst ? firstMain : secondMain, firstExtra = CopyDeck(p0Extra),
                    secondDeck = p0GoesFirst ? secondMain : firstMain, secondExtra = CopyDeck(p1Extra),
                };
            }
            _playerReplay?.Begin(seed, p0Main, p0Extra, p1Main, p1Extra, p0GoesFirst, startingLp);
            AddLog($"듀얼 개시! ({(p0GoesFirst ? "플레이어 선공" : "AI 선공")}) — 덱 무작위 셔플 완료");
            StepUntilPrompt();
            return true;
        }

        private static uint[] CopyDeck(IReadOnlyList<uint> cards)
        {
            if (cards == null || cards.Count == 0) return Array.Empty<uint>();
            var copy = new uint[cards.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = cards[i];
            return copy;
        }

        internal DuelSession ReplayForPlanning(Func<bool> expired)
        {
            var replay = _planningReplay;
            if (replay == null || RetryCount != 0 || expired()) return null;
            var copy = new DuelSession { _loadingPlanningReplay = true };
            bool ready = false;
            try
            {
                if (!copy.StartNewDuel(replay.firstDeck, replay.firstExtra, replay.secondDeck, replay.secondExtra,
                    replay.first, replay.startingLp, replay.seed, replay.destinyDeck,
                    replay.opponentOpening, replay.opponentDestiny, replay.playerOpening)) return null;
                foreach (var response in replay.responses)
                {
                    if (expired() || copy.IsFinished) return null;
                    if (copy.ActivePrompt == null) copy.StepUntilPrompt();
                    var prompt = copy.ActivePrompt;
                    if (prompt == null || prompt.messageType != response.message || prompt.playerId != response.player) return null;
                    copy.SubmitResponse(response.bytes);
                    if (copy.RetryCount != 0) return null;
                }
                if (copy.ActivePrompt == null) copy.StepUntilPrompt();
                copy._loadingPlanningReplay = false;
                if (copy.IsFinished || copy.ActivePrompt == null || copy.TurnNumber != TurnNumber || copy.CurrentPhase != CurrentPhase)
                    return null;
                copy.RefreshZonesFromCore();
                ready = true;
                return copy;
            }
            finally
            {
                if (!ready) copy.Dispose();
            }
        }

        internal void BeginSpeculation(int player, bool stopAfterTurn = false)
        {
            _speculatingPlayer = player;
            _planningTurnLimit = stopAfterTurn ? TurnNumber : -1;
            SpeculationUncertain = false;
            ClearDeckSelection();
        }

        private void LoadDeckForPlayer(byte playerId, IReadOnlyList<uint> main, IReadOnlyList<uint> extra,
            System.Random shuffle, UserDeck appearanceDeck, IReadOnlyList<uint> opponentDestiny)
        {
            if (main != null && main.Count > 0)
            {
                var shuffled = Enumerable.Range(0, main.Count).ToArray();
                for (int i = shuffled.Length - 1; i > 0; i--)
                {
                    int j = shuffle.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }
                for (int i = shuffled.Length - 1; i >= 0; i--)
                    AddDeckCard(main[shuffled[i]], YgoConstants.LOCATION_DECK,
                        appearanceDeck?.GetAppearance(appearanceDeck.main, shuffled[i]) ?? default);
            }
            if (extra != null)
            {
                for (int i = 0; i < extra.Count; i++)
                    AddDeckCard(extra[i], YgoConstants.LOCATION_EXTRA,
                        appearanceDeck?.GetAppearance(appearanceDeck.extra, i) ?? default);
            }

            void AddDeckCard(uint code, uint location, DeckCardAppearance appearance)
            {
                bool marked = location == YgoConstants.LOCATION_DECK &&
                    (appearance.destinyDraw || opponentDestiny?.Contains(code) == true);
                bool styled = UsesDeckAppearances && !appearance.IsDefault;
                uint before = marked || styled ? YgoNativeBridge.YGO_QueryCount(NativePtr, playerId, location) : 0;
                YgoNativeBridge.YGO_AddCard(NativePtr, playerId, 0, code, playerId, location, 0, YgoConstants.POS_FACEDOWN_DEFENSE);
                if ((!marked && !styled) || YgoNativeBridge.YGO_QueryCount(NativePtr, playerId, location) != before + 1) return;
                uint identity = YgoNativeBridge.YGO_QueryCardIdentity(NativePtr, playerId, location, before);
                if (identity == 0) return;
                if (marked) _destinyCards[playerId].Add(identity);
                if (styled) _deckAppearances[identity] = appearance;
            }
        }

        private static uint[] RemoveOpeningCard(IReadOnlyList<uint> deck, uint code)
        {
            if (code == 0 || deck == null) return null;
            for (int i = 0; i < deck.Count; i++)
                if (deck[i] == code) return deck.Where((_, index) => index != i).ToArray();
            return null;
        }

        private void PlaceOpeningCard(byte player, uint code, uint monsterSequence = 2)
        {
            var info = CardDatabase.Get(code);
            if (info == null) return;
            bool fieldSpell = (info.type & YgoConstants.TYPE_FIELD) != 0;
            uint location = info.IsMonster ? YgoConstants.LOCATION_MZONE : YgoConstants.LOCATION_SZONE;
            uint sequence = info.IsMonster ? monsterSequence : fieldSpell ? 5u : 2u;
            YgoNativeBridge.YGO_AddCard(NativePtr, player, 0, code, player, location, sequence, YgoConstants.POS_FACEUP_ATTACK);
        }

        internal DeckCardAppearance? GetDeckAppearance(byte player, uint location, uint sequence)
        {
            if (!UsesDeckAppearances) return null;
            if (_deckAppearances.Count == 0) return default(DeckCardAppearance);
            uint identity = YgoNativeBridge.YGO_QueryCardIdentity(NativePtr, player, location, sequence);
            return AppearanceOf(identity);
        }

        private DeckCardAppearance? AppearanceOf(uint identity) =>
            UsesDeckAppearances ? _deckAppearances.GetValueOrDefault(identity) : null;

        public void SubmitResponse(byte[] buffer) => SubmitResponse(buffer, preserveDeckSelection: false);

        internal void SubmitPlanningResponse(byte[] buffer) => SubmitResponse(buffer, preserveDeckSelection: true);

        internal void SubmitAutomaticResponseInt(int value, string origin) =>
            SubmitResponse(BitConverter.GetBytes(value), preserveDeckSelection: false, origin: origin);

        private void SubmitResponse(byte[] buffer, bool preserveDeckSelection, string origin = null)
        {
            if (NativePtr == IntPtr.Zero || IsFinished) return;
            var prevPrompt = ActivePrompt;
            int prevRetry = RetryCount;
            int recorded = _playerReplay?.Submitted(prevPrompt, buffer, TurnNumber, CurrentPhase, HumanPlayerIndex, origin) ?? 0;
            bool processed = false;
            try
            {
                if (_planningReplay != null)
                {
                    if (prevPrompt == null || buffer == null || buffer.Length == 0 || _planningReplay.responses.Count >= MaxReplayResponses)
                        _planningReplay = null;
                    else _planningReplay.responses.Add((prevPrompt.messageType, prevPrompt.playerId, (byte[])buffer.Clone()));
                }
                if (_speculatingPlayer >= 0) TrackSpeculativeSelection(prevPrompt, buffer, preserveDeckSelection);
                if (prevPrompt?.messageType == YgoConstants.MSG_SORT_CARD) TrackSortedDeckTop(prevPrompt, buffer);
                if (prevPrompt?.messageType == YgoConstants.MSG_SELECT_BATTLECMD && buffer?.Length >= sizeof(int))
                {
                    int command = BitConverter.ToInt32(buffer, 0);
                    int index = command >> 16;
                    _chosenAttacker = (command & 0xffff) == 1 && (uint)index < prevPrompt.attackable.Count
                        ? prevPrompt.attackable[index] : null;
                }
                ActivePrompt = null;
                if (prevPrompt?.messageType == YgoConstants.MSG_DESTINY_DRAW)
                    ResolveDestinyDraw(prevPrompt.playerId, buffer?.Length >= sizeof(int) && BitConverter.ToInt32(buffer, 0) == 1);
                else YgoNativeBridge.SetResponse(NativePtr, buffer);
                StepUntilPrompt();
                if (_observedTopSort.Remaining > 0) ForgetObservedDeckTop(_observedTopSort.Player);
                if (!IsFinished && ActivePrompt == null && RetryCount > prevRetry && prevPrompt != null)
                {
                    ActivePrompt = prevPrompt;
                }
                processed = true;
            }
            finally
            {
                _playerReplay?.Acknowledge(recorded, processed && prevPrompt != null && buffer?.Length > 0 && RetryCount == prevRetry,
                    RetryCount - prevRetry);
                FinishPlayerReplay();
            }
        }

        private void TrackSpeculativeSelection(DuelPrompt prompt, byte[] buffer, bool preserveDeckSelection)
        {
            bool appendSelection = false;
            if (preserveDeckSelection && prompt?.playerId == _speculatingPlayer && _selectedFromDeck.Count > 0)
            {
                bool sameEffect = _deckSelectionEffect != null && ReferenceEquals(_deckSelectionEffect, SelectionChainLink);
                // A selected Deck card can still need a zone, position, or destination.
                if (prompt.messageType is YgoConstants.MSG_SELECT_PLACE or YgoConstants.MSG_SELECT_POSITION ||
                    prompt.messageType == YgoConstants.MSG_SELECT_OPTION && sameEffect) return;
                // Some effects ask for distinct cards in separate prompts, then move them together.
                appendSelection = sameEffect && prompt.messageType == YgoConstants.MSG_SELECT_CARD &&
                    prompt.SelectionHint == _deckSelectionHint;
            }
            if (!appendSelection) ClearDeckSelection();
            if (prompt == null || prompt.playerId != _speculatingPlayer || buffer == null || buffer.Length < 4) return;
            int action = BitConverter.ToInt32(buffer, 0);
            if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && action == 0 && buffer.Length >= 8)
            {
                int count = Math.Min(BitConverter.ToInt32(buffer, 4), (buffer.Length - 8) / 4);
                for (int i = 0; i < count; i++) Add(BitConverter.ToInt32(buffer, 8 + i * 4));
            }
            else if (prompt.messageType == YgoConstants.MSG_SELECT_UNSELECT_CARD)
            {
                foreach (var card in prompt.unselectCandidates) Remember(card);
                if (action == 1 && buffer.Length >= 8) Add(BitConverter.ToInt32(buffer, 4));
            }

            void Add(int index)
            {
                if (index < 0 || index >= prompt.candidates.Count) return;
                Remember(prompt.candidates[index]);
            }

            void Remember(SelectableCardItem card)
            {
                if (card.controller == _speculatingPlayer && card.location == YgoConstants.LOCATION_DECK)
                {
                    _selectedFromDeck.Add(card.code);
                    if (preserveDeckSelection)
                    {
                        _deckSelectionEffect = SelectionChainLink;
                        _deckSelectionHint = prompt.SelectionHint;
                    }
                }
            }
        }

        private void ClearDeckSelection()
        {
            _selectedFromDeck.Clear();
            _deckSelectionEffect = null;
            _deckSelectionHint = 0;
        }

        public void SubmitResponseInt(int val)
        {
            SubmitResponse(BitConverter.GetBytes(val));
        }

        internal static byte[] PackCardSelection(params int[] indices)
        {
            using var stream = new MemoryStream(8 + indices.Length * 4);
            using var writer = new BinaryWriter(stream);
            writer.Write(0);
            writer.Write(indices.Length);
            foreach (int index in indices) writer.Write(index);
            return stream.ToArray();
        }

        public void Surrender(int losingPlayer)
        {
            IsFinished = true;
            Winner = 1 - losingPlayer;
            WinReason = 0;
            ActivePrompt = null;
            AddLog(losingPlayer == HumanPlayerIndex ? "플레이어가 항복했습니다." : "상대가 항복했습니다.");
            FinishPlayerReplay("surrender");
        }

        public bool ShouldPassChainPrompt(DuelPrompt prompt, ChainConfirmationMode mode)
        {
            if (!IsHumanOptionalChainPrompt(prompt)) return false;
            foreach (var candidate in prompt.candidates)
                if (IsChainCandidateVisible(prompt, candidate, mode)) return false;
            return true;
        }

        public bool IsChainCandidateVisible(DuelPrompt prompt, SelectableCardItem candidate, ChainConfirmationMode mode) =>
            ShouldOfferFastChainEffects(prompt, mode) || candidate.isTriggeredEffect ||
            (mode == ChainConfirmationMode.Auto && candidate.isEventSpecificEffect);

        internal bool HasEffectActivationAt(DuelPrompt prompt, ChainConfirmationMode mode, byte controller,
            uint location, uint? sequence = null)
        {
            if (prompt == null || prompt.playerId != HumanPlayerIndex || controller != HumanPlayerIndex) return false;

            bool Matches(SelectableCardItem item) => item.controller == controller &&
                IsActivationLocation(item.location, location) && (sequence == null || item.sequence == sequence);

            switch (prompt.messageType)
            {
                case YgoConstants.MSG_SELECT_IDLECMD:
                case YgoConstants.MSG_SELECT_BATTLECMD:
                    return prompt.activatable.Exists(Matches);
                case YgoConstants.MSG_SELECT_CHAIN:
                    return prompt.candidates.Exists(item => Matches(item) && IsChainCandidateVisible(prompt, item, mode));
                case YgoConstants.MSG_SELECT_EFFECTYN:
                    return !prompt.IsAttackDecision && prompt.promptCardCode != 0 && prompt.promptCon == controller &&
                        IsActivationLocation(prompt.promptLoc, location) && (sequence == null || prompt.promptSeq == sequence);
                default:
                    return false;
            }
        }

        internal static bool IsActivationLocation(uint source, uint location) =>
            source == location || location == YgoConstants.LOCATION_MZONE &&
            source == (YgoConstants.LOCATION_MZONE | YgoConstants.LOCATION_OVERLAY);

        public bool ShouldOfferFastChainEffects(DuelPrompt prompt, ChainConfirmationMode mode)
        {
            if (!IsHumanOptionalChainPrompt(prompt) || prompt.chainTriggerInfoMissing ||
                mode == ChainConfirmationMode.On) return true;
            if (mode == ChainConfirmationMode.Off) return false;

            const uint summonOrAttack = YgoConstants.TIMING_SUMMON | YgoConstants.TIMING_SPSUMMON |
                YgoConstants.TIMING_FLIPSUMMON | YgoConstants.TIMING_ATTACK;
            return CurrentChain.Count > 0 ||
                ((prompt.hintTimingSelf | prompt.hintTimingOther) & summonOrAttack) != 0 ||
                (TurnPlayer != HumanPlayerIndex && (prompt.hintTimingOther & YgoConstants.TIMING_END_PHASE) != 0);
        }

        private bool IsHumanOptionalChainPrompt(DuelPrompt prompt) =>
            prompt?.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.playerId == HumanPlayerIndex &&
            !prompt.forced && !prompt.IsTriggerChain;

        public void StepUntilPrompt()
        {
            if (NativePtr == IntPtr.Zero || IsFinished || ActivePrompt?.messageType == YgoConstants.MSG_DESTINY_DRAW) return;
            if (!TracksFieldReadouts) _fieldReadoutBaseline = null;
            else _fieldReadoutBaseline ??= CaptureFieldReadouts(fromNative: true);

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
                if (_automaticReplayResponse != 0)
                {
                    _playerReplay?.Acknowledge(_automaticReplayResponse, RetryCount == _automaticReplayRetry,
                        RetryCount - _automaticReplayRetry);
                    _automaticReplayResponse = 0;
                }

                if (status == YgoConstants.OCG_DUEL_STATUS_END)
                {
                    IsFinished = true;
                    break;
                }
                if (SpeculationPassedTurnLimit) break;
                if (ActivePrompt?.messageType == YgoConstants.MSG_DESTINY_DRAW) break;
                if (status == YgoConstants.OCG_DUEL_STATUS_AWAITING)
                {
                    var prompt = ActivePrompt;
                    if (prompt == null || prompt.messageType != YgoConstants.MSG_SELECT_CHAIN ||
                        prompt.forced || prompt.candidates.Count != 0)
                        break;

                    // The core requests a pass even when no optional chain effects are available.
                    _automaticReplayResponse = _playerReplay?.Submitted(prompt, BitConverter.GetBytes(-1), TurnNumber,
                        CurrentPhase, HumanPlayerIndex, "automatic-empty-chain", replayViaSession: false) ?? 0;
                    _automaticReplayRetry = RetryCount;
                    ActivePrompt = null;
                    YgoNativeBridge.SetResponseInt(NativePtr, -1);
                }
            }

            if (!_loadingPlanningReplay) RefreshZonesFromCore();
            // A group can need several zone and position selections before its cards arrive.
            // Other prompts, including a summon-negation response, see the cards already arriving.
            if (IsFinished || ActivePrompt != null &&
                ActivePrompt.messageType is not (YgoConstants.MSG_SELECT_PLACE or YgoConstants.MSG_SELECT_POSITION))
            {
                CompleteFieldArrivalPresentation();
                if (!_loadingPlanningReplay) SampleFieldReadouts(fromNative: false);
            }
            if (IsFinished)
            {
                _fieldReadoutBaseline = null;
                FinishPlayerReplay();
            }
        }

        private void ParseMessages(byte[] buf)
        {
            using (var ms = new MemoryStream(buf))
            using (var br = new BinaryReader(ms))
            {
                // Native processing emits the comparison and battle together, with optional hints in between.
                (BattleComparisonValue? Attacker, BattleComparisonValue? Defender) comparison = default;
                CardMoveEvent firstSwapMove = null;
                Dictionary<(byte Controller, uint Location, uint Sequence), LiveCardState> positionReadouts = null;
                bool sampleReadouts = false;
                while (ms.Position + 4 <= ms.Length)
                {
                    uint sz = br.ReadUInt32();
                    if (sz == 0 || ms.Position + sz > ms.Length) break;
                    long msgEnd = ms.Position + sz;
                    byte msgType = br.ReadByte();
                    if (msgType != YgoConstants.MSG_MOVE && firstSwapMove != null)
                    {
                        NotifyCardMoved(firstSwapMove);
                        firstSwapMove = null;
                    }
                    var previousPrompt = ActivePrompt;
                    HandleMessage(msgType, br, msgEnd, ref comparison, ref firstSwapMove, ref positionReadouts);
                    sampleReadouts |= msgType is YgoConstants.MSG_MOVE or YgoConstants.MSG_SWAP or YgoConstants.MSG_POS_CHANGE
                        or YgoConstants.MSG_SET or YgoConstants.MSG_SUMMONED or YgoConstants.MSG_FLIPSUMMONED
                        or YgoConstants.MSG_SPSUMMONED or YgoConstants.MSG_ADD_COUNTER or YgoConstants.MSG_REMOVE_COUNTER
                        or YgoConstants.MSG_DRAW or YgoConstants.MSG_NEW_TURN or YgoConstants.MSG_NEW_PHASE;
                    if (ActivePrompt != null && ActivePrompt != previousPrompt && _pendingSelectionHint is { } selection)
                    {
                        if (ActivePrompt.playerId == selection.player) ActivePrompt.SelectionHint = selection.hint;
                        _pendingSelectionHint = null;
                    }
                    ms.Position = msgEnd;
                }
                if (firstSwapMove != null) NotifyCardMoved(firstSwapMove);
                // The native state reflects the whole returned batch. Queue the new values after its animations.
                if (sampleReadouts && _movementGroups.Count == 0 && CurrentChain.Count == 0 && !_awaitingFieldArrivalCompletion)
                    SampleFieldReadouts();
            }
        }

        private void CompleteFieldArrivalPresentation()
        {
            _awaitingFieldArrivalCompletion = false;
            OnFieldArrivalBoundary?.Invoke();
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

        internal bool IsKnownInOpposingHand(byte observer, uint code) =>
            observer < _knownOpponentHands.Length && _knownOpponentHands[observer]?.ContainsKey(code) == true;

        internal uint GetObservedOwnDeckTop(byte observer) => observer < _observedDeckTop.Length &&
            _observedDeckTop[observer].DeckCount == Players[observer].deckCount ? _observedDeckTop[observer].Code : 0;

        private void TrackSortedDeckTop(DuelPrompt prompt, byte[] response)
        {
            byte player = prompt.playerId;
            if (player >= _observedDeckTop.Length) return;
            ForgetObservedDeckTop(player);
            int count = Players[player].deckCount;
            var cards = prompt.candidates;
            if (cards.Count == 0 || cards.Count > count || response == null) return;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                // The sort prompt discloses its cards to the deciding player. A bottom sort or another player's deck provides no evidence.
                if (card.controller != player || card.location != YgoConstants.LOCATION_DECK ||
                    card.sequence != count - i - 1 || card.code == 0) return;
            }

            bool retainOrder = response.Length == 1 && response[0] == byte.MaxValue;
            int top = retainOrder ? 0 :
                response.Length == cards.Count && response.All(rank => rank < cards.Count) && response.Distinct().Count() == cards.Count
                    ? Array.IndexOf(response, (byte)0) : -1;
            _observedDeckTop[player] = top < 0 ? default : (cards[top].code, count);
            if (top >= 0 && !retainOrder) _observedTopSort = (player, cards.Count);
        }

        private void ForgetObservedDeckTop(byte player)
        {
            if (player < _observedDeckTop.Length) _observedDeckTop[player] = default;
            if (_observedTopSort.Player == player) _observedTopSort = default;
        }

        private void TrackOpposingHandDeparture(byte owner, uint code, uint destination, uint position)
        {
            if (owner >= 2 || _knownOpponentHands[1 - owner] is not { Count: > 0 } known) return;

            bool visibleDestination = destination == YgoConstants.LOCATION_GRAVE ||
                (destination & YgoConstants.LOCATION_OVERLAY) != 0 ||
                (destination is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE or
                    YgoConstants.LOCATION_REMOVED or YgoConstants.LOCATION_EXTRA) && (position & YgoConstants.POS_FACEUP) != 0;
            if (visibleDestination && code != 0)
            {
                ForgetOne(code);
                return;
            }

            // Any recorded card could be the one that left through a face-down Set or another concealed move.
            foreach (uint candidate in new List<uint>(known.Keys)) ForgetOne(candidate);

            void ForgetOne(uint card)
            {
                if (!known.TryGetValue(card, out int copies)) return;
                if (copies == 1) known.Remove(card);
                else known[card] = copies - 1;
            }
        }

        internal LiveCardState GetVisibleFieldCard(uint code, byte controller, uint location, uint sequence)
        {
            if (code == 0 || controller >= Players.Length) return null;
            var player = Players[controller];
            var zone = location switch
            {
                YgoConstants.LOCATION_MZONE => player.mzone,
                YgoConstants.LOCATION_SZONE => player.szone,
                _ => null,
            };
            var live = zone != null && sequence < zone.Length ? zone[sequence] : null;
            return live?.IsFaceUp == true && live.code == code ? live : null;
        }

        private LiveCardState CaptureChainFieldCard(SelectableCardItem source)
        {
            if ((OnChainActivated == null && OnChainResolving == null) || NativePtr == IntPtr.Zero ||
                source.controller >= Players.Length || source.code == 0 ||
                (source.position & YgoConstants.POS_FACEUP) == 0 ||
                source.location is not (YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE))
                return null;

            var zone = QueryLocationCards(source.controller, source.location);
            var card = source.sequence < zone.Count ? zone[(int)source.sequence] : null;
            return card?.IsFaceUp == true && card.code == source.code ? card : null;
        }

        internal LiveCardState GetAttackSelectionSource(DuelPrompt prompt)
        {
            if (prompt == null || !ReferenceEquals(ActivePrompt, prompt)) return null;
            if (prompt.IsDirectAttackChoice && prompt.promptLoc == YgoConstants.LOCATION_MZONE)
                return GetVisibleFieldCard(prompt.promptCardCode, prompt.promptCon, prompt.promptLoc, prompt.promptSeq);
            if (!(prompt.IsAttackDecision || prompt.IsAttackTargetSelection) || _chosenAttacker is not { } source)
                return null;
            // Some effects let the defender choose the target of the already selected attacker.
            return GetVisibleFieldCard(source.code, source.controller, source.location, source.sequence);
        }

        private void ClearAttack()
        {
            HasActiveAttack = false;
            _chosenAttacker = null;
        }

        private void NotifyCardMoved(CardMoveEvent move, CardMoveEvent swapPartner = null)
        {
            if (_resolvingChainLink != null && (move.reason & YgoConstants.REASON_DESTROY) != 0)
                _resolvingChainLink.DestroyedCard = true;
            TrackNegatedGraveMovement(move);
            TrackPublicMonsterMoves(move, swapPartner);
            FollowPublicChainSources(move, swapPartner);
            CaptureMoveSource(move);
            if (swapPartner != null) CaptureMoveSource(swapPartner);
            InvalidateFieldReadouts(move);
            if (swapPartner != null) InvalidateFieldReadouts(swapPartner);
            if (move.DestinationReadout is { } destination && _fieldReadoutBaseline != null)
                _fieldReadoutBaseline[(move.toCon, move.toLoc, move.toSeq)] = destination;
            if (move.summonType.HasValue || move.ReturnEffectId.HasValue) _awaitingFieldArrivalCompletion = true;
            if (_chosenAttacker is { } source)
            {
                var slot = (source.controller, source.location, source.sequence);
                if (AffectsSlot(move, slot) || swapPartner != null && AffectsSlot(swapPartner, slot)) _chosenAttacker = null;
            }
            OnCardLocationsChanged?.Invoke(move, swapPartner);
            PublishCardMove(move);
            if (swapPartner != null) PublishCardMove(swapPartner);
        }

        private void ResetSummonTracking()
        {
            Array.Clear(_summonedMonsters, 0, _summonedMonsters.Length);
            _pendingSpecialSummons.Clear();
        }

        private void TrackPublicMonsterMoves(CardMoveEvent move, CardMoveEvent partner)
        {
            bool firstSpent = SpentAtSource(move);
            bool secondSpent = SpentAtSource(partner);
            ClearSlots(move);
            if (partner != null) ClearSlots(partner);
            if (firstSpent) TransferSpentNegate(move);
            if (secondSpent) TransferSpentNegate(partner);

            bool SpentAtSource(CardMoveEvent card) => card is { code: BaronneCode, fromLoc: YgoConstants.LOCATION_MZONE } &&
                (card.fromPos & YgoConstants.POS_FACEUP) != 0 && _spentBaronneNegates.Contains((card.fromCon, card.fromSeq));
            void ClearSlots(CardMoveEvent card)
            {
                if (card.fromLoc == YgoConstants.LOCATION_MZONE)
                {
                    _spentBaronneNegates.Remove((card.fromCon, card.fromSeq));
                    if (card.toLoc != card.fromLoc || card.toCon != card.fromCon || card.toSeq != card.fromSeq)
                        _pendingSpecialSummons.Remove((card.fromCon, card.fromSeq));
                }
                if (card.toLoc == YgoConstants.LOCATION_MZONE) _spentBaronneNegates.Remove((card.toCon, card.toSeq));
            }
            void TransferSpentNegate(CardMoveEvent card)
            {
                if (card.toLoc == YgoConstants.LOCATION_MZONE && (card.toPos & YgoConstants.POS_FACEUP) != 0)
                    _spentBaronneNegates.Add((card.toCon, card.toSeq));
            }
        }

        private static bool AffectsSlot(CardMoveEvent move, (byte, uint, uint) slot)
        {
            var from = (move.fromCon, move.fromLoc, move.fromSeq);
            var to = (move.toCon, move.toLoc, move.toSeq);
            return from != to && (slot == from || slot == to);
        }

        private void TrackNegatedGraveMovement(CardMoveEvent move)
        {
            if (move.toLoc != YgoConstants.LOCATION_GRAVE) return;
            for (int i = CurrentChain.Count - 1; i >= 0; i--)
            {
                var link = CurrentChain[i];
                if (!link.ActivationNegated || link.NegatedGraveController.HasValue || link.code != move.code ||
                    link.ActivationSource != (move.fromCon, move.fromLoc, move.fromSeq)) continue;
                link.NegatedGraveController = move.toCon;
                break;
            }
        }

        private void FollowPublicChainSources(CardMoveEvent move, CardMoveEvent partner)
        {
            foreach (var link in CurrentChain)
            {
                if (link.PublicFieldSource is not { } slot) continue;
                var transfer = StartsAt(move, slot) ? move : StartsAt(partner, slot) ? partner : null;
                if (transfer == null)
                {
                    if (AffectsSlot(move, slot) || partner != null && AffectsSlot(partner, slot))
                        link.PublicFieldSource = null;
                    continue;
                }

                link.PublicFieldSource = transfer.code == link.code && (transfer.fromPos & YgoConstants.POS_FACEUP) != 0 &&
                    (transfer.toPos & YgoConstants.POS_FACEUP) != 0 &&
                    (transfer.toLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE)
                        ? (transfer.toCon, transfer.toLoc, transfer.toSeq) : null;
            }

            static bool StartsAt(CardMoveEvent item, (byte, uint, uint) slot) =>
                item != null && slot == (item.fromCon, item.fromLoc, item.fromSeq);
        }

        private void CaptureMoveSource(CardMoveEvent move)
        {
            if (move.fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE &&
                _fieldReadoutBaseline?.TryGetValue((move.fromCon, move.fromLoc, move.fromSeq), out var source) == true &&
                source.code == move.code && source.position == move.fromPos)
                move.SourceReadout = source;
        }

        private void PublishCardMove(CardMoveEvent move)
        {
            if (move.fromLoc == YgoConstants.LOCATION_MZONE && move.fromPos == YgoConstants.POS_FACEUP_ATTACK &&
                move.toLoc == YgoConstants.LOCATION_REMOVED && (move.toPos & YgoConstants.POS_FACEUP) != 0 &&
                move.TemporarilyBanished && move.code != 0)
            {
                _temporaryAttackers ??= new();
                _temporaryAttackers.Add((move.fromCon, move.toCon, move.code, move.TemporaryBanishCardId));
            }
            else if (_temporaryAttackers?.Count > 0 && move.fromLoc == YgoConstants.LOCATION_REMOVED &&
                move.toLoc != YgoConstants.LOCATION_REMOVED)
            {
                _temporaryAttackers.RemoveWhere(card => card.BanishController == move.fromCon && card.Code == move.code &&
                    (!move.TemporaryBanishCardId.HasValue || !card.CardId.HasValue || card.CardId == move.TemporaryBanishCardId));
            }

            if (_resolvingChainLink is { negated: false } source && source.triggerPlayer != TurnPlayer &&
                move.fromCon == TurnPlayer &&
                (move.fromLoc is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE) &&
                (move.toCon != move.fromCon || move.toLoc != move.fromLoc))
                TurnInterruptions++;

            if (YgoConstants.IsFieldZone(move.toLoc, move.toSeq) &&
                (move.fromCon != move.toCon || move.fromLoc != move.toLoc || move.fromSeq != move.toSeq))
                Players[move.toCon].FieldZoneVersion++;

            OnCardMoved?.Invoke(move);
        }

        private static CardMoveEvent CreateSwapMove(SelectableCardItem card, SelectableCardItem destination) =>
            new CardMoveEvent
            {
                code = card.code,
                fromCon = card.controller, fromLoc = card.location, fromSeq = card.sequence, fromPos = card.position,
                toCon = destination.controller, toLoc = destination.location, toSeq = destination.sequence, toPos = card.position,
            };

        private void ReportSkippedChainNegations(int nextLink)
        {
            // The engine can skip SOLVING/SOLVED entirely after a link's activation is negated.
            // Publish these outcomes before the next lower link so presentation follows chain resolution order.
            for (int i = CurrentChain.Count - 1; i >= nextLink; i--)
            {
                var link = CurrentChain[i];
                if (!link.negated || link.Resolved) continue;
                link.ResolutionReadout ??= link.ActivationReadout;
                link.Resolved = true;
                OnChainResolving?.Invoke(link);
                OnChainResolved?.Invoke(link);
            }
        }

        private void HandleMessage(byte msgType, BinaryReader br, long msgEnd,
            ref (BattleComparisonValue? Attacker, BattleComparisonValue? Defender) comparison, ref CardMoveEvent firstSwapMove,
            ref Dictionary<(byte Controller, uint Location, uint Sequence), LiveCardState> positionReadouts)
        {
            if (msgType is YgoConstants.MSG_RETRY or YgoConstants.MSG_NEW_TURN or YgoConstants.MSG_SWAP_GRAVE_DECK or
                YgoConstants.MSG_REVERSE_DECK or YgoConstants.MSG_DECK_TOP)
            {
                Array.Clear(_observedDeckTop, 0, _observedDeckTop.Length);
                _observedTopSort = default;
            }
            if (_speculatingPlayer >= 0 && msgType is YgoConstants.MSG_CONFIRM_DECKTOP or YgoConstants.MSG_DECK_TOP or
                YgoConstants.MSG_CONFIRM_EXTRATOP or YgoConstants.MSG_RANDOM_SELECTED or YgoConstants.MSG_TOSS_COIN or
                YgoConstants.MSG_TOSS_DICE)
                SpeculationUncertain = true;
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
                        _pendingSelectionHint = (player, desc);
                    }
                    else if (htype == YgoConstants.HINT_MESSAGE)
                    {
                        string message = desc > 499 ? CardDatabase.ResolveDescription(desc) : null;
                        if (message != null) AddLog(message);
                    }
                    else if (htype == YgoConstants.HINT_ZONE &&
                        _resolvingChainLink is { code: InfiniteImpermanenceCode, negated: false,
                            triggerLoc: YgoConstants.LOCATION_SZONE, triggerSeq: < 5 } link &&
                        player == link.triggerPlayer && desc == (1UL << ((int)link.triggerSeq + 8)))
                    {
                        // The Lua effect sends this hint only after both negations have been applied.
                        link.AppliedColumnNegation = true;
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
                    _activatedMonsterEffects?.Clear();
                    _usedSharedTurnEffects?.Clear();
                    _usedSharedTurnActivations?.Clear();
                    ResetSummonTracking();
                    _temporaryAttackers?.Clear();
                    TurnPlayer = br.ReadByte();
                    TurnNumber++;
                    TurnInterruptions = 0;
                    ClearAttack();
                    string who = TurnPlayer == HumanPlayerIndex ? "내 턴" : "상대 턴";
                    AddLog($"=== 턴 {TurnNumber}: {who} ===");
                    OnTurnStarted?.Invoke(TurnNumber, TurnPlayer);
                    break;
                }

                case YgoConstants.MSG_NEW_PHASE:
                {
                    CurrentPhase = br.ReadUInt16();
                    ClearAttack();
                    AddLog($"[{YgoConstants.GetPhaseNameKR(CurrentPhase)}]");
                    OnPhaseChanged?.Invoke(CurrentPhase, TurnPlayer);
                    break;
                }

                case YgoConstants.MSG_DRAW:
                {
                    byte p = br.ReadByte();
                    ForgetObservedDeckTop(p);
                    // The planner uses only the opponent's hand size and skips their optional chains.
                    if (_speculatingPlayer >= 0 && !SpeculationPassedTurnLimit &&
                        (_planningTurnLimit < 0 || p == _speculatingPlayer)) SpeculationUncertain = true;
                    if (_confirmedCards.Count != 0)
                        _confirmedCards.RemoveWhere(c => c.Player == p &&
                            (c.Location == YgoConstants.LOCATION_HAND || c.Location == YgoConstants.LOCATION_DECK));
                    uint cnt = br.ReadUInt32();
                    string who = p == HumanPlayerIndex ? "플레이어" : "상대";
                    AddLog($"{who} 드로우 {cnt}장");
                    if ((OnCardsDrawn != null || OnStyledCardsDrawn != null) && cnt <= (msgEnd - br.BaseStream.Position) / 8)
                    {
                        var codes = new uint[cnt];
                        for (int i = 0; i < codes.Length; i++)
                        {
                            uint code = br.ReadUInt32();
                            uint position = br.ReadUInt32();
                            if (p == HumanPlayerIndex || (position & YgoConstants.POS_FACEUP) != 0)
                                codes[i] = code;
                        }
                        OnCardsDrawn?.Invoke(p, codes);
                        if (OnStyledCardsDrawn != null)
                        {
                            var appearances = new DeckCardAppearance?[codes.Length];
                            bool hasIdentities = msgEnd - br.BaseStream.Position >= codes.Length * sizeof(uint);
                            for (int i = 0; i < codes.Length; i++)
                            {
                                uint identity = hasIdentities ? br.ReadUInt32() : 0;
                                appearances[i] = AppearanceOf(codes[i] == 0 ? 0 : identity);
                            }
                            OnStyledCardsDrawn.Invoke(p, codes, appearances);
                        }
                    }
                    break;
                }

                case YgoConstants.MSG_DAMAGE:
                {
                    byte p = br.ReadByte();
                    uint amt = br.ReadUInt32();
                    uint reason = msgEnd - br.BaseStream.Position == sizeof(uint) ? br.ReadUInt32() : 0;
                    LifePointChangeKind kind = reason == YgoConstants.REASON_BATTLE ? LifePointChangeKind.BattleDamage :
                        (reason & YgoConstants.REASON_EFFECT) != 0 ? LifePointChangeKind.EffectDamage : LifePointChangeKind.Damage;
                    Players[p].lp = Math.Max(0, Players[p].lp - (int)amt);
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} 데미지 -{amt} LP (남은 LP: {Players[p].lp})");
                    NotifyLifePointChange(p, -(int)amt, kind);
                    break;
                }

                case YgoConstants.MSG_RECOVER:
                {
                    byte p = br.ReadByte();
                    uint amt = br.ReadUInt32();
                    Players[p].lp += (int)amt;
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} 회복 +{amt} LP (LP: {Players[p].lp})");
                    NotifyLifePointChange(p, (int)amt, LifePointChangeKind.Recovery);
                    break;
                }

                case YgoConstants.MSG_PAY_LPCOST:
                {
                    byte p = br.ReadByte();
                    uint amt = br.ReadUInt32();
                    Players[p].lp = Math.Max(0, Players[p].lp - (int)amt);
                    AddLog($"{(p == HumanPlayerIndex ? "플레이어" : "상대")} LP 코스트 -{amt} (LP: {Players[p].lp})");
                    NotifyLifePointChange(p, -(int)amt, LifePointChangeKind.Payment);
                    break;
                }

                case YgoConstants.MSG_LPUPDATE:
                {
                    byte p = br.ReadByte();
                    int nlp = br.ReadInt32();
                    int previous = Players[p].lp;
                    Players[p].lp = Math.Max(0, nlp);
                    if (Players[p].lp != previous)
                        NotifyLifePointChange(p, Players[p].lp - previous, LifePointChangeKind.Adjustment);
                    break;
                }

                case YgoConstants.MSG_SUMMONING:
                {
                    uint code = br.ReadUInt32();
                    br.ReadBytes(10);
                    AddLog($"일반 소환: [{CardDatabase.GetName(code)}]");
                    break;
                }

                case YgoConstants.MSG_SUMMONED:
                    _summonedMonsters[_resolvingChainLink?.triggerPlayer ?? (byte)TurnPlayer]++;
                    CompleteFieldArrivalPresentation();
                    break;

                case YgoConstants.MSG_SPSUMMONING:
                {
                    var card = ReadLocIntoItem(br, br.ReadUInt32());
                    _pendingSpecialSummons.Add((card.controller, card.sequence));
                    AddLog(card.code != 0 ? $"특수 소환: [{CardDatabase.GetName(card.code)}]" : "뒷면 표시 특수 소환");
                    break;
                }

                case YgoConstants.MSG_SPSUMMONED:
                    _summonedMonsters[_resolvingChainLink?.triggerPlayer ?? (byte)TurnPlayer] += _pendingSpecialSummons.Count;
                    _pendingSpecialSummons.Clear();
                    CompleteFieldArrivalPresentation();
                    break;

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
                    NotifyPositionChanged(new CardMoveEvent
                    {
                        code = code,
                        fromCon = controller, fromLoc = location, fromSeq = sequence, fromPos = previous,
                        toCon = controller, toLoc = location, toSeq = sequence, toPos = position,
                    }, ref positionReadouts);
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
                    uint identity = 0;
                    if (code == CardIdentityTag && msgEnd - br.BaseStream.Position >= 32)
                    {
                        identity = br.ReadUInt32();
                        code = br.ReadUInt32();
                    }
                    byte fCon = br.ReadByte();
                    byte fLoc = br.ReadByte();
                    uint fSeq = br.ReadUInt32();
                    uint fPos = br.ReadUInt32();
                    byte tCon = br.ReadByte();
                    byte tLoc = br.ReadByte();
                    uint tSeq = br.ReadUInt32();
                    uint tPos = br.ReadUInt32();
                    uint reason = msgEnd - br.BaseStream.Position >= sizeof(uint) ? br.ReadUInt32() : 0;
                    long extraLength = msgEnd - br.BaseStream.Position;
                    uint? presentation = extraLength == sizeof(uint) ? br.ReadUInt32() : null;
                    bool tagged = extraLength is 2 * sizeof(uint) or 3 * sizeof(uint);
                    uint tag = tagged ? br.ReadUInt32() : 0;
                    uint extraValue = tagged ? br.ReadUInt32() : 0;
                    uint? temporaryCard = null;
                    if (tag == TemporaryBanishGroupTag)
                    {
                        presentation = tag;
                        temporaryCard = extraValue;
                    }
                    else if (tag == FieldReturnTag && extraLength == 3 * sizeof(uint)) temporaryCard = br.ReadUInt32();
                    uint swapPart = tag == SwapMoveTag ? extraValue : 0;
                    uint? summonType = tLoc == YgoConstants.LOCATION_MZONE &&
                        presentation is not (DestructionGroupTag or MaterialGroupTag or PositionGroupTag or TemporaryBanishGroupTag)
                        ? presentation : null;
                    if (_speculatingPlayer >= 0 && fLoc == YgoConstants.LOCATION_DECK && tLoc != YgoConstants.LOCATION_DECK &&
                        (fCon != _speculatingPlayer || !_selectedFromDeck.Remove(code))) SpeculationUncertain = true;
                    if (fLoc == YgoConstants.LOCATION_DECK || tLoc == YgoConstants.LOCATION_DECK)
                    {
                        // A submitted top sort is echoed as a fixed number of masked moves into the same deck's top slot.
                        if (_observedTopSort.Remaining > 0 && code == 0 && fCon == _observedTopSort.Player && tCon == fCon &&
                            fLoc == YgoConstants.LOCATION_DECK && tLoc == fLoc && tSeq == _observedDeckTop[fCon].DeckCount - 1)
                            _observedTopSort.Remaining--;
                        else
                        {
                            if (fLoc == YgoConstants.LOCATION_DECK) ForgetObservedDeckTop(fCon);
                            if (tLoc == YgoConstants.LOCATION_DECK) ForgetObservedDeckTop(tCon);
                        }
                    }
                    _confirmedCards.Remove((fCon, fLoc, fSeq, code));
                    if (fLoc == YgoConstants.LOCATION_HAND && (tLoc != fLoc || tCon != fCon))
                        TrackOpposingHandDeparture(fCon, code, tLoc, tPos);

                    var ev = new CardMoveEvent
                    {
                        code = code,
                        fromCon = fCon, fromLoc = fLoc, fromSeq = fSeq, fromPos = fPos,
                        toCon = tCon, toLoc = tLoc, toSeq = tSeq, toPos = tPos,
                        reason = reason,
                        summonType = summonType,
                        ReturnEffectId = tag == FieldReturnTag ? extraValue : null,
                        TemporaryBanishCardId = temporaryCard,
                        Appearance = AppearanceOf(identity),
                        PresentationGroup = _movementGroups.TryPeek(out var group) && presentation == group.Tag ? group.Key : null,
                    };
                    if (firstSwapMove != null)
                    {
                        var first = firstSwapMove;
                        firstSwapMove = null;
                        if (swapPart == 2)
                        {
                            NotifyCardMoved(first, ev);
                            break;
                        }
                        NotifyCardMoved(first);
                    }
                    if (swapPart == 1) firstSwapMove = ev;
                    else NotifyCardMoved(ev);
                    break;
                }

                case YgoConstants.MSG_SWAP:
                {
                    var first = ReadLocIntoItem(br, br.ReadUInt32());
                    var second = ReadLocIntoItem(br, br.ReadUInt32());
                    if (first.location == YgoConstants.LOCATION_DECK) ForgetObservedDeckTop(first.controller);
                    if (second.location == YgoConstants.LOCATION_DECK) ForgetObservedDeckTop(second.controller);
                    _confirmedCards.Remove((first.controller, first.location, first.sequence, first.code));
                    _confirmedCards.Remove((second.controller, second.location, second.sequence, second.code));
                    if (first.controller != second.controller || first.location != second.location)
                    {
                        if (first.location == YgoConstants.LOCATION_HAND && first.controller < 2)
                            _knownOpponentHands[1 - first.controller]?.Clear();
                        if (second.location == YgoConstants.LOCATION_HAND && second.controller < 2)
                            _knownOpponentHands[1 - second.controller]?.Clear();
                    }
                    NotifyCardMoved(CreateSwapMove(first, second), CreateSwapMove(second, first));
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
                    var positionGroup = msgEnd - br.BaseStream.Position == sizeof(uint) && br.ReadUInt32() == PositionGroupTag &&
                        _movementGroups.TryPeek(out var group) && group.Tag == PositionGroupTag ? group.Key : null;
                    NotifyPositionChanged(new CardMoveEvent
                    {
                        code = code,
                        fromCon = controller, fromLoc = location, fromSeq = sequence, fromPos = previous,
                        toCon = controller, toLoc = location, toSeq = sequence, toPos = position,
                        PresentationGroup = positionGroup,
                    }, ref positionReadouts);
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
                    Dictionary<uint, HashSet<uint>> handReveals = null;
                    int visibleCount = 0;
                    for (uint i = 0; i < cnt; i++)
                    {
                        uint code = br.ReadUInt32();
                        byte controller = br.ReadByte();
                        uint location = br.ReadByte();
                        uint sequence = br.ReadUInt32();
                        if (msgType == YgoConstants.MSG_CONFIRM_CARDS && recipient < 2 && controller == 1 - recipient &&
                            location == YgoConstants.LOCATION_HAND && code != 0)
                        {
                            handReveals ??= new();
                            if (!handReveals.TryGetValue(code, out var slots)) handReveals.Add(code, slots = new());
                            slots.Add(sequence);
                        }
                        if (_speculatingPlayer >= 0 && recipient == _speculatingPlayer && controller != _speculatingPlayer &&
                            location is YgoConstants.LOCATION_HAND or YgoConstants.LOCATION_DECK or YgoConstants.LOCATION_EXTRA)
                            SpeculationUncertain = true;
                        if (code == 0 || (!shownToHuman && controller != HumanPlayerIndex)) continue;
                        if (shownToHuman && controller != HumanPlayerIndex)
                            _confirmedCards.Add((controller, location, sequence, code));
                        visibleCount++;
                        if (names.Count < 4)
                            names.Add($"[{CardDatabase.GetName(code)}]");
                    }
                    if (handReveals != null)
                    {
                        var known = _knownOpponentHands[recipient] ??= new();
                        // Repeated confirmations can show the same copy again. Only one packet proves distinct slots.
                        foreach (var reveal in handReveals)
                        {
                            known.TryGetValue(reveal.Key, out int copies);
                            known[reveal.Key] = Math.Max(copies, reveal.Value.Count);
                        }
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
                    if (msgType == YgoConstants.MSG_SHUFFLE_DECK) ForgetObservedDeckTop(player);
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

                case YgoConstants.MSG_TAG_SWAP:
                {
                    byte player = br.ReadByte();
                    ForgetObservedDeckTop(player);
                    if (player < 2) _knownOpponentHands[1 - player]?.Clear();
                    break;
                }

                case YgoConstants.MSG_CHAINING:
                {
                    uint code = br.ReadUInt32();
                    var source = ReadLocIntoItem(br, code);
                    byte tCon = br.ReadByte();
                    byte tLoc = br.ReadByte();
                    uint tSeq = br.ReadUInt32();
                    ulong desc = br.ReadUInt64();
                    int chainNo = (int)br.ReadUInt32();
                    // The bundled engine appends flags to the length-delimited MSG_CHAINING.
                    byte activationFlags = br.BaseStream.Position < msgEnd ? br.ReadByte() : (byte)0;
                    uint identity = msgEnd - br.BaseStream.Position >= sizeof(uint) ? br.ReadUInt32() : 0;
                    if (_speculatingPlayer >= 0 && tCon != _speculatingPlayer) SpeculationUncertain = true;
                    var activation = CaptureChainFieldCard(source);
                    var link = new ChainLinkInfo
                    {
                        chainNumber = chainNo,
                        code = code,
                        triggerPlayer = tCon,
                        triggerLoc = tLoc,
                        triggerSeq = tSeq,
                        description = desc,
                        IsCardActivation = (activationFlags & 1) != 0,
                        HasOncePerTurnActivationLimit = (activationFlags & 2) != 0,
                        ActivationSource = (source.controller, source.location, source.sequence),
                        ActivationReadout = activation,
                        Appearance = AppearanceOf(identity),
                        PublicFieldSource = activation != null ? (activation.controller, activation.location, activation.sequence) : null,
                    };
                    CurrentChain.Add(link);
                    if (code == BaronneCode && desc == BaronneNegation && source.location == YgoConstants.LOCATION_MZONE)
                        _spentBaronneNegates.Add((source.controller, source.sequence));
                    if (tLoc == YgoConstants.LOCATION_MZONE && code != 0 && (desc >> 20) == code)
                        (_activatedMonsterEffects ??= new()).Add((tCon, desc));
                    if (HasSharedOncePerTurnText(CardDatabase.Get(code)))
                        (_usedSharedTurnEffects ??= new()).Add((tCon, CardDatabase.GetDeckPrintingKey(code)));
                    AddLog($"체인 {chainNo}: [{CardDatabase.GetName(code)}] 발동");
                    OnChainActivated?.Invoke(link);
                    break;
                }

                case YgoConstants.MSG_BECOME_TARGET:
                {
                    if (OnCardsTargeted == null || msgEnd - br.BaseStream.Position < sizeof(uint)) break;
                    uint count = br.ReadUInt32();
                    const int locationSize = 2 * sizeof(byte) + 2 * sizeof(uint);
                    if (count != (msgEnd - br.BaseStream.Position) / locationSize ||
                        (msgEnd - br.BaseStream.Position) % locationSize != 0) break;

                    var targets = new List<(byte Controller, uint Location, uint Sequence)>();
                    for (uint i = 0; i < count; i++)
                    {
                        byte controller = br.ReadByte();
                        uint location = br.ReadByte();
                        uint sequence = br.ReadUInt32();
                        br.ReadUInt32(); // Position is not needed to mark a slot, including a concealed card.
                        if (controller < Players.Length && location is YgoConstants.LOCATION_MZONE or YgoConstants.LOCATION_SZONE)
                            targets.Add((controller, location, sequence));
                    }
                    if (targets.Count == 0) break;

                    var link = _resolvingChainLink ?? CurrentChain.LastOrDefault(item => !item.Resolved);
                    OnCardsTargeted.Invoke(link?.PublicFieldSource ?? link?.ActivationSource, targets);
                    break;
                }

                case YgoConstants.MSG_CHAIN_SOLVING:
                {
                    byte idx = br.ReadByte();
                    ReportSkippedChainNegations(idx);
                    _resolvingChainLink = null;
                    if (idx > 0 && idx <= CurrentChain.Count)
                    {
                        var link = CurrentChain[idx - 1];
                        _resolvingChainLink = link;
                        SolvedChainLinks.Add((idx, link.code));
                        AddLog($"체인 {idx} 처리: [{CardDatabase.GetName(link.code)}]");
                        SampleFieldReadouts();
                        // If the original card has become private or left the field, keep the face it activated with.
                        link.ResolutionReadout = link.PublicFieldSource is { } slot &&
                            _fieldReadoutBaseline?.TryGetValue(slot, out var visible) == true && visible.code == link.code
                                ? visible : link.ActivationReadout;
                        OnChainResolving?.Invoke(link);
                    }
                    break;
                }

                case YgoConstants.MSG_CHAIN_SOLVED:
                    SampleFieldReadouts();
                    if (_resolvingChainLink != null)
                    {
                        var link = _resolvingChainLink;
                        link.Resolved = true;
                        if (link.HasOncePerTurnActivationLimit && !link.ActivationNegated &&
                            CardDatabase.Get(link.code)?.desc?.Contains("이 카드명의 카드는 1턴에 1장밖에 발동할 수 없") == true)
                            (_usedSharedTurnActivations ??= new()).Add((link.triggerPlayer, CardDatabase.GetDeckPrintingKey(link.code)));
                        OnChainResolved?.Invoke(link);
                    }
                    if (_deckSelectionEffect != null && ReferenceEquals(_deckSelectionEffect, _resolvingChainLink))
                        ClearDeckSelection();
                    _resolvingChainLink = null;
                    break;

                case YgoConstants.MSG_CHAIN_NEGATED:
                case YgoConstants.MSG_CHAIN_DISABLED:
                {
                    byte idx = br.ReadByte();
                    if (idx > 0 && idx <= CurrentChain.Count)
                    {
                        var link = CurrentChain[idx - 1];
                        if (!link.negated && link.triggerPlayer == TurnPlayer) TurnInterruptions++;
                        link.negated = true;
                        link.ActivationNegated |= msgType == YgoConstants.MSG_CHAIN_NEGATED;
                    }
                    AddLog($"체인 {idx} 무효화");
                    break;
                }

                case YgoConstants.MSG_CHAIN_END:
                    SampleFieldReadouts();
                    ReportSkippedChainNegations(0);
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
                    if (_chosenAttacker is { } source &&
                        (source.controller != AttackerCon || source.location != AttackerLoc || source.sequence != AttackerSeq))
                        _chosenAttacker = null;
                    HasActiveAttack = true;
                    AddLog(TargetLoc == 0 ? "직접 공격 선언!" : "몬스터 공격 선언!");
                    OnAttackDeclared?.Invoke((byte)AttackerCon, AttackerLoc, AttackerSeq, (byte)TargetCon, TargetLoc, TargetSeq);
                    break;
                }

                case YgoConstants.MSG_BATTLE:
                {
                    ClearAttack();
                    var values = comparison;
                    comparison = default;
                    OnBattleResolved?.Invoke(BattleCardStats.Read(br, values.Attacker), BattleCardStats.Read(br, values.Defender));
                    break;
                }

                case YgoConstants.MSG_CUSTOM_MSG:
                {
                    if (msgEnd - br.BaseStream.Position == sizeof(uint) + sizeof(byte))
                    {
                        uint tag = br.ReadUInt32();
                        if (tag == DestinyDrawTag)
                        {
                            OfferDestinyDraw(br.ReadByte());
                            break;
                        }
                        if (tag is DestructionGroupTag or MaterialGroupTag or PositionGroupTag or TemporaryBanishGroupTag)
                        {
                            if (br.ReadByte() != 0) _movementGroups.Push((tag, new object()));
                            else if (_movementGroups.TryPeek(out var group) && group.Tag == tag)
                            {
                                _movementGroups.Pop();
                                if (_movementGroups.Count == 0 && CurrentChain.Count == 0 && !_awaitingFieldArrivalCompletion)
                                    SampleFieldReadouts();
                            }
                            break;
                        }
                        br.BaseStream.Position -= sizeof(uint);
                    }
                    var values = BattleComparisonValue.ReadMessage(br, msgEnd);
                    if (values.Attacker.HasValue) comparison = values;
                    break;
                }

                case YgoConstants.MSG_ATTACK_DISABLED:
                case YgoConstants.MSG_DAMAGE_STEP_END:
                    ClearAttack();
                    break;

                case YgoConstants.MSG_FIELD_DISABLED:
                    FieldDisabledMask = br.ReadUInt32();
                    break;

                case YgoConstants.MSG_SELECT_BATTLECMD:
                {
                    ClearAttack();
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
                        promptPosition = br.ReadUInt32(),
                    };
                    p.promptDesc = br.ReadUInt64();
                    if (p.IsTriggerActivationConfirmation)
                        p.promptEffectDesc = YgoNativeBridge.YGO_GetSingleTriggerDescription(NativePtr, p.playerId, p.promptCardCode, p.promptDesc);
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
                    p.hintTimingSelf = br.ReadUInt32();
                    p.hintTimingOther = br.ReadUInt32();
                    uint cnt = br.ReadUInt32();
                    for (uint i = 0; i < cnt; i++)
                    {
                        var item = ReadLocIntoItem(br, br.ReadUInt32());
                        item.description = br.ReadUInt64();
                        item.mode = br.ReadByte();
                        p.candidates.Add(item);
                    }
                    if (cnt != 0 && !p.forced && !p.IsTriggerChain)
                    {
                        var triggerFlags = new byte[p.candidates.Count];
                        p.chainTriggerInfoMissing = YgoNativeBridge.YGO_GetChainTriggerFlags(
                            NativePtr, p.playerId, triggerFlags, cnt) == 0;
                        for (int i = 0; i < triggerFlags.Length; i++)
                        {
                            p.candidates[i].isTriggeredEffect = (triggerFlags[i] & TriggeredEffectFlag) != 0;
                            p.candidates[i].isEventSpecificEffect = (triggerFlags[i] & EventSpecificEffectFlag) != 0;
                        }
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
                    var source = (Controller: br.ReadByte(), Location: (uint)br.ReadByte(), Sequence: br.ReadUInt32());
                    uint sourcePosition = br.ReadUInt32();
                    var target = (Controller: br.ReadByte(), Location: (uint)br.ReadByte(), Sequence: br.ReadUInt32());
                    uint targetPosition = br.ReadUInt32();
                    AddLog($"장착: {(source.Controller == HumanPlayerIndex ? "내" : "상대")} 마함[{source.Sequence + 1}] → {(target.Controller == HumanPlayerIndex ? "내" : "상대")} 몬스터[{target.Sequence + 1}]");
                    if (OnCardEquipped == null || source.Location != YgoConstants.LOCATION_SZONE || target.Location != YgoConstants.LOCATION_MZONE ||
                        (sourcePosition & YgoConstants.POS_FACEUP) == 0 || (targetPosition & YgoConstants.POS_FACEUP) == 0) break;

                    var changes = CaptureFieldReadoutChanges(fromNative: true);
                    if (_fieldReadoutBaseline?.TryGetValue(source, out var equipment) == true &&
                        _fieldReadoutBaseline.TryGetValue(target, out var monster) &&
                        equipment.EquipTarget == (monster.controller, monster.sequence))
                        OnCardEquipped(equipment, monster, changes);
                    else if (changes != null) OnFieldReadoutsChanged?.Invoke(changes);
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

        private void InvalidateFieldReadouts(CardMoveEvent move)
        {
            if (_fieldReadoutBaseline == null) return;
            // A move can replace a same-name copy. Overlay changes refresh at their material handoff.
            _fieldReadoutBaseline.Remove((move.fromCon, move.fromLoc & ~YgoConstants.LOCATION_OVERLAY, move.fromSeq));
            _fieldReadoutBaseline.Remove((move.toCon, move.toLoc & ~YgoConstants.LOCATION_OVERLAY, move.toSeq));
        }

        private void NotifyPositionChanged(CardMoveEvent move,
            ref Dictionary<(byte Controller, uint Location, uint Sequence), LiveCardState> positionReadouts)
        {
            if (_fieldReadoutBaseline != null && NativePtr != IntPtr.Zero && move.ChangesPositionInPlace &&
                (move.toPos & YgoConstants.POS_FACEUP) != 0)
            {
                // This packet contains the resolved position change, before any later chain effect.
                positionReadouts ??= CaptureFieldReadouts(fromNative: true);
                if (positionReadouts.TryGetValue((move.toCon, move.toLoc, move.toSeq), out var live) &&
                    live.code == move.code && live.position == move.toPos)
                    move.DestinationReadout = live;
            }
            NotifyCardMoved(move);
        }

        private Dictionary<(byte Controller, uint Location, uint Sequence), LiveCardState> CaptureFieldReadouts(bool fromNative)
        {
            var field = new Dictionary<(byte Controller, uint Location, uint Sequence), LiveCardState>();
            for (byte player = 0; player < Players.Length; player++)
            {
                AddVisible(fromNative ? QueryLocationCards(player, YgoConstants.LOCATION_MZONE) : Players[player].mzone);
                AddVisible(fromNative ? QueryLocationCards(player, YgoConstants.LOCATION_SZONE) : Players[player].szone);
            }
            return field;

            void AddVisible(IReadOnlyList<LiveCardState> cards)
            {
                for (int index = 0; index < cards.Count; index++)
                {
                    var card = cards[index];
                    if (card?.IsFaceUp == true && card.code != 0)
                        field.Add((card.controller, card.location, card.sequence), card);
                }
            }
        }

        private void SampleFieldReadouts(bool fromNative = true)
        {
            var changes = CaptureFieldReadoutChanges(fromNative);
            if (changes != null) OnFieldReadoutsChanged?.Invoke(changes);
        }

        private List<(LiveCardState Before, LiveCardState After)> CaptureFieldReadoutChanges(bool fromNative)
        {
            var before = _fieldReadoutBaseline;
            if (before == null) return null;

            var field = CaptureFieldReadouts(fromNative);
            _fieldReadoutBaseline = field;
            List<(LiveCardState Before, LiveCardState After)> changes = null;
            foreach (var (slot, previous) in before)
            {
                if (!field.TryGetValue(slot, out var current) || previous.code != current.code ||
                    previous.position != current.position || SameReadouts(previous, current)) continue;
                (changes ??= new()).Add((previous, current));
            }
            return changes;
        }

        private static bool SameReadouts(LiveCardState a, LiveCardState b) =>
            a.attack == b.attack && a.defense == b.defense && a.baseAttack == b.baseAttack && a.baseDefense == b.baseDefense &&
            a.level == b.level && a.rank == b.rank && a.link == b.link && a.linkMarker == b.linkMarker &&
            a.lscale == b.lscale && a.rscale == b.rscale && a.type == b.type && a.race == b.race && a.attribute == b.attribute &&
            a.CurrentNameCode == b.CurrentNameCode && a.effectsDisabled == b.effectsDisabled &&
            a.Counters.AsSpan().SequenceEqual(b.Counters);

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

        internal bool HasCardInDeck(byte playerId, Predicate<CardInfo> matches)
        {
            if (NativePtr == IntPtr.Zero) return false;
            var cards = QueryLocationCards(playerId, YgoConstants.LOCATION_DECK, keepNulls: false, flags: YgoConstants.QUERY_CODE);
            return cards.Exists(card => CardDatabase.Get(card.code) is CardInfo info && matches(info));
        }

        internal void RefreshVisibleEquipTargets(byte playerId)
        {
            if (TracksFieldReadouts || NativePtr == IntPtr.Zero) return;
            const uint flags = YgoConstants.QUERY_CODE | YgoConstants.QUERY_POSITION | YgoConstants.QUERY_EQUIP_CARD;
            var observed = QueryLocationCards(playerId, YgoConstants.LOCATION_SZONE, flags: flags);
            var current = Players[playerId].szone;
            for (int slot = 0; slot < current.Length && slot < observed.Count; slot++)
            {
                if (current[slot] is { IsFaceUp: true } equipment && observed[slot] is { IsFaceUp: true } queried &&
                    equipment.code == queried.code)
                    equipment.EquipTarget = queried.EquipTarget;
            }
        }

        private List<LiveCardState> QueryLocationCards(byte playerId, uint loc, bool keepNulls = true, uint flags = YgoConstants.QUERY_DEFAULT)
        {
            var result = new List<LiveCardState>();
            if (loc == YgoConstants.LOCATION_SZONE && TracksFieldReadouts) flags |= YgoConstants.QUERY_EQUIP_CARD;
            byte[] raw = YgoNativeBridge.QueryLocationBytes(NativePtr, flags, playerId, loc);
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
                    Appearance = GetDeckAppearance(playerId, loc, (uint)seqIdx),
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
                        case YgoConstants.QUERY_EQUIP_CARD when blkSize >= 14 && raw[valPos + 1] == YgoConstants.LOCATION_MZONE &&
                            (BitConverter.ToUInt32(raw, valPos + 6) & YgoConstants.POS_FACEUP) != 0:
                            card.EquipTarget = (raw[valPos], BitConverter.ToUInt32(raw, valPos + 2));
                            break;
                        case YgoConstants.QUERY_OWNER: card.owner = raw[valPos]; break;
                        case YgoConstants.QUERY_IS_PUBLIC: card.isPublic = raw[valPos] != 0; break;
                        case YgoConstants.QUERY_STATUS:
                            card.effectsDisabled = (BitConverter.ToUInt32(raw, valPos) &
                                (YgoConstants.STATUS_DISABLED | YgoConstants.STATUS_FORBIDDEN)) != 0;
                            break;
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
                        case YgoConstants.QUERY_COUNTERS when blkSize >= 8:
                        {
                            int count = (int)Math.Min(BitConverter.ToUInt32(raw, valPos), (uint)((blkSize - 8) / 4));
                            if (count == 0) break;
                            // The core sends its ordered counter map as 16-bit types paired with 16-bit quantities.
                            var counters = new (ushort Type, ushort Count)[count];
                            int visible = 0;
                            for (int i = 0, offset = valPos + 4; i < count; i++, offset += 4)
                            {
                                ushort quantity = BitConverter.ToUInt16(raw, offset + 2);
                                if (quantity > 0) counters[visible++] = (BitConverter.ToUInt16(raw, offset), quantity);
                            }
                            if (visible != 0)
                            {
                                if (visible < count) Array.Resize(ref counters, visible);
                                card.Counters = counters;
                            }
                            break;
                        }
                    }
                    pos = blkStart + blkSize;
                }
                if (!card.IsFaceUp) card.EquipTarget = null;
                result.Add(card);
                seqIdx++;
            }
            return result;
        }

        private void NotifyLifePointChange(byte player, int delta, LifePointChangeKind kind)
        {
            int lp = Players[player].lp;
            OnLifePointChange?.Invoke(player, delta, lp, kind);
            OnLifePointsChanged?.Invoke(player, delta, lp);
        }

        private void AddLog(string line)
        {
            if (Logs.Count > 250) Logs.RemoveAt(0);
            Logs.Add(line);
            OnLogAdded?.Invoke(line);
        }

        public void Dispose()
        {
            FinishPlayerReplay(IsFinished && Winner >= 0 ? "finished" : "interrupted", force: true);
            _playerReplay?.Dispose();
            _planningReplay = null;
            _speculatingPlayer = -1;
            _fieldReadoutBaseline = null;
            _awaitingFieldArrivalCompletion = false;
            _movementGroups.Clear();
            if (NativePtr != IntPtr.Zero)
            {
                YgoNativeBridge.YGO_DestroyDuel(NativePtr);
                NativePtr = IntPtr.Zero;
            }
        }

        private void FinishPlayerReplay(string status = null, bool force = false)
        {
            if (_playerReplay == null || status == null && !IsFinished) return;
            _playerReplay.End(status ?? (Winner >= 0 ? "finished" : "interrupted"), Winner, WinReason, TurnNumber,
                RetryCount, new[] { Players[0].lp, Players[1].lp }, force);
        }
    }
}
