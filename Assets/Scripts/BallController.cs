using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class BallController : MonoBehaviour
{
    public float jumpForce = 5f;
    public float maxY = 4.5f;
    public float minY = -5f;
    private Rigidbody2D rb;
    private bool isAlive = true;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        if (!isAlive) return;

        #if ENABLE_INPUT_SYSTEM
        if (IsPrimaryPressThisFrame())
        {
            rb.linearVelocity = Vector2.up * jumpForce;
        }
        #else
        if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
        {
            rb.linearVelocity = Vector2.up * jumpForce;
        }
        #endif

        // Ограничение по высоте
        Vector3 pos = transform.position;
        if (pos.y > maxY)
        {
            pos.y = maxY;
            transform.position = pos;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0);
        }

        // Проверка на падение ниже экрана
        if (pos.y < minY && isAlive)
        {
            isAlive = false;
            GameManager.Instance.GameOver();
        }
    }

    #if ENABLE_INPUT_SYSTEM
    private static bool IsPrimaryPressThisFrame()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;

        if (Touchscreen.current != null)
        {
            var touches = Touchscreen.current.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].press.wasPressedThisFrame) return true;
            }
        }

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;

        return false;
    }
    #endif

    private void OnCollisionEnter2D(Collision2D collision)
    {
        isAlive = false;
        GameManager.Instance.GameOver();
    }
} 