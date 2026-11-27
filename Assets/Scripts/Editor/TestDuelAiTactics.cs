using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static class TestDuelAiTactics
    {
        private const uint Ash = 14558127, Upstart = 70368879, OriginalSpoils = 89023486, SnakeEyeAsh = 9674034;
        private const uint Stratos = 40044918, Reinforcement = 32807846, Baronne = 84815190;
        private const uint Nibiru = 27204311, LinkSpider = 98978921, Dandylion = 15341821;
        private const uint Foolish = 81439173, Passcode = 93104632, Imperm = 10045474, GeminiElf = 69140098;
        private const uint Merrli = 74078255, Sulliek = 74920585;
        private const uint BrandedFusion = 44362883;
        private static readonly uint[] Empty = Array.Empty<uint>();

        public static void RunHeadless()
        {
            int exit = 1;
            int scriptErrors = 0;
            try
            {
                string streaming = Application.streamingAssetsPath;
                Require(YgoNativeBridge.Initialize(streaming, (kind, message) =>
                {
                    if (kind != 0) return;
                    scriptErrors++;
                    Debug.LogError(message);
                }) == 0, "Could not initialize the duel engine.");
                CardDatabase.Load(streaming);
                TestChainChooser.Run();
                Run();
                AiBattleProbe.VerifyFixtures();
                Require(scriptErrors == 0, $"Native AI test script errors: {scriptErrors}");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        internal static void Run()
        {
            VerifyNegationTargets();
            VerifyNativeFieldNegation(Imperm);
            VerifyNativeFieldNegation(DuelTactics.EffectVeiler);
            VerifyTriggerOrder();
            VerifyNativeTriggerProtection();
            VerifyAshCommitment();
            VerifyBaitAgainstBaronne();
            VerifySummonBudget();
            VerifyRevealedSweeper();
            VerifyGxResponses();
            VerifyGxSummonChoices();
            VerifyNativeBentenSearch();
            VerifyNativeRitualAcePreservation();
            VerifyNativeRitualWithSpareMaterials();
            VerifyAdditionalRitualAces();
            VerifyGxSelfDamageAndDestruction();
            VerifyVolcanicEmperorSiege();
            VerifyFieldAceEffects();
            VerifyDrytronAceCosts();
            VerifyAccesscodeCosts();
            VerifyGxBattlePriority();
            Debug.Log("[VERIFY AI TACTICS] Ash conservation, Baronne bait, summon budget, revealed sweeper and chain ordering passed. Retry=0");
        }

        private static void VerifyGxResponses()
        {
            const uint maxxC = 23434538, calledBy = 24224830, droplet = 24299458, predaprimitive = 89176044;
            using var duel = new DuelSession();
            duel.Players[0].deckCount = 30;
            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
            var maxx = new SelectableCardItem { code = maxxC, controller = 0, location = YgoConstants.LOCATION_HAND };
            var called = new SelectableCardItem { code = calledBy, controller = 0, location = YgoConstants.LOCATION_SZONE };
            var upstart = new ChainLinkInfo { code = Upstart, triggerPlayer = 1 };
            duel.CurrentChain.Add(upstart);
            Require(!DuelTactics.CanActivate(duel, chain, maxx), "Maxx C was wasted on a draw effect.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = BrandedFusion, triggerPlayer = 1 };
            Require(DuelTactics.CanActivate(duel, chain, maxx), "Maxx C missed an opponent's Branded Fusion.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = BrandedFusion, triggerPlayer = 0 };
            Require(!DuelTactics.CanActivate(duel, chain, maxx), "Maxx C was chained to a friendly Branded Fusion.");

            duel.CurrentChain[0] = new ChainLinkInfo { code = Ash, triggerPlayer = 1, triggerLoc = YgoConstants.LOCATION_HAND };
            duel.Players[1].grave.Add(new LiveCardState { code = Ash });
            Require(DuelTactics.CanActivate(duel, chain, called), "Called by the Grave missed an opposing Ash in the GY.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = Ash, triggerPlayer = 0, triggerLoc = YgoConstants.LOCATION_HAND };
            Require(!DuelTactics.CanActivate(duel, chain, called), "Called by the Grave responded to a friendly effect.");
            duel.CurrentChain[0] = new ChainLinkInfo { code = Ash, triggerPlayer = 1, triggerLoc = YgoConstants.LOCATION_HAND };
            duel.CurrentChain.Add(new ChainLinkInfo { code = calledBy, triggerPlayer = 0 });
            var target = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = 0,
                min = 1, max = 1, SelectionHint = DuelTactics.Banish };
            target.candidates.Add(new SelectableCardItem { code = Baronne, controller = 1, location = YgoConstants.LOCATION_GRAVE });
            target.candidates.Add(new SelectableCardItem { code = Ash, controller = 1, location = YgoConstants.LOCATION_GRAVE });
            Require(Smart().ComputeResponse(duel, target).SequenceEqual(DuelSession.PackCardSelection(1)),
                "Called by the Grave ignored the immediate chain source.");

            var cost = new SelectableCardItem { code = Stratos, controller = 0, location = YgoConstants.LOCATION_HAND };
            var boss = new SelectableCardItem { code = Baronne, controller = 0, location = YgoConstants.LOCATION_MZONE };
            target.SelectionHint = DuelTactics.SendToGrave;
            target.max = 2;
            target.candidates.Clear();
            target.candidates.AddRange(new[] { boss, cost });
            duel.CurrentChain.Clear();
            duel.CurrentChain.Add(new ChainLinkInfo { code = droplet });
            Require(Smart().ComputeResponse(duel, target).SequenceEqual(DuelSession.PackCardSelection(1)),
                "Droplet should send only the cheaper card when one cost suffices.");
            var primitive = new SelectableCardItem { code = predaprimitive, location = YgoConstants.LOCATION_GRAVE };
            Require(!DuelTactics.CanActivate(duel, chain, primitive), "Predaprimitive's optional trigger destroyed an own Fusion.");
            primitive.location = YgoConstants.LOCATION_HAND;
            Require(DuelTactics.CanActivate(duel, chain, primitive), "Predaprimitive lost its Fusion Spell search from hand.");
            Debug.Log("[VERIFY GX AI RESPONSE] Maxx C, Called by the Grave, and Droplet cost targeting passed.");
        }

        private static void VerifyGxSummonChoices()
        {
            using var duel = new DuelSession();
            var summon = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = 0, min = 1, max = 1 };
            summon.candidates.Add(new SelectableCardItem { code = 87746184, location = YgoConstants.LOCATION_EXTRA });
            summon.candidates.Add(new SelectableCardItem { code = 44146295, location = YgoConstants.LOCATION_EXTRA });
            duel.CurrentChain.Add(new ChainLinkInfo { code = BrandedFusion });
            Require(ChoosesSecond(), "Branded Fusion failed to choose a legal Mirrorjade.");
            summon.candidates[1].code = 60461804;
            duel.CurrentChain[0].code = 52947044;
            Require(ChoosesSecond(), "Fusion Destiny failed to choose a legal DPE.");
            summon.candidates[1].code = 58481572;
            duel.CurrentChain[0].code = 21143940;
            Require(ChoosesSecond(), "Mask Change failed to choose a legal Dark Law.");

            const uint garura = 11765832;
            var poly = new SelectableCardItem { code = 48130397 };
            var darkDragon = new LiveCardState { code = 74677422, position = YgoConstants.POS_FACEUP_ATTACK,
                attribute = YgoConstants.ATTRIBUTE_DARK, race = YgoConstants.RACE_DRAGON };
            var darkDragonTwo = new LiveCardState { code = 88264978, position = YgoConstants.POS_FACEUP_ATTACK,
                attribute = YgoConstants.ATTRIBUTE_DARK, race = YgoConstants.RACE_DRAGON };
            duel.Players[0].extra.Add(new LiveCardState { code = garura });
            duel.Players[0].mzone[0] = darkDragon;
            duel.Players[0].mzone[1] = darkDragonTwo;
            Require(!DuelTactics.CanActivate(duel, summon, poly), "Super Polymerization spent only friendly material.");
            duel.Players[0].mzone[1] = null;
            duel.Players[1].mzone[0] = darkDragonTwo;
            Require(DuelTactics.CanActivate(duel, summon, poly), "Super Polymerization missed usable opposing material.");

            var benten = new SelectableCardItem { code = 77235086, location = YgoConstants.LOCATION_HAND };
            var ace = new SelectableCardItem { code = 39618799, location = YgoConstants.LOCATION_MZONE };
            summon.candidates.Clear();
            summon.candidates.AddRange(new[] { ace, benten });
            summon.SelectionHint = DuelTactics.Release;
            duel.CurrentChain[0].code = 39996157;
            Require(ChoosesSecond(), "The Ritual tribute should use Benten's search before spending the established ace.");
            var powerBond = new SelectableCardItem { code = 37630732 };
            duel.Players[0].lp = 1000;
            duel.Players[0].extra.Clear();
            duel.Players[0].extra.Add(new LiveCardState { code = 74157028 });
            Require(!DuelTactics.CanActivate(duel, summon, powerBond), "Power Bond paid lethal burn when Battle Phase was unavailable.");
            Debug.Log("[VERIFY GX AI SUMMON] Branded, HERO, Super Polymerization and Benten choices passed.");

            bool ChoosesSecond() => Smart().ComputeResponse(duel, summon).SequenceEqual(DuelSession.PackCardSelection(1));
        }

        private static void VerifyGxBattlePriority()
        {
            using var duel = new DuelSession();
            var attacker = new LiveCardState { code = 89631139, position = YgoConstants.POS_FACEUP_ATTACK, attack = 3000 };
            var yubel = new LiveCardState { code = 78371393, position = YgoConstants.POS_FACEUP_ATTACK };
            var pain = new LiveCardState { code = 65261141, position = YgoConstants.POS_FACEUP_ATTACK };
            var own = duel.Players[0];
            var rival = duel.Players[1];
            own.mzone[0] = attacker;
            rival.lp = 2000;
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, playerId = 0, canBp = true, canEp = true };
            idle.repositionable.Add(new SelectableCardItem { code = attacker.code, location = YgoConstants.LOCATION_MZONE });
            Require(Read(Smart().ComputeResponse(duel, idle)) == 6, "A visible direct lethal should enter Battle Phase immediately.");
            rival.mzone[0] = yubel;
            var battle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_BATTLECMD, playerId = 0, canM2 = true };
            var attack = new SelectableCardItem { code = attacker.code, location = YgoConstants.LOCATION_MZONE };
            battle.attackable.Add(attack);
            Require(Read(Smart().ComputeResponse(duel, battle)) == 2, "The AI attacked into Yubel's lethal reflection.");
            own.mzone[0] = yubel;
            own.szone[0] = pain;
            rival.mzone[0] = attacker;
            attack.code = yubel.code;
            Require(Read(Smart().ComputeResponse(duel, battle)) == 1, "Yubel did not exploit Nightmare Pain's reflection.");
            Debug.Log("[VERIFY GX AI BATTLE] Open lethal, Yubel defense and Nightmare Pain retaliation passed.");
        }

        private static void VerifyNativeBentenSearch()
        {
            const uint ritual = 39996157, dakini = 39618799, benten = 77235086, petitAngel = 76103404;
            uint[] required = { ritual, dakini, benten, petitAngel };
            using var duel = YgoProjectBuilder.StartSeedMatching("Dakini and Benten", Deck(required), Empty, Deck(GeminiElf), Empty, true,
                current => required.All(code => current.Players[0].hand.Any(card => card.code == code)));
            bool searched = false;
            duel.OnCardMoved += move => searched |= move.fromLoc == YgoConstants.LOCATION_DECK &&
                move.toCon == 0 && move.toLoc == YgoConstants.LOCATION_HAND && duel.ResolvingChainLink?.code == benten;
            SubmitIdle(duel, 5, ritual);
            var ai = Smart();
            for (int step = 0; step < 25 && !duel.IsFinished && duel.RetryCount == 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (IsIdle(prompt)) break;
                duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
            }
            Require(duel.Players[0].mzone.Any(card => card?.code == dakini) &&
                duel.Players[0].grave.Any(card => card.code == benten) && searched &&
                duel.RetryCount == 0, "Native Dakini must use Benten and resolve the resulting LIGHT Fairy search.");
            Debug.Log("[VERIFY GX AI RITUAL] Native Dakini summoned and Benten's search resolved. Retry=0");
        }

        private static void VerifyNativeRitualAcePreservation()
        {
            const uint ritual = 39996157, vishnu = 78316184, idaten = 3629090, tribute = 98777036;
            var deck = Deck(ritual, ritual, vishnu, idaten, tribute);
            using var duel = YgoProjectBuilder.StartSeedMatching("Vishnu into Idaten", deck, Empty, Deck(GeminiElf), Empty, true,
                current => current.Players[0].hand.Count(card => card.code == ritual) == 2 &&
                    new[] { vishnu, idaten, tribute }.All(code => current.Players[0].hand.Any(card => card.code == code)));
            SubmitIdle(duel, 5, ritual);
            Drive(duel, IsIdle, vishnu);
            var own = duel.Players[0];
            var idle = duel.ActivePrompt;
            Require(own.mzone.Any(card => card is { code: vishnu, IsFaceUp: true }) &&
                idle.activatable.Any(card => card.code == ritual && card.location == YgoConstants.LOCATION_HAND),
                "The native engine must offer a second Ritual while Vishnu is the only usable material.");
            var ai = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(), searchCombos: true)
            {
                Difficulty = AIDifficulty.Master,
            };
            int choice = Read(ai.ComputeResponse(duel, idle));
            Require(choice == 7, $"With only Vishnu as material, the AI must end its turn instead of summoning Idaten. Choice={choice}");
            duel.SubmitResponseInt(choice);
            Drive(duel, IsIdle);
            Require(duel.TurnNumber == 2 && own.mzone.Any(card => card is { code: vishnu, IsFaceUp: true }) &&
                own.hand.Any(card => card.code == idaten) && duel.RetryCount == 0, "Vishnu did not survive the turn.");
            Debug.Log("[VERIFY GX AI RITUAL] Kept Vishnu when the only available follow-up would tribute it for Idaten. Retry=0");
        }

        private static void VerifyNativeRitualWithSpareMaterials()
        {
            const uint ritual = 39996157, absolute = 11398951, vishnu = 78316184, idaten = 3629090,
                tribute = 98777036, petitAngel = 76103404;
            foreach (uint secondSpell in new[] { ritual, absolute })
            {
                uint[] openingHand = { ritual, secondSpell, vishnu, idaten, tribute, GeminiElf, petitAngel };
                using var duel = new DuelSession(recordPlanningReplay: true);
                var deck = Deck(openingHand);
                var opponent = Deck(GeminiElf);
                bool found = false;
                for (ulong n = 1; n <= 600 && !found; n++)
                {
                    Require(duel.StartNewDuel(deck, Empty, opponent, Empty, seed: new[] { n * 29 + 101, n * 47 + 7, n * 71 + 13, n * 97 + 3 },
                        playerOpening: new DuelOpening(8000, openingHand.Length, 0)), "Could not start the prepared Ritual duel.");
                    found = duel.Players[0].hand.Select(card => card.code).OrderBy(code => code)
                        .SequenceEqual(openingHand.OrderBy(code => code));
                }
                Require(found, "Could not draw the prepared Ritual opening.");
                SubmitIdle(duel, 5, ritual);
                var material = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_SUM, vishnu);
                int tributeIndex = material.candidates.FindIndex(card => card.code == tribute);
                Require(tributeIndex >= 0, "Vishnu's initial tribute was absent.");
                duel.SubmitResponse(DuelSession.PackCardSelection(tributeIndex));
                var idle = Drive(duel, IsIdle);
                var second = idle.activatable.Find(card => card.code == secondSpell && card.location == YgoConstants.LOCATION_HAND);
                Require(second != null, "The engine did not offer the second Ritual.");
                SubmitIdle(duel, 5, secondSpell);
                var ai = Smart();
                for (int step = 0; step < 25 && !duel.IsFinished && duel.RetryCount == 0; step++)
                {
                    var prompt = duel.ActivePrompt;
                    if (prompt == null) { duel.StepUntilPrompt(); continue; }
                    if (IsIdle(prompt)) break;
                    duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
                }
                Require(duel.Players[0].mzone.Any(card => card is { code: vishnu, IsFaceUp: true }) &&
                    duel.Players[0].mzone.Any(card => card is { code: idaten, IsFaceUp: true }) && duel.RetryCount == 0,
                    $"The native Ritual {secondSpell} did not summon Idaten while keeping Vishnu. " + string.Join(" | ", duel.Logs.TakeLast(8)));
                Debug.Log($"[VERIFY GX AI RITUAL] {secondSpell} summoned Idaten with spare materials while Vishnu stayed on the field. Retry=0");
            }
        }

        private static void VerifyAdditionalRitualAces()
        {
            const uint ritual = DuelAiPolicy.MachineAngelRitual, idaten = 3629090, summonedSkull = 70781052;
            var ritualDeck = new[] { idaten }.Concat(Enumerable.Repeat(ritual, 39)).ToArray();
            foreach (uint ace in new[] { DuelAiPolicy.HeraldOfUltimateness, DuelAiPolicy.HeraldOfPerfection, DuelAiPolicy.Izana })
            {
                using var duel = new DuelSession(recordPlanningReplay: true);
                OpenGxAiFixture(duel, ritualDeck, Empty, 8000, ace,
                    own => own.hand.Any(card => card.code == ritual) && own.hand.Any(card => card.code == idaten));
                var idle = Drive(duel, IsIdle);
                Require(idle.activatable.Any(card => card.code == ritual), $"The engine did not offer to tribute {ace} for Idaten.");
                var pilot = new TacticalAI(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded(), searchCombos: true)
                    { Difficulty = AIDifficulty.Master };
                int action = Read(pilot.ComputeResponse(duel, idle));
                Require(action == 7, $"The AI chose {action} instead of preserving ritual ace {ace}.");
                duel.SubmitResponseInt(action);
                Drive(duel, IsIdle);
                Require(duel.Players[0].mzone.Any(card => card?.code == ace) && duel.RetryCount == 0,
                    $"Ritual ace {ace} did not survive a legal follow-up.");

                using var mock = new DuelSession();
                mock.Players[0].mzone[0] = FieldMonster(ace);
                var tribute = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
                tribute.summonable.Add(Effect(summonedSkull, 0, YgoConstants.LOCATION_HAND));
                Require(Read(Smart().ComputeResponse(mock, tribute)) == 7, $"{ace} was offered for a normal tribute summon.");
                mock.Players[0].mzone[1] = FieldMonster(GeminiElf, 1);
                var cost = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_TRIBUTE, min = 1, max = 1 };
                cost.candidates.Add(new SelectableCardItem { code = ace, location = YgoConstants.LOCATION_MZONE });
                cost.candidates.Add(new SelectableCardItem { code = GeminiElf, location = YgoConstants.LOCATION_MZONE, sequence = 1 });
                Require(Smart().ComputeResponse(mock, cost).SequenceEqual(DuelSession.PackCardSelection(1)),
                    $"Tribute selection spent {ace} instead of the spare monster.");
                Debug.Log($"[VERIFY GX RITUAL ACE] {ace}: preserved through native ritual and spare-material tribute selection.");
            }
            using var merciful = new DuelSession(recordPlanningReplay: true);
            const uint draw = DuelAiPolicy.MercifulMachineAngel;
            OpenGxAiFixture(merciful, Deck(draw), Empty, 8000, DuelAiPolicy.Dakini, own => own.hand.Count == 5);
            var active = Drive(merciful, IsIdle);
            Require(active.activatable.Any(card => card.code == draw) && Read(Smart().ComputeResponse(merciful, active)) == 7,
                "Merciful Machine Angel sacrificed the lone Dakini for a draw.");
            var board = merciful.Players[0];
            var magnificent = Effect(DuelAiPolicy.MagnificentMachineAngel, 0);
            Require(!DuelTactics.CanActivate(merciful, active, magnificent), "Magnificent Machine Angel offered the lone Dakini.");
            board.hand.Add(FieldMonster(DuelAiPolicy.Benten));
            Require(DuelTactics.CanActivate(merciful, active, magnificent), "Magnificent Machine Angel rejected a spare Benten.");
            Debug.Log("[VERIFY GX RITUAL ACE] Merciful and Magnificent Machine Angel preserved a lone Dakini.");
        }

        private static void VerifyGxSelfDamageAndDestruction()
        {
            const uint bond = DuelAiPolicy.PowerBond, cyberDragon = 70095154, cyberTwin = 74157028,
                fortress = 79229522, overdragon = 64599569, squirmer = 24215921, spirit = 90829280, yubel = 78371393;
            using (var duel = new DuelSession(recordPlanningReplay: true))
            {
                OpenGxAiFixture(duel, Deck(bond, cyberDragon, cyberDragon), new[] { cyberTwin, fortress }, 2800, 0,
                    own => own.hand.Any(card => card.code == bond) && own.hand.Count(card => card.code == cyberDragon) >= 2);
                var idle = Drive(duel, IsIdle);
                Require(idle.activatable.Any(card => card.code == bond), "The native Power Bond fixture lacks a legal Fusion.");
                Require(!DuelTactics.CanActivate(duel, idle, Effect(bond, 0, YgoConstants.LOCATION_HAND)),
                    "An unavailable Fortress made lethal Power Bond damage appear safe.");
                int action = Read(Smart().ComputeResponse(duel, idle));
                Require((action & 0xffff) != 5, "The AI activated lethal Power Bond.");
            }
            using (var duel = new DuelSession(recordPlanningReplay: true))
            {
                OpenGxAiFixture(duel, Deck(bond, cyberDragon, cyberDragon), new[] { cyberTwin }, 2800, 0,
                    own => own.hand.Any(card => card.code == bond) && own.hand.Count(card => card.code == cyberDragon) >= 2,
                    second: true, rivalLife: 4000);
                Drive(duel, IsIdle);
                duel.SubmitResponseInt(7);
                var lethal = Drive(duel, IsIdle);
                Require(lethal.playerId == 1 && lethal.canBp && (Read(Smart().ComputeResponse(duel, lethal)) & 0xffff) == 5,
                    "The AI missed a native Power Bond lethal that ends before the burn.");
            }
            using (var duel = new DuelSession())
            {
                var own = duel.Players[0];
                own.lp = 2800;
                own.extra.Add(FieldMonster(overdragon));
                for (int i = 0; i < 4; i++) own.hand.Add(FieldMonster(cyberDragon));
                var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD };
                Require(!DuelAiPolicy.CanRiskPowerBond(duel, idle), "Variable original ATK made Overdragon look free of damage.");
                own.lp = 3201;
                Require(DuelAiPolicy.CanRiskPowerBond(duel, idle), "Power Bond rejected a nonlethal damage ceiling.");
            }
            foreach (bool replacement in new[] { false, true })
            {
                using var duel = new DuelSession();
                OpenGxAiFixture(duel, replacement ? Deck(squirmer, squirmer, yubel) : Deck(squirmer), Empty, 8000,
                    spirit, own => own.hand.Any(card => card.code == squirmer));
                Drive(duel, IsIdle);
                SubmitIdle(duel, 5, squirmer);
                var optional = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_YESNO);
                int choice = Read(Smart().ComputeResponse(duel, optional));
                Require(choice == (replacement ? 1 : 0), "Squirmer did not distinguish free development from pointless self-destruction.");
                if (!replacement)
                {
                    duel.SubmitResponseInt(choice);
                    Drive(duel, IsIdle);
                    Require(duel.Players[0].mzone.Any(card => card?.code == spirit) && duel.RetryCount == 0,
                        "Squirmer did not preserve Spirit of Yubel without a replacement.");
                }
            }
            using (var duel = new DuelSession())
            {
                const uint exodia = 83257450, frame = 64043465, strike = 40605147;
                var own = duel.Players[0];
                var rival = duel.Players[1];
                own.lp = 5000;
                rival.lp = 6000;
                own.mzone[0] = FieldMonster(exodia);
                own.szone[0] = FieldMonster(strike);
                own.szone[1] = FieldMonster(frame, 1);
                own.szone[2] = FieldMonster(frame, 2);
                var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
                var blaze = new SelectableCardItem { code = exodia, location = YgoConstants.LOCATION_MZONE,
                    description = DuelTactics.ExoddBlazeEquipEffect };
                Require(!DuelTactics.CanActivate(duel, idle, blaze), "Exodd Blaze halved life and swept only its own back row in Main Phase 2.");
                idle.canBp = true;
                Require(DuelTactics.CanActivate(duel, idle, blaze), "Exodd Blaze missed an open lethal attack.");
                rival.mzone[0] = FieldMonster(yubel);
                Require(!DuelTactics.CanActivate(duel, idle, blaze), "Exodd Blaze opened an attack into lethal Yubel reflection.");
            }
            using (var duel = new DuelSession())
            {
                var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 0 };
                var guide = FieldMonster(10802915);
                duel.Players[1].mzone[0] = guide;
                duel.CurrentChain.Add(new ChainLinkInfo { code = guide.code, triggerPlayer = 1,
                    triggerLoc = YgoConstants.LOCATION_MZONE });
                duel.Players[0].szone[0] = FieldMonster(5851097);
                var droplet = Effect(DuelTactics.ForbiddenDroplet, 0);
                Require(!DuelTactics.CanActivate(duel, chain, droplet),
                    "Droplet broke Vanity's Emptiness to negate a summon already forbidden by it.");
            }
            using (var duel = new DuelSession())
            {
                const uint droplet = DuelTactics.ForbiddenDroplet, herald = DuelAiPolicy.HeraldOfUltimateness;
                var own = duel.Players[0];
                own.mzone[0] = FieldMonster(herald);
                duel.Players[1].mzone[0] = FieldMonster(Stratos);
                var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN };
                var spell = new SelectableCardItem { code = droplet, location = YgoConstants.LOCATION_SZONE, sequence = 2 };
                own.szone[2] = FieldMonster(droplet, 2);
                Require(!DuelTactics.CanActivate(duel, chain, spell),
                    "Droplet activated with only the established Herald available as its cost.");
                own.hand.Add(FieldMonster(GeminiElf));
                Require(DuelTactics.CanActivate(duel, chain, spell), "Droplet ignored a spare hand cost.");
                duel.CurrentChain.Add(new ChainLinkInfo { code = droplet });
                var cost = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, SelectionHint = DuelTactics.SendToGrave,
                    min = 1, max = 1 };
                cost.candidates.Add(new SelectableCardItem { code = herald, location = YgoConstants.LOCATION_MZONE });
                cost.candidates.Add(new SelectableCardItem { code = GeminiElf, location = YgoConstants.LOCATION_HAND });
                Require(Smart().ComputeResponse(duel, cost).SequenceEqual(DuelSession.PackCardSelection(1)),
                    "Droplet sent Herald instead of the expendable card.");
            }
            Debug.Log("[VERIFY GX SELF DAMAGE] Power Bond, native Squirmer, and Exodd Blaze timing passed.");
        }

        private static void VerifyVolcanicEmperorSiege()
        {
            const uint emperor = DuelTactics.VolcanicEmperor, excluder = DuelTactics.MiracleExcluder, dolphin = 17955766;
            using var duel = new DuelSession();
            Require(duel.StartNewDuel(Deck(excluder, dolphin), Empty, Deck(GeminiElf), Empty,
                seed: new[] { 11UL, 29UL, 71UL, 97UL }, playerOpening: new DuelOpening(500, 5, 0, destinyUses: 0),
                opponentOpening: new DuelOpening(8000, 5, emperor)), "Could not start the Volcanic Emperor fixture.");
            var prompt = duel.ActivePrompt;
            Require(prompt?.messageType == YgoConstants.MSG_SELECT_IDLECMD, "The Volcanic fixture did not open in Main Phase.");
            int summon = prompt.activatable.FindIndex(card => card.code == excluder);
            Require(summon >= 0 && DuelTactics.HasLethalSpecialSummonBurn(duel, 0) &&
                !DuelTactics.CanActivate(duel, prompt, prompt.activatable[summon]),
                "The native Excluder summon was not rejected at 500 LP against Volcanic Emperor.");
            Require(BitConverter.ToInt32(Smart().ComputeResponse(duel, prompt), 0) != ((summon << 16) | 5),
                "The main-phase AI activated the fatal special summon.");

            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN };
            var increase = Effect(DuelTactics.VisionIncrease, 1, YgoConstants.LOCATION_SZONE);
            Require(!DuelTactics.CanActivate(duel, chain, increase), "Increase still chains into fatal summon damage.");
            Require(DuelTactics.CanActivate(duel, chain, Effect(excluder, 2, YgoConstants.LOCATION_GRAVE)),
                "The AI blocked Excluder's non-summoning banish effect.");
            duel.Players[0].lp = 501;
            Require(DuelTactics.CanActivate(duel, prompt, prompt.activatable[summon]),
                "A single 500 damage trigger should not disable a recoverable summon line at 501 LP.");
            duel.Players[0].lp = 500;
            duel.Players[1].mzone[2].effectsDisabled = true;
            Require(DuelTactics.CanActivate(duel, prompt, prompt.activatable[summon]),
                "The AI treated a negated Emperor as an active burn threat.");
            Require(duel.RetryCount == 0, "The Volcanic Emperor fixture retried a response.");
            Debug.Log("[VERIFY GX VOLCANIC BURN] Fatal optional summons rejected, non-summoning effects and safe windows kept.");
        }

        private static void OpenGxAiFixture(DuelSession duel, uint[] deck, uint[] extra, int life, uint ace,
            Func<PlayerFieldSnapshot, bool> ready, bool second = false, int rivalLife = 8000)
        {
            for (ulong seed = 1; seed <= 32; seed++)
            {
                Require(duel.StartNewDuel(Deck(GeminiElf), Empty, deck, extra, p0GoesFirst: second,
                    seed: new[] { seed, 29UL, 71UL, 97UL }, opponentOpening: new DuelOpening(life, 5, ace),
                    playerOpening: new DuelOpening(rivalLife, 5, 0)),
                    "Could not start the GX native fixture.");
                if (ready(duel.Players[duel.AiPlayerIndex])) return;
            }
            throw new InvalidOperationException("Could not draw the GX native fixture opening.");
        }

        private static void VerifyFieldAceEffects()
        {
            const uint mirrorjade = 44146295, advance = 4064925, pain = 65261141, inferno = 44352516,
                cartesia = 95515789, increase = 22865492, chaosGiant = 51788412, unicorn = 38342335,
                spirit = 90829280, horn = 41230939, sunrise = 22908820, avian = 21844576;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            var rival = duel.Players[1];
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            var spare = FieldMonster(GeminiElf, 1);
            rival.szone[0] = new LiveCardState { code = Foolish };
            var mirrorEffect = Effect(mirrorjade, 0, YgoConstants.LOCATION_MZONE);
            Require(!DuelTactics.CanActivate(duel, idle, mirrorEffect), "Mirrorjade spent its banish on its empty opposing monster field.");
            Require(!DuelTactics.CanActivate(duel, idle, Effect(64043465, 0)), "Exodd Frame returned a friendly monster on an empty opposing field.");
            rival.szone[0] = null;
            Require(!DuelTactics.CanActivate(duel, idle, Effect(unicorn, 0, YgoConstants.LOCATION_MZONE)),
                "Knightmare Unicorn returned itself with an empty opposing field.");
            rival.mzone[0] = FieldMonster(Stratos);
            Require(DuelTactics.CanActivate(duel, idle, mirrorEffect) && DuelTactics.CanActivate(duel, idle, Effect(64043465, 0)),
                "Mirrorjade or Exodd Frame missed a valid opposing monster.");

            own.mzone[0] = FieldMonster(chaosGiant);
            var draw = Effect(advance, 1);
            Require(!DuelTactics.CanActivate(duel, idle, draw), "Ancient Gear Advance traded Chaos Giant for a draw.");
            own.mzone[1] = spare;
            Require(DuelTactics.CanActivate(duel, idle, draw), "Ancient Gear Advance rejected a spare tribute.");
            Require(ChooseCost(advance, DuelTactics.Release, chaosGiant, spare.code), "Ancient Gear Advance chose Chaos Giant over spare material.");

            own.mzone[0] = FieldMonster(unicorn);
            own.mzone[1] = null;
            var search = Effect(pain, 0);
            Require(!DuelTactics.CanActivate(duel, idle, search), "Nightmare Pain destroyed its only Unicorn.");
            own.mzone[1] = FieldMonster(spirit, 1);
            Require(DuelTactics.CanActivate(duel, idle, search), "Nightmare Pain rejected the Spirit of Yubel destruction trigger.");
            Require(ChooseCost(pain, DuelTactics.Destroy, unicorn, spirit), "Nightmare Pain destroyed Unicorn instead of Spirit of Yubel.");

            own.mzone[0] = FieldMonster(horn);
            own.mzone[1] = null;
            own.grave.Add(FieldMonster(82562802));
            own.szone[0] = new LiveCardState { code = 45078193, EquipTarget = (0, 0) };
            var recycle = Effect(inferno, 0);
            Require(!DuelTactics.CanActivate(duel, idle, recycle), "Cyberdark Inferno discarded live equipment.");
            own.szone[0] = null;
            Require(DuelTactics.CanActivate(duel, idle, recycle), "Cyberdark Inferno could not equip a previously unequipped monster.");

            own.mzone[0] = FieldMonster(mirrorjade);
            own.mzone[0].position = YgoConstants.POS_FACEDOWN_DEFENSE;
            own.mzone[1] = FieldMonster(cartesia, 1);
            own.mzone[5] = FieldMonster(38811586, 5);
            own.extra.Add(FieldMonster(11321089));
            var fuse = Effect(cartesia, 1, YgoConstants.LOCATION_MZONE);
            Require(!DuelTactics.CanActivate(duel, idle, fuse), "Cartesia gave up Mirrorjade for Chimera without the Spell payoff.");
            own.extra.Add(FieldMonster(24915933));
            Require(!DuelTactics.CanActivate(duel, idle, fuse), "Cartesia counted Albion the Sanctifire Dragon as legal material.");
            own.hand.Add(FieldMonster(DuelTactics.EffectVeiler));
            Require(DuelTactics.CanActivate(duel, idle, fuse), "Cartesia skipped a Fusion that could preserve Mirrorjade.");
            own.mzone[5] = null;

            own.mzone[0] = FieldMonster(46986414);
            own.mzone[1] = null;
            var rod = Effect(7084129, 1, YgoConstants.LOCATION_GRAVE);
            Require(!DuelTactics.CanActivate(duel, idle, rod), "Magician's Rod tributed Dark Magician just to recover itself.");
            own.mzone[1] = FieldMonster(DuelTactics.EffectVeiler, 1);
            Require(DuelTactics.CanActivate(duel, idle, rod), "Magician's Rod could not use a spare Spellcaster.");

            own.mzone[0] = FieldMonster(sunrise);
            own.mzone[1] = null;
            var extend = Effect(increase, 1);
            Require(!DuelTactics.CanActivate(duel, idle, extend), "Increase tributed its only Sunrise.");
            own.mzone[1] = FieldMonster(avian, 1);
            Require(DuelTactics.CanActivate(duel, idle, extend) && ChooseCost(increase, DuelTactics.Release, sunrise, avian),
                "Increase did not extend using a spare HERO.");
            Require(LinkComboPolicy.NeedsMaterialProjection(own, new SelectableCardItem
                { code = 1948619, location = YgoConstants.LOCATION_EXTRA }), "Wonder Driver could trade away Sunrise without projection.");
            Debug.Log("[VERIFY GX AI FIELD COST] Mirrorjade, Advance, Nightmare Pain, Inferno, Cartesia and Increase passed.");

            bool ChooseCost(uint source, ulong hint, uint primary, uint secondary)
            {
                duel.CurrentChain.Clear();
                duel.CurrentChain.Add(new ChainLinkInfo { code = source });
                var prompt = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, min = 1, max = 1, SelectionHint = hint };
                prompt.candidates.Add(new SelectableCardItem { code = primary, location = YgoConstants.LOCATION_MZONE });
                prompt.candidates.Add(new SelectableCardItem { code = secondary, location = YgoConstants.LOCATION_MZONE, sequence = 1 });
                return Smart().ComputeResponse(duel, prompt).SequenceEqual(DuelSession.PackCardSelection(1));
            }
        }

        private static void VerifyDrytronAceCosts()
        {
            const uint dad = 56863746, qua = 95209656, meteonis = 22398665, meteoroa = 24166324,
                alpha = 97148796, beta = 33543890, gamma = 60037599, phoenix = 2857636;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.mzone[0] = FieldMonster(dad);
            own.grave.Add(FieldMonster(qua));
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            var ritual = Effect(meteonis, 0, YgoConstants.LOCATION_HAND);
            idle.activatable.Add(ritual);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteonis replaced the established DAD with QUA.");
            own.mzone[0].position = YgoConstants.POS_FACEDOWN_DEFENSE;
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteonis treated the face-down DAD as expendable.");
            own.mzone[0].position = YgoConstants.POS_FACEUP_ATTACK;
            own.mzone[1] = FieldMonster(beta, 1);
            own.mzone[2] = FieldMonster(gamma, 2);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 5, "Meteonis rejected two spare Machines.");
            own.mzone[1] = own.mzone[2] = null;
            idle.activatable[0] = Effect(meteoroa, 1);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteoroa replaced the established DAD with QUA.");
            idle.activatable[0] = Effect(meteoroa, 0);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Meteoroa tributed DAD for banished resources.");
            idle.activatable[0] = Effect(alpha, 0, YgoConstants.LOCATION_GRAVE);
            own.hand.Add(FieldMonster(meteonis));
            Require(Read(Smart().ComputeResponse(duel, idle)) == 7, "Alpha tributed an established DAD.");
            own.mzone[1] = FieldMonster(beta, 1);
            Require(Read(Smart().ComputeResponse(duel, idle)) == 5, "Alpha rejected a spare Drytron monster.");
            Require(LinkComboPolicy.NeedsMaterialProjection(own, new SelectableCardItem
                { code = phoenix, location = YgoConstants.LOCATION_EXTRA }), "A small Link could trade away DAD without projection.");
            Require(LinkComboPolicy.NeedsMaterialProjection(own, new SelectableCardItem
                { code = 98127546, location = YgoConstants.LOCATION_EXTRA }), "A high-Link summon could trade away DAD without projection.");
            Debug.Log("[VERIFY GX AI FIELD COST] Drytron rituals, starter tributes and Link commitments passed.");
        }

        private static void VerifyAccesscodeCosts()
        {
            const uint accesscode = 86066372, unicorn = 38342335;
            using var duel = new DuelSession();
            var own = duel.Players[0];
            own.mzone[0] = FieldMonster(accesscode);
            own.mzone[0].attack = 5300;
            duel.Players[1].mzone[0] = FieldMonster(GeminiElf);
            var idle = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_IDLECMD, canEp = true };
            idle.activatable.Add(Effect(accesscode, 1, YgoConstants.LOCATION_MZONE));
            var ai = Smart();
            Require(Read(ai.ComputeResponse(duel, idle)) == 7, "Accesscode banished itself for an optional removal.");
            own.grave.Add(FieldMonster(unicorn));
            Require(Read(ai.ComputeResponse(duel, idle)) == 5, "Accesscode missed a Link in the GY.");
            var cost = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, min = 1, max = 1, SelectionHint = DuelTactics.Banish };
            cost.candidates.Add(new SelectableCardItem { code = accesscode, location = YgoConstants.LOCATION_MZONE });
            cost.candidates.Add(new SelectableCardItem { code = unicorn, location = YgoConstants.LOCATION_GRAVE });
            Require(ai.ComputeResponse(duel, cost).SequenceEqual(DuelSession.PackCardSelection(1)), "Accesscode did not reserve its own body.");
            Require(Read(ai.ComputeResponse(duel, idle)) == 7, "An already-spent Link Attribute was treated as another valid cost.");
            Debug.Log("[VERIFY GX AI FIELD COST] Accesscode protected its body and used the available GY Link.");
        }

        private static SelectableCardItem Effect(uint code, uint index, uint location = YgoConstants.LOCATION_SZONE) =>
            new() { code = code, location = location, description = ((ulong)code << 20) | index };

        private static LiveCardState FieldMonster(uint code, uint slot = 0)
        {
            var info = CardDatabase.Get(code);
            return new LiveCardState { code = code, sequence = slot, position = YgoConstants.POS_FACEUP_ATTACK,
                location = YgoConstants.LOCATION_MZONE, attack = info.atk, defense = info.def, level = info.level,
                type = info.type, race = info.race, attribute = info.attr };
        }

        private static void VerifyNegationTargets()
        {
            using var duel = new DuelSession();
            var chain = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, playerId = 1 };
            var impermanence = new SelectableCardItem { code = Imperm, controller = 1, location = YgoConstants.LOCATION_HAND };
            var veiler = new SelectableCardItem { code = 97268402, controller = 1, location = YgoConstants.LOCATION_HAND };
            duel.Players[0].mzone[0] = new LiveCardState { code = 83011277, position = YgoConstants.POS_FACEUP_ATTACK, type = YgoConstants.TYPE_EFFECT };
            duel.Players[0].mzone[1] = new LiveCardState { code = 60806437, position = YgoConstants.POS_FACEUP_ATTACK, type = YgoConstants.TYPE_EFFECT };
            Require(!DuelTactics.CanActivate(duel, chain, impermanence) && !DuelTactics.CanActivate(duel, chain, veiler),
                "Field negation should be held against Mystic Tomato and UFO Turtle.");
            duel.Players[0].mzone[2] = new LiveCardState { code = Stratos, position = YgoConstants.POS_FACEUP_ATTACK, type = YgoConstants.TYPE_EFFECT };
            foreach (var phase in new[] { YgoConstants.PHASE_MAIN1, YgoConstants.PHASE_MAIN2, YgoConstants.PHASE_END })
            {
                typeof(DuelSession).GetProperty(nameof(DuelSession.CurrentPhase)).SetValue(duel, phase);
                Require(!DuelTactics.CanActivate(duel, chain, impermanence) && !DuelTactics.CanActivate(duel, chain, veiler),
                    $"Idle phase window {phase} spent field negation on a monster with no pending effect.");
            }
            duel.CurrentChain.Add(new ChainLinkInfo { code = Stratos, triggerLoc = YgoConstants.LOCATION_MZONE, triggerSeq = 2 });
            Require(DuelTactics.CanActivate(duel, chain, impermanence) && DuelTactics.CanActivate(duel, chain, veiler),
                "Field negation should answer the current Stratos effect, even in the End Phase.");
            duel.CurrentChain.Add(new ChainLinkInfo { code = Imperm, triggerPlayer = 1 });
            var targets = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CARD, playerId = 1 };
            targets.candidates.Add(new SelectableCardItem { code = 83011277, location = YgoConstants.LOCATION_MZONE });
            targets.candidates.Add(new SelectableCardItem { code = Stratos, location = YgoConstants.LOCATION_MZONE, sequence = 2 });
            Require(DuelTactics.ChooseHandtrapTarget(duel, targets) == 1,
                "Field negation chose an unrelated face-up monster instead of the pending effect source.");
        }

        private static void VerifyNativeFieldNegation(uint negate)
        {
            const uint avian = 21844576;
            using var duel = YgoProjectBuilder.StartSeedMatching("Native field negation", Deck(Stratos, avian),
                Empty, Deck(negate), Empty, true, current => current.Players[0].hand.Any(card => card.code == Stratos));
            var ai = Smart();
            SubmitIdle(duel, 0, Stratos);
            var trigger = Drive(duel, prompt =>
                prompt.messageType == YgoConstants.MSG_SELECT_EFFECTYN && prompt.promptCardCode == Stratos ||
                IncludesChainEffect(prompt, Stratos));
            int activation = trigger.messageType == YgoConstants.MSG_SELECT_EFFECTYN ? 1 :
                trigger.candidates.FindIndex(card => card.code == Stratos);
            duel.SubmitResponseInt(activation);

            var responseWindow = Drive(duel, prompt => IncludesChainEffect(prompt, negate));
            var source = duel.CurrentChain.Single();
            int response = Read(ai.ComputeResponse(duel, responseWindow));
            Require(source.code == Stratos && response >= 0 && responseWindow.candidates[response].code == negate,
                $"{negate} did not answer Stratos as Chain Link 2.");
            duel.SubmitResponseInt(response);
            var target = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.playerId == 1);
            int index = target.candidates.FindIndex(card => card.code == Stratos && card.sequence == source.triggerSeq);
            Require(index >= 0 && ai.ComputeResponse(duel, target).SequenceEqual(DuelSession.PackCardSelection(index)),
                $"{negate} selected a different card from the live chain source.");
            duel.SubmitResponse(ai.ComputeResponse(duel, target));
            Drive(duel, IsIdle);
            Require(duel.SolvedChainLinks.Any(link => link.code == negate && link.chainNumber == 2) &&
                source.negated && duel.RetryCount == 0, $"{negate} did not resolve ahead of Stratos.");
            Debug.Log($"[VERIFY AI FIELD NEGATION] {negate} resolved as CL2 and negated Stratos. Retry=0");
        }

        private static void VerifyTriggerOrder()
        {
            using var duel = new DuelSession();
            var order = new DuelPrompt { messageType = YgoConstants.MSG_SORT_CHAIN, isChainSort = true };
            order.candidates.Add(Trigger(Sulliek, 1));
            order.candidates.Add(Trigger(Merrli, 0));
            order.candidates.Add(Trigger(Merrli, 1));
            var ai = new TacticalAI(null);
            Require(ai.ComputeResponse(duel, order).SequenceEqual(new byte[] { 1, 2, 0 }),
                "Chain permutation must protect the Graveyard Fusion before the search and mill.");
            var nativeChoice = new DuelPrompt { messageType = YgoConstants.MSG_SELECT_CHAIN, speCount = 0x7f };
            nativeChoice.candidates.AddRange(order.candidates);
            Require(Read(ai.ComputeResponse(duel, nativeChoice)) == 2, "SEGOC should lead with the Fusion effect.");

            SelectableCardItem Trigger(uint code, int effect) => new()
            {
                code = code, location = YgoConstants.LOCATION_GRAVE,
                description = ((ulong)code << 20) | (uint)effect, isTriggeredEffect = true,
            };
        }

        private static void VerifyAshCommitment()
        {
            uint[] lead = Deck(Upstart, OriginalSpoils, SnakeEyeAsh, GeminiElf);
            using var duel = YgoProjectBuilder.StartSeedMatching("Ash conservation", lead, Empty, Deck(Ash), Empty, true,
                current => new[] { Upstart, OriginalSpoils, GeminiElf }.All(code => current.Players[0].hand.Any(card => card.code == code)));
            var ai = Smart();
            SubmitIdle(duel, 5, Upstart);
            var upstartWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Ash));
            Require(Read(ai.ComputeResponse(duel, upstartWindow)) == -1, "Ash should pass on Upstart Goblin.");
            duel.SubmitResponseInt(-1);
            Drive(duel, IsIdle);
            SubmitIdle(duel, 0, GeminiElf);
            Drive(duel, IsIdle);
            SubmitIdle(duel, 5, OriginalSpoils);
            var snakeEyeWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Ash));
            int response = Read(ai.ComputeResponse(duel, snakeEyeWindow));
            Require(response >= 0 && snakeEyeWindow.candidates[response].code == Ash,
                "Ash should be saved to answer Original Sinful Spoils.");
            duel.SubmitResponseInt(response);
            Drive(duel, IsIdle);
            Require(duel.SolvedChainLinks.Any(link => link.code == Ash) &&
                duel.Players[0].mzone.All(card => card?.code != SnakeEyeAsh), "The native engine did not resolve the Ash response.");
        }

        private static void VerifyNativeTriggerProtection()
        {
            const uint painfulChoice = 74191942, aquaSpirit = 40916023, kitkallos = 92731385;
            uint[] offered = { Merrli, Sulliek, aquaSpirit, Passcode, GeminiElf };
            using var duel = YgoProjectBuilder.StartSeedMatching("Native chain blocking",
                Deck(offered.Append(painfulChoice).ToArray()), new[] { kitkallos }, Deck(Ash), Empty, true,
                current => current.Players[0].hand.Any(card => card.code == painfulChoice));
            SubmitIdle(duel, 5, painfulChoice);
            var selectFive = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.min == 5);
            int[] indices = offered.Select(code => selectFive.candidates.FindIndex(card => card.code == code)).ToArray();
            Require(indices.All(index => index >= 0), "The five chain-blocking fixture cards must be available in the Deck.");
            duel.SubmitResponse(DuelSession.PackCardSelection(indices));
            var opponentPick = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD && prompt.playerId == 1);
            int elf = opponentPick.candidates.FindIndex(card => card.code == GeminiElf);
            Require(elf >= 0, "The opponent must be able to return Gemini Elf to hand.");
            duel.SubmitResponse(DuelSession.PackCardSelection(elf));

            var ai = Smart();
            var simultaneous = Drive(duel, prompt => prompt.IsTriggerChain && prompt.candidates.Count == 2);
            int fusion = Read(ai.ComputeResponse(duel, simultaneous));
            Require(fusion >= 0 && simultaneous.candidates[fusion].code == Merrli,
                "The actual simultaneous window should put the Graveyard Fusion on Chain Link 1.");
            duel.SubmitResponseInt(fusion);
            var followUp = Drive(duel, prompt => prompt.IsTriggerChain);
            int search = Read(ai.ComputeResponse(duel, followUp));
            Require(search >= 0 && followUp.candidates[search].code == Sulliek,
                "The legal Sulliek search should occupy Chain Link 2.");
            duel.SubmitResponseInt(search);
            var ashWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Ash));
            Require(duel.CurrentChain.Count == 2 && duel.CurrentChain[0].code == Merrli && duel.CurrentChain[1].code == Sulliek,
                "The engine must preserve the AI's chosen order before offering Ash to the other player.");
            var protectedFusion = duel.CurrentChain[0];
            var coverSearch = duel.CurrentChain[1];
            duel.SubmitResponseInt(ashWindow.candidates.FindIndex(card => card.code == Ash));
            Drive(duel, IsIdle);
            Require(protectedFusion.Resolved && !protectedFusion.negated && coverSearch.Resolved && coverSearch.negated &&
                duel.Players[0].mzone.Any(card => card?.code == kitkallos) && duel.RetryCount == 0,
                "The Graveyard Fusion should resolve after Ash negates the search on top of it.");
        }

        private static void VerifyBaitAgainstBaronne()
        {
            const uint instant = 1845204, ready = 63854005, theseus = 96334243, flameSwordsman = 45231177;
            using var duel = YgoProjectBuilder.StartSeedMatching("Going second bait", Deck(instant, ready),
                new[] { theseus, flameSwordsman, Baronne }, Deck(Reinforcement, Stratos), Empty, true,
                current => current.Players[0].hand.Any(card => card.code == instant) &&
                           current.Players[0].hand.Any(card => card.code == ready) &&
                           current.Players[1].hand.Any(card => card.code == Stratos) &&
                           current.Players[1].hand.Count(card => card.code == Reinforcement) >= 2);
            SubmitIdle(duel, 5, ready);
            Drive(duel, IsIdle, flameSwordsman);
            SubmitIdle(duel, 5, instant);
            Drive(duel, IsIdle, theseus);
            SubmitIdle(duel, 1, Baronne);
            Drive(duel, IsIdle);
            Require(duel.SummonedMonstersThisTurn(0) == 3, "Three successful Extra Deck summons were not tracked.");
            duel.SubmitResponseInt(7);
            var opening = Drive(duel, prompt => IsIdle(prompt) && prompt.playerId == 1);
            Require(duel.TurnNumber == 2 && duel.SummonedMonstersThisTurn(0) == 0,
                "The summon count should reset on a new turn.");
            var ai = Smart();
            int bait = Read(ai.ComputeResponse(duel, opening));
            Require((bait & 0xffff) == 5 && opening.activatable[bait >> 16].code == Reinforcement,
                "The AI should lead with Reinforcement while holding a legal Stratos follow-up.");
            duel.SubmitResponseInt(bait);
            var availableNegate = Drive(duel, prompt => IncludesChainEffect(prompt, Baronne));
            duel.SubmitResponseInt(availableNegate.candidates.FindIndex(card => card.code == Baronne));
            var followUp = Drive(duel, IsIdle);
            var boss = duel.Players[0].mzone.Single(card => card?.code == Baronne);
            Require(duel.HasSpentBaronneNegate(boss) && followUp.summonable.Any(card => card.code == Stratos) &&
                DuelTactics.ChooseGoingSecondBait(duel, followUp, (_, _) => true) == null,
                "Once Baronne spends its negate the AI should stop looking for bait and retain the starter.");
        }

        private static void VerifySummonBudget()
        {
            uint[] starters = Deck(Foolish, Passcode, Imperm, GeminiElf, Dandylion);
            using var duel = YgoProjectBuilder.StartSeedMatching("Nibiru threshold", starters, new[] { LinkSpider },
                Deck(Nibiru), Empty, true, current =>
                    new[] { Foolish, Passcode, Imperm, GeminiElf }.All(code => current.Players[0].hand.Any(card => card.code == code)));
            SubmitIdle(duel, 4, Imperm);
            Drive(duel, IsIdle);
            SubmitIdle(duel, 0, GeminiElf);
            Drive(duel, IsIdle);
            Require(duel.SummonedMonstersThisTurn(0) == 1, "A set must not increase the successful summon count.");
            SubmitIdle(duel, 5, Foolish);
            Drive(duel, IsIdle, Dandylion);
            Require(duel.SummonedMonstersThisTurn(0) == 3, "A two-token group should add two successful summons.");
            SubmitIdle(duel, 5, Passcode);
            var idle = Drive(duel, IsIdle);
            Require(duel.SummonedMonstersThisTurn(0) == 4 && idle.spSummonable.Any(card => card.code == LinkSpider),
                "Expected four successful summons and a legal fifth summon.");
            var risk = DuelTactics.GetExtensionRisk(duel, idle);
            Require(risk == DuelTactics.ExtensionRisk.Nibiru && !DuelTactics.CanExtend(risk, duel.Players[0], 1,
                idle.spSummonable.First(card => card.code == LinkSpider)), "The cheap fifth summon should be vetoed.");
            Require((Read(Smart().ComputeResponse(duel, idle)) & 0xffff) != 1,
                "The actual Smart pilot bypassed its extension check.");
            Require(DuelTactics.CanExtend(risk, duel.Players[0], 1,
                new SelectableCardItem { code = Baronne, location = YgoConstants.LOCATION_EXTRA }),
                "The fifth summon should be available if it establishes a live monster negate.");

            // Force that otherwise avoidable summon and check the real Nibiru script becomes legal.
            SubmitIdle(duel, 1, LinkSpider);
            var nibiruWindow = Drive(duel, prompt => IncludesChainEffect(prompt, Nibiru));
            Require(duel.SummonedMonstersThisTurn(0) == 5 && nibiruWindow.playerId == 1,
                "The public count and the engine's Nibiru window disagree.");
        }

        private static void VerifyRevealedSweeper()
        {
            const uint draggedDown = 16435215, raigeki = 12580477, ready = 63854005, swordsman = 45231177;
            using var duel = YgoProjectBuilder.StartSeedMatching("Revealed field wipe",
                Deck(draggedDown, ready, GeminiElf, Passcode), new[] { swordsman }, Deck(raigeki, GeminiElf), Empty, true,
                current => new[] { draggedDown, ready, GeminiElf, Passcode }.All(code => current.Players[0].hand.Any(card => card.code == code)) &&
                           current.Players[1].hand.Any(card => card.code == raigeki) &&
                           current.Players[1].hand.Any(card => card.code == GeminiElf));
            SubmitIdle(duel, 5, ready);
            Drive(duel, IsIdle, swordsman);
            SubmitIdle(duel, 0, GeminiElf);
            var beforeReveal = Drive(duel, IsIdle);
            Require(DuelTactics.GetExtensionRisk(duel, beforeReveal) == DuelTactics.ExtensionRisk.None,
                "An unrevealed Raigeki should not be treated as known.");

            SubmitIdle(duel, 5, draggedDown);
            var selection = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD);
            int keepRaigeki = selection.candidates.FindIndex(card => card.controller == 1 && card.code == GeminiElf);
            Require(keepRaigeki >= 0, "Dragged Down should be able to discard the monster while revealing Raigeki.");
            duel.SubmitResponse(TacticalAI.EncodePlannedSelection(selection, keepRaigeki));
            var discardOwn = Drive(duel, prompt => prompt.messageType == YgoConstants.MSG_SELECT_CARD);
            int spare = discardOwn.candidates.FindIndex(card => card.code != Passcode);
            duel.SubmitResponse(TacticalAI.EncodePlannedSelection(discardOwn, Mathf.Max(0, spare)));
            var afterReveal = Drive(duel, IsIdle);
            var risk = DuelTactics.GetExtensionRisk(duel, afterReveal);
            var extender = afterReveal.activatable.FirstOrDefault(card => card.code == Passcode);
            Require(duel.IsKnownInOpposingHand(0, raigeki) && risk == DuelTactics.ExtensionRisk.Sweeper &&
                extender != null && !DuelTactics.CanExtend(risk, duel.Players[0], 5, extender),
                "A revealed Raigeki should preserve the available token extender.");
        }

        private static TacticalAI Smart() => new(DuelAiPolicy.LoadIncluded(), LinkComboPolicy.LoadIncluded());

        internal static uint[] Deck(params uint[] cycle) => Enumerable.Range(0, 40).Select(i => cycle[i % cycle.Length]).ToArray();

        internal static bool IsIdle(DuelPrompt prompt) => prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD;

        private static bool IncludesChainEffect(DuelPrompt prompt, uint code) =>
            prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && prompt.candidates.Any(card => card.code == code);

        private static int Read(byte[] response) => BitConverter.ToInt32(response, 0);

        internal static void SubmitIdle(DuelSession duel, int command, uint code)
        {
            var prompt = duel.ActivePrompt;
            Require(prompt != null && IsIdle(prompt), "Expected an idle command prompt.");
            var choices = command switch
            {
                0 => prompt.summonable,
                1 => prompt.spSummonable,
                4 => prompt.sSetable,
                5 => prompt.activatable,
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            };
            int index = choices.FindIndex(item => item.code == code);
            Require(index >= 0, $"Missing legal idle command {command} for {code}.");
            duel.SubmitResponseInt((index << 16) | command);
        }

        internal static DuelPrompt Drive(DuelSession duel, Func<DuelPrompt, bool> stop, uint selected = 0)
        {
            var basic = new TacticalAI();
            for (int step = 0; step < 28 && !duel.IsFinished && duel.RetryCount == 0; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (stop(prompt)) return prompt;
                if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN)
                    duel.SubmitResponseInt(prompt.forced ? 0 : -1);
                else if (prompt.messageType is YgoConstants.MSG_SELECT_EFFECTYN or YgoConstants.MSG_SELECT_YESNO)
                    duel.SubmitResponseInt(0);
                else if (prompt.messageType == YgoConstants.MSG_SELECT_CARD && selected != 0 &&
                    prompt.candidates.FindIndex(card => card.code == selected) is int index && index >= 0)
                    duel.SubmitResponse(TacticalAI.EncodePlannedSelection(prompt, index));
                else if (IsIdle(prompt))
                    throw new InvalidOperationException($"Unexpected idle before the expected native prompt on turn {duel.TurnNumber}.");
                else duel.SubmitResponse(basic.ComputeResponse(duel, prompt));
            }

            throw new InvalidOperationException($"Expected native prompt never arrived (turn {duel.TurnNumber}, retry {duel.RetryCount}). " +
                string.Join(" | ", duel.Logs.TakeLast(10)));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
