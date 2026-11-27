using System.Text.RegularExpressions;

namespace YGOPortable.Core
{
    internal static class BeyondDestinyReading
    {
        private static readonly Regex Emphasis = new(@"\*\*([^*\r\n]+)\*\*", RegexOptions.CultureInvariant);
        private static readonly Regex InlineLabel = new(@"`([^`\r\n]+)`", RegexOptions.CultureInvariant);
        private static readonly Regex Ellipsis = new(@"\.{3,}", RegexOptions.CultureInvariant);
        private static readonly Regex Exclamation = new(@"!{2,}", RegexOptions.CultureInvariant);

        internal static string Typeset(string text)
        {
            text = InlineLabel.Replace(text, "$1");
            text = Emphasis.Replace(text, "<b>$1</b>");
            text = Ellipsis.Replace(text, "…");
            text = Exclamation.Replace(text, "!");
            return text.Length > 1 && text[0] == '"' && text[^1] == '"'
                ? "“" + text.Substring(1, text.Length - 2) + "”" : text;
        }

        internal static float[] RevealTimes(string text, int[] characterOffsets)
        {
            var schedule = new float[characterOffsets.Length];
            float seconds = 0f;
            for (int i = 0; i < schedule.Length; ++i)
            {
                char current = text[characterOffsets[i]];
                seconds += current is '\n' or '\r' ? .035f : 1f / 24f;
                schedule[i] = seconds;
                char next = i + 1 < schedule.Length ? text[characterOffsets[i + 1]] : '\0';
                // Let a group such as “?!” or “…!” breathe once, after the final mark.
                if (next is '.' or '?' or '!' or '…') continue;
                seconds += current switch
                {
                    '.' or '?' or '!' => .14f,
                    '…' => .22f,
                    ',' or '—' => .075f,
                    '\n' => .105f,
                    _ => 0f,
                };
            }
            return schedule;
        }

        internal static float AutoHold(int characters, bool newScene) =>
            System.Math.Clamp(characters * .043f, 1.60f, 6f) + (newScene ? .75f : 0f);
    }
}
