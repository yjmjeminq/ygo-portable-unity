using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.Video;

namespace YGOPortable.UI
{
    internal sealed class SummonVideoOverlay : MonoBehaviour, IPointerDownHandler
    {
        private const float Opacity = .88f;
        private const double DecoderTimeout = 2.5;
        private const int MaxRenderEdge = 1920;
        private VideoPlayer _player;
        private RawImage _image;
        private RenderTexture _target;
        private bool _ended, _failed, _skipped;

        internal bool FinishedOrSkipped { get; private set; }
        internal bool Skipped => _skipped;
        internal static bool CanRender => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null &&
            SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32);

        internal IEnumerator Play(string path, Func<bool> paused)
        {
            if (!CanRender) yield break;
            try
            {
                if (!Prepare(path)) yield break;
                double deadline = Time.realtimeSinceStartupAsDouble + DecoderTimeout;
                while (_player != null && !_player.isPrepared && !_failed && !_skipped && Time.realtimeSinceStartupAsDouble < deadline)
                    yield return null;

                if (_player != null && !_failed && !_skipped)
                {
                    if (!_player.isPrepared) PlaybackError(_player, "Video preparation timed out.");
                    else if (CreateTarget()) yield return RunPlayer(paused);
                }

                if (_image == null) yield break;
                float startAlpha = _image.color.a;
                bool completed = _ended && !_failed && _image.texture != null && startAlpha > 0f;
                float duration = _skipped ? .06f : .14f;
                if (startAlpha > 0f)
                {
                    double start = Time.realtimeSinceStartupAsDouble;
                    float progress;
                    do
                    {
                        progress = Mathf.Clamp01((float)(Time.realtimeSinceStartupAsDouble - start) / duration);
                        _image.color = new Color(1f, 1f, 1f, startAlpha * (1f - progress));
                        if (progress < 1f) yield return null;
                    }
                    while (progress < 1f);
                }
                FinishedOrSkipped = _skipped || completed;
            }
            finally { ReleasePlayback(); }
        }

        private bool Prepare(string path)
        {
            var input = gameObject.AddComponent<CanvasGroup>();
            input.ignoreParentGroups = true;
            DuelUiKit.AddImage(gameObject, DuelUiKit.WhiteSprite, Color.clear);
            _image = DuelUiKit.FillRect("Video", transform).gameObject.AddComponent<RawImage>();
            _image.color = Color.clear;
            _image.raycastTarget = false;

            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.source = VideoSource.Url;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.None;
            _player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            _player.waitForFirstFrame = true;
            _player.skipOnDrop = true;
            _player.errorReceived += PlaybackError;
            _player.loopPointReached += PlaybackEnded;
            try
            {
                _player.url = path;
                _player.Prepare();
                return true;
            }
            catch (Exception error) when (error is UnityException || error is InvalidOperationException ||
                error is ArgumentException)
            {
                PlaybackError(_player, error.Message);
                return false;
            }
        }

        private bool CreateTarget()
        {
            uint width = _player.width, height = _player.height;
            if (width == 0 || height == 0) return false;
            float scale = Mathf.Min(1f, Mathf.Min(MaxRenderEdge, SystemInfo.maxTextureSize) / (float)Math.Max(width, height));
            try
            {
                _target = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(width * scale)),
                    Mathf.Max(1, Mathf.RoundToInt(height * scale)), 0, RenderTextureFormat.ARGB32) { name = "Summon video" };
                if (!_target.Create()) return false;
            }
            catch (UnityException error)
            {
                PlaybackError(_player, error.Message);
                return false;
            }
            var aspect = _image.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = width / (float)height;
            _image.texture = _player.targetTexture = _target;
            return true;
        }

        private IEnumerator RunPlayer(Func<bool> paused)
        {
            long lastFrame = -1;
            double lastProgress = Time.realtimeSinceStartupAsDouble;
            double visibleSince = -1;
            bool playing = false;
            while (_player != null && !_ended && !_failed && !_skipped)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                bool shouldPlay = !paused();
                if (shouldPlay != playing)
                {
                    if (!SetPlaying(shouldPlay)) break;
                    playing = shouldPlay;
                    lastProgress = now;
                }
                long frame = _player.frame;
                if (frame != lastFrame)
                {
                    lastFrame = frame;
                    lastProgress = now;
                }
                if (frame >= 0 && _player.texture != null)
                {
                    if (visibleSince < 0) visibleSince = now;
                    float alpha = Mathf.Clamp01((float)(now - visibleSince) / .095f);
                    _image.color = new Color(1f, 1f, 1f, Mathf.Lerp(.12f, Opacity, alpha));
                }
                if (playing && now - lastProgress >= DecoderTimeout)
                {
                    PlaybackError(_player, "Video playback stalled.");
                    break;
                }
                yield return null;
            }
        }

        private bool SetPlaying(bool playing)
        {
            try
            {
                if (playing) _player.Play();
                else _player.Pause();
                return true;
            }
            catch (Exception error) when (error is UnityException || error is InvalidOperationException)
            {
                PlaybackError(_player, error.Message);
                return false;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _skipped = true;
        }

        private void PlaybackEnded(VideoPlayer source) => _ended = true;

        private void PlaybackError(VideoPlayer source, string message)
        {
            _failed = true;
            Debug.LogWarning($"[SummonVideo] {message}");
        }

        internal void ReleasePlayback()
        {
            if (_player != null)
            {
                _player.errorReceived -= PlaybackError;
                _player.loopPointReached -= PlaybackEnded;
                _player.Stop();
                _player.targetTexture = null;
                _player = null;
            }
            if (_image != null)
            {
                _image.color = Color.clear;
                _image.texture = null;
            }
            if (_target == null) return;
            _target.Release();
            if (Application.isPlaying) Destroy(_target);
            else DestroyImmediate(_target);
            _target = null;
        }

        private void OnDisable() => ReleasePlayback();
    }
}
