using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using Debug = UnityEngine.Debug;

namespace YGOPortable.Editor
{
    public static class TestNewDeckLevels
    {
        public static void RunSmokeTest()
        {
            int exitCode = 0;
            try
            {
                string streaming = Application.streamingAssetsPath;
                int scriptErrors = 0;
                var errorMsgs = new HashSet<string>();

                if (YgoNativeBridge.Initialize(streaming, (type, msg) =>
                {
                    if (type == 0)
                    {
                        scriptErrors++;
                        errorMsgs.Add(msg);
                    }
                }) != 0)
                {
                    throw new InvalidOperationException("Failed to initialize YgoNativeBridge.");
                }

                CardDatabase.Load(streaming);
                DeckRepository.Load(streaming, Path.Combine(Path.GetTempPath(), "ygo_smoke_test_" + Guid.NewGuid().ToString("N")),
                    seedDefaultDecks: false);

                var general = DuelAiPolicy.LoadIncluded() ?? throw new InvalidOperationException("The general AI policy is missing.");
                var links = LinkComboPolicy.LoadIncluded() ?? throw new InvalidOperationException("The Link AI policy is missing.");
                void CheckNativeErrors()
                {
                    if (scriptErrors != 0)
                        throw new InvalidOperationException($"Native script errors: {scriptErrors}. {string.Join(" | ", errorMsgs.Take(5))}");
                }

                var roster = VerifyAllLevels();
                VerifyDeckStyles(roster.Select(entry => entry.Deck).ToArray());
                VerifyDefensiveSummonAgainstStrongOpponent(general, links, CheckNativeErrors);
                CheckNativeErrors();

                // Verify OpponentDeckLevels for sample characters
                var sampleChecks = new (int charId, int expectedLevels)[]
                {
                    (104, 10),  // 어둠의 유우기 (DM, 10레벨)
                    (94,  10),  // 카이바 세토 (DM, 10레벨)
                    (119, 10),  // 유우키 쥬다이 (GX, 10레벨)
                    (117, 10),  // 만죠메 쥰 (GX, 10레벨)
                    (35,  10),  // 후도 유세이 (5D's, 10레벨)
                    (16,  10),  // 잭 아틀라스 (5D's, 10레벨)
                    (3202, 5),  // 세크트 이쥬인 (5D's 코믹스, 5레벨)
                    (196,  4),  // 미궁 형제 (DM, 4레벨)
                    (2020, 19), // 메타 덱 상대 (2026 메타 유지)
                };

                foreach (var (cid, expected) in sampleChecks)
                {
                    var ch = DeckRepository.Characters.FirstOrDefault(c => c.id == cid);
                    var decks = OpponentDeckLevels.GetDecks(cid);
                    if (ch == null || decks.Count != expected)
                        throw new InvalidOperationException($"Level count mismatch for charId {cid}: expected {expected}, got {decks.Count}");
                    Debug.Log($"[SMOKE-LEVELS] {ch.name} ({ch.seriesName}): {decks.Count} levels OK | Lv.1={decks[0].title} | Max={decks[decks.Count - 1].title}");
                }

                // Keep the representative multi-turn fixtures in addition to the complete opening audit.
                var matchups = new (int charA, int lvA, int charB, int lvB, int seed)[]
                {
                    // DM
                    (104, 10, 94,  10, 101), // 어둠의 유우기 Lv.10 vs 카이바 세토 Lv.10
                    (81,  10, 103, 10, 102), // 죠노우치 카츠야 Lv.10 vs 어둠의 마리크 Lv.10
                    (105, 8,  84,  8,  103), // 무토 유우기 Lv.8 vs 페가수스 Lv.8
                    // GX
                    (119, 6,  104, 6,  201), // 유우키 쥬다이 Lv.6 (마스크드 혼합) vs 어둠의 유우기 Lv.6 (드라군 혼합)
                    (119, 10, 136, 10, 202), // 유우키 쥬다이 Lv.10 vs 유벨 Lv.10
                    (137, 10, 109, 10, 203), // 마루후지 료 Lv.10 (사이버 드래곤) vs 에드 피닉스 Lv.10 (D-HERO)
                    (117, 6,  120, 10, 204), // 만죠메 쥰 Lv.6 (광암룡 혼합) vs 요한 안데르센 Lv.10 (보옥수)
                    (107, 10, 113, 5,  205), // 텐죠인 아스카 Lv.10 vs 미사와 다이치 Lv.5
                    // 5D's
                    (35,  6,  16,  6,  301), // 후도 유세이 Lv.6 (액셀싱크로 혼합) vs 잭 아틀라스 Lv.6 (염마룡 혼합)
                    (35,  10, 36,  10, 302), // 후도 유세이 Lv.10 vs Z-one Lv.10 (시계신)
                    (1,   10, 10,  10, 303), // 이자요이 아키 Lv.10 (블랙 로즈) vs 크로우 호건 Lv.10 (BF)
                    (19,  10, 3,   10, 304), // 키류 쿄스케 Lv.10 (인페르니티) vs 안티노미 Lv.10 (TG)
                    // ZEXAL
                    (174, 10, 156, 10, 401), // 츠쿠모 유마 Lv.10 (유토피아) vs 텐조 카이토 Lv.10 (갤럭시아이즈)
                    (168, 10, 160, 10, 402), // 카미시로 료가(샤크) Lv.10 vs 낫슈 Lv.10
                    (158, 10, 172, 10, 403), // 미자엘 Lv.10 (타키온) vs 벡터 Lv.10
                    // ARC-V
                    (72,  6,  43,  10, 501), // 사카키 유우야 Lv.6 (팬텀 혼합) vs 아카바 레이지 Lv.10 (D/D/D)
                    (71,  10, 68,  10, 502), // 유토 Lv.10 (팬텀 나이츠) vs 유고 Lv.10 (SR)
                    (69,  10, 61,  10, 503), // 유리 Lv.10 (프레데터 플랜츠) vs 쿠로사키 슌 Lv.10 (RR)
                    // VRAINS & Manga
                    (143, 6,  189, 10, 601), // 플레이메이커 Lv.6 (사이버스 코드 토커) vs 리볼버 Lv.10 (바렐/바렛)
                    (187, 10, 140, 10, 602), // 소울버너 Lv.10 (샐러맨그레이트) vs 블루 엔젤 Lv.10 (트릭스터)
                    (3202, 5, 3305, 5, 603), // 세크트 이쥬인 Lv.5 vs 야구모 쿄지 Lv.5 (코믹스 대결)
                };

                foreach (var (cA, lvA, cB, lvB, seed) in matchups)
                {
                    var chA = DeckRepository.Characters.First(c => c.id == cA);
                    var chB = DeckRepository.Characters.First(c => c.id == cB);
                    var deckA = OpponentDeckLevels.GetDecks(cA)[lvA - 1];
                    var deckB = OpponentDeckLevels.GetDecks(cB)[lvB - 1];

                    RunSingleDuel(chA.name, lvA, deckA, chB.name, lvB, deckB, seed, general, links, CheckNativeErrors);
                }

                // Rotate the second seat so every runtime deck plays once in each seat.
                int totalDecisions = 0, earlyFinishes = 0;
                int offset = (roster.Length + 1) / 2;
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < roster.Length; i++)
                {
                    var first = roster[i];
                    var second = roster[(i + offset) % roster.Length];
                    var result = RunOpeningDuel(first, second, i, general, links, CheckNativeErrors);
                    totalDecisions += result.Decisions;
                    if (result.Finished) earlyFinishes++;
                    if ((i + 1) % 25 == 0 || i + 1 == roster.Length)
                        Debug.Log($"[SMOKE-ROSTER-PROGRESS] {i + 1}/{roster.Length} opening duels; " +
                            $"{totalDecisions} responses; elapsed={clock.Elapsed.TotalSeconds:F1}s; " +
                            $"last={first.Character.name}/Lv.{first.Level} vs {second.Character.name}/Lv.{second.Level}");
                }

                Debug.Log($"[SMOKE-ROSTER-266] All {DeckRepository.Characters.Count} characters and {roster.Length} runtime deck levels " +
                    $"played in both seats across {roster.Length} opening duels, each reaching turn 3 or a valid duel result. " +
                    $"Responses={totalDecisions}, earlyResults={earlyFinishes}, Retries=0, ScriptErrors={scriptErrors}");
                CheckNativeErrors();
                Debug.Log($"[SMOKE-SUMMARY] ALL MATCHUPS PASSED! Levels={roster.Length}, " +
                    $"OpeningDuels={roster.Length}, Retries=0, ScriptErrors={scriptErrors}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SMOKE-FAIL] {ex}");
                exitCode = 1;
            }
            finally
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static (OpponentCharacter Character, int Level, PresetDeckInfo Deck)[] VerifyAllLevels()
        {
            var roster = new List<(OpponentCharacter Character, int Level, PresetDeckInfo Deck)>();
            var ids = new HashSet<int>();
            foreach (var character in DeckRepository.Characters)
            {
                var levels = OpponentDeckLevels.GetDecks(character.id);
                if (levels.Count == 0)
                    throw new InvalidOperationException($"No levels for opponent {character.id} ({character.name}).");
                for (int i = 0; i < levels.Count; i++)
                {
                    var deck = levels[i];
                    if (deck == null || deck.charId != character.id || !ids.Add(deck.id) ||
                        OpponentDeckLevels.GetLevel(character.id, deck.id) != i + 1 ||
                        deck.main == null || deck.main.Length is < 40 or > 60 ||
                        deck.extra == null || deck.extra.Length > 15 || deck.side == null || deck.side.Length > 15)
                        throw new InvalidOperationException($"Invalid level definition for opponent {character.id}, level {i + 1}.");
                    var cards = deck.main.Concat(deck.extra).Concat(deck.side).ToArray();
                    if (cards.Any(code => CardDatabase.Get(code) is not CardInfo card ||
                        (card.type & (YgoConstants.TYPE_MONSTER | YgoConstants.TYPE_SPELL | YgoConstants.TYPE_TRAP)) == 0 ||
                        (card.type & YgoConstants.TYPE_TOKEN) != 0) ||
                        deck.main.Any(code => CardDatabase.Get(code).IsExtra) ||
                        deck.extra.Any(code => !CardDatabase.Get(code).IsExtra))
                        throw new InvalidOperationException($"Invalid card or deck placement in {character.name} Lv.{i + 1} ({deck.id}).");
                    if (cards.GroupBy(CollectionProgress.GetDeckCopyKey).Any(group => group.Count() > CollectionProgress.DeckCopyLimit))
                        throw new InvalidOperationException($"Too many copies of the same card in {character.name} Lv.{i + 1} ({deck.id}).");
                    if (deck.sigCard != 0 && !deck.main.Concat(deck.extra).Contains(deck.sigCard))
                        Debug.LogWarning($"[SMOKE-SIGNATURE] {character.name} Lv.{i + 1} ({deck.id}) lists an ace absent from its deck: {deck.sigCard}.");
                    roster.Add((character, i + 1, deck));
                }
            }
            if (roster.Count < 2 || DeckRepository.PresetDecks.Any(deck => !ids.Contains(deck.id)))
                throw new InvalidOperationException("The runtime levels omit an existing opponent preset.");
            Debug.Log($"[SMOKE-ALL-LEVELS] {DeckRepository.Characters.Count} opponents, {roster.Count} unique levels, " +
                $"all {DeckRepository.PresetDecks.Count} roster presets retained; card placement, copy limits and level indices passed.");
            return roster.ToArray();
        }

