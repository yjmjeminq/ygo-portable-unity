using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
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
                VerifyExpandedDuelCard(canvas.transform);
                VerifyChainSourceIdentity();
                var animator = CardAnimationManager.Ensure(canvas);
                VerifyFeaturedCards(animator);
                VerifySummonReadout(animator);
                VerifyBattleComparisonProtocol();
                VerifyBattleVisibility(animator);
                VerifyLinkDirectAttack(animator);
                VerifyAttackSpacing(animator);
                VerifyAttackAfterLayoutChange(animator);
                VerifyLifeFeedbackRouting(animator);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            }
            Debug.Log("[VERIFY CARD PRESENTATION] Foil geometry, hidden and reused cards, signature reveals and distinct summon paths passed.");
        }

        internal static void VerifyOverframePaths()
        {
            CardOverframeProfile.PrepareContours();
            var profileType = typeof(CardOverframeProfile);
            const BindingFlags profileFields = BindingFlags.NonPublic | BindingFlags.Static;
            var bundled = (Dictionary<uint, CardOverframeProfile>)profileType.GetField("Profiles", profileFields).GetValue(null);
            var local = (Dictionary<(uint, int), CardOverframeProfile>)profileType.GetField("LocalProfiles", profileFields).GetValue(null);
            var indices = profileType.GetField("_triangles", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var contour in bundled.Values.Concat(local.Values).Distinct())
            {
                var prepared = (int[])indices.GetValue(contour);
                Require(prepared != null && prepared.Length == (contour.Uv.Length - 2) * 3,
                    "Startup did not prepare a complete triangulation for all artwork variants.");
            }
            Debug.Log("[VERIFY OVERFRAME PREP] Bundled and alternate silhouettes triangulated before the card renderer bound them.");

            var canvas = new GameObject("Overframe Verification", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            try
            {
                var holder = LotdUiKit.CreateRect("View", canvas.transform, new Vector2(.25f, .16f), new Vector2(.52f, .83f),
                    Vector2.zero, Vector2.zero);
                var card = CompositeCardView.Create(holder, "Detailed", detailedOverlay: true);
                card.EnableOverframe();
                CardOverframeInput.Attach(holder, card, false, Rect.MinMaxRect(.25f, .16f, .52f, .83f), Rect.MinMaxRect(.28f, .19f, .50f, .74f));
                foreach (uint code in new[] { 84815190u, 46986414u, BlueEyes, 9674034u })
                {
                    card.SetCard(code);
                    Canvas.ForceUpdateCanvases();
                    var layer = card.transform.Find("Overframe");
                    var graphic = layer?.Find("Depth/Subject").GetComponent<CardOverframeGraphic>();
                    Require(card.HasOverframe && !graphic.maskable && !graphic.raycastTarget &&
                        layer.parent == card.transform && layer.GetSiblingIndex() == card.transform.childCount - 1 &&
                        graphic.mainTexture == card.ArtworkSprite.texture,
                        $"The face of {code} did not use its own art in a separate unmasked overlay.");
                    using var mesh = new VertexHelper();
                    typeof(CardOverframeGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(VertexHelper) }, null).Invoke(graphic, new object[] { mesh });
                    var profile = CardOverframeProfile.Get(code, -1);
                    Require(mesh.currentVertCount == profile.Uv.Length * 2 && mesh.currentIndexCount == (profile.Uv.Length * 3 - 2) * 3,
                        $"The silhouette of {code} did not triangulate.");
                    float bottom = float.PositiveInfinity, top = float.NegativeInfinity, right = float.NegativeInfinity;
                    UIVertex v = default;
                    for (int i = 0; i < mesh.currentVertCount; i++)
                    {
                        mesh.PopulateUIVertex(ref v, i);
                        bottom = Mathf.Min(bottom, v.position.y);
                        top = Mathf.Max(top, v.position.y);
                        right = Mathf.Max(right, v.position.x);
                    }
                    var shimmer = layer.Find("Depth/Shimmer").GetComponent<CardOverframeGraphic>();
                    card.PoseOverframe(1f, 1f, Vector2.zero, .02f);
                    var warm = shimmer.canvasRenderer.GetColor();
                    card.PoseOverframe(1f, 1f, Vector2.zero, .61f);
                    var cool = shimmer.canvasRenderer.GetColor();
                    Require(warm.r > cool.r && cool.b > warm.b, $"The shimmer of ace {code} did not cycle through the spectrum.");

                    var rect = graphic.rectTransform.rect;
                    Require(bottom > rect.yMin + .30f * rect.height && top > rect.yMax + .15f * rect.height && right > rect.xMax,
                        $"The silhouette of {code} did not clear the upper and side edges or crossed the lower text.");
                }

                var input = holder.GetComponent<CardOverframeInput>();
                var tick = typeof(CardOverframeInput).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                tick.Invoke(input, null);
                var pointer = new PointerEventData(EventSystem.current) { pointerId = 4, button = PointerEventData.InputButton.Left };
                pointer.position = RectTransformUtility.WorldToScreenPoint(null, holder.position);
                input.OnPointerDown(pointer);
                pointer.position += new Vector2(80f, 40f);
                input.OnBeginDrag(pointer);
                var drag = (Vector2)typeof(CardOverframeInput).GetField("_drag", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
                Require(drag.sqrMagnitude > .05f, "Dragging the viewer did not set a parallax offset.");
                typeof(CardOverframeInput).GetMethod("ShowDepth", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(input, new object[] { drag, .21f });
                var tilt = (RectTransform)card.transform.Find("Overframe/Depth");
                Require(tilt.anchoredPosition.sqrMagnitude > 1f && tilt.localRotation != Quaternion.identity &&
                    card.transform.localRotation != Quaternion.identity, "Frame and portrait did not tilt independently.");
                input.OnEndDrag(pointer);
                card.SetCard(BlueEyes, faceDown: true);
                Require(!card.HasOverframe, "A face-down view leaked the featured artwork.");
                card.SetCard(20721928);
                Require(!card.HasOverframe, "Reusing the viewer leaked the previous contour.");

                var animator = CardAnimationManager.Ensure(canvas);
                VerifyOverframeAnimation(animator);
                Debug.Log("[VERIFY OVERFRAME] Four bundled silhouettes, layering, touch parallax, hidden card reuse, summon bounce and chain cut-ins passed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas.gameObject); }
            VerifyOverframeArtworkSources();
        }

        private static void VerifyOverframeArtworkSources()
        {
            const uint magician = 46986414, blueEyesPrinting = 89631140;
            string folder = Path.Combine(Path.GetTempPath(), "ygo-overframe-art-" + Guid.NewGuid().ToString("N"));
            string packPath = Path.Combine(folder, "cardart.pack");
            Directory.CreateDirectory(folder);
            const string magic = "MDAR";
            var texture = new Texture2D(8, 8, TextureFormat.RGB24, false);
            texture.SetPixels32(Enumerable.Repeat(new Color32(72, 110, 180, 255), 64).ToArray());
            texture.Apply();
            byte[] localArt = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            byte[] damagedArt = { 1, 2, 3, 4 };
            using (var pack = new BinaryWriter(File.Create(packPath)))
            {
                pack.Write(System.Text.Encoding.ASCII.GetBytes(magic));
                pack.Write(1u);
                pack.Write(2u);
                const uint offset = 12 + 2 * 16;
                pack.Write(magician);
                pack.Write(0);
                pack.Write(offset);
                pack.Write((uint)damagedArt.Length);
                pack.Write(BlueEyes);
                pack.Write(0);
                pack.Write(offset + (uint)damagedArt.Length);
                pack.Write((uint)localArt.Length);
                pack.Write(damagedArt);
                pack.Write(localArt);
            }

            var canvas = new GameObject("Overframe Artwork Verification", typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1600f, 900f);
            CardArtLoader.Shutdown();
            CardArtLoader.Initialize(Application.streamingAssetsPath, folder);
            try
            {
                var view = CompositeCardView.Create(canvas.transform, "Viewer", detailedOverlay: true, duelArtwork: true);
                view.EnableOverframe();
                view.SetCard(magician);
                var fallback = CardArtLoader.GetArtworkSource(magician, 0);
                Require(fallback == (magician, -1) && view.HasOverframe,
                    "An unreadable local painting did not use the bundled painting and its silhouette.");
                var graphic = view.transform.Find("Overframe/Depth/Subject").GetComponent<CardOverframeGraphic>();
                var profileField = typeof(CardOverframeGraphic).GetField("_profile", BindingFlags.NonPublic | BindingFlags.Instance);
                Require(ReferenceEquals(profileField.GetValue(graphic), CardOverframeProfile.Get(magician, -1)) &&
                    !ReferenceEquals(profileField.GetValue(graphic), CardOverframeProfile.Get(magician, 0)),
                    "The decoded fallback used the silhouette of the originally selected artwork.");

                view.SetCard(BlueEyes);
                Require(CardArtLoader.GetArtworkSource(BlueEyes, 0) == (BlueEyes, 0) &&
                    view.ArtworkSprite.texture.width == 8 && !view.HasOverframe,
                    "A valid replacement art inherited the unrelated bundled silhouette.");
                view.SetCard(blueEyesPrinting);
                Require(CardArtLoader.GetArtworkSource(blueEyesPrinting, -1) == (BlueEyes, -1) && view.HasOverframe &&
                    ReferenceEquals(profileField.GetValue(graphic), CardOverframeProfile.Get(BlueEyes, -1)) &&
                    view.CurrentCode == blueEyesPrinting,
                    "An equivalent printing lost its borrowed silhouette or changed card identity.");
                view.SetCard(magician);
                Require(ReferenceEquals(profileField.GetValue(graphic), CardOverframeProfile.Get(magician, -1)),
                    "Returning to the first card retained an old silhouette in the same viewer.");

                var animator = CardAnimationManager.Ensure(canvas);
                var pose = new CardPose(new Vector2(0f, -90f), new Vector2(92f, 134f));
                animator.QueueMove(new CardFlight(blueEyesPrinting, pose, pose, true, false), CardMotion.SpecialSummon, null);
                var queue = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
                var move = queue.Dequeue();
                Require(move.MoveNext() && move.Current is IEnumerator, "The equivalent printing skipped its summon reveal.");
                var reveal = (IEnumerator)move.Current;
                Require(reveal.MoveNext(), "The equivalent printing did not show a cut-in.");
                SampleAnimation((IEnumerator)reveal.Current)(1f);
                Require(animator.transform.Find("MovingCard/Overframe/Depth/Subject").gameObject.activeInHierarchy,
                    "The summon and card viewer did not choose the same decoded silhouette.");
                animator.Clear();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas.gameObject);
                CardArtLoader.Shutdown();
                CardArtLoader.Initialize(Application.streamingAssetsPath);
                File.Delete(packPath);
                Directory.Delete(folder);
            }
            Debug.Log("[VERIFY OVERFRAME ARTWORK] Local decode failure, valid replacement, and equivalent-printing art kept their own silhouettes.");
        }

        private static void VerifyOverframeAnimation(CardAnimationManager animator)
        {
            var start = new CardPose(new Vector2(-80f, -160f), new Vector2(92f, 134f));
            var end = new CardPose(new Vector2(220f, -80f), new Vector2(92f, 134f));
            var queue = (Queue<IEnumerator>)typeof(CardAnimationManager).GetField("_steps",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);
            bool landed = false;
            animator.QueueMove(new CardFlight(BlueEyes, start, end, true, false), CardMotion.SpecialSummon, () => landed = true);
            var move = queue.Dequeue();
            Require(move.MoveNext() && move.Current is IEnumerator, "The ace did not play its summon reveal.");
            var reveal = (IEnumerator)move.Current;
            Require(reveal.MoveNext(), "The overframe summon did not start.");
            var enter = SampleAnimation((IEnumerator)reveal.Current);
            var view = animator.transform.Find("MovingCard");
            Require(view.GetComponent<CompositeCardView>().CurrentCode == 0 && view.Find("Overframe") == null,
                "The summon revealed the subject before its card was turned up.");
            enter(.51f);
            Canvas.ForceUpdateCanvases();
            var layer = view.Find("Overframe");
            var tilt = layer.Find("Depth");
            var fixedRect = (RectTransform)view;
            Vector2 initialSize = fixedRect.sizeDelta;
            TrackStableOverframe(layer, enter, .53f, 1f);
            Require(fixedRect.sizeDelta == initialSize && Mathf.Approximately(fixedRect.localScale.y, 1f),
                "The summoned card did not reach its display size without changing its layout.");
            Require(layer.gameObject.activeSelf && Mathf.Approximately(tilt.localScale.x, 1.25f) &&
                animator.transform.Find("ChainImpactWave").GetComponent<DuelEffectGraphic>().Tint == DuelStyle.ChainGold &&
                animator.transform.Find("Chain").GetComponent<DuelEffectGraphic>().Tint == DuelStyle.ChainGold,
                "The summon did not produce gold feedback or enlarge the overframe at impact.");
            Require(reveal.MoveNext(), "The summon omitted the overframe landing.");
            SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(Mathf.Approximately(tilt.localScale.x, 1f), "The subject did not settle to its intended size.");
            Require(reveal.MoveNext(), "The ace did not travel to the field.");
            TrackStableOverframe(layer, SampleAnimation((IEnumerator)reveal.Current), .05f, 1f);
            float fittedHeight = Mathf.Min(end.Size.y, end.Size.x / LotdUiKit.CardAspectRatio);
            Require(fixedRect.sizeDelta == initialSize && Mathf.Abs(fixedRect.rect.height * fixedRect.localScale.y - fittedHeight) < .1f &&
                Vector2.Distance(fixedRect.anchoredPosition, end.Position) < .1f, "The fixed summon layout missed its destination.");
            Require(!reveal.MoveNext() && !move.MoveNext() && landed && (view == null || !view.gameObject.activeSelf) &&
                animator.transform.Find("ChainImpactWave") == null, "The overframe cut-in outlived the summon.");

            landed = false;
            animator.QueueMove(new CardFlight(BlueEyes, start, end, false, false), CardMotion.SpecialSummon, () => landed = true);
            move = queue.Dequeue();
            Require(move.MoveNext() && move.Current.GetType().Name.StartsWith("<RevealSummon>", StringComparison.Ordinal),
                "An ace that left the field lost its overframe on the next summon of the same duel.");
            reveal = (IEnumerator)move.Current;
            Require(reveal.MoveNext(), "The resummon cut-in did not start.");
            SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(animator.transform.Find("MovingCard/Overframe/Depth/Subject").gameObject.activeInHierarchy &&
                animator.transform.Find("ChainImpactWave") != null, "The resummoned ace did not show its subject and shockwave.");
            while (reveal.MoveNext()) SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(!move.MoveNext() && landed && animator.transform.Find("ChainImpactWave") == null,
                "The resummoned ace did not complete its field arrival.");

            animator.QueueMove(new CardFlight(BlueEyes, start, end, true, true), CardMotion.SpecialSummon, null);
            move = queue.Dequeue();
            Require(move.MoveNext() && !move.Current.GetType().Name.StartsWith("<RevealSummon>", StringComparison.Ordinal) &&
                animator.transform.Find("MovingCard/Overframe") == null, "A face-down summon exposed the ace's identity.");
            Debug.Log("[VERIFY OVERFRAME RESUMMON] The same ace showed its cut-in on a second face-up summon and stayed hidden on a face-down summon.");

            animator.Clear();
            VerifyGroupedOverframe(animator, queue);
            animator.Clear();
            animator.QueueChain(start, 9674034, 1, false);
            var chain = queue.Dequeue();
            Require(chain.MoveNext(), "The ace activation did not play its cut-in.");
            var sample = SampleAnimation((IEnumerator)chain.Current);
            sample(.49f);
            Canvas.ForceUpdateCanvases();
            var cutIn = animator.transform.Find("MovingCard/Overframe");
            Require(cutIn != null && cutIn.Find("Depth/Subject").gameObject.activeInHierarchy,
                "The ace activation omitted its overframe subject.");
            TrackStableOverframe(cutIn, sample, .51f, .98f);
            animator.Clear();
            Debug.Log("[VERIFY OVERFRAME MESH] Summon and activation reused their subject and shimmer meshes while scaling.");

        }

        private static void TrackStableOverframe(Transform overframe, Action<float> frame, float from, float to)
        {
            var subject = overframe.Find("Depth/Subject").GetComponent<CardOverframeGraphic>();
            var shimmer = overframe.Find("Depth/Shimmer").GetComponent<CardOverframeGraphic>();
            int dirty = 0;
            void CountRebuild() => dirty++;
            subject.RegisterDirtyVerticesCallback(CountRebuild);
            shimmer.RegisterDirtyVerticesCallback(CountRebuild);
            for (int i = 0; i < 20; i++)
            {
                frame(Mathf.Lerp(from, to, i / 19f));
                Canvas.ForceUpdateCanvases();
            }
            subject.UnregisterDirtyVerticesCallback(CountRebuild);
            shimmer.UnregisterDirtyVerticesCallback(CountRebuild);
            Require(dirty == 0, $"Transform animation requested {dirty} unnecessary overframe mesh rebuilds.");
        }

        private static void VerifyGroupedOverframe(CardAnimationManager animator, Queue<IEnumerator> queue)
        {
            var cardSize = new Vector2(92f, 134f);
            var targets = new[]
            {
                new CardPose(new Vector2(-210f, -110f), cardSize),
                new CardPose(new Vector2(-100f, -110f), cardSize),
                new CardPose(new Vector2(10f, -110f), cardSize),
            };
            uint[] cards = { BlueEyes, 9674034, 46986414 };
            int departed = 0, landed = 0, refreshed = 0;
            for (int i = 0; i < cards.Length; i++)
            {
                var from = new CardPose(new Vector2(-80f + i * 100f, -235f), cardSize);
                var flight = new CardFlight(cards[i], from, targets[i], true, i == 2);
                animator.StageFieldArrival(flight, CardMotion.SpecialSummon, 0, YgoConstants.LOCATION_MZONE, (uint)i,
                    null, () => departed++, () => landed++, () => refreshed++);
            }
            animator.CompleteFieldArrivals();
            var arrival = queue.Dequeue();
            Require(arrival.MoveNext() && arrival.Current is IEnumerator, "The simultaneous arrivals did not enter a shared cut-in.");
            var reveal = (IEnumerator)arrival.Current;
            Require(reveal.MoveNext() && departed == cards.Length && landed == 0, "The group left the origin at different times.");
            var pop = SampleAnimation((IEnumerator)reveal.Current);
            Require(animator.GetComponentsInChildren<CompositeCardView>().Count(view => view.CurrentCode == 0) == 3,
                "A grouped card showed its identity before turning over.");
            pop(.52f);
            Canvas.ForceUpdateCanvases();
            var views = animator.GetComponentsInChildren<CompositeCardView>();
            var ace = views.Single(view => view.CurrentCode == BlueEyes);
            var excel = views.Single(view => view.CurrentCode == 9674034);
            var hidden = views.Single(view => view.CurrentCode == 0);
            TrackStableOverframe(ace.transform.Find("Overframe"), pop, .54f, 1f);
            TrackStableOverframe(excel.transform.Find("Overframe"), pop, .54f, 1f);
            var left = (RectTransform)ace.transform;
            var right = (RectTransform)excel.transform;
            Require(ace.HasOverframe && excel.HasOverframe && !hidden.HasOverframe && views.Length == 3 &&
                left.anchoredPosition.x < 0f && right.anchoredPosition.x > 0f &&
                right.anchoredPosition.x - left.anchoredPosition.x > left.rect.width * 1.48f &&
                Mathf.Approximately(ace.transform.Find("Overframe/Depth").localScale.x, 1.25f) &&
                animator.transform.Find("ChainImpactWave").GetComponent<DuelEffectGraphic>().Tint == DuelStyle.ChainGold,
                "The grouped aces overlapped, lost their bounce, or revealed the face-down card.");
            Require(reveal.MoveNext(), "The grouped cut-in omitted the settling stage.");
            SampleAnimation((IEnumerator)reveal.Current)(1f);
            Require(!reveal.MoveNext() && animator.transform.Find("GroupSummonShade") == null &&
                landed == 0 && refreshed == 0, "The grouped cut-in published the field too early.");
            Require(arrival.MoveNext(), "The group omitted its movement to the field.");
            var land = SampleAnimation((IEnumerator)arrival.Current);
            land(.5f);
            Require(landed == 0 && refreshed == 0, "One card became live while the remaining group was still moving.");
            land(1f);
            Require(Vector2.Distance(left.anchoredPosition, targets[0].Position) < .1f &&
                Vector2.Distance(right.anchoredPosition, targets[1].Position) < .1f &&
                Vector2.Distance(((RectTransform)hidden.transform).anchoredPosition, targets[2].Position) < .1f,
                "One grouped card did not finish in its original target slot.");
            Require(!arrival.MoveNext() && landed == cards.Length && refreshed == 1 &&
                animator.GetComponentsInChildren<CompositeCardView>().Length == 0,
                "The engine's group did not land together or left a visible proxy on the field.");
            Debug.Log("[VERIFY OVERFRAME GROUP] Two face-up aces shared the cut-in, the face-down card stayed hidden, and all three arrived together.");
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
            Require(card.transform.Find("AspectBox/AtkDef/Stat").GetComponent<Text>().text == "ATK/1600  DEF/1400",
                "The duel face styling changed the printed card used outside a duel.");
        }

        private static void VerifyExpandedDuelCard(Transform parent)
        {
            const uint accesscode = 86066372, utopia = 84013237, oddEyes = 16178681;
            var card = CompositeCardView.Create(parent, "Expanded Duel Card", detailedOverlay: true, duelArtwork: true);
            try
            {
                var face = card.transform.Find("AspectBox");
                var badge = face.Find("LevelInfo/Rating");
                var number = badge.Find("Number").GetComponent<Text>();
                var symbol = badge.Find("Symbol").GetComponent<Image>();
                var attack = face.Find("AtkDef/Attack/Digits/Value").GetComponent<Text>();
                var defense = face.Find("AtkDef/Defense");
                card.SetCard(accesscode);
                Require(number.text == "4" && symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Link) &&
                    attack.text == "2300" && attack.font == DuelTypography.NumberFont && !defense.gameObject.activeSelf,
                    "The enlarged Link card did not use its Link rating and attack alone.");

                card.SetCard(utopia, live: new LiveCardState
                {
                    code = utopia, type = CardDatabase.Get(utopia).type, rank = 0,
                    position = YgoConstants.POS_FACEUP_ATTACK, attack = 2500, defense = 2000,
                });
                Require(number.text == "0" && badge.gameObject.activeSelf &&
                    symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Rank) && defense.gameObject.activeSelf,
                    "A reused enlarged card lost Rank 0 or kept its Link layout.");

                card.SetCard(oddEyes);
                var left = face.Find("LeftScale/Value").GetComponent<Text>();
                var right = face.Find("RightScale/Value").GetComponent<Text>();
                Require(left.text == "4" && right.text == "4" && left.font == DuelTypography.NumberFont &&
                    right.font == DuelTypography.NumberFont && symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Level),
                    "The displayed Pendulum monster lost its pair of printed scales.");

                card.SetCard(oddEyes, live: new LiveCardState
                {
                    code = oddEyes, type = YgoConstants.TYPE_SPELL | YgoConstants.TYPE_PENDULUM,
                    location = YgoConstants.LOCATION_SZONE, sequence = 4, position = YgoConstants.POS_FACEUP_ATTACK,
                    lscale = 4, rscale = 7,
                });
                Require(number.text == "7" && symbol.sprite == DuelStatSymbols.Rating(DuelRatingKind.Scale) &&
                    !left.gameObject.activeSelf && !right.gameObject.activeSelf && !face.Find("AtkDef").gameObject.activeSelf,
                    "The enlarged card did not show the active scale in the right Pendulum Zone.");
                card.SetCard(0, faceDown: true);
                Require(!badge.gameObject.activeSelf && !face.Find("AtkDef").gameObject.activeSelf &&
                    face.Find("CardBack").gameObject.activeSelf,
                    "A face-down expanded card kept a live combat or rating badge.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(card.gameObject);
            }
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

        private static void VerifyChainSourceIdentity()
        {
            const uint breaker = 71413901, zone = YgoConstants.LOCATION_MZONE;
            const uint faceUp = YgoConstants.POS_FACEUP_ATTACK;
            using var duel = new DuelSession();
            var first = new ChainLinkInfo { code = breaker, PublicFieldSource = ((byte)0, zone, 0u) };
            var second = new ChainLinkInfo { code = breaker, PublicFieldSource = ((byte)1, zone, 2u) };
            duel.CurrentChain.Add(first);
            duel.CurrentChain.Add(second);
            var notify = typeof(DuelSession).GetMethod("NotifyCardMoved", BindingFlags.Instance | BindingFlags.NonPublic);

            void Notify(CardMoveEvent move, CardMoveEvent partner = null) => notify.Invoke(duel, new object[] { move, partner });

            Notify(new CardMoveEvent
                {
                    code = breaker, fromCon = 0, fromLoc = zone, fromSeq = 0, fromPos = faceUp,
                    toCon = 1, toLoc = zone, toSeq = 2, toPos = faceUp,
                },
                new CardMoveEvent
                {
                    code = breaker, fromCon = 1, fromLoc = zone, fromSeq = 2, fromPos = faceUp,
                    toCon = 0, toLoc = zone, toSeq = 0, toPos = faceUp,
                });
            Require(first.PublicFieldSource == ((byte)1, zone, 2u) && second.PublicFieldSource == ((byte)0, zone, 0u),
                "Simultaneous moves assigned both chain sources to the same copy.");
            Notify(new CardMoveEvent { code = breaker, fromCon = 1, fromLoc = zone, fromSeq = 2, fromPos = faceUp,
                toCon = 1, toLoc = YgoConstants.LOCATION_GRAVE });
            Notify(new CardMoveEvent { code = breaker, fromCon = 1, fromLoc = YgoConstants.LOCATION_HAND,
                toCon = 1, toLoc = zone, toSeq = 2, toPos = faceUp });
            Require(first.PublicFieldSource == null && second.PublicFieldSource == ((byte)0, zone, 0u),
                "A departed chain source was rebound to a newly summoned copy.");
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
                Require(root.Find("Caption/Value").GetComponent<Text>().text == "링크 소환" &&
                    root.Find("Caption/Rating/Number")?.GetComponent<Text>().text == "4" &&
                    root.Find("Caption/Rating/Symbol").GetComponent<Image>().sprite == DuelStatSymbols.Rating(DuelRatingKind.Link) &&
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

        private static void VerifyAttackAfterLayoutChange(CardAnimationManager animator)
        {
            animator.Clear();
            var attackerPose = new CardPose(new Vector2(-155f, -64f), new Vector2(68f, 99f));
            var defenderPose = new CardPose(new Vector2(95f, 70f), new Vector2(68f, 99f), 90f);
            var attackArea = new Vector2(112f, 105f);
            var defenseArea = new Vector2(106f, 100f);
            Vector2 scale = Vector2.one, offset = Vector2.zero;
            float artScale = 1f;
            CardPose ReadPose(CardPose pose) =>
                new(Vector2.Scale(pose.Position, scale) + offset, pose.Size * artScale, pose.Angle);
            var attacker = attackerPose.FollowLayout(() => ReadPose(attackerPose));
            var defender = defenderPose.FollowLayout(() => ReadPose(defenderPose));
            Vector2 ReadHit() => ReadPose(defenderPose).Position;
            var steps = (Queue<IEnumerator>)typeof(CardAnimationManager)
                .GetField("_steps", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animator);

            animator.QueueAttackArrow(attacker, ReadHit, false);
            var arrow = steps.Dequeue();
            Require(arrow.MoveNext(), "The queued attack arrow did not begin.");
            var arrowFrame = SampleAnimation((IEnumerator)arrow.Current);
            offset = new Vector2(76f, -15f);
            arrowFrame(.66f);
            var track = (RectTransform)animator.transform.Find("AttackPath");
            Require(Vector2.Distance(track.anchoredPosition, (ReadPose(attackerPose).Position + ReadHit()) * .5f) < .08f,
                "The declared attack arrow stayed behind after moving both field anchors.");
            arrowFrame(1f);
            Require(!arrow.MoveNext(), "The responsive attack arrow did not finish.");
            offset = Vector2.zero;

            const uint mysticalElf = 15025844;
            var attack = new BattleCardStats(0, YgoConstants.LOCATION_MZONE, 1, YgoConstants.POS_FACEUP_ATTACK, 3000, 2500);
            var defense = new BattleCardStats(1, YgoConstants.LOCATION_MZONE, 1, YgoConstants.POS_FACEUP_DEFENSE, 800, 2000);
            var comparison = animator.CreateBattleReadout(DuelFieldUI.MobileBattleStatsArea, attack, defense, 0, false, false, mobileLayout: true);
            var attackCard = new DuelCardReadout(CardDatabase.Get(BlueEyes), battleStats: attack);
            var defenseCard = new DuelCardReadout(CardDatabase.Get(mysticalElf), battleStats: defense);
            var flight = new CardFlight(BlueEyes, attacker, attacker, false, false);
            int arrivals = 0;
            animator.QueueAttack(flight, ReadHit(), false, false, comparison, attackCard, attackArea, () => arrivals++,
                () => (attacker, attackArea, defender, animator.CreateBattleCardReadout("DefendingCardReadout",
                    defenseCard, true, defenderPose.Position, defenseArea)),
                () => (ReadHit(), Vector2.Scale(attackArea, scale), Vector2.Scale(defenseArea, scale)));
            var step = steps.Dequeue();
            Require(step.MoveNext(), "The responsive attack did not begin.");
            var windup = SampleAnimation((IEnumerator)step.Current);
            scale = new Vector2(.79f, .94f);
            offset = new Vector2(-66f, 28f);
            artScale = .86f;
            windup(0f);
            var art = (RectTransform)animator.transform.Find("MovingCard");
            var attackingStats = (RectTransform)animator.transform.Find("AttackingCardReadout");
            var defendingStats = (RectTransform)animator.transform.Find("DefendingCardReadout");
            Require(Vector2.Distance(art.anchoredPosition, ReadPose(attackerPose).Position) < .08f &&
                Vector2.Distance(attackingStats.sizeDelta, Vector2.Scale(attackArea, scale)) < .08f &&
                Vector2.Distance(defendingStats.anchoredPosition, ReadHit()) < .08f &&
                Vector2.Distance(defendingStats.sizeDelta, Vector2.Scale(defenseArea, scale)) < .08f,
                "The attack path or one of its stat plates kept the old position or width.");
            windup(1f);
            Require(step.MoveNext(), "The resized attack did not reach its target.");
            SampleAnimation((IEnumerator)step.Current)(1f);
            Require(!Bounds(attackingStats).Overlaps(Bounds(defendingStats)),
                "The resized lunge crossed into the defender's live readout.");
            Require(step.MoveNext(), "The resized attack did not enter its impact and return.");
            var returning = SampleAnimation((IEnumerator)step.Current);
            var impact = (RectTransform)animator.transform.Find("Strike");
            offset = new Vector2(34f, -19f);
            returning(.43f);
            Require(Vector2.Distance(impact.anchoredPosition, ReadHit()) < .08f &&
                Vector2.Distance(defendingStats.anchoredPosition, ReadHit()) < .08f &&
                defendingStats.Find("CardReadout/Combat/Defense/Digits/Value").GetComponent<Text>().text == "2000",
                "The impact or live defense value drifted after a second layout change.");
            returning(1f);
            Require(Vector2.Distance(art.anchoredPosition, ReadPose(attackerPose).Position) < .08f &&
                !step.MoveNext() && arrivals == 1, "The attacker did not return to the resized slot.");
            Debug.Log("[VERIFY DUEL RESPONSIVE ATTACK] The declared path, stat plates and impact followed layout changes through the complete attack.");
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
