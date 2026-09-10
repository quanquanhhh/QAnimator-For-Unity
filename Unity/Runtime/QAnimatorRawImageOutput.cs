using UnityEngine;
using UnityEngine.UI;

namespace QAnimator.Unity
{
    [RequireComponent(typeof(RawImage))]
    public sealed class QAnimatorRawImageOutput : MonoBehaviour
    {
        [SerializeField] private QAnimatorPlayer _player;
        private RawImage _rawImage;

        private void Awake()
        {
            _rawImage = GetComponent<RawImage>();
            if (_player == null)
                _player = GetComponent<QAnimatorPlayer>();
        }

        private void OnEnable()
        {
            if (_player == null) return;
            _player.OnTextureCreated += HandleTextureCreated;
            _player.OnTextureReleased += HandleTextureReleased;
            _rawImage.texture = _player.Texture;
        }

        private void OnDisable()
        {
            if (_player == null) return;
            _player.OnTextureCreated -= HandleTextureCreated;
            _player.OnTextureReleased -= HandleTextureReleased;
        }

        private void HandleTextureCreated(Texture2D texture)
        {
            if (_rawImage != null)
                _rawImage.texture = texture;
        }

        private void HandleTextureReleased()
        {
            if (_rawImage != null)
                _rawImage.texture = null;
        }
    }
}
