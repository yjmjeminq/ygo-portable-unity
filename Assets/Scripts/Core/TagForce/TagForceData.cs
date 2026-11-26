using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    internal readonly struct TagForceSeries
    {
        internal readonly string Id, Title, Region, AssetFolder;

        internal TagForceSeries(string id, string title, string region, string assetFolder)
        {
            Id = id;
            Title = title;
            Region = region;
            AssetFolder = assetFolder;
        }
    }

    [Serializable]
    internal sealed class TagForceData
    {
        internal const string GxId = "gx";
        internal const string LegacyGxFile = "tagforce_gx_script.json";
        internal const string ProtagonistName = "홍련의 듀얼 퀸";
        internal const string PlayerStanding = "TagForce/GX/Standing/full_protagonist.png";
        internal const string PlayerSd = "TagForce/GX/SD/sd_protagonist.png";
        internal static readonly TagForceSeries[] Series =
        {
            new("dm", "DM", "도미노 시티", "DM"),
            new(GxId, "GX", "듀얼 아카데미아 화산섬", "GX"),
            new("5ds", "5D's", "네오 도미노 · 새틀라이트", "5DS"),
            new("zexal", "ZEXAL", "하트랜드 시티", "ZEXAL"),
            new("arcv", "ARC-V", "마이아미 시티", "ARCV"),
            new("vrains", "VRAINS", "LINK VRAINS", "VRAINS"),
        };
        internal const int MaxFieldCharacters = 5;
        internal static readonly string[] TimeKeys = { "morning", "day", "evening" };
        internal static readonly string[] TimeNames = { "아침", "낮", "저녁" };
        public string seriesId;
        public bool useLegacyGx;
        public TagForceSpot[] spots, fieldSpots, fieldScenes;
        public TagForceItem[] gifts;
        public TagForcePersona[] personas;
        public TagForceVisitor[] roster;
        public TagForceBreadShop drawBreadCatalog;
        public TagForceTopic[] conversationTopics;
        public TagForcePaths assetConfig;
        internal TagForceSpot[] Hubs { get; private set; }

        internal static TagForceData Load(string directory, TagForceSeries series)
        {
            string script = $"tagforce_{series.Id}_script.json";
            string index = $"tagforce_{series.Id}.json";
            var gx = series.Id == GxId ? null : ReadScenario(directory, LegacyGxFile);
            foreach (var selected in new[] { ReadScenario(directory, script), ReadScenario(directory, index) })
            {
                if (selected?.seriesId != series.Id || selected.useLegacyGx || !selected.HasSpots()) continue;
                if (selected.gifts?.Length is not > 0)
                    selected.gifts = gx?.gifts ?? Array.Empty<TagForceItem>();
                if (selected.conversationTopics?.Length is not > 0)
                    selected.conversationTopics = gx?.conversationTopics ?? Array.Empty<TagForceTopic>();
                if (selected.drawBreadCatalog?.breads?.Length is not > 0)
                    selected.drawBreadCatalog = gx?.drawBreadCatalog;
                selected.assetConfig ??= new TagForcePaths();
                if (selected.roster?.Length > 0) selected.BuildRoster(directory, series);
                if (!selected.IsUsable()) continue;
                var staffed = new HashSet<string>(selected.personas.SelectMany(persona => persona.forms)
                    .SelectMany(form => form.spots));
                selected.Hubs = selected.roster?.Length > 0 ? selected.spots :
                    selected.spots.Where(spot => staffed.Contains(spot.spotId)).ToArray();
                return selected;
            }
            return null;
        }

        internal static TagForceData LoadRpgConfiguration(string directory, string seriesId)
        {
            var data = Read(directory, $"tagforce_{seriesId}.json");
            return data?.seriesId == seriesId && data.HasSpots() ? data : null;
        }

        private static TagForceData ReadScenario(string directory, string name) =>
            Read(Path.Combine(directory, "TagForce"), name) ?? Read(directory, name);

        private static TagForceData Read(string directory, string name)
        {
            string path = Path.Combine(directory, name);
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<TagForceData>(json);
                if (data != null && data.drawBreadCatalog?.breads?.Length is not > 0)
                {
                    var breads = JsonUtility.FromJson<TagForceBreadList>(json)?.drawBreadCatalog;
                    if (breads?.Length > 0) data.drawBreadCatalog = TagForceBreadShop.FromList(breads);
                }
                if (data?.personas != null)
                    foreach (var persona in data.personas)
                    {
                        foreach (var form in persona?.forms ?? Array.Empty<TagForceForm>())
                            if (form?.activeTimes != null) form.activeTimes = form.activeTimes.Select(NormalizeTime).Distinct().ToArray();
                        foreach (var mail in persona?.pdaMails ?? Array.Empty<TagForceMail>())
                            if (mail != null) mail.timeSlot = NormalizeTime(mail.timeSlot);
                    }
                return data;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning($"[TagForceWorld] {name}: {error.Message}");
                return null;
            }
        }

        private static string NormalizeTime(string time) => time is "sunset" or "night" ? "evening" : time;

        private bool HasSpots() => spots?.Length > 0 &&
            spots.All(spot => !string.IsNullOrEmpty(spot?.spotId) && !string.IsNullOrEmpty(spot.name)) &&
            spots.Select(spot => spot.spotId).Distinct().Count() == spots.Length;

        private bool IsUsable() => HasSpots() && assetConfig != null && gifts != null && conversationTopics != null &&
            personas?.Length > 0 && personas.All(persona => !string.IsNullOrEmpty(persona?.personaId) && persona.forms?.Length > 0 &&
                persona.forms.All(form => form?.activeTimes?.Length > 0 && form.activeTimes.All(TimeKeys.Contains) &&
                    form.spots?.Length > 0 && form.spots.All(id => Spot(id) != null) && form.greetingLines != null) &&
                persona.pdaMails != null && persona.bondEvents != null &&
                persona.topicReplies != null && persona.giftReactions != null) &&
            personas.Select(persona => persona.personaId).Distinct().Count() == personas.Length &&
            (drawBreadCatalog == null || drawBreadCatalog.breads?.Length > 0);

        private void BuildRoster(string directory, TagForceSeries series)
        {
            var people = new List<TagForcePersona>();
            foreach (var visitor in roster)
            {
                if (visitor == null || Spot(visitor.spotId) is not { } spot ||
                    DeckRepository.GetCharacter(visitor.charId) is not { } character) continue;
                string art = $"TagForce/{series.AssetFolder}";
                string sd = $"{art}/Characters_SD/sd_{character.id}.png";
                string standingFolder = series.Id == GxId ? "Standing" : "Characters_Standing";
                if (series.Id == GxId && !File.Exists(Path.Combine(directory, sd)))
                    sd = $"{art}/SD/sd_{character.id}.png";
                people.Add(new TagForcePersona
                {
                    personaId = $"{series.Id}_{character.id}",
                    baseCharId = character.id,
                    displayName = character.name,
                    partnerRole = "Rival",
                    partnerSkillName = "듀얼 동료",
                    favoriteTopic = "duel",
                    pdaMails = Array.Empty<TagForceMail>(),
                    bondEvents = Array.Empty<TagForceBondEvent>(),
                    giftReactions = new TagForceGiftReactions
                    {
                        favoriteLine = "좋아하는 걸 기억해 줬네. 고마워!",
                        normalLine = "고마워. 다음에 또 만나.",
                    },
                    topicReplies = new TagForceTopicReplies
                    {
                        duel = "덱에는 듀얼리스트의 성격이 드러나. 네 덱은 어때?",
                        academy = $"{spot.name}에선 서로 다른 덱을 만날 수 있어.",
                        food = "듀얼을 마치면 함께 매점에 들르자.",
                        mystery = "한 판 같이 해 보면 서로 더 잘 알게 될 거야.",
                    },
                    forms = new[]
                    {
                        new TagForceForm
                        {
                            formCharId = character.id, formName = character.name, formTitle = spot.name,
                            sdSpritePath = sd,
                            standingSpritePath = $"{art}/{standingFolder}/full_{character.id}.png",
                            activeTimes = TimeKeys, spots = new[] { spot.spotId },
                            greetingLines = new TagForceGreetings
                            {
                                low = $"{spot.name}에 온 걸 환영해. 듀얼 한 판 어때?",
                                mid = "또 만났네. 오늘은 어떤 덱을 가져왔어?",
                                high = "함께라면 더 강한 상대에게 도전할 수 있겠어.",
                            },
                            duelStartLine = "오늘 듀얼도 끝까지 즐겨 보자.",
                            winLine = "다음 듀얼도 기대할게.",
                            loseLine = "좋은 듀얼이었어. 또 한 판 하자.",
                        },
                    },
                });
            }
            personas = people.ToArray();
        }

        internal TagForcePersona Persona(string id) => Array.Find(personas, entry => entry.personaId == id);
        internal TagForceSpot Spot(string id) => Array.Find(spots, entry => entry.spotId == id);
        internal TagForceSpot Hub(string id) => Array.Find(Hubs, entry => entry.spotId == id);

        internal string MeetingSpot(TagForceSave state, TagForcePersona persona, TagForceMail mail)
        {
            var form = persona.DisplayForm(state);
            if (form.spots.Contains(mail.spotId) && Hub(mail.spotId) != null) return mail.spotId;
            return form.spots[0];
        }
        internal TagForceItem Item(string id) => Array.Find(gifts, entry => entry.giftId == id) ??
            (drawBreadCatalog == null ? null : Array.Find(drawBreadCatalog.breads, entry => entry.breadId == id));

        internal IEnumerable<(TagForcePersona Persona, TagForceForm Form)> Visitors(TagForceSave state, string spot)
        {
            int capacity = MaxFieldCharacters - (Persona(state.partnerId) != null ? 1 : 0);
            var available = new List<(TagForcePersona Persona, TagForceForm Form)>();
            foreach (var persona in personas)
            {
                if (persona.personaId == state.partnerId) continue;
                var form = persona.ActiveForm(state);
                if (form != null && form.spots.Contains(spot)) available.Add((persona, form));
            }
            int start = available.Count > capacity ? (state.day - 1) % available.Count : 0;
            for (int i = 0; i < Math.Min(capacity, available.Count); i++)
                yield return available[(start + i) % available.Count];
        }

        internal IEnumerable<(TagForcePersona Persona, TagForceMail Mail)> Mail(TagForceSave state)
        {
            foreach (var persona in personas)
                foreach (var mail in persona.pdaMails)
                    if (mail.timeSlot == TimeKeys[state.time] && Spot(mail.spotId) != null)
                        yield return (persona, mail);
        }

        internal TagForceItem DrawBread(int roll)
        {
            var breads = drawBreadCatalog.breads;
            foreach (var bread in breads)
            {
                roll -= Math.Max(0, bread.weight);
                if (roll < 0) return bread;
            }
            return breads[^1];
        }
    }

    [Serializable] internal sealed class TagForceVisitor
    {
        public int charId;
        public string spotId;
        public int unlockAfter;
        public bool storyOnly;
        public TagForceVisitMove[] storyMoves;
    }

    [Serializable] internal sealed class TagForceVisitMove
    {
        public int after;
        public string spotId;
        public bool hidden;
    }

    [Serializable] internal sealed class TagForceSpot
    {
        public string spotId, name, subtitle, mapPath, fieldTheme, surfaceType, fieldLight;
        public float worldX, worldY, nearScale, farScale;
        public Vector2[] walkPolygon;
        public TagForceGroundObstacle[] obstacles;
        public int unlockAfter;
        public string[] defaultPickups, routes;
    }

    [Serializable] internal sealed class TagForceGroundObstacle
    {
        public Vector2[] vertices;
    }

    [Serializable] internal sealed class TagForcePaths
    {
        public string worldMapPath, myRoomPath, breadShopPath;
    }

    [Serializable] internal sealed class TagForcePersona
    {
        public string personaId, displayName, dormOrFaction, partnerRole, partnerSkillName, partnerSkillDesc;
        public bool isTagEligible;
        public string partnerRefusal;
        public TagForcePartnerQuotes partnerQuotes;
        public string favoriteGiftId, favoriteTopic;
        public int baseCharId;
        public TagForceForm[] forms;
        public TagForceMail[] pdaMails;
        public TagForceBondEvent[] bondEvents;
        public TagForceBondEvent grandPrixEnding;
        public TagForceTopicReplies topicReplies;
        public TagForceGiftReactions giftReactions;

        internal TagForceForm ActiveForm(TagForceSave save)
        {
            int hearts = save.Relationship(personaId).Hearts;
            var available = forms.Where(form => Math.Min(form.minHeart, TagForceRelation.HeartCount) <= hearts &&
                form.activeTimes.Contains(TagForceData.TimeKeys[save.time]))
                .OrderByDescending(form => form.minHeart).ToArray();
            if (available.Length == 0) return null;
            int equivalent = available.Count(form => form.minHeart == available[0].minHeart);
            return available[(save.day - 1) % equivalent];
        }

        internal TagForceForm DisplayForm(TagForceSave save) => ActiveForm(save) ??
            Array.Find(forms, form => form.formCharId == baseCharId) ?? forms[0];

        internal TagForceBondEvent NextBond(TagForceRelation relation) =>
            bondEvents.FirstOrDefault(bond => bond.RequiredHearts <= relation.Hearts &&
                !relation.clearedBonds.Contains(bond.heartLevel));

        internal bool CanPartner => partnerRole == "Heroine" || (isTagEligible && partnerRole == "TagHeroine");
        internal int OpeningLifeBonus => CanPartner ? 2000 : partnerRole == "Rival" ? 1000 : 0;
        internal int OpeningHand => partnerRole == "Hero" ? 6 : 5;
        internal int DestinyLimit => personaId == "gx_judai" ? 3000 : 2000;
        internal float WinDpMultiplier => personaId == "gx_chazz" || personaId == "gx_manjome" ||
            personaId == "gx_titan" ? 1.3f : 1f;
    }

    [Serializable] internal sealed class TagForceForm
    {
        public int formCharId, minHeart;
        public string formName, formTitle, lotdBust, sdSpritePath, standingSpritePath;
        public string duelStartLine, winLine, loseLine;
        public string[] activeTimes, spots;
        public TagForceGreetings greetingLines;
        internal string Greet(int hearts) => hearts >= 4 ? greetingLines.high : hearts >= 2 ? greetingLines.mid : greetingLines.low;
        internal IReadOnlyList<PresetDeckInfo> Decks => OpponentDeckLevels.GetDecks(formCharId);
    }

    [Serializable] internal sealed class TagForceGreetings { public string low, mid, high; }
    [Serializable] internal sealed class TagForcePartnerQuotes { public string followLine, duelReady; }
    [Serializable] internal sealed class TagForceGiftReactions
    {
        public string favoriteLine, normalLine, loved, liked, neutral;
        internal string Reply(bool favorite, TagForceItem item) => favorite ? favoriteLine ?? loved ?? liked :
            normalLine ?? (item?.flavor is "legendary" or "luxury" ? liked ?? neutral : neutral ?? liked);
    }
    [Serializable] internal sealed class TagForceTopicReplies
    {
        public string duel, academy, food, mystery;
        public string domino_city, millennium, riding_duel, city_satellite, signers, xyz_duel, heartland, numbers;
        internal string Reply(string id) => id switch
        {
            "duel" => duel, "academy" => academy, "food" => food, "domino_city" => domino_city, "millennium" => millennium,
            "riding_duel" => riding_duel, "city_satellite" => city_satellite, "signers" => signers,
            "xyz_duel" => xyz_duel, "heartland" => heartland, "numbers" => numbers, _ => mystery,
        };
    }
    [Serializable] internal sealed class TagForceTopic { public string topicId, name, desc; }
    [Serializable] internal sealed class TagForceMail { public string mailId, sender, timeSlot, spotId, subject, body; }
    [Serializable] internal sealed class TagForceDialogueLine { public string speaker, text; }
    [Serializable] internal sealed class TagForceBondEvent
    {
        public int heartLevel;
        public string title;
        public TagForceDialogueLine[] dialogues;
        public TagForceChoice choice;
        internal int RequiredHearts => Math.Clamp(heartLevel, 1, TagForceRelation.HeartCount);
    }
    [Serializable] internal sealed class TagForceChoice { public string prompt; public TagForceOption[] options; }
    [Serializable] internal sealed class TagForceOption { public string text, reply, effectType; }

    [Serializable] internal sealed class TagForceBreadList { public TagForceItem[] drawBreadCatalog; }

    [Serializable] internal sealed class TagForceBreadShop
    {
        public int drawPriceDp;
        public string shopkeeperName, shopkeeperGreeting;
        public TagForceItem[] breads;

        internal static TagForceBreadShop FromList(TagForceItem[] entries)
        {
            var breads = entries.Where(entry => !string.IsNullOrEmpty(entry?.breadId)).ToArray();
            if (breads.Length == 0) return null;
            foreach (var bread in breads)
            {
                int price = Math.Max(1, bread.priceDp);
                bread.weight = Math.Max(1, 300 / price);
                bread.baseAffinity = Math.Clamp(price / 4, 10, 80);
                bread.favoriteAffinity = bread.baseAffinity * 2;
            }
            return new TagForceBreadShop
            {
                drawPriceDp = breads.Min(bread => Math.Max(1, bread.priceDp)),
                shopkeeperName = "샌드위치 매점",
                shopkeeperGreeting = "마음에 드는 샌드위치를 뽑아 선물해 보세요.",
                breads = breads,
            };
        }
    }

    [Serializable] internal sealed class TagForceItem
    {
        public string giftId, breadId, name, desc, iconPath, flavor;
        public int baseAffinity, favoriteAffinity, priceDp, weight;
        public bool bonusRandomCard;
        public string[] preferredFactions, preferredRoles;
        internal string Id => giftId ?? breadId;
        internal bool IsFavorite(TagForcePersona persona) => Id == persona.favoriteGiftId ||
            (preferredFactions?.Contains(persona.dormOrFaction) ?? false) ||
            (preferredRoles?.Contains(persona.partnerRole) ?? false) ||
            (breadId == "bread_golden_egg" && persona.favoriteGiftId == "golden_egg_bread");
    }

    [Serializable] internal sealed class TagForceSave
    {
        internal const int AffinityVersion = 1;
        public int affinityVersion;
        public int day = 1, time;
        public string spotId;
        public string partnerId, inviteId, inviteSpot;
        public string grandPrixPartner;
        public int grandPrixRound;
        public List<TagForceRelation> relations = new();
        public List<TagForceStock> items = new();
        public List<string> pickups = new();
        public List<int> bags = new();
        public List<string> seenMail = new();
        public List<string> conversations = new();

        internal void Normalize()
        {
            day = Math.Max(day, 1);
            time = Mathf.Clamp(time, 0, TagForceData.TimeKeys.Length - 1);
            grandPrixRound = Mathf.Clamp(grandPrixRound, 0, 2);
            relations ??= new(); items ??= new(); pickups ??= new(); bags ??= new();
            seenMail ??= new(); conversations ??= new();
            relations.RemoveAll(relation => string.IsNullOrEmpty(relation?.personaId));
            items.RemoveAll(item => string.IsNullOrEmpty(item?.id) || item.count <= 0);
            foreach (var relation in relations)
            {
                relation.affinity = affinityVersion == 0 ? Math.Clamp(relation.affinity, 0, 500) * 8 :
                    Math.Clamp(relation.affinity, 0, TagForceRelation.MaxAffinity);
                relation.unlockedLevel = Math.Max(1, relation.unlockedLevel);
                relation.clearedBonds ??= new();
            }
            affinityVersion = AffinityVersion;
        }

        internal TagForceSave Copy() => JsonUtility.FromJson<TagForceSave>(JsonUtility.ToJson(this));

        internal TagForceRelation Relationship(string id) =>
            relations.Find(entry => entry.personaId == id) ?? new TagForceRelation { personaId = id };

        internal TagForceRelation EditRelationship(string id)
        {
            var relation = Relationship(id);
            if (!relations.Contains(relation)) relations.Add(relation);
            return relation;
        }

        internal void AddAffinity(string id, int points)
        {
            var relation = EditRelationship(id);
            relation.affinity = (int)Math.Clamp((long)relation.affinity + InvitationBoost(id, points),
                0, TagForceRelation.MaxAffinity);
        }

        internal int InvitationBoost(string id, int points) =>
            inviteId == id && inviteSpot == spotId ? (points * 3 + 1) / 2 : points;

        internal int Count(string id) => items.Find(entry => entry.id == id)?.count ?? 0;

        internal void ChangeItem(string id, int delta)
        {
            var entry = items.Find(item => item.id == id);
            if (entry == null)
            {
                entry = new TagForceStock { id = id };
                items.Add(entry);
            }
            entry.count = Math.Max(0, entry.count + delta);
            if (entry.count == 0) items.Remove(entry);
        }

        internal void Advance()
        {
            if (++time == TagForceData.TimeKeys.Length)
            {
                time = 0;
                if (day < int.MaxValue) day++;
            }
            pickups.Clear();
            bags.Clear();
            seenMail.Clear();
            conversations.Clear();
            inviteId = inviteSpot = null;
        }
    }

    [Serializable] internal sealed class TagForceRelation
    {
        internal const int HeartCount = 4;
        internal const int MaxAffinity = 4000;
        public string personaId;
        public int affinity;
        public int unlockedLevel = 1;
        public List<int> clearedBonds = new();
        public bool masterReward;
        public bool wonGrandPrix;
        internal int Hearts => affinity / (MaxAffinity / HeartCount);
    }

    [Serializable] internal sealed class TagForceStock { public string id; public int count; }

    [Serializable] internal sealed class TagForceWorldSave
    {
        public string seriesId;
        public TagForceSave progress;
    }

    internal sealed class TagForceDuel
    {
        internal string SeriesId { get; }
        internal TagForcePersona Persona { get; }
        internal TagForceForm Form { get; }
        internal TagForcePersona Partner { get; }
        internal PresetDeckInfo Deck { get; }
        internal int Level { get; }
        internal TagForceBondEvent Bond { get; }
        internal string Effect { get; }
        internal bool GrandPrix { get; }
        internal uint PartnerAce { get; }
        internal DuelOpening Opening => new(8000 + (Partner?.OpeningLifeBonus ?? 0) + (Effect == "SafeBuff" ? 1000 : 0),
            Partner?.OpeningHand ?? 5, 0, Partner?.DestinyLimit ?? 2000);

        internal TagForceDuel(string seriesId, TagForcePersona persona, TagForceForm form, TagForcePersona partner, PresetDeckInfo deck,
            int level, TagForceBondEvent bond, string effect, bool grandPrix, uint partnerAce)
        {
            SeriesId = seriesId;
            Persona = persona;
            Form = form;
            Partner = partner;
            Deck = deck;
            Level = level;
            Bond = bond;
            Effect = effect;
            GrandPrix = grandPrix;
            PartnerAce = partnerAce;
        }
    }
}
