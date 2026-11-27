namespace YGOPortable.Core
{
    internal readonly struct BeyondDestinySoundscape
    {
        internal readonly string Music, Ambience;

        internal BeyondDestinySoundscape(string music, string ambience)
        {
            Music = music;
            Ambience = ambience;
        }

        internal BeyondDestinySoundscape After(BeyondDestinyCampaign.Cue cue) => cue.kind switch
        {
            "music" => new BeyondDestinySoundscape(cue.assetPath, Ambience),
            "background" => new BeyondDestinySoundscape(Music, null),
            "sound" when cue.action == "loop" => new BeyondDestinySoundscape(Music, cue.assetPath),
            "sound" when cue.action == "silence" => new BeyondDestinySoundscape(Music, null),
            "audio" => new BeyondDestinySoundscape(null, cue.assetPath),
            _ => this,
        };

        internal BeyondDestinySoundscape After(BeyondDestinyCampaign.Cue[] cues)
        {
            var state = this;
            foreach (var cue in cues) state = state.After(cue);
            return state;
        }
    }
}
