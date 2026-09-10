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
            if (_player.Texture != null)
                HandleTextureCreated(_player.Texture);
        }

        private void OnDisable()
        {
            if (_player != null)
                _player.OnTextureCreated -= HandleTextureCreated;
        }

        private void HandleTextureCreated(Texture2D texture)
        {
            DisposeSprite();
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

        private void OnDestroy()
        {
            DisposeSprite();
        }

        private void DisposeSprite()
        {
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
