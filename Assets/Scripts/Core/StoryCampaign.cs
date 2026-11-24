using System;
using System.Globalization;

namespace YGOPortable.Core
{
    internal static class StoryCampaign
    {
        internal const int EpisodeCount = 30;
        internal const int LegacyEpisodeCount = 50;
        private const string EpisodeSeparator = "_story30_ep_";

        internal static string EpisodeId(string series, int number) =>
            series + EpisodeSeparator + number.ToString("00", CultureInfo.InvariantCulture);

        internal static string LegacyEpisodeId(string series, int number) =>
            series + "_ep_" + number.ToString("00", CultureInfo.InvariantCulture);

        internal static string LegacyClearFor(string episodeId)
        {
            if (string.IsNullOrEmpty(episodeId)) return null;
            int separator = episodeId.IndexOf(EpisodeSeparator, StringComparison.Ordinal);
            if (separator <= 0 || !int.TryParse(episodeId.AsSpan(separator + EpisodeSeparator.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number is < 1 or > EpisodeCount)
                return null;

            // Preserve the first clear and the finale, with intermediate progress spread across the shorter season.
            int legacy = 1 + (number - 1) * (LegacyEpisodeCount - 1) / (EpisodeCount - 1);
            return LegacyEpisodeId(episodeId.Substring(0, separator), legacy);
        }
    }
}
