using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.UI;

namespace QAnimator.Unity
{
    /// <summary>
    /// A self-contained UGUI QAnimator player and renderer.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("UI/QEmo Graphic")]
    public sealed class QEmoGraphic : MaskableGraphic
    {
        [SerializeField] private TextAsset _source;
        [SerializeField] private TextAsset[] _additionalAnimations = Array.Empty<TextAsset>();
        [SerializeField] private string _animation;
        [SerializeField] private bool _autoPlay = true;
        [SerializeField] private bool _loop = true;
        [SerializeField, Min(0f)] private float _speed = 1f;
        [SerializeField] private bool _useUnscaledTime = true;
        [SerializeField] private FilterMode _filterMode = FilterMode.Bilinear;

        private QAnimatorDecoder _decoder;
        private Texture2D _texture;
        private bool _isPlaying;
        private float _time;
        private int _currentFrame = -1;
        private TextAsset _loadedAsset;
        private string _animationName;
        private string _lastError;

        public event Action OnComplete;

        public TextAsset AnimationBytes
        {
            get => _source;
            set
            {
                if (_source == value && (_loadedAsset == value || value == null))
                    return;

                _source = value;
                if (_source == null)
                    Unload();
                else
                {
                    _animation = _source.name;
                    Load(_source);
                }
            }
        }

        public bool AutoPlay
        {
            get => _autoPlay;
            set => _autoPlay = value;
        }

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
        public string AnimationName => _animationName ?? string.Empty;
        public string LastError => _lastError;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_decoder == null)
                TryLoadSelectedAnimation();
            if (Application.isPlaying && _autoPlay && _decoder != null)
                Play();
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            float delta = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            Advance(delta);
        }

        protected override void OnDisable()
        {
            Unload();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            _decoder = null;
            DisposeTexture();
            base.OnDestroy();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            _speed = Mathf.Max(0f, _speed);
            if (_texture != null)
                _texture.filterMode = _filterMode;
            base.OnValidate();
        }
