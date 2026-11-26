using System.IO;
using UnityEngine;
using YGOPortable.Core;

namespace YGOPortable.UI
{
    internal static class RpgWardrobe
    {
        private const string Root = "TagForce/5DS/";
        private const string Everyday = "everyday";
        private const string Duel = "duel";

        internal static int EpisodeNumber(StoryEpisodeData episode) =>
            episode?.deckId is >= 59001 and <= 59050 ? episode.deckId - 59000 : 0;

        internal static string ForStory(int id, int episode) => ResortOverride(id) ?? AutomaticForStory(id, episode);

        private static string AutomaticForStory(int id, int episode) => AvailableOutfit(id, id switch
        {
            1 when episode is >= 1 and <= 3 => "school",
            1 when episode is 32 or 34 => "workshop",
            1 when episode is >= 35 and <= 44 => "riding",
            1 when episode is >= 9 and <= 13 or >= 18 and <= 27 or >= 45 and <= 49 => Duel,
            1 => Everyday,
            32 when episode == 33 => "city",
            220 when episode == 30 => "press",
            _ => null,
        });

        internal static string ForField(int id, int region, int episode) => ResortOverride(id) ?? AutomaticForField(id, region, episode);

        internal static string AutomaticForField(int id, int region, int episode)
        {
            if (region == TagForceWorldUI.RegionForEpisode(episode)) return AutomaticForStory(id, episode);
            return AvailableOutfit(id, (id, region) switch
            {
                (1, TagForceWorldUI.RpgAcademia) => "school",
                (1, TagForceWorldUI.RpgWrgp) => "riding",
                (1, TagForceWorldUI.RpgStadium or TagForceWorldUI.RpgMoment or TagForceWorldUI.RpgArk) => Duel,
                (1, _) => Everyday,
                (32, TagForceWorldUI.RpgPoppo) => "city",
                (220, TagForceWorldUI.RpgPoppo) => "press",
                _ => null,
            });
        }

        internal static string PortraitStem(int id, string outfit) =>
            $"Characters_Standing/full_{OutfitCharacter(id, outfit)}" + (UsesBasePortrait(outfit) ? "" : $"_{outfit}");

        internal static bool UsesBasePortrait(string outfit) => outfit == null || outfit == Duel;

        internal static string Preview(int id, string outfit) => $"{PortraitStem(id, outfit)}_determined.png";

        internal static string WalkAtlas(int id, string outfit) =>
            outfit == Duel ? $"WalkCycles/sd_{id}_field.png" : $"WalkCycles/sd_{OutfitCharacter(id, outfit)}_{outfit}_field.png";

        internal static bool HasResortAssets(int id) => Exists(id, CollectionProgress.RpgResortSkin);

        private static string ResortOverride(int id) =>
            CollectionProgress.RpgEquippedOutfit(OutfitCharacter(id, CollectionProgress.RpgResortSkin)) == CollectionProgress.RpgResortSkin &&
            HasResortAssets(id) ? CollectionProgress.RpgResortSkin : null;

        private static int OutfitCharacter(int id, string outfit) => outfit != CollectionProgress.RpgResortSkin ? id : id switch
        {
            9 or 230 => 220, // The ordinary story/field versions of Carly share a wardrobe.
            25 => 218,
            _ => id,
        };

        private static string AvailableOutfit(int id, string outfit)
        {
            if (outfit != null && Exists(id, outfit)) return outfit;
            return id == 1 && outfit != Everyday && Exists(id, Everyday) ? Everyday : null;
        }

        private static bool Exists(int id, string outfit) =>
            File.Exists(Path.Combine(Application.streamingAssetsPath, Root, Preview(id, outfit))) &&
            File.Exists(Path.Combine(Application.streamingAssetsPath, Root, WalkAtlas(id, outfit)));
    }
}
