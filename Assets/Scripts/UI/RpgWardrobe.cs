using System.IO;
using UnityEngine;

namespace YGOPortable.UI
{
    internal static class RpgWardrobe
    {
        private const string Root = "TagForce/5DS/";
        private const string Everyday = "everyday";
        private const string Duel = "duel";

        internal static int EpisodeNumber(StoryEpisodeData episode) =>
            episode?.deckId is >= 59001 and <= 59050 ? episode.deckId - 59000 : 0;

        internal static string ForStory(int id, int episode) => AvailableOutfit(id, id switch
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

        internal static string ForField(int id, int region, int episode)
        {
            if (region == TagForceWorldUI.RegionForEpisode(episode)) return ForStory(id, episode);
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
            $"Characters_Standing/full_{id}" + (outfit == null || outfit == Everyday ? "" : $"_{outfit}");

        internal static string Preview(int id, string outfit) => $"{PortraitStem(id, outfit)}_determined.png";

        internal static string WalkAtlas(int id, string outfit) =>
            outfit == Duel ? $"WalkCycles/sd_{id}_field.png" : $"WalkCycles/sd_{id}_{outfit}_field.png";

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
