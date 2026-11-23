using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YGOPortable.AI;
using YGOPortable.Core;

namespace YGOPortable.Editor
{
    internal static class TestDestinyDraw
    {
        public static void Run()
        {
            int exit = 1;
            try
            {
                string streaming = Application.streamingAssetsPath;
                CardDatabase.Load(streaming);
                Require(YgoNativeBridge.Initialize(streaming, null) == 0, "The native engine did not start.");
                VerifyDraw();
                VerifyNoOffer();
                Debug.Log("[VERIFY DESTINY DRAW] Main Deck favorites, exact physical copies, normal draw timing, decline and once per duelist passed.");
                exit = 0;
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                YgoNativeBridge.Shutdown();
                if (Application.isBatchMode) EditorApplication.Exit(exit);
            }
        }

        private static void VerifyNoOffer()
        {
            var deck = new UserDeck();
            for (int i = 0; i < 40; i++) deck.main.Add(20721928);
            ulong[] seed = { 7021, 31, 508, 67 };
            using var duel = new DuelSession();
            for (int i = 0; i < deck.main.Count; i++)
            {
                deck.SetAppearance(deck.main, i, new DeckCardAppearance { destinyDraw = true });
                Require(duel.StartNewDuel(deck.main, deck.extra, deck.main, deck.extra, startingLp: 2000, seed: seed,
                    playerDeckAppearance: deck), "Could not start the missing-favorite fixture.");
                if (duel.Players[duel.HumanPlayerIndex].hand.Any(card => card.Appearance?.destinyDraw == true))
                {
                    Require(NormalDrawWithoutOffer(duel), "A registered card already in the hand was offered.");
                    break;
                }
                deck.SetAppearance(deck.main, i, default);
                Require(i + 1 < deck.main.Count, "The fixed seed did not draw a marked opening card.");
            }

            for (int i = 0; i < deck.main.Count; i++)
                deck.SetAppearance(deck.main, i, new DeckCardAppearance { destinyDraw = true });
            Require(duel.StartNewDuel(deck.main, deck.extra, deck.main, deck.extra, startingLp: 2001, seed: seed,
                playerDeckAppearance: deck) && NormalDrawWithoutOffer(duel), "Destiny Draw was offered above 2,000 LP.");

            static bool NormalDrawWithoutOffer(DuelSession session)
            {
                for (int step = 0; step < 48; step++)
                {
                    var prompt = session.ActivePrompt;
                    if (prompt == null || session.IsFinished || prompt.messageType == YgoConstants.MSG_DESTINY_DRAW) return false;
                    if (session.TurnNumber == 3 && prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD)
                        return session.Players[session.HumanPlayerIndex].hand.Count == 6;
                    if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp) session.SubmitResponseInt(7);
                    else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced) session.SubmitResponseInt(-1);
                    else return false;
                }
                return false;
            }
        }

        private static void VerifyDraw()
        {
            const uint normalMonster = 20721928, opponentAce = 58932615;
            var deck = new UserDeck();
            for (int i = 0; i < 40; i++)
                deck.InsertCard(deck.main, i, normalMonster, new DeckCardAppearance { destinyDraw = i % 2 == 0 });
            string json = JsonUtility.ToJson(deck);
            var restored = JsonUtility.FromJson<UserDeck>(json);
            Require(restored.Copies().Count(copy => copy.Appearance.destinyDraw) == 20,
                "The registered physical copies did not survive serialization.");
            var opponent = Enumerable.Repeat(normalMonster, 30).Concat(Enumerable.Repeat(opponentAce, 10)).ToArray();
            var ai = new TacticalAI { Difficulty = AIDifficulty.Easy };
            var used = new List<(int Turn, byte Player)>();
            var offers = new List<(int Turn, byte Player)>();
            using var duel = new DuelSession(recordPlanningReplay: true);
            duel.OnDestinyDraw += player => used.Add((duel.TurnNumber, player));
            bool exactCopy = false;
            bool choseAce = false;
            duel.OnStyledCardsDrawn += (player, codes, styles) =>
            {
                if (duel.TurnNumber == 3 && player == duel.HumanPlayerIndex)
                    exactCopy = codes.Count == 1 && codes[0] == normalMonster && styles[0]?.destinyDraw == true;
            };
            duel.OnCardsDrawn += (player, codes) =>
            {
                if (duel.TurnNumber == 4 && player == duel.AiPlayerIndex)
                    choseAce = codes.Count == 1;
            };
            Require(duel.StartNewDuel(restored.main, restored.extra, opponent, Array.Empty<uint>(), startingLp: 2000,
                seed: new ulong[] { 18, 5163, 512, 735 }, playerDeckAppearance: restored,
                opponentDestiny: new[] { opponentAce }), "Could not start the draw test duel.");
            for (int step = 0; step < 100 && duel.TurnNumber <= 5 && !duel.IsFinished; step++)
            {
                var prompt = duel.ActivePrompt;
                if (prompt == null) { duel.StepUntilPrompt(); continue; }
                if (prompt.messageType == YgoConstants.MSG_DESTINY_DRAW)
                {
                    offers.Add((duel.TurnNumber, prompt.playerId));
                    duel.SubmitResponseInt(duel.TurnNumber == 2 ? 0 : 1);
                    if (duel.TurnNumber == 4)
                        choseAce &= duel.Players[duel.AiPlayerIndex].hand[^1].code == opponentAce;
                }
                else if (prompt.messageType == YgoConstants.MSG_SELECT_IDLECMD && prompt.canEp) duel.SubmitResponseInt(7);
                else if (prompt.messageType == YgoConstants.MSG_SELECT_CHAIN && !prompt.forced) duel.SubmitResponseInt(-1);
                else duel.SubmitResponse(ai.ComputeResponse(duel, prompt));
            }
            Require(offers.SequenceEqual(new[] { (2, (byte)duel.AiPlayerIndex), (3, (byte)duel.HumanPlayerIndex),
                    (4, (byte)duel.AiPlayerIndex) }) && used.Count == 2 && exactCopy && choseAce && duel.RetryCount == 0,
                $"Unexpected draw flow. Offers: {string.Join(", ", offers)}. Used: {string.Join(", ", used)}. Exact copy: {exactCopy}. AI ace: {choseAce}.");
        }

        private static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidOperationException(error);
        }
    }
}
