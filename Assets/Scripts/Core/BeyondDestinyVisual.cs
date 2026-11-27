using System;

namespace YGOPortable.Core
{
    internal enum BeyondDestinyCamera { Still, Push, Hero, Guest, Detail, Pullback, Pan, LookUp, Pit }
    internal enum BeyondDestinyLight { None, White, Crimson, Violet, Blue, Warm, Dark }

    internal readonly struct BeyondDestinyVisual
    {
        internal readonly BeyondDestinyCamera Camera;
        internal readonly BeyondDestinyLight Light, Curtain;
        internal readonly bool Shake;

        private BeyondDestinyVisual(BeyondDestinyCamera camera, BeyondDestinyLight light, BeyondDestinyLight curtain, bool shake)
        {
            Camera = camera;
            Light = light;
            Curtain = curtain;
            Shake = shake;
        }

        internal BeyondDestinyVisual After(BeyondDestinyCampaign.Cue cue)
        {
            string text = cue.text ?? "";
            if (cue.kind == "standing")
                return text.Contains("클로즈업", StringComparison.Ordinal)
                    ? new BeyondDestinyVisual(BeyondDestinyCamera.Guest, Light, Curtain, Shake)
                    : this;
            BeyondDestinyCamera camera = Camera;
            if (Has(text, "멀어져", "줌아웃")) camera = BeyondDestinyCamera.Pullback;
            else if (Has(text, "패닝", "시선이 이동")) camera = BeyondDestinyCamera.Pan;
            else if (Has(text, "발코니를 비춘다")) camera = BeyondDestinyCamera.LookUp;
            else if (Has(text, "피트 모니터 앞")) camera = BeyondDestinyCamera.Pit;
            else if (Has(text, "얼굴로 좁혀", "디바인의 얼굴이 클로즈업")) camera = BeyondDestinyCamera.Guest;
            else if (Has(text, "목덜미 흉터")) camera = BeyondDestinyCamera.Hero;
            else if (Has(text, "소매를 꽉 붙잡는", "손이 맞잡히는")) camera = BeyondDestinyCamera.Detail;
            else if (Has(text, "클로즈업", "줌인")) camera = BeyondDestinyCamera.Push;

            bool whiteout = Has(text, "화이트 아웃", "Fade to White", "White Out");
            bool dark = Has(text, "암전", "Fade Out", "어두워지");
            bool transition = !Has(text, "순간적으로", "짧은", "점멸");
            BeyondDestinyLight curtain = transition && dark ? BeyondDestinyLight.Dark :
                whiteout ? BeyondDestinyLight.White : Curtain;
            BeyondDestinyLight light = Light;
            if (whiteout || Has(text, "화이트 플래시", "White Flash")) light = BeyondDestinyLight.White;
            else if (Has(text, "노이즈", "섬광", "번쩍", "점멸", "물든다", "가득 찬다", "반점", "충격 이펙트"))
                light = Has(text, "보랏빛", "보라색") ? BeyondDestinyLight.Violet :
                    Has(text, "붉", "진홍", "적색", "장미빛") ? BeyondDestinyLight.Crimson :
                    Has(text, "푸른빛") ? BeyondDestinyLight.Blue :
                    Has(text, "따스", "햇살") ? BeyondDestinyLight.Warm : BeyondDestinyLight.White;
            else if (dark) light = BeyondDestinyLight.Dark;
            else if (Has(text, "헤드라이트", "햇살")) light = BeyondDestinyLight.Warm;
            return new BeyondDestinyVisual(camera, light, curtain, Shake || Has(text, "흔들", "진동", "움찔"));
        }

        private static bool Has(string text, params string[] terms) =>
            Array.Exists(terms, term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