#endif

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_texture == null)
                return;

            Rect rect = GetPixelAdjustedRect();
            Color32 vertexColor = color;
            vertexHelper.AddVert(new Vector3(rect.xMin, rect.yMin), vertexColor, new Vector2(0f, 0f));
            vertexHelper.AddVert(new Vector3(rect.xMin, rect.yMax), vertexColor, new Vector2(0f, 1f));
            vertexHelper.AddVert(new Vector3(rect.xMax, rect.yMax), vertexColor, new Vector2(1f, 1f));
            vertexHelper.AddVert(new Vector3(rect.xMax, rect.yMin), vertexColor, new Vector2(1f, 0f));
            vertexHelper.AddTriangle(0, 1, 2);
            vertexHelper.AddTriangle(2, 3, 0);
        }

        public void Load(TextAsset asset)
        {
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            _source = asset;
            _animation = asset.name;
            LoadAssignedAsset(asset);
        }

        public void Load(byte[] data)
        {
            LoadInternal(data, null, null);
        }

        public void Unload()
        {
            _isPlaying = false;
            _time = 0f;
            _currentFrame = -1;
            _decoder = null;
            _loadedAsset = null;
            _animationName = null;
            _lastError = null;
            DisposeTexture();
        }

        public void Play(TextAsset asset)
        {
            Load(asset);
            Play();
        }

        public void Play(byte[] data)
        {
            Load(data);
            Play();
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

        /// <summary>
        /// Plays an assigned animation by TextAsset name. Each QAnimator v1 bytes file
        /// contains one animation; additional files may be assigned to this component.
        /// </summary>
        public void Play(string animationName)
        {
            if (string.IsNullOrEmpty(animationName))
                throw new ArgumentException("An animation name is required.", nameof(animationName));
            TextAsset asset = FindAssignedAnimation(animationName);
            if (asset == null)
                throw new ArgumentException(
                    $"Animation '{animationName}' is not assigned to this QEmoGraphic.",
                    nameof(animationName));

            _animation = asset.name;
            if (_decoder == null || _loadedAsset != asset)
                LoadAssignedAsset(asset);
            Restart();
        }

        public void Pause()
        {
            _isPlaying = false;
        }

        public void Resume()
        {
            EnsureLoaded();
            if (_time < Duration)
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

        public override void SetNativeSize()
        {
            if (_decoder == null)
                return;

            rectTransform.anchorMax = rectTransform.anchorMin;
            rectTransform.sizeDelta = new Vector2(_decoder.Width, _decoder.Height);
            SetVerticesDirty();
        }

        private void LoadAssignedAsset(TextAsset asset)
        {
            LoadInternal(asset.bytes, asset.name, asset);
        }

        private void LoadInternal(byte[] data, string animationName, TextAsset loadedAsset)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            _isPlaying = false;
            _time = 0f;
            _currentFrame = -1;
            _decoder = null;
            _loadedAsset = null;
            _animationName = null;
            _lastError = null;
            DisposeTexture();

            Texture2D texture = null;
            try
            {
                var decoder = new QAnimatorDecoder(data);
                texture = new Texture2D(
                    decoder.Width,
                    decoder.Height,
                    TextureFormat.RGBA32,
                    mipChain: false,
                    linear: false)
                {
                    name = "QEmoGraphic Playback Texture",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = _filterMode,
                    hideFlags = HideFlags.DontSave,
                };

                decoder.DecodeFrame(0);
                texture.LoadRawTextureData(decoder.RgbaBuffer);
                texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                _decoder = decoder;
                _texture = texture;
                _currentFrame = 0;
                _loadedAsset = loadedAsset;
                _animationName = string.IsNullOrEmpty(animationName) ? "Default" : animationName;
                texture = null;
                SetVerticesDirty();
                SetMaterialDirty();
            }
            finally
            {
                if (texture != null)
                    DestroyTexture(texture);
            }
        }

        private void Advance(float delta)
        {
            if (!_isPlaying || _decoder == null || _speed <= 0f || delta <= 0f)
                return;

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
                throw new InvalidOperationException("QEmoGraphic has no animation loaded.");
        }

        private TextAsset FindAssignedAnimation(string animationName)
        {
            if (_source != null && string.Equals(_source.name, animationName, StringComparison.Ordinal))
                return _source;

            if (_additionalAnimations == null)
                return null;

            for (int i = 0; i < _additionalAnimations.Length; i++)
            {
                TextAsset asset = _additionalAnimations[i];
                if (asset != null && string.Equals(asset.name, animationName, StringComparison.Ordinal))
                    return asset;
            }

            return null;
        }

        private TextAsset GetSelectedAnimation()
        {
            TextAsset selected = string.IsNullOrEmpty(_animation)
                ? null
                : FindAssignedAnimation(_animation);
            return selected != null ? selected : _source;
        }

        private void TryLoadSelectedAnimation()
        {
            TextAsset asset = GetSelectedAnimation();
            if (asset == null)
                return;

            try
            {
                _animation = asset.name;
                LoadAssignedAsset(asset);
            }
            catch (Exception exception)
            {
                _lastError = exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private void DisposeTexture()
        {
            if (_texture == null)
                return;

            Texture2D texture = _texture;
            _texture = null;
            SetVerticesDirty();
            SetMaterialDirty();
            DestroyTexture(texture);
        }

        private static void DestroyTexture(Texture2D texture)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(texture);
            else
#endif
                Destroy(texture);
        }

#if UNITY_EDITOR
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void ReloadEditorPreview()
        {
            if (Application.isPlaying)
                return;

            if (GetSelectedAnimation() == null)
                Unload();
            else
                TryLoadSelectedAnimation();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void UpdateEditorPreview(float deltaTime)
        {
            if (!Application.isPlaying)
                Advance(deltaTime);
        }
#endif
    }
}