        private static void VerifyDeckStyles(IReadOnlyList<PresetDeckInfo> roster)
        {
            var expected = new (int Deck, DuelDeckStyle Style)[]
            {
                (51199, DuelDeckStyle.Development),
                (20672, DuelDeckStyle.Control),
                (20675, DuelDeckStyle.Lock),
                (20673, DuelDeckStyle.Resource),
                (20687, DuelDeckStyle.Resource),
            };
            foreach (var entry in expected)
            {
                var deck = roster.FirstOrDefault(candidate => candidate.id == entry.Deck) ??
                    throw new InvalidOperationException($"Required style fixture {entry.Deck} is missing.");
                var actual = DuelDeckStrategy.Classify(deck.main, deck.extra.Length);
                if (actual != entry.Style)
                    throw new InvalidOperationException($"Deck {deck.id} ({deck.title}) must use {entry.Style}, got {actual}.");
                Debug.Log($"[SMOKE-STYLE] {deck.id} {deck.title}: {actual}");
            }
            var styles = roster.GroupBy(deck => DuelDeckStrategy.Classify(deck.main, deck.extra.Length))
                .ToDictionary(group => group.Key, group => group.Count());
            foreach (DuelDeckStyle style in Enum.GetValues(typeof(DuelDeckStyle)))
                if (!styles.ContainsKey(style))
                    throw new InvalidOperationException($"The roster does not exercise style {style}.");
            Debug.Log("[SMOKE-ALL-STYLES] " + string.Join(", ", styles.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
        }

        private static (int Decisions, bool Finished) RunOpeningDuel(
            (OpponentCharacter Character, int Level, PresetDeckInfo Deck) first,
            (OpponentCharacter Character, int Level, PresetDeckInfo Deck) second,
            int index, DuelAiPolicy general, LinkComboPolicy links, Action checkNativeErrors)
        {
            using var session = new DuelSession(recordPlanningReplay: true);
            var pilots = new[]
            {
                new TacticalAI(general, links, searchCombos: true) { Difficulty = AIDifficulty.Smart },
                new TacticalAI(general, links, searchCombos: true) { Difficulty = AIDifficulty.Smart },
            };
            string label = $"{first.Character.name}/Lv.{first.Level}/{first.Deck.id} vs " +
                $"{second.Character.name}/Lv.{second.Level}/{second.Deck.id}";
            ulong n = (ulong)(index + 1) * 131UL + 997UL;
            ulong[] seed = { n * 29 + 1, n * 47 + 7, n * 71 + 13, n * 97 + 3 };
            if (!session.StartNewDuel(first.Deck.main, first.Deck.extra, second.Deck.main, second.Deck.extra, true, seed: seed))
                throw new InvalidOperationException($"Opening duel start failed: {label}.");
            checkNativeErrors();
            const int MaxResponses = 512;
            var reachedMain = new bool[2];
            int steps = 0;
            while (!session.IsFinished && session.TurnNumber <= 2)
            {
                if (steps >= MaxResponses)
                    throw new InvalidOperationException($"Opening reached {MaxResponses} responses before completing both turns: {label}. " +
                        $"Turn={session.TurnNumber}, prompt={session.ActivePrompt?.messageType}; {string.Join(" | ", session.Logs.TakeLast(6))}");
                var prompt = RequirePrompt(session, label);
                if (prompt == null) break;
                if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.playerId == session.TurnPlayer)
                    reachedMain[prompt.playerId] = true;
                byte[] response = PlanResponse(pilots[prompt.playerId], session, prompt, label);
                session.SubmitResponse(response);
                RequireAccepted(session, prompt, response, label, checkNativeErrors);
                steps++;
            }
            if (!session.IsFinished && (!reachedMain[0] || !reachedMain[1] || session.TurnNumber < 3))
                throw new InvalidOperationException($"Opening did not complete both Main Phases: {label}.");
            if (session.IsFinished && session.Winner is < 0 or > 2)
                throw new InvalidOperationException($"Opening ended without a valid winner or draw: {label}.");
            return (steps, session.IsFinished);
        }

        private static DuelPrompt RequirePrompt(DuelSession session, string label)
        {
            if (session.ActivePrompt == null) session.StepUntilPrompt();
            if (!session.IsFinished && session.ActivePrompt == null)
                throw new InvalidOperationException($"Duel stopped without a response prompt or result: {label}, turn {session.TurnNumber}.");
            return session.ActivePrompt;
        }

        private static byte[] PlanResponse(TacticalAI ai, DuelSession session, DuelPrompt prompt, string label)
        {
            int advances = 0;
            while (!ai.AdvanceComboPlanning(session, prompt))
            {
                if (++advances >= 128)
                    throw new InvalidOperationException($"Combo planning exceeded its bounded work units: {label}, prompt {prompt.messageType}.");
            }
            return ai.ComputeResponse(session, prompt);
        }

        private static void RequireAccepted(DuelSession session, DuelPrompt prompt, byte[] response, string label, Action checkNativeErrors)
        {
            checkNativeErrors();
            if (session.RetryCount != 0)
                throw new InvalidOperationException($"The engine retried {label}: turn {session.TurnNumber}, prompt {prompt.messageType}, " +
                    $"hint {prompt.SelectionHint}, answer {BitConverter.ToString(response)}, retries={session.RetryCount}. " +
                    string.Join(" | ", session.Logs.TakeLast(6)));
        }

        private static void VerifyDefensiveSummonAgainstStrongOpponent(DuelAiPolicy general, LinkComboPolicy links, Action checkNativeErrors)
        {
            AiMatchBenchmark.VerifyNormalSetUnderPressure();
            var ai = new TacticalAI(general, links, searchCombos: true) { Difficulty = AIDifficulty.Smart };
            using var session = new DuelSession(recordPlanningReplay: true);
            uint[] p0Main = Enumerable.Repeat(69247929u, 40).ToArray();
            uint[] p1Main = Enumerable.Repeat(7084129u, 40).ToArray();
            uint[] empty = Array.Empty<uint>();
            ulong[] seed = { 101, 203, 307, 409 };
            if (!session.StartNewDuel(p0Main, empty, p1Main, empty, true, seed: seed))
                throw new InvalidOperationException("Could not start the defensive-summon regression fixture.");
            checkNativeErrors();

            // Turn 1: P0 summons Warwolf (2000 ATK) and ends turn
            session.SubmitResponseInt(0);
            checkNativeErrors();
            if (session.RetryCount != 0)
                throw new InvalidOperationException("The defensive-summon setup rejected its initial summon.");
            int setupSteps = 0;
            while (session.ActivePrompt?.playerId != 0 || session.ActivePrompt.messageType != YgoConstants.MSG_SELECT_IDLECMD)
            {
                if (session.IsFinished || ++setupSteps > 80)
                    throw new InvalidOperationException("The defensive-summon fixture failed to reach its initial idle prompt.");
                var prompt = RequirePrompt(session, "defensive-summon setup");
                if (prompt == null) throw new InvalidOperationException("The defensive-summon fixture ended during setup.");
                byte[] response = PlanResponse(ai, session, prompt, "defensive-summon setup");
                session.SubmitResponse(response);
                RequireAccepted(session, prompt, response, "defensive-summon setup", checkNativeErrors);
            }
            session.SubmitResponseInt(7); // P0 End Phase
            checkNativeErrors();
            if (session.RetryCount != 0 || session.TurnNumber != 2 ||
                session.ActivePrompt?.messageType != YgoConstants.MSG_SELECT_IDLECMD || !session.Players[0].mzone.Any(monster =>
                    monster is { code: 69247929, attack: 2000, IsFaceUp: true, IsAttack: true }))
                throw new InvalidOperationException("The position-choice fixture did not reach Main Phase with the native Warwolf field.");

            foreach (var (code, expectedPos) in new (uint, uint)[]
            {
                (63977008, YgoConstants.POS_FACEUP_DEFENSE), // 1300 ATK < 2000 -> Defense!
                (7084129,  YgoConstants.POS_FACEUP_DEFENSE), // 1600 ATK < 2000 -> Defense!
                (70095154, YgoConstants.POS_FACEUP_ATTACK),  // 2100 ATK > 2000 -> Attack!
            })
            {
                var posPrompt = new DuelPrompt
                {
                    messageType = YgoConstants.MSG_SELECT_POSITION,
                    playerId = 1,
                    promptCardCode = code,
                    allowedPositions = (byte)(YgoConstants.POS_FACEUP_ATTACK | YgoConstants.POS_FACEUP_DEFENSE),
                };
                uint chosen = BitConverter.ToUInt32(ai.ComputeResponse(session, posPrompt), 0);
                if (chosen != expectedPos)
                    throw new InvalidOperationException($"Expected {expectedPos} for {code} against 2000 ATK, got {chosen}");
            }
            Debug.Log("[SMOKE-DEFENSE] Low-life Set and special summon responses against a native 2000 ATK monster passed.");
        }

        private static void RunSingleDuel(
            string nameA, int lvA, PresetDeckInfo deckA,
            string nameB, int lvB, PresetDeckInfo deckB,
            int seed, DuelAiPolicy general, LinkComboPolicy links, Action checkNativeErrors)
        {
            using var session = new DuelSession(recordPlanningReplay: true);
            var ai0 = new TacticalAI(general, links, searchCombos: true) { Difficulty = AIDifficulty.Smart };
            var ai1 = new TacticalAI(general, links, searchCombos: true) { Difficulty = AIDifficulty.Smart };

            ulong n = (ulong)seed * 97 + 8191UL;
            ulong[] seedArr = { n * 29 + 101, n * 47 + 7, n * 71 + 13, n * 97 + 3 };
            bool started = session.StartNewDuel(deckA.main, deckA.extra, deckB.main, deckB.extra, p0GoesFirst: true, seed: seedArr);
            if (!started)
                throw new InvalidOperationException($"StartNewDuel failed: {nameA} Lv.{lvA} vs {nameB} Lv.{lvB}");
            checkNativeErrors();

            int steps = 0;
            int stepsThisTurn = 0;
            int longestTurn = 0;
            var summonedExtraA = new List<string>();
            var summonedExtraB = new List<string>();

            session.OnCardMoved += move =>
            {
                if (move.toLoc != YgoConstants.LOCATION_MZONE || (move.reason & YgoConstants.REASON_SPSUMMON) == 0)
                    return;
                var card = CardDatabase.Get(move.code);
                if (card == null) return;
                bool isNotable = card.IsExtra || (card.type & YgoConstants.TYPE_RITUAL) != 0 || card.level >= 7;
                if (!isNotable) return;
                string cname = CardDatabase.GetName(move.code);
                var list = move.toCon == 0 ? summonedExtraA : summonedExtraB;
                if (!list.Contains(cname)) list.Add(cname);
            };

            const int MaxTurns = 8;
            const int MaxResponsesPerTurn = 160;
            string label = $"{nameA} Lv.{lvA} vs {nameB} Lv.{lvB}";

            while (!session.IsFinished && session.TurnNumber <= MaxTurns)
            {
                var prompt = RequirePrompt(session, label);
                if (prompt == null) break;

                int turn = session.TurnNumber;
                var ai = prompt.playerId == 0 ? ai0 : ai1;
                byte[] resp = PlanResponse(ai, session, prompt, label);
                session.SubmitResponse(resp);
                RequireAccepted(session, prompt, resp, label, checkNativeErrors);
                steps++;
                longestTurn = Math.Max(longestTurn, ++stepsThisTurn);
                if (session.TurnNumber != turn) stepsThisTurn = 0;
                else if (!session.IsFinished && stepsThisTurn >= MaxResponsesPerTurn)
                    throw new InvalidOperationException($"Representative fixture stalled on turn {turn} after {stepsThisTurn} responses: " +
                        $"{label}, phase {session.CurrentPhase}, last prompt {prompt.messageType}/{prompt.SelectionHint}. " +
                        string.Join(" | ", session.Logs.TakeLast(6)));
            }

            string outcome = session.IsFinished
                ? $"Winner={(session.Winner == 2 ? "Draw" : session.Winner == 0 ? nameA : nameB)} (Turn {session.TurnNumber})"
                : $"CompletedBoundedTurns={MaxTurns}, Turn {session.TurnNumber} LP={session.Players[0].lp}:{session.Players[1].lp}";

            string exA = summonedExtraA.Count > 0 ? string.Join(", ", summonedExtraA) : "없음";
            string exB = summonedExtraB.Count > 0 ? string.Join(", ", summonedExtraB) : "없음";

            Debug.Log($"[SMOKE-DUEL] {nameA} Lv.{lvA} ({deckA.title}) vs {nameB} Lv.{lvB} ({deckB.title}) | {outcome} | Steps={steps}, MaxResponsesInTurn={longestTurn}, Retries={session.RetryCount}, Branches={ai0.SimulatedComboBranches}/{ai1.SimulatedComboBranches} | ExtraA=[{exA}] | ExtraB=[{exB}]");
        }
    }
}
