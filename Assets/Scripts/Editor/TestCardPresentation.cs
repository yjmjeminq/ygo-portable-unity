using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                VerifyBattleComparisonProtocol();
                VerifyBattleVisibility(animator);
                VerifyLinkDirectAttack(animator);
                VerifyAttackSpacing(animator);
                VerifyLifeFeedbackRouting(animator);
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
            var plate = animator.SummonPlate(new CardFlight(accesscode, pose, pose, false, false), "링크 소환", Vector2.zero);
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

        private static void VerifyBattleComparisonProtocol()
        {
            using var duel = new DuelSession();
            var events = new List<(BattleCardStats Attacker, BattleCardStats Defender)>();
            duel.OnBattleResolved += (attacker, defender) => events.Add((attacker, defender));
            var parse = typeof(DuelSession).GetMethod("ParseMessages", BindingFlags.Instance | BindingFlags.NonPublic);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            void WriteComparison()
            {
                writer.Write(15u);
                writer.Write(YgoConstants.MSG_CUSTOM_MSG);
                writer.Write(0x31424759u);
                writer.Write((byte)BattleStatSource.Effect);
                writer.Write(uint.MaxValue);
                writer.Write((byte)BattleStatSource.Defense);
                writer.Write(2000u);
            }

            void WriteBattle()
            {
                writer.Write(39u);
                writer.Write(YgoConstants.MSG_BATTLE);
                for (byte player = 0; player < 2; player++)
                {
                    writer.Write(player);
                    writer.Write((byte)YgoConstants.LOCATION_MZONE);
                    writer.Write(0u);
                    writer.Write(YgoConstants.POS_FACEUP_DEFENSE);
                    writer.Write(1200);
                    writer.Write(2000);
                    writer.Write((byte)0);
                }
            }

            WriteComparison();
            writer.Write(2u);
            writer.Write(YgoConstants.MSG_CUSTOM_MSG);
            writer.Write((byte)0);
            WriteBattle();
            WriteBattle();
            WriteComparison(); // An incomplete native step cannot supply values to a later message buffer.
            parse.Invoke(duel, new object[] { stream.ToArray() });
            stream.SetLength(0);
            WriteBattle();
            parse.Invoke(duel, new object[] { stream.ToArray() });
            Require(events.Count == 3 &&
                events[0].Attacker.ComparedValue?.Source == BattleStatSource.Effect &&
                events[0].Attacker.ComparedValue?.Value == uint.MaxValue &&
                events[0].Defender.ComparedValue?.Source == BattleStatSource.Defense &&
                events[0].Defender.ComparedValue?.Value == 2000 &&
                events.All(item => item.Attacker.Attack == 1200 && item.Defender.Defense == 2000) &&
                events.Skip(1).All(item => item.Attacker.ComparedValue == null && item.Defender.ComparedValue == null),
                "Battle comparison data leaked between attacks or replaced their original stats.");
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

        private static void VerifyLinkDirectAttack(CardAnimationManager animator)
        {
            const uint accesscode = 86066372;
            var link = new BattleCardStats(1, YgoConstants.LOCATION_MZONE, 0, YgoConstants.POS_FACEUP_ATTACK, 5300, 0);
            var card = new DuelCardReadout(CardDatabase.Get(accesscode), battleStats: link);
            var start = new CardPose(new Vector2(-80f, 90f), new Vector2(76f, 110f), 180f);
            var flight = new CardFlight(accesscode, start, start, false, false);
            var comparison = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, link, default, 0, true, false);
            int arrivals = 0;
            animator.QueueAttack(flight, new Vector2(100f, -280f), true, true, comparison, card,
                new Vector2(120f, 115f), () => arrivals++);
            VerifyAttackFlight(animator, "5300", null, "4");
            Require(arrivals == 1, "The direct attack did not finish once.");
        }

        private static void VerifyAttackSpacing(CardAnimationManager animator)
        {
            const uint wagon = 34496660, mysticalElf = 15025844;
            foreach (var (start, end, opponent) in new[]
            {
                (new Vector2(48f, -70f), new Vector2(48f, 32f), false),
                (new Vector2(-182f, 62f), new Vector2(20f, -61f), true),
            })
            {
                animator.Clear();
                var resting = new CardPose(opponent ? start : start - Vector2.up * 7f, new Vector2(70f, 102f), opponent ? 90f : 0f);
                var from = new CardPose(start, resting.Size * (opponent ? 1f : 1.05f), resting.Angle);
                var defender = new CardPose(end, new Vector2(70f, 102f), -90f);
                var targetArt = LotdUiKit.CreateRect("Target Card", animator.transform, Vector2.one * .5f,
                    Vector2.one * .5f, Vector2.zero, Vector2.zero);
                targetArt.anchoredPosition = end;
                targetArt.sizeDelta = defender.Size;
                targetArt.localRotation = Quaternion.Euler(0f, 0f, defender.Angle);

                var sourceCard = CardDatabase.Get(opponent ? wagon : BlueEyes);
                byte player = opponent ? (byte)1 : (byte)0;
                var attack = new BattleCardStats(player, YgoConstants.LOCATION_MZONE, 1,
                    opponent ? YgoConstants.POS_FACEUP_DEFENSE : YgoConstants.POS_FACEUP_ATTACK, sourceCard.atk, sourceCard.def);
                var defense = new BattleCardStats((byte)(1 - player), YgoConstants.LOCATION_MZONE, 1,
                    YgoConstants.POS_FACEUP_DEFENSE, 800, 2000);
                var target = new DuelCardReadout(CardDatabase.Get(mysticalElf), battleStats: defense);
                var comparison = animator.CreateBattleReadout(DuelFieldUI.BattleStatsArea, attack, defense, 0, false, false);
                var queued = new CardPose(start - Vector2.one * 14f, from.Size, from.Angle);
                var flight = new CardFlight(sourceCard.code, queued, resting, false, false);
                var size = new Vector2(118f, opponent ? 108f : 113f);
                int arrivals = 0;
                animator.QueueAttack(flight, end + Vector2.one * 24f, false, opponent, comparison,
                    new DuelCardReadout(sourceCard, battleStats: attack), size, () => arrivals++,
                    () => (from, size, defender,
                        animator.CreateBattleCardReadout("DefendingCardReadout", target, !opponent, end, new Vector2(110f, 100f))));

                bool firstFrame = true;
                VerifyAttackFlight(animator, sourceCard.atk.ToString(), sourceCard.def.ToString(),
                    sourceCard.level.ToString(), () =>
                    {
                        var art = (RectTransform)animator.transform.Find("MovingCard");
                        if (firstFrame)
                        {
                            Require(art.anchoredPosition == from.Position, "The attack skipped the current displayed pose.");
                            var labels = (RectTransform)animator.transform.Find("AttackingCardReadout/CardReadout");
                            Require(opponent || labels.anchoredPosition.y < 0f,
                                "The raised card's readout did not keep its clearance on the first frame.");
                            firstFrame = false;
                        }
                        Require(!Bounds(art).Overlaps(Bounds(targetArt)), "The raised attack covered the target card.");
                        var impact = (RectTransform)animator.transform.Find("Strike");
                        Require(impact == null || impact.anchoredPosition == end,
                            "The impact used the old queued point instead of the defender's current position.");
                    });
                Require(arrivals == 1, "The spaced attack did not finish once.");
            }
            Debug.Log("[VERIFY DUEL ATTACK SPACING] A focused adjacent attack and a diagonal defense-position attack kept both cards and stat plates separate through windup, contact and return.");
        }

        private static void VerifyLifeFeedbackRouting(CardAnimationManager animator)
        {
            animator.Clear();
            var own = LotdUiKit.CreateRect("Own LP", animator.transform.parent, new Vector2(.05f, .04f),
                new Vector2(.19f, .075f), Vector2.zero, Vector2.zero);
            var rival = LotdUiKit.CreateRect("Rival LP", animator.transform.parent, new Vector2(.77f, .93f),
                new Vector2(.93f, .965f), Vector2.zero, Vector2.zero);
            var pose = new CardPose(Vector2.zero, new Vector2(70f, 102f));
            var card = new DuelCardReadout(CardDatabase.Get(BlueEyes));
            var updates = new List<int>();
            animator.QueueAttack(new CardFlight(BlueEyes, pose, pose, false, false), new Vector2(30f, 200f),
                true, false, null, card, new Vector2(118f, 108f), null);
            animator.QueueLifeChange(own, -800, LifePointChangeKind.BattleDamage, () => updates.Add(-800));
            animator.QueueLifeChange(rival, -500, LifePointChangeKind.BattleDamage, () => updates.Add(-500));
            animator.QueueLifeChange(own, -1000, LifePointChangeKind.Payment, () => updates.Add(-1000));
            animator.QueueLifeChange(rival, -300, LifePointChangeKind.EffectDamage, () => updates.Add(-300));

            VerifyAttackFlight(animator, "3000", "2500", "8", () =>
            {
                var feedback = animator.transform.Cast<Transform>().Where(node => node.name == "LPChange").ToArray();
                if (animator.transform.Find("Strike") == null)
                {
                    Require(feedback.Length == 0 && updates.Count == 0, "LP changed before the strike.");
                    return;
                }
                Require(feedback.Length == 2 && updates.SequenceEqual(new[] { -800, -500 }),
                    "Two battle damage events did not appear together at contact.");
                CheckFeedback(feedback[0], own, "전투 피해", "-800");
                CheckFeedback(feedback[1], rival, "전투 피해", "-500");
            });
            Require(animator.transform.Find("LPChange") == null, "Battle damage remained after the strike.");
            var queue = (Queue<IEnumerator>)typeof(CardAnimationManager)
                .GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            var standalone = queue.Where(step => step.GetType().Name.StartsWith("<PlayLifeChanges>", StringComparison.Ordinal)).ToArray();
            Require(standalone.Length == 2, "An unrelated effect or payment joined the preceding attack.");
            Require(standalone[0].MoveNext(), "The LP payment did not render.");
            CheckFeedback(animator.transform.Find("LPChange"), own, "LP 지불", "-1000");
            Require(!standalone[0].MoveNext() && standalone[1].MoveNext(), "The effect damage was not shown after the payment.");
            CheckFeedback(animator.transform.Find("LPChange"), rival, "효과 피해", "-300");
            Require(!standalone[1].MoveNext() && updates.SequenceEqual(new[] { -800, -500, -1000, -300 }),
                "The displayed LP changes were applied in the wrong order.");

            void CheckFeedback(Transform feedback, RectTransform anchor, string kind, string value)
            {
                var rect = (RectTransform)feedback;
                Require(rect.anchoredPosition == animator.GetPoint(anchor, Vector2.one * .5f) &&
                    rect.sizeDelta == animator.GetAreaSize(anchor) &&
                    rect.Find("Kind/Text").GetComponent<Text>().text == kind &&
                    rect.Find("Value/Text").GetComponent<Text>().text == value &&
                    rect.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget),
                    "The LP feedback had the wrong owner, reason, amount or boundary.");
            }
            UnityEngine.Object.DestroyImmediate(own.gameObject);
            UnityEngine.Object.DestroyImmediate(rival.gameObject);
            Debug.Log("[VERIFY DUEL LP FEEDBACK] Two targets received damage at one impact. A later payment and effect used their own HUD steps.");
        }

        internal static void VerifyAttackFlight(CardAnimationManager animator, string attack, string defense, string rating,
            Action inspectFrame = null)
        {
            var steps = (Queue<IEnumerator>)typeof(CardAnimationManager)
                .GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            var step = steps.Single(candidate => candidate.GetType().Name.StartsWith("<Attack>", StringComparison.Ordinal));
            Vector2 previous = Vector2.zero;
            float unitsPerCardHeight = 0f;
            for (int stage = 0; stage < 3; stage++)
            {
                Require(step.MoveNext() && step.Current is IEnumerator, "The attack omitted an animation stage.");
                var sample = SampleAnimation((IEnumerator)step.Current);
                var art = (RectTransform)animator.transform.Find("MovingCard");
                var stats = (RectTransform)animator.transform.Find("AttackingCardReadout");
                var strip = stats.Find("CardReadout/Combat");
                float ratio = stats.sizeDelta.x / art.sizeDelta.y;
                if (stage == 0) unitsPerCardHeight = ratio;
                Require(art.gameObject.activeInHierarchy && stats.anchoredPosition == art.anchoredPosition &&
                    stats.localRotation == Quaternion.identity && Mathf.Approximately(unitsPerCardHeight, ratio) &&
                    (stage == 0 || (stats.anchoredPosition - previous).sqrMagnitude > 1f),
                    "The attacking monster's stat plate did not follow its pose.");
                Require(strip.Find("Attack/Digits/Value").GetComponent<Text>().text == attack &&
                    (defense == null ? !strip.Find("Defense").gameObject.activeSelf :
                        strip.Find("Defense/Digits/Value").GetComponent<Text>().text == defense) &&
                    stats.Find("CardReadout/Labels/Rating/Value").GetComponent<Text>().text == rating &&
                    stats.GetComponentsInChildren<Graphic>().All(graphic => !graphic.raycastTarget),
                    "The flying card omitted its current stats, rank, link or level, or blocked input.");
                previous = stats.anchoredPosition;
                var defender = (RectTransform)animator.transform.Find("DefendingCardReadout");
                var content = (RectTransform)stats.Find("CardReadout");
                for (int frame = 0; frame <= 16; frame++)
                {
                    sample(frame / 16f);
                    Require(stats.anchoredPosition == art.anchoredPosition &&
                        (defender == null || !Bounds(content).Overlaps(Bounds(defender))),
                        "The battle readouts separated from their card or overlapped each other during the attack.");
                    inspectFrame?.Invoke();
                }
            }
            Require(!step.MoveNext() && animator.transform.Find("AttackingCardReadout") == null &&
                animator.transform.Find("BattleReadout") == null && !animator.transform.Find("MovingCard").gameObject.activeSelf,
                "The attack left a transient card or stats on screen.");
        }

        internal static Action<float> SampleAnimation(IEnumerator frames)
        {
            Require(frames.MoveNext(), "The animation did not render its first frame.");
            var sample = (Action<float>)frames.GetType().GetField("apply",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(frames);
            Require(sample != null, "The renderer did not expose its interpolated frames.");
            return sample;
        }

        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners.Min(point => point.x), corners.Min(point => point.y),
                corners.Max(point => point.x), corners.Max(point => point.y));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
