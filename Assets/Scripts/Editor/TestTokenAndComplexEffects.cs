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
            TestDestinyHeroDestroyerPhoenixEnforcer();
            TestTearlamentsSegocAndMandatoryVsOptionalTriggers();
            TestArchetypeCatalogAndPureDeckBuilder();
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
            var glimmer = CardDatabase.Get(41458362);
            if (YgoNativeBridge.YGO_HasCard(41458362) != 1 || glimmer == null || glimmer.name != "기석 토큰" ||
                (glimmer.type & (YgoConstants.TYPE_TOKEN | YgoConstants.TYPE_NORMAL)) != (YgoConstants.TYPE_TOKEN | YgoConstants.TYPE_NORMAL) ||
                glimmer.race != YgoConstants.RACE_ROCK || glimmer.attr != YgoConstants.ATTRIBUTE_LIGHT)
                throw new Exception("Glimmer Token is missing or has invalid metadata");

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
            var spGlimmer = CardArtLoader.GetArtSprite(41458362);

            if (spMoYe == null || spMoYe == CardArtLoader.GetFallbackArt())
                throw new Exception("Swordsoul Token failed to load Mo Ye generator artwork!");
            if (spAdv == null || spAdv == CardArtLoader.GetFallbackArt())
                throw new Exception("Adventurer Token failed to load Rite of Aramesir generator artwork!");
            if (spSheep == null || spSheep == CardArtLoader.GetFallbackArt())
                throw new Exception("Sheep Token failed to load Scapegoat generator artwork!");
            if (spNibiru == null || spNibiru == CardArtLoader.GetFallbackArt())
                throw new Exception("Nibiru Token failed to load Nibiru generator artwork!");
            if (spGlimmer == null || spGlimmer == CardArtLoader.GetFallbackArt())
                throw new Exception("Glimmer Token failed to load Luminous generator artwork!");

            Debug.Log($"[TOKEN TEST 0] All token presentation checks passed: Art sizes=({spMoYe.rect.width}x{spMoYe.rect.height})");
        }

        private static void TestSwordsoulTunerTokenAndSynchro()
        {
            Debug.Log("[TOKEN TEST 1] Swordsoul Mo Ye -> Tuner Token -> Level 8 Synchro Chixiao...");

            var p0Main = new List<uint>();
            for (int i = 0; i < 20; i++) p0Main.Add(20001443);
            for (int i = 0; i < 20; i++) p0Main.Add(56495147);
            uint[] p0Extra = { 69248256 }; // Chixiao (Lv 8 Synchro)

            var p1Main = new List<uint>();
            while (p1Main.Count < 40) p1Main.Add(20721928);

            using (var s = YgoProjectBuilder.StartSeedMatching("Swordsoul", p0Main.ToArray(), p0Extra, p1Main.ToArray(), Array.Empty<uint>(), true,
                session => session.Players[0].hand.Any(c => c.code == 20001443) && session.Players[0].hand.Any(c => c.code == 56495147)))
            {
                var ai = new TacticalAI { Difficulty = AIDifficulty.Smart };
                bool summonedMoYe = false;
                bool tokenCreated = false;
                bool synchroChixiao = false;

                int guard = 0;
                while (!s.IsFinished && guard++ < 80)
                {
                    if (s.Players[0].mzone.Any(c => c != null && c.code == 69248256))
                    {
                        synchroChixiao = true;
                        break;
                    }
                    var token = s.Players[0].mzone.FirstOrDefault(c => c != null && c.code == 20001444);
                    if (token != null && !tokenCreated)
                    {
                        const uint tokenFlags = YgoConstants.TYPE_TOKEN | YgoConstants.TYPE_TUNER;
                        if ((token.type & tokenFlags) != tokenFlags || token.level != 4)
                            throw new Exception("Mo Ye did not generate a level 4 Tuner Token");
                        tokenCreated = true;
                    }
                    if (s.ActivePrompt == null) { s.StepUntilPrompt(); continue; }
                    var p = s.ActivePrompt;
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
                                Debug.Log($"[ACTION] Summoning Mo Ye (idx={idx}), Hand=[{string.Join(",", handCodes)}]");
                                s.SubmitResponseInt((idx << 16) | 0);
                                continue;
                            }
                            if (tokenCreated && !synchroChixiao && p.spSummonable.Any(m => m.code == 69248256))
                            {
                                int idx = p.spSummonable.FindIndex(m => m.code == 69248256);
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

                    s.SubmitResponse(ai.ComputeResponse(s, p));
                }

                Debug.Log($"Logs:\n{string.Join("\n", s.Logs.TakeLast(15))}");
                if (!summonedMoYe) throw new Exception("Failed to summon Mo Ye");
                if (!tokenCreated) throw new Exception("Failed to generate Swordsoul Token");
                if (!synchroChixiao || s.RetryCount != 0) throw new Exception("Chixiao Synchro Summon did not resolve cleanly");
            }
        }

        private static void TestAdventurerTokenAndFatefulAdventure()
        {
            Debug.Log("[TOKEN TEST 2] Rite of Aramesir -> Adventurer Token...");

            var p0Main = new List<uint>();
            for (int i = 0; i < 20; i++) p0Main.Add(3285551);
            for (int i = 0; i < 19; i++) p0Main.Add(20721928);
            p0Main.Add(39568067); // Fateful Adventure

            var p1Main = new List<uint>();
            while (p1Main.Count < 40) p1Main.Add(20721928);

            using (var s = YgoProjectBuilder.StartSeedMatching("Adventurer", p0Main.ToArray(), Array.Empty<uint>(), p1Main.ToArray(), Array.Empty<uint>(), true,
                session => session.Players[0].hand.Any(c => c.code == 3285551) && session.Players[0].hand.All(c => c.code != 39568067)))
            {
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

                    var tok = s.Players[0].mzone.FirstOrDefault(c => c != null && c.code == 3285552);
                    if (tok != null && !tokenCreated)
                    {
                        tokenCreated = true;
                        Debug.Log($"[TOKEN TEST 2] Adventurer Token detected: Code={tok.code}, Atk={tok.attack}, Def={tok.defense}, Lv={tok.level}, Race=0x{tok.race:X}");
                        if (tok.attack != 2000 || tok.defense != 2000)
                            throw new Exception($"Adventurer Token should have 2000/2000 ATK/DEF, got {tok.attack}/{tok.defense}");
                        if (tok.level != 4)
                            throw new Exception($"Adventurer Token should be Level 4, got {tok.level}");
                    }
                    if (tokenCreated && s.Players[0].szone.Any(c => c != null && c.code == 39568067 && c.IsFaceUp))
                        break;

                    s.SubmitResponse(ai.ComputeResponse(s, p));
                }

                if (!activatedRite) throw new Exception("Failed to activate Rite of Aramesir");
                if (!tokenCreated) throw new Exception("Failed to generate Adventurer Token");
                if (s.RetryCount != 0 || !s.Players[0].szone.Any(c => c != null && c.code == 39568067 && c.IsFaceUp))
                    throw new Exception("Rite of Aramesir did not place Fateful Adventure from the Deck");
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

            using (var s = YgoProjectBuilder.StartSeedMatching("Scapegoat and Dark Hole", p0Main.ToArray(), Array.Empty<uint>(), p1Main.ToArray(), Array.Empty<uint>(), true,
                session => session.Players[0].hand.Any(c => c.code == 73915051) && session.Players[0].hand.Any(c => c.code == 53129443)))
            {
                var ai = new TacticalAI { Difficulty = AIDifficulty.Smart };
                bool activatedScapegoat = false;
                bool activatedDarkHole = false;
                bool tokensVanished = false;

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
                        tokensVanished = true;
                        break;
                    }
                }

                if (!activatedScapegoat || !activatedDarkHole)
                    throw new Exception($"Failed steps: Scapegoat={activatedScapegoat}, DarkHole={activatedDarkHole}");
                if (!tokensVanished || s.RetryCount != 0) throw new Exception("Scapegoat token removal did not resolve cleanly");
            }
        }

        private static void TestDestinyHeroDestroyerPhoenixEnforcer()
        {
            Debug.Log("[TOKEN & EFFECT TEST 4] DPE (Destiny HERO - Destroyer Phoenix Enforcer) Optional Cancel & GY Standby Revive...");

            var p0Main = new List<uint>();
            for (int i = 0; i < 34; i++) p0Main.Add(52947044); // Fusion Destiny (52947044)
            for (int i = 0; i < 4; i++) p0Main.Add(53129443); // Dark Hole
            p0Main.Add(81866673); // Dasher (Lv 6 D-HERO)
            p0Main.Add(16605586); // Denier (D-HERO)
            uint[] p0Extra = { 60461804 }; // DPE

            var p1Main = new List<uint>();
            while (p1Main.Count < 40) p1Main.Add(20721928);

            using (var s = YgoProjectBuilder.StartSeedMatching("Phoenix Enforcer", p0Main.ToArray(), p0Extra, p1Main.ToArray(), Array.Empty<uint>(), true,
                session => session.Players[0].hand.Any(c => c.code == 52947044) && session.Players[0].hand.Any(c => c.code == 53129443)))
            {
                var ai = new TacticalAI { Difficulty = AIDifficulty.Smart };
                bool activatedFusionDestiny = false;
                bool summonedDPE = false;
                bool passedDPEQuickEffect = false;
                bool activatedDarkHole = false;
                bool triggeredDPEGraveEffect = false;
                bool revivedDPEInOppStandby = false;

                int guard = 0;
                while (!s.IsFinished && guard++ < 80)
                {
                    if (s.ActivePrompt == null) { s.StepUntilPrompt(); continue; }
                    var p = s.ActivePrompt;
                    Debug.Log($"[DPE PROMPT] Player={p.playerId}, Msg={p.messageType}, ActCount={p.activatable.Count}, ActCodes=[{string.Join(",", p.activatable.Select(a => a.code))}], Candidates={p.candidates.Count}");

                    bool dpeOnField = s.Players[0].mzone.Any(c => c != null && c.code == 60461804);
                    if (dpeOnField && !summonedDPE)
                    {
                        summonedDPE = true;
                        Debug.Log("[DPE TEST] DPE successfully summoned to field!");
                    }

                    if (p.playerId == 0)
                    {
                        // 1. Activate Fusion Destiny from hand in Main Phase 1
                        if (p.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                        {
                            var h = s.Players[0].hand.Select(c => c.code).ToList();
                            Debug.Log($"[DPE TEST IDLECMD] Hand: [{string.Join(",", h)}], ActCount: {p.activatable.Count}");
                            if (!activatedFusionDestiny && p.activatable.Any(a => a.code == 52947044))
                            {
                                int idx = p.activatable.FindIndex(a => a.code == 52947044);
                                activatedFusionDestiny = true;
                                Debug.Log($"[DPE TEST] Activating Fusion Destiny (idx={idx})");
                                s.SubmitResponseInt((idx << 16) | 5);
                                continue;
                            }

                            // 3. Once DPE is on field and optional quick effect was tested (passed), activate Dark Hole to destroy DPE
                            if (summonedDPE && passedDPEQuickEffect && !activatedDarkHole && p.activatable.Any(a => a.code == 53129443))
                            {
                                int idx = p.activatable.FindIndex(a => a.code == 53129443);
                                activatedDarkHole = true;
                                Debug.Log($"[DPE TEST] Activating Dark Hole to destroy DPE (idx={idx})");
                                s.SubmitResponseInt((idx << 16) | 5);
                                continue;
                            }

                            // If DPE was destroyed and GY effect triggered, end turn so next Standby Phase arrives
                            if (triggeredDPEGraveEffect)
                            {
                                Debug.Log("[DPE TEST] Ending Turn 1 to proceed to Turn 2 Standby Phase...");
                                s.SubmitResponseInt(7); // End Phase
                                continue;
                            }
                        }

                        // 2. Test optional cancel: when MSG_SELECT_CHAIN asks if we want to activate DPE's quick effect, submit -1 (PASS)
                        if (p.messageType == YgoConstants.MSG_SELECT_CHAIN)
                        {
                            int dpeChain = p.candidates.FindIndex(c => c.code == 60461804);
                            if (summonedDPE && !passedDPEQuickEffect && dpeChain >= 0)
                            {
                                passedDPEQuickEffect = true;
                                Debug.Log("[DPE TEST] MSG_SELECT_CHAIN: DPE optional quick effect offered -> Submitting -1 (PASS / Do not destroy)");
                                s.SubmitResponseInt(-1); // PASS
                                continue;
                            }
                        }

                        // 4. When DPE is destroyed, ocgcore prompts MSG_SELECT_EFFECTYN for card in GRAVE (promptCardCode == 60461804)
                        if (p.messageType == YgoConstants.MSG_SELECT_EFFECTYN && p.promptCardCode == 60461804)
                        {
                            triggeredDPEGraveEffect = true;
                            Debug.Log($"[DPE TEST] MSG_SELECT_EFFECTYN for DPE in {YgoConstants.GetLocationNameKR(p.promptLoc)} -> Submitting 1 (YES, register delayed revive)");
                            s.SubmitResponseInt(1);
                            continue;
                        }

                        // 5. During Turn 2 (opponent's turn) Standby Phase: ocgcore prompts MSG_SELECT_CARD with playerId == 0
                        if (p.messageType == YgoConstants.MSG_SELECT_CARD && triggeredDPEGraveEffect && s.TurnNumber >= 2)
                        {
                            int dpePick = p.candidates.FindIndex(c => c.code == 60461804);
                            if (dpePick >= 0)
                            {
                                Debug.Log($"[DPE TEST] Standby Phase: Selecting DPE from Graveyard (idx={dpePick}, candidateCount={p.candidates.Count}, askedPlayer={p.playerId})");
                                s.SubmitResponse(EncodeSelectIndices(dpePick));
                                revivedDPEInOppStandby = true;
                                continue;
                            }
                        }
                    }

                    s.SubmitResponse(ai.ComputeResponse(s, p));

                    if (revivedDPEInOppStandby && s.Players[0].mzone.Any(c => c != null && c.code == 60461804))
                    {
                        Debug.Log("[DPE TEST] DPE successfully REVIVED from GY on opponent's Standby Phase!");
                        break;
                    }
                }

                if (!activatedFusionDestiny) throw new Exception("Failed to activate Fusion Destiny");
                if (!summonedDPE) throw new Exception("Failed to summon DPE");
                if (!passedDPEQuickEffect) throw new Exception("Failed to test DPE quick effect pass/cancel");
                if (!triggeredDPEGraveEffect) throw new Exception("Failed to trigger DPE graveyard effect");
                if (!revivedDPEInOppStandby || s.RetryCount != 0 || !s.Players[0].mzone.Any(c => c != null && c.code == 60461804))
                    throw new Exception("Failed to complete DPE revival during opponent's Standby Phase");
            }
        }

        private static void TestTearlamentsSegocAndMandatoryVsOptionalTriggers()
        {
            Debug.Log("[TOKEN & EFFECT TEST 5] Tearlaments GY Trigger Order (SEGOC) & Mandatory vs Optional Distinction...");

            // 74191942 = Painful Choice (고통의 선택: sends 4 cards from Deck to GY simultaneously by card effect)
            // 15341821 = Dandylion (댄디라이언: Mandatory GY trigger -> summons Fluff Tokens 15341822 and 15341823)
            // 79814787 = The White Stone of Legend (전설의 백석: Mandatory GY trigger -> adds Blue-Eyes 89631139 from Deck to Hand)
            // 572850   = Tearlaments Scheiren (티아라멘츠 셰이렌: Optional GY trigger -> Fusion Summon from GY)
            // 74078255 = Tearlaments Merrli (티아라멘츠 메이루: Optional GY trigger -> Fusion Summon from GY)
            // 89631139 = Blue-Eyes White Dragon (푸른 눈의 백룡)
            // 92731385 = Tearlaments Kitkallos (티아라멘츠 키토칼로스: Extra Deck Fusion Monster)
            var p0Main = new List<uint>();
            for (int i = 0; i < 34; i++) p0Main.Add(74191942);
            p0Main.Add(15341821);
            p0Main.Add(79814787);
            p0Main.Add(572850);
            p0Main.Add(74078255);
            p0Main.Add(89631139);
            p0Main.Add(89631139);
            uint[] p0Extra = { 92731385 };

            var p1Main = new List<uint>();
            while (p1Main.Count < 40) p1Main.Add(20721928);

            using (var s = YgoProjectBuilder.StartSeedMatching("Tearlaments simultaneous triggers", p0Main.ToArray(), p0Extra, p1Main.ToArray(), Array.Empty<uint>(), true,
                session => session.Players[0].hand.Count == 5 && session.Players[0].hand.All(c => c.code == 74191942)))
            {
                var ai = new TacticalAI { Difficulty = AIDifficulty.Smart };
                bool activatedPainfulChoice = false;
                bool pickedPainfulFive = false;
                bool oppPickedBlueEyes = false;
                bool verifiedForcedMandatorySegoc = false;
                bool verifiedOptionalTearlamentsSegoc = false;
                bool verifiedOptionalPassSecondTear = false;
                bool verifiedKitkallosOptionalDecline = false;

                int guard = 0;
                while (!s.IsFinished && guard++ < 80)
                {
                    if (s.ActivePrompt == null) { s.StepUntilPrompt(); continue; }
                    var p = s.ActivePrompt;

                    // 1. Activate Painful Choice from hand
                    if (p.playerId == 0 && p.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                    {
                        if (!activatedPainfulChoice && p.activatable.Any(a => a.code == 74191942))
                        {
                            int idx = p.activatable.FindIndex(a => a.code == 74191942);
                            activatedPainfulChoice = true;
                            Debug.Log($"[SEGOC TEST] Activating Painful Choice (idx={idx})");
                            s.SubmitResponseInt((idx << 16) | 5);
                            continue;
                        }
                        if (verifiedKitkallosOptionalDecline)
                        {
                            break;
                        }
                    }

                    // 2. Painful Choice: Player 0 picks 5 cards from Deck (Dandylion, White Stone, Scheiren, Merrli, Blue-Eyes)
                    if (p.playerId == 0 && p.messageType == YgoConstants.MSG_SELECT_CARD && !pickedPainfulFive && p.min == 5 && p.max == 5)
                    {
                        int iDandy = p.candidates.FindIndex(c => c.code == 15341821);
                        int iStone = p.candidates.FindIndex(c => c.code == 79814787);
                        int iScheiren = p.candidates.FindIndex(c => c.code == 572850);
                        int iMerrli = p.candidates.FindIndex(c => c.code == 74078255);
                        int iBewd = p.candidates.FindIndex(c => c.code == 89631139);
                        if (iDandy < 0 || iStone < 0 || iScheiren < 0 || iMerrli < 0 || iBewd < 0)
                            throw new Exception("Missing expected cards in Deck for Painful Choice");
                        pickedPainfulFive = true;
                        Debug.Log("[SEGOC TEST] Selecting 5 cards for Painful Choice: Dandylion, White Stone, Scheiren, Merrli, Blue-Eyes");
                        s.SubmitResponse(EncodeSelectIndices(iDandy, iStone, iScheiren, iMerrli, iBewd));
                        continue;
                    }

                    // 3. Painful Choice: Opponent (Player 1) picks Blue-Eyes (89631139) to add to P0's hand, sending the 4 triggers to GY simultaneously!
                    if (p.playerId == 1 && p.messageType == YgoConstants.MSG_SELECT_CARD && pickedPainfulFive && !oppPickedBlueEyes)
                    {
                        int iBewd = p.candidates.FindIndex(c => c.code == 89631139);
                        if (iBewd < 0) throw new Exception("Opponent could not find Blue-Eyes among Painful Choice candidates");
                        oppPickedBlueEyes = true;
                        Debug.Log("[SEGOC TEST] Opponent picked Blue-Eyes -> Dandylion, White Stone, Scheiren, Merrli sent to GY simultaneously!");
                        s.SubmitResponse(EncodeSelectIndices(iBewd));
                        continue;
                    }

                    // 4. SEGOC Step A: Mandatory GY Triggers (Dandylion + White Stone) MUST be offered first with forced == true
                    if (p.playerId == 0 && p.messageType == YgoConstants.MSG_SELECT_CHAIN && oppPickedBlueEyes && !verifiedForcedMandatorySegoc)
                    {
                        Debug.Log($"[SEGOC TEST] Mandatory Trigger Chain Prompt: forced={p.forced}, IsTriggerChain={p.IsTriggerChain}, candidates=[{string.Join(",", p.candidates.Select(c => c.code))}]");
                        if (!p.forced)
                            throw new Exception("Expected p.forced == true for simultaneous mandatory GY triggers (Dandylion + White Stone)!");
                        if (!p.IsTriggerChain)
                            throw new Exception("Expected p.IsTriggerChain (speCount == 0x7f) == true for SEGOC mandatory triggers!");
                        if (p.candidates.Count != 2 || !p.candidates.Any(c => c.code == 15341821) || !p.candidates.Any(c => c.code == 79814787))
                            throw new Exception("Expected exactly Dandylion (15341821) and White Stone (79814787) in mandatory trigger candidates!");
                        if (p.candidates.Any(c => c.code == 572850 || c.code == 74078255))
                            throw new Exception("Optional Tearlaments triggers must NOT be mixed into Mandatory trigger selection!");

                        // Choose White Stone (79814787) as Chain Link 1 (so Dandylion automatically becomes Chain Link 2)
                        int stoneIdx = p.candidates.FindIndex(c => c.code == 79814787);
                        verifiedForcedMandatorySegoc = true;
                        Debug.Log($"[SEGOC TEST] Choosing White Stone (idx={stoneIdx}) as Mandatory Chain Link 1 (Dandylion auto-assigned to Chain Link 2)");
                        s.SubmitResponseInt(stoneIdx);
                        continue;
                    }

                    // 5. SEGOC Step B: Optional GY Triggers (Tearlaments Scheiren + Merrli) offered AFTER mandatory triggers with forced == false
                    if (p.playerId == 0 && p.messageType == YgoConstants.MSG_SELECT_CHAIN && verifiedForcedMandatorySegoc && !verifiedOptionalTearlamentsSegoc)
                    {
                        Debug.Log($"[SEGOC TEST] Optional Trigger Chain Prompt #1: forced={p.forced}, IsTriggerChain={p.IsTriggerChain}, currentChain={s.CurrentChain.Count}, candidates=[{string.Join(",", p.candidates.Select(c => c.code))}]");
                        if (p.forced)
                            throw new Exception("Expected p.forced == false for optional Tearlaments GY triggers!");
                        if (!p.IsTriggerChain)
                            throw new Exception("Expected p.IsTriggerChain (speCount == 0x7f) == true for SEGOC optional triggers!");
                        if (s.CurrentChain.Count != 2 || s.CurrentChain[0].code != 79814787 || s.CurrentChain[1].code != 15341821)
                            throw new Exception("Expected Chain Link 1 = White Stone (79814787) and Chain Link 2 = Dandylion (15341821) before optional triggers!");
                        if (p.candidates.Count != 2 || !p.candidates.Any(c => c.code == 572850) || !p.candidates.Any(c => c.code == 74078255))
                            throw new Exception("Expected both Tearlaments Scheiren (572850) and Merrli (74078255) in optional trigger candidates!");

                        // Choose Merrli (74078255) as Chain Link 3
                        int merrliIdx = p.candidates.FindIndex(c => c.code == 74078255);
                        verifiedOptionalTearlamentsSegoc = true;
                        Debug.Log($"[SEGOC TEST] Choosing Tearlaments Merrli (idx={merrliIdx}) as Optional Chain Link 3");
                        s.SubmitResponseInt(merrliIdx);
                        continue;
                    }

                    // 6. SEGOC Step C: Remaining Optional Trigger (Scheiren) offered for Chain Link 4 -> test declining/passing (-1)
                    if (p.playerId == 0 && p.messageType == YgoConstants.MSG_SELECT_CHAIN && verifiedOptionalTearlamentsSegoc && !verifiedOptionalPassSecondTear)
                    {
                        Debug.Log($"[SEGOC TEST] Optional Trigger Chain Prompt #2: forced={p.forced}, IsTriggerChain={p.IsTriggerChain}, currentChain={s.CurrentChain.Count}, candidates=[{string.Join(",", p.candidates.Select(c => c.code))}]");
                        if (p.forced)
                            throw new Exception("Expected p.forced == false for remaining optional Tearlaments trigger!");
                        if (s.CurrentChain.Count != 3 || s.CurrentChain[2].code != 74078255)
                            throw new Exception("Expected Chain Link 3 = Tearlaments Merrli (74078255)!");
                        if (!p.candidates.Any(c => c.code == 572850))
                            throw new Exception("Expected remaining Tearlaments Scheiren (572850) as candidate for Chain Link 4!");

                        verifiedOptionalPassSecondTear = true;
                        Debug.Log("[SEGOC TEST] Submitting -1 (PASS) on Tearlaments Scheiren so only Merrli activates as CL3!");
                        s.SubmitResponseInt(-1);
                        continue;
                    }

                    // 7. Post-Chain Resolution: Kitkallos (92731385) was Fusion Summoned on CL3 -> triggers single optional effect via MSG_SELECT_EFFECTYN
                    if (p.playerId == 0 && p.messageType == YgoConstants.MSG_SELECT_EFFECTYN && p.promptCardCode == 92731385)
                    {
                        verifiedKitkallosOptionalDecline = true;
                        Debug.Log("[SEGOC TEST] Kitkallos on-summon optional trigger (MSG_SELECT_EFFECTYN) received -> Submitting 0 (NO / Decline)");
                        s.SubmitResponseInt(0);
                        continue;
                    }

                    s.SubmitResponse(ai.ComputeResponse(s, p));
                }

                if (!verifiedForcedMandatorySegoc) throw new Exception("Failed to verify mandatory GY trigger SEGOC");
                if (!verifiedOptionalTearlamentsSegoc) throw new Exception("Failed to verify optional Tearlaments GY trigger SEGOC");
                if (!verifiedOptionalPassSecondTear) throw new Exception("Failed to verify declining 2nd optional Tearlaments trigger");
                if (!verifiedKitkallosOptionalDecline) throw new Exception("Failed to verify Kitkallos Fusion Summon and on-summon MSG_SELECT_EFFECTYN");

                // Verify LIFO reverse chain resolution order: CL3 (Merrli 74078255) -> CL2 (Dandylion 15341821) -> CL1 (White Stone 79814787)
                int cl3Idx = s.SolvedChainLinks.FindIndex(x => x.chainNumber == 3 && x.code == 74078255);
                int cl2Idx = s.SolvedChainLinks.FindIndex(x => x.chainNumber == 2 && x.code == 15341821);
                int cl1Idx = s.SolvedChainLinks.FindIndex(x => x.chainNumber == 1 && x.code == 79814787);
                Debug.Log($"[SEGOC TEST] SolvedChainLinks LIFO check: CL3_pos={cl3Idx}, CL2_pos={cl2Idx}, CL1_pos={cl1Idx}");
                if (cl3Idx < 0 || cl2Idx < 0 || cl1Idx < 0 || !(cl3Idx < cl2Idx && cl2Idx < cl1Idx))
                    throw new Exception("Chain did not resolve in LIFO reverse order (CL3 Merrli -> CL2 Dandylion -> CL1 White Stone)!");

                // Verify end board & hand state:
                // - Kitkallos (92731385) on field
                // - 2 Fluff Tokens on field
                // - Merrli (74078255) & Scheiren (572850) returned from GY to Deck as fusion materials!
                bool hasKitkallos = s.Players[0].mzone.Any(c => c != null && c.code == 92731385);
                int fluffTokens = s.Players[0].mzone.Count(c => c != null &&
                    (c.type & YgoConstants.TYPE_TOKEN) != 0 &&
                    (c.code == 15341822 || CardDatabase.Get(c.code)?.alias == 15341822));
                bool merrliInGy = s.Players[0].grave.Any(c => c.code == 74078255);
                bool scheirenInGy = s.Players[0].grave.Any(c => c.code == 572850);
                int bewdInHand = s.Players[0].hand.Count(c => c.code == 89631139);
                Debug.Log($"[SEGOC TEST] Post-Chain Board: Kitkallos={hasKitkallos}, FluffTokens={fluffTokens}, MerrliInGY={merrliInGy}, ScheirenInGY={scheirenInGy}, BlueEyesInHand={bewdInHand}");

                if (!hasKitkallos) throw new Exception("Tearlaments Kitkallos should be Fusion Summoned on the field!");
                if (fluffTokens != 2) throw new Exception($"Expected 2 Fluff Tokens from Dandylion, got {fluffTokens}");
                if (merrliInGy || scheirenInGy) throw new Exception("Merrli and Scheiren should have been returned from GY to Deck as Fusion Materials!");
                if (bewdInHand != 2) throw new Exception($"Expected 2 Blue-Eyes in hand (1 from Painful Choice + 1 from White Stone), got {bewdInHand}");
            }
        }

        private static void TestArchetypeCatalogAndPureDeckBuilder()
        {
            Debug.Log("[TOKEN & EFFECT TEST 6] Pure Archetype Catalog & Example Deck Builder...");
            var archetypes = CardDatabase.GetAllArchetypes();
            Debug.Log($"[ARCHETYPE TEST] Total Pure Archetypes discovered: {archetypes.Count}");
            if (archetypes.Count < 350)
                throw new Exception($"Expected >= 350 archetypes, got {archetypes.Count}");

            string[] requiredArchetypes = { "티아라멘츠", "크샤트리라", "라뷰린스", "푸른 눈", "블랙 매지션", "상검", "섬도희", "드래곤메이드", "엘리멘틀 히어로", "데스티니 히어로", "유벨", "천배룡" };
            foreach (string req in requiredArchetypes)
            {
                var arch = archetypes.FirstOrDefault(a => a.name.Contains(req));
                if (arch == null)
                    throw new Exception($"Missing required archetype '{req}' in GetAllArchetypes()");
                var (main, extra) = CardDatabase.BuildPureArchetypeDeck(arch);
                if (main.Count != 40)
                    throw new Exception($"Archetype '{arch.name}' example deck main count should be 40, got {main.Count}");
                if (extra.Count > 15)
                    throw new Exception($"Archetype '{arch.name}' example deck extra count should be <= 15, got {extra.Count}");
            }

            foreach (var arch in archetypes)
            {
                var (main, extra) = CardDatabase.BuildPureArchetypeDeck(arch);
                if (main.Count != 40)
                    throw new Exception($"Archetype '{arch.name}' built {main.Count} main deck cards instead of 40");
                if (extra.Count > 15)
                    throw new Exception($"Archetype '{arch.name}' built {extra.Count} extra deck cards (> 15)");
            }
            Debug.Log($"[ARCHETYPE TEST] Verified 40-card pure example deck generation across all {archetypes.Count} archetypes!");
        }
    }
}
