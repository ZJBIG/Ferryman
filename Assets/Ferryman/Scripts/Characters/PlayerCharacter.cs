using UnityEngine;

namespace Ferryman.Characters
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class PlayerCharacter : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float moveSpeed = 4f;

        private Rigidbody2D body;
        private Vector2 moveInput;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        private void Update()
        {
            moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }

        private void FixedUpdate()
        {
            Move(moveInput);
        }

        // Apply movement during a physics step; collisions are resolved by Unity.
        public void Move(Vector2 direction)
        {
            body.velocity = Vector2.ClampMagnitude(direction, 1f) * moveSpeed;
        }

        private void OnDisable()
        {
            moveInput = Vector2.zero;
            if (body != null)
                body.velocity = Vector2.zero;
        }
    }
}
