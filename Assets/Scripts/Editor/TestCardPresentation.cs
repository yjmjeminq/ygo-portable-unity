using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using YGOPortable.Core;
using YGOPortable.UI;

namespace YGOPortable.Editor
{
    internal static class TestCardPresentation
    {
        private const uint BlueEyes = 89631139;
        private const uint AlternateBlueEyes = 89631134;

        internal static void Run()
        {
            var canvas = new GameObject("Card Presentation Verification", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            try
            {
                var card = CompositeCardView.Create(canvas.transform, "Card", true);
                VerifyFoil(card);
                VerifySummonMotion(card);
                var animator = CardAnimationManager.Ensure(canvas);
                VerifyFeaturedCards(animator);
                VerifySummonReadout(animator);
                VerifyBattleVisibility(animator);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
            Debug.Log("[VERIFY CARD PRESENTATION] Foil geometry, hidden and reused cards, signature reveals and distinct summon paths passed.");
        }

        private static void VerifyFoil(CompositeCardView card)
        {
            const uint macroCosmos = 30241314;
            const uint sparkman = 20721928;
            card.SetCard(BlueEyes);
            Canvas.ForceUpdateCanvases();
            var finish = card.GetComponentInChildren<CardFinishGraphic>();
            Require(finish != null && finish.Rarity == CardRarity.UltraRare && !finish.raycastTarget,
                "A visible UR card did not get a non-interactive foil surface.");
            using var mesh = new VertexHelper();
            var populate = typeof(CardFinishGraphic).GetMethod("OnPopulateMesh",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            populate.Invoke(finish, new object[] { mesh });
            int ultraVertices = mesh.currentVertCount;
            UIVertex reflection = default;
            Require(ultraVertices > 20, "The foil did not build its reflection and bevel mesh.");
            mesh.PopulateUIVertex(ref reflection, 9);
            float before = reflection.position.x;
            card.SetRarityHighlight(1f);
            populate.Invoke(finish, new object[] { mesh });
            mesh.PopulateUIVertex(ref reflection, 9);
            Require(reflection.position.x > before, "Highlighting the UR card did not move the foil light.");

            card.SetCard(BlueEyes, faceDown: true);
            Require(!finish.gameObject.activeSelf, "A face-down UR card exposed its foil.");
            card.SetCard(macroCosmos);
            populate.Invoke(finish, new object[] { mesh });
            Require(finish.gameObject.activeSelf && finish.Rarity == CardRarity.SuperRare && mesh.currentVertCount < ultraVertices,
                "An SR card did not replace the UR finish with its own foil.");
            card.SetCard(sparkman);
            Require(!finish.gameObject.activeSelf, "Reusing a card view left the foil on a normal card.");
        }

        private static void VerifySummonMotion(CompositeCardView card)
        {
            var motion = new CardMoveEvent
            {
                code = 5148778, // Fusion/Pendulum monster
                reason = YgoConstants.REASON_SPSUMMON,
                fromLoc = YgoConstants.LOCATION_EXTRA,
                fromPos = YgoConstants.POS_FACEUP_ATTACK,
            };
            Require(CardAnimationManager.SummonEffect(motion.code, DuelFieldUI.MonsterArrivalMotion(motion)).kind == DuelEffectKind.Pendulum,
                "A face-up Extra Deck Pendulum monster was shown as a Fusion Summon.");
            motion.fromPos = YgoConstants.POS_FACEDOWN_DEFENSE;
            Require(CardAnimationManager.SummonEffect(motion.code, DuelFieldUI.MonsterArrivalMotion(motion)).kind == DuelEffectKind.Fusion,
                "A Fusion Summon lost its own visual treatment.");
            motion.fromLoc = YgoConstants.LOCATION_GRAVE;
            Require(DuelFieldUI.MonsterArrivalMotion(motion) == CardMotion.SpecialSummon,
                "Reviving a Fusion monster triggered the Fusion Summon effect.");

            var from = new CardPose(new Vector2(-300f, -120f), new Vector2(90f, 130f));
            var to = new CardPose(Vector2.zero, new Vector2(220f, 319f));
            var flight = new CardFlight(BlueEyes, from, to, false, false);
            var rect = (RectTransform)card.transform;
            CardAnimationManager.ApplySummonEntrance(card, flight, DuelEffectKind.Synchro, .5f);
            Vector2 synchro = rect.anchoredPosition;
            CardAnimationManager.ApplySummonEntrance(card, flight, DuelEffectKind.Xyz, .5f);
            Vector2 xyz = rect.anchoredPosition;
            Require(synchro.y > xyz.y + 50f && xyz.x > synchro.x + 30f, "The special summons used the same entrance path.");
            CardAnimationManager.ApplySummonEntrance(card, flight, DuelEffectKind.Link, 1f);
            Require(Vector2.Distance(rect.anchoredPosition, to.Position) < .1f, "The reveal did not settle at its target pose.");
        }

        private static void VerifyFeaturedCards(CardAnimationManager animator)
        {
            var mine = new CardPose(Vector2.zero, Vector2.one);
            var opponent = new CardPose(Vector2.zero, Vector2.one, 180f);
            Require(!animator.TakeFeaturedSummon(new CardFlight(BlueEyes, mine, mine, false, true)) &&
                animator.TakeFeaturedSummon(new CardFlight(BlueEyes, mine, mine, true, false)) &&
                !animator.TakeFeaturedSummon(new CardFlight(AlternateBlueEyes, mine, mine, false, false)) &&
                animator.TakeFeaturedSummon(new CardFlight(BlueEyes, opponent, opponent, false, false)),
                "The portrait was exposed while hidden, repeated for alternate art, or suppressed for the other player.");
            animator.Clear();
            Require(animator.TakeFeaturedSummon(new CardFlight(BlueEyes, mine, mine, false, false)),
                "Starting a fresh duel did not clear the featured summons.");
        }

        private static void VerifySummonReadout(CardAnimationManager animator)
        {
            const uint accesscode = 86066372;
            var pose = new CardPose(Vector2.zero, new Vector2(160, 230));
            var create = typeof(CardAnimationManager).GetMethod("SummonPlate", BindingFlags.Instance | BindingFlags.NonPublic);
            var plate = (CanvasGroup)create.Invoke(animator, new object[]
                { new CardFlight(accesscode, pose, pose, false, false), "링크 소환", Vector2.zero });
            try
            {
                var root = plate.transform;
                Require(root.Find("Caption/Value").GetComponent<Text>().text == "링크 소환 · 링크 4" &&
                    root.Find("SummonStats/Attack/Digits/Value").GetComponent<Text>().text == "2300" &&
                    !root.Find("SummonStats/Defense").gameObject.activeSelf,
                    "A Link summon omitted its rating or added a defense value.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(plate.gameObject);
            }
        }

        private static void VerifyBattleVisibility(CardAnimationManager animator)
        {
            var link = new BattleCardStats(1, YgoConstants.LOCATION_MZONE, 0, YgoConstants.POS_FACEUP_ATTACK, 2300, 0);
            var hidden = new BattleCardStats(0, YgoConstants.LOCATION_MZONE, 2, YgoConstants.POS_FACEDOWN_DEFENSE, 800, 2000);
            var direct = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, link, default, 0, true, false);
            var unrevealed = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, link, hidden, 0, true, false);
            try
            {
                Require(direct.transform.Find("Attacker/Role/Text").GetComponent<Text>().text == "상대 직접 공격" &&
                    !direct.transform.Find("Attacker/Values/Defense").gameObject.activeSelf && direct.transform.Find("Defender") == null,
                    "A direct attack showed a defender or a Link defense value.");
                Require(unrevealed.transform.Find("Attacker/Role/Text").GetComponent<Text>().text == "상대 공격" &&
                    unrevealed.transform.Find("Defender") == null && unrevealed.alpha == 0f && !unrevealed.blocksRaycasts &&
                    Array.TrueForAll(unrevealed.GetComponentsInChildren<Graphic>(), graphic => !graphic.raycastTarget),
                    "The queued readout exposed a hidden defender, claimed an attack was direct, or intercepted input.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(direct.gameObject);
                UnityEngine.Object.DestroyImmediate(unrevealed.gameObject);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
