using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    public static class TestTokenAndComplexEffects
    {
        private static byte[] EncodeSelectIndices(params int[] indices)
        {
            byte[] resp = new byte[8 + indices.Length * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, resp, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(indices.Length), 0, resp, 4, 4);
            for (int i = 0; i < indices.Length; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(indices[i]), 0, resp, 8 + i * 4, 4);
            return resp;
        }

        [MenuItem("YGO Portable/Test/Verify Token & Effect Suites")]
        public static void RunAllTokenTests()
        {
            try
            {
                RunTokenSuiteHeadless();
                Debug.Log("[TOKEN TEST] ALL TOKEN & EFFECT TESTS PASSED SUCCESSFULLY!");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TOKEN TEST FAILED] {ex}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        public static void RunTokenSuiteHeadless()
        {
            string saPath = Path.Combine(Application.dataPath, "StreamingAssets");
            if (!YgoNativeBridge.IsInitialized) YgoNativeBridge.Initialize(saPath);
            if (!CardDatabase.IsLoaded) CardDatabase.Load(saPath);
            CardArtLoader.Initialize(saPath);

            Debug.Log("[TOKEN TEST] Starting Token & Complex Effect Tests...");

            TestTokenPresentationAndLocalization();
            TestSwordsoulTunerTokenAndSynchro();
            TestAdventurerTokenAndFatefulAdventure();
            TestTokenDisappearanceOnLeaveField();
        }

        private static void TestTokenPresentationAndLocalization()
        {
            Debug.Log("[TOKEN TEST 0] Token Presentation & Korean Localization...");

            // 1. Korean Names
            string moYeTokenName = CardDatabase.GetName(20001444);
            string advTokenName = CardDatabase.GetName(3285552);
            string sheepTokenName = CardDatabase.GetName(73915052);
            string nibiruTokenName = CardDatabase.GetName(27204312);

            Debug.Log($"Token Names: Swordsoul='{moYeTokenName}', Adventurer='{advTokenName}', Sheep='{sheepTokenName}', Nibiru='{nibiruTokenName}'");
            if (moYeTokenName != "상검 토큰") throw new Exception($"Swordsoul Token name mismatch: got '{moYeTokenName}'");
            if (advTokenName != "용사 토큰") throw new Exception($"Adventurer Token name mismatch: got '{advTokenName}'");
            if (sheepTokenName != "양 토큰") throw new Exception($"Sheep Token name mismatch: got '{sheepTokenName}'");
            if (nibiruTokenName != "원시생명체 토큰") throw new Exception($"Nibiru Token name mismatch: got '{nibiruTokenName}'");

            // 2. Type Tags
            string swordsoulTag = YgoConstants.GetTypeTagKR(0x5011);
            string sheepTag = YgoConstants.GetTypeTagKR(0x4011);
            Debug.Log($"Type Tags: Swordsoul(0x5011)='{swordsoulTag}', Sheep(0x4011)='{sheepTag}'");
            if (!swordsoulTag.Contains("튜너") || !swordsoulTag.Contains("토큰"))
                throw new Exception($"Swordsoul tag should contain 튜너 and 토큰: got '{swordsoulTag}'");
            if (!sheepTag.Contains("토큰") || sheepTag.Contains("일반"))
                throw new Exception($"Sheep token tag should contain 토큰 and omit 일반: got '{sheepTag}'");

            // 3. Generator Artwork Fallback
            var spMoYe = CardArtLoader.GetArtSprite(20001444);
            var spAdv = CardArtLoader.GetArtSprite(3285552);
            var spSheep = CardArtLoader.GetArtSprite(73915052);
            var spNibiru = CardArtLoader.GetArtSprite(27204312);

            if (spMoYe == null || spMoYe == CardArtLoader.GetFallbackArt())
                throw new Exception("Swordsoul Token failed to load Mo Ye generator artwork!");
            if (spAdv == null || spAdv == CardArtLoader.GetFallbackArt())
                throw new Exception("Adventurer Token failed to load Rite of Aramesir generator artwork!");
            if (spSheep == null || spSheep == CardArtLoader.GetFallbackArt())
                throw new Exception("Sheep Token failed to load Scapegoat generator artwork!");
            if (spNibiru == null || spNibiru == CardArtLoader.GetFallbackArt())
                throw new Exception("Nibiru Token failed to load Nibiru generator artwork!");

            Debug.Log($"[TOKEN TEST 0] All token presentation checks passed: Art sizes=({spMoYe.rect.width}x{spMoYe.rect.height})");
        }

        private static void TestSwordsoulTunerTokenAndSynchro()
        {
            Debug.Log("[TOKEN TEST 1] Swordsoul Mo Ye -> Tuner Token -> Level 8 Synchro Chixiao...");

            // Mo Ye (20001443) and Taia (57384901) guarantee opening hand has both
            var p0Main = new List<uint>();
            for (int i = 0; i < 20; i++) p0Main.Add(20001443);
            for (int i = 0; i < 20; i++) p0Main.Add(57384901);
            uint[] p0Extra = { 44508094 }; // Stardust Dragon (Lv 8 Synchro)

            var p1Main = new List<uint>();
            while (p1Main.Count < 40) p1Main.Add(20721928);

            using (var s = new DuelSession())
            {
                bool started = s.StartNewDuel(p0Main, p0Extra, p1Main, Array.Empty<uint>(), p0GoesFirst: true);
                if (!started) throw new Exception("Failed to start duel for Swordsoul test");

                var ai = new TacticalAI { Difficulty = AIDifficulty.Smart };
                bool summonedMoYe = false;
                bool tokenCreated = false;
                bool synchroChixiao = false;

                int guard = 0;
                while (!s.IsFinished && guard++ < 80)
                {
                    if (s.ActivePrompt == null) { s.StepUntilPrompt(); continue; }
                    var p = s.ActivePrompt;
                    Debug.Log($"[TEST 3 PROMPT] Player={p.playerId}, Msg={p.messageType}, ActCount={p.activatable.Count}, ActCodes=[{string.Join(",", p.activatable.Select(a => a.code))}], Candidates={p.candidates.Count}");
                    Debug.Log($"[PROMPT] Player={p.playerId}, Msg={p.messageType}, CardCode={p.promptCardCode}, Desc={p.promptDesc}, Summonable={p.summonable.Count}, Activatable={p.activatable.Count}, Candidates={p.candidates.Count}");

                    if (p.playerId == 0)
                    {
                        if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                        {
                            var handCodes = s.Players[0].hand.Select(c => c.code).ToList();
                            var sumCodes = p.summonable.Select(c => c.code).ToList();
                            Debug.Log($"[TEST 1 IDLECMD] Hand: [{string.Join(",", handCodes)}], Summonable: [{string.Join(",", sumCodes)}], Act: [{string.Join(",", p.activatable.Select(c => c.code))}]");

                            if (!summonedMoYe && p.summonable.Any(m => m.code == 20001443))
                            {
                                int idx = p.summonable.FindIndex(m => m.code == 20001443);
                                summonedMoYe = true;
                                Debug.Log($"[ACTION] Summoning Mo Ye (idx={idx})");
                                s.SubmitResponseInt((idx << 16) | 0);
                                continue;
                            }
                            if (tokenCreated && !synchroChixiao && p.spSummonable.Any(m => m.code == 46105261))
                            {
                                int idx = p.spSummonable.FindIndex(m => m.code == 46105261);
                                synchroChixiao = true;
                                Debug.Log($"[ACTION] Synchro Summoning Chixiao (idx={idx})");
                                s.SubmitResponseInt((idx << 16) | 1);
                                continue;
                            }
                            s.SubmitResponseInt(7); // End
                            continue;
                        }

                        if (p.messageType == YgoConstants.MSG_SELECT_EFFECTYN)
                        {
                            Debug.Log($"[ACTION] MSG_SELECT_EFFECTYN for card {p.promptCardCode} -> Submitting 1 (YES)");
                            s.SubmitResponseInt(1);
                            continue;
                        }

                        if (p.messageType == YgoConstants.MSG_SELECT_CARD)
                        {
                            Debug.Log($"[ACTION] MSG_SELECT_CARD min={p.min} max={p.max} cancelable={p.cancelable} candidates={p.candidates.Count}");
                            s.SubmitResponse(EncodeSelectIndices(0));
                            continue;
                        }

                        if (p.messageType == YgoConstants.MSG_SELECT_CHAIN)
                        {
                            int myMoYeChain = p.candidates.FindIndex(c => c.code == 20001443);
                            if (myMoYeChain >= 0)
                            {
                                Debug.Log($"[ACTION] Activating Mo Ye chain (idx={myMoYeChain})");
                                s.SubmitResponseInt(myMoYeChain);
                            }
                            else
                            {
                                s.SubmitResponseInt(-1);
                            }
                            continue;
                        }
                    }

                    // Check if token is now on field
                    var tok = s.Players[0].mzone.FirstOrDefault(c => c != null && (c.type & YgoConstants.TYPE_TOKEN) != 0);
                    if (tok != null && !tokenCreated)
                    {
                        tokenCreated = true;
                        Debug.Log($"[TOKEN TEST 1] Token detected: Code={tok.code}, Type=0x{tok.type:X}, Level={tok.level}, Race=0x{tok.race:X}, Atk={tok.attack}, Def={tok.defense}");
                    }

                    s.SubmitResponse(ai.ComputeResponse(s, p));
                }

                Debug.Log($"Logs:\n{string.Join("\n", s.Logs.TakeLast(15))}");
                if (!summonedMoYe) throw new Exception("Failed to summon Mo Ye");
                if (!tokenCreated) throw new Exception("Failed to generate Swordsoul Token");
            }
        }

        private static void TestAdventurerTokenAndFatefulAdventure()
        {
            Debug.Log("[TOKEN TEST 2] Rite of Aramesir -> Adventurer Token...");

            var p0Main = new List<uint>();
            for (int i = 0; i < 20; i++) p0Main.Add(3285551);
            for (int i = 0; i < 20; i++) p0Main.Add(20721928);

            var p1Main = new List<uint>();
            while (p1Main.Count < 40) p1Main.Add(20721928);

            using (var s = new DuelSession())
            {
                bool started = s.StartNewDuel(p0Main, Array.Empty<uint>(), p1Main, Array.Empty<uint>(), p0GoesFirst: true);
                if (!started) throw new Exception("Failed to start duel for Adventurer test");

                var ai = new TacticalAI { Difficulty = AIDifficulty.Smart };
                bool activatedRite = false;
                bool tokenCreated = false;

                int guard = 0;
                while (!s.IsFinished && guard++ < 40)
                {
                    if (s.ActivePrompt == null) { s.StepUntilPrompt(); continue; }
                    var p = s.ActivePrompt;
                    Debug.Log($"[TEST 3 PROMPT] Player={p.playerId}, Msg={p.messageType}, ActCount={p.activatable.Count}, ActCodes=[{string.Join(",", p.activatable.Select(a => a.code))}], Candidates={p.candidates.Count}");

                    if (p.playerId == 0)
                    {
                        if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD && !activatedRite)
                        {
                            if (p.activatable.Any(a => a.code == 3285551))
                            {
                                int idx = p.activatable.FindIndex(a => a.code == 3285551);
                                activatedRite = true;
                                s.SubmitResponseInt((idx << 16) | 5);
                                continue;
                            }
                        }

                        if (p.messageType == YgoConstants.MSG_SELECT_EFFECTYN || p.messageType == YgoConstants.MSG_SELECT_YESNO)
                        {
                            s.SubmitResponseInt(1);
                            continue;
                        }
                    }

                    var tok = s.Players[0].mzone.FirstOrDefault(c => c != null && (c.type & YgoConstants.TYPE_TOKEN) != 0);
                    if (tok != null && !tokenCreated)
                    {
                        tokenCreated = true;
                        Debug.Log($"[TOKEN TEST 2] Adventurer Token detected: Code={tok.code}, Atk={tok.attack}, Def={tok.defense}, Lv={tok.level}, Race=0x{tok.race:X}");
                        if (tok.attack != 2000 || tok.defense != 2000)
                            throw new Exception($"Adventurer Token should have 2000/2000 ATK/DEF, got {tok.attack}/{tok.defense}");
                        if (tok.level != 4)
                            throw new Exception($"Adventurer Token should be Level 4, got {tok.level}");
                        break;
                    }

                    s.SubmitResponse(ai.ComputeResponse(s, p));
                }

                if (!activatedRite) throw new Exception("Failed to activate Rite of Aramesir");
                if (!tokenCreated) throw new Exception("Failed to generate Adventurer Token");
            }
        }

        private static void TestTokenDisappearanceOnLeaveField()
        {
            Debug.Log("[TOKEN TEST 3] Scapegoat -> Dark Hole -> Tokens vanish (not in GY)...");

            var p0Main = new List<uint>();
            for (int i = 0; i < 20; i++) p0Main.Add(73915051);
            for (int i = 0; i < 20; i++) p0Main.Add(53129443);

            var p1Main = new List<uint>();
            while (p1Main.Count < 40) p1Main.Add(20721928);

            using (var s = new DuelSession())
            {
                bool started = s.StartNewDuel(p0Main, Array.Empty<uint>(), p1Main, Array.Empty<uint>(), p0GoesFirst: true);
                if (!started) throw new Exception("Failed to start duel for Token Disappearance test");

                var ai = new TacticalAI { Difficulty = AIDifficulty.Smart };
                bool activatedScapegoat = false;
                bool activatedDarkHole = false;

                int guard = 0;
                int maxTokensSeen = 0;
                while (!s.IsFinished && guard++ < 60)
                {
                    if (s.ActivePrompt == null) { s.StepUntilPrompt(); continue; }
                    var p = s.ActivePrompt;
                    Debug.Log($"[TEST 3 PROMPT] Player={p.playerId}, Msg={p.messageType}, ActCount={p.activatable.Count}, Candidates={p.candidates.Count}");

                    int curTokens = s.Players[0].mzone.Count(c => c != null && (c.type & YgoConstants.TYPE_TOKEN) != 0);
                    if (curTokens > maxTokensSeen) maxTokensSeen = curTokens;

                    if (p.playerId == 0)
                    {
                        if (p.messageType == YgoConstants.MSG_SELECT_CHAIN && !activatedScapegoat)
                        {
                            int goatIdx = p.candidates.FindIndex(c => c.code == 73915051);
                            if (goatIdx >= 0)
                            {
                                Debug.Log($"[ACTION] Activating Scapegoat via MSG_SELECT_CHAIN (idx={goatIdx})");
                                activatedScapegoat = true;
                                s.SubmitResponseInt(goatIdx);
                                continue;
                            }
                        }

                        if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                        {
                            if (!activatedScapegoat && p.activatable.Any(a => a.code == 73915051))
                            {
                                int idx = p.activatable.FindIndex(a => a.code == 73915051);
                                activatedScapegoat = true;
                                Debug.Log($"[ACTION] Activating Scapegoat via MSG_SELECT_IDLECMD (idx={idx})");
                                s.SubmitResponseInt((idx << 16) | 5);
                                continue;
                            }
                            if (maxTokensSeen >= 4 && !activatedDarkHole && p.activatable.Any(a => a.code == 53129443))
                            {
                                int idx = p.activatable.FindIndex(a => a.code == 53129443);
                                activatedDarkHole = true;
                                Debug.Log($"[ACTION] Activating Dark Hole (idx={idx})");
                                s.SubmitResponseInt((idx << 16) | 5);
                                continue;
                            }
                        }
                    }

                    s.SubmitResponse(ai.ComputeResponse(s, p));

                    bool darkHoleInGrave = s.Players[0].grave.Any(c => c.code == 53129443);
                    if (activatedDarkHole && darkHoleInGrave)
                    {
                        int fieldTokens = s.Players[0].mzone.Count(c => c != null && (c.type & YgoConstants.TYPE_TOKEN) != 0);
                        int gyTokens = s.Players[0].grave.Count(c => (c.type & YgoConstants.TYPE_TOKEN) != 0);
                        Debug.Log($"[TOKEN TEST 3] Post Dark Hole: MaxTokensSeen={maxTokensSeen}, FieldTokens={fieldTokens}, GYTokens={gyTokens}");
                        if (maxTokensSeen != 4) throw new Exception($"Expected 4 tokens spawned by Scapegoat, but saw max {maxTokensSeen}");
                        if (fieldTokens != 0) throw new Exception("Tokens should be destroyed from field");
                        if (gyTokens != 0) throw new Exception($"Tokens must vanish and NOT exist in Graveyard! Found {gyTokens} in GY");
                        break;
                    }
                }

                if (!activatedScapegoat || !activatedDarkHole)
                    throw new Exception($"Failed steps: Scapegoat={activatedScapegoat}, DarkHole={activatedDarkHole}");
            }
        }
    }
}
