using UnityEngine;

namespace QAnimator.Unity
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class QAnimatorSpriteRendererOutput : MonoBehaviour
    {
        [SerializeField] private QAnimatorPlayer _player;
        [SerializeField, Min(0.01f)] private float _pixelsPerUnit = 100f;
        [SerializeField] private Vector2 _pivot = new(0.5f, 0.5f);

        private SpriteRenderer _renderer;
        private Sprite _sprite;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_player == null)
                _player = GetComponent<QAnimatorPlayer>();
        }

        private void OnEnable()
        {
            if (_player == null) return;
            _player.OnTextureCreated += HandleTextureCreated;
            _player.OnTextureReleased += HandleTextureReleased;
            if (_player.Texture != null)
                HandleTextureCreated(_player.Texture);
        }

        private void OnDisable()
        {
            if (_player == null) return;
            _player.OnTextureCreated -= HandleTextureCreated;
            _player.OnTextureReleased -= HandleTextureReleased;
        }

        private void HandleTextureCreated(Texture2D texture)
        {
            DisposeSprite(clearRenderer: false);
            _sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                _pivot,
                _pixelsPerUnit,
                0,
                SpriteMeshType.FullRect);
            _sprite.name = "QAnimator Playback Sprite";
            _renderer.sprite = _sprite;
        }

        private void HandleTextureReleased()
        {
            DisposeSprite(clearRenderer: true);
        }

        private void OnDestroy()
        {
            DisposeSprite(clearRenderer: true);
        }

        private void DisposeSprite(bool clearRenderer)
        {
            if (clearRenderer && _renderer != null)
                _renderer.sprite = null;
            if (_sprite == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(_sprite);
            else
#endif
                Destroy(_sprite);
            _sprite = null;
        }
    }
}
