using System;
using UnityEngine;

namespace QAnimator.Unity
{
    [DisallowMultipleComponent]
    public sealed class QAnimatorPlayer : MonoBehaviour
    {
        [SerializeField] private bool _playOnAwake;
        [SerializeField] private bool _loop = true;
        [SerializeField, Min(0f)] private float _speed = 1f;
        [SerializeField] private bool _useUnscaledTime = true;
        [SerializeField] private TextAsset _source;
        [SerializeField] private FilterMode _filterMode = FilterMode.Bilinear;

        private QAnimatorDecoder _decoder;
        private Texture2D _texture;
        private bool _isPlaying;
        private float _time;
        private int _currentFrame = -1;

        public event Action OnComplete;
        public event Action<Texture2D> OnTextureCreated;

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public float Speed
        {
            get => _speed;
            set => _speed = Mathf.Max(0f, value);
        }

        public bool UseUnscaledTime
        {
            get => _useUnscaledTime;
            set => _useUnscaledTime = value;
        }

        public bool IsPlaying => _isPlaying;
        public bool IsLoaded => _decoder != null;
        public float CurrentTime => _time;
        public float Duration => _decoder?.Duration ?? 0f;
        public float Fps => _decoder?.Fps ?? 0f;
        public int CurrentFrame => _currentFrame;
        public int FrameCount => _decoder?.FrameCount ?? 0;
        public int Width => _decoder?.Width ?? 0;
        public int Height => _decoder?.Height ?? 0;
        public Texture2D Texture => _texture;
        public bool HasAlpha => _decoder?.HasAlpha ?? false;

        private void Awake()
        {
            if (_source != null)
                Load(_source.bytes);
        }

        private void Start()
        {
            if (_playOnAwake && _decoder != null)
                Play();
        }

        private void Update()
        {
            if (!_isPlaying || _decoder == null || _speed <= 0f)
                return;

            float delta = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _time += delta * _speed;
            float duration = _decoder.Duration;
            if (duration <= 0f)
                return;

            if (_time >= duration)
            {
                if (_loop)
                {
                    _time %= duration;
                    DecodeAndUpload(FrameFromTime(_time));
                }
                else
                {
                    _time = duration;
                    DecodeAndUpload(_decoder.FrameCount - 1);
                    _isPlaying = false;
                    OnComplete?.Invoke();
                }
                return;
            }

            DecodeAndUpload(FrameFromTime(_time));
        }

        public void Load(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            Load(asset.bytes);
        }

        public void Load(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            _isPlaying = false;
            _time = 0f;
            _currentFrame = -1;
            _decoder = null;
            DisposeTexture();

            var decoder = new QAnimatorDecoder(data);
            var texture = new Texture2D(decoder.Width, decoder.Height, TextureFormat.RGBA32, mipChain: false, linear: false)
            {
                name = "QAnimator Playback Texture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = _filterMode,
            };

            _decoder = decoder;
            _texture = texture;
            DecodeAndUpload(0);
            OnTextureCreated?.Invoke(_texture);
        }

        public void Unload()
        {
            _isPlaying = false;
            _time = 0f;
            _currentFrame = -1;
            _decoder = null;
            DisposeTexture();
        }

        public void Play()
        {
            EnsureLoaded();
            if (_time >= Duration)
            {
                _time = 0f;
                DecodeAndUpload(0);
            }
            _isPlaying = true;
        }

        public void Pause() => _isPlaying = false;

        public void Resume()
        {
            EnsureLoaded();
            if (_time >= Duration)
                return;
            _isPlaying = true;
        }

        public void Stop()
        {
            _isPlaying = false;
            _time = 0f;
            if (_decoder != null)
                DecodeAndUpload(0);
        }

        public void Restart()
        {
            EnsureLoaded();
            _time = 0f;
            DecodeAndUpload(0);
            _isPlaying = true;
        }

        public void Seek(float seconds)
        {
            EnsureLoaded();
            _time = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, Duration));
            DecodeAndUpload(FrameFromTime(_time));
        }

        public void SetFilterMode(FilterMode filterMode)
        {
            _filterMode = filterMode;
            if (_texture != null)
                _texture.filterMode = filterMode;
        }

        private int FrameFromTime(float seconds)
        {
            if (_decoder == null || _decoder.FrameCount <= 1)
                return 0;
            int frame = Mathf.FloorToInt(seconds * _decoder.Fps);
            return Mathf.Clamp(frame, 0, _decoder.FrameCount - 1);
        }

        private void DecodeAndUpload(int frame)
        {
            if (_decoder == null || _texture == null || frame == _currentFrame)
                return;

            _decoder.DecodeFrame(frame);
            _texture.LoadRawTextureData(_decoder.RgbaBuffer);
            _texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            _currentFrame = frame;
        }

        private void EnsureLoaded()
        {
            if (_decoder == null)
                throw new InvalidOperationException("QAnimatorPlayer has no animation loaded.");
        }

        private void OnDestroy()
        {
            _decoder = null;
            DisposeTexture();
        }

        private void DisposeTexture()
        {
            if (_texture == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(_texture);
            else
#endif
                Destroy(_texture);
            _texture = null;
        }
    }
}
