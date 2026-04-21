using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

[RequireComponent(typeof(Camera))]
public class FreeCam : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    public float fastSpeedMultiplier = 3f;
    public float sensitivity = 2f;
    public float climbSpeed = 5f;

    [Header("Control Options")]
    public bool lockCursor = true;
    public KeyCode fastKey = KeyCode.LeftShift;
    public KeyCode upKey = KeyCode.E;
    public KeyCode downKey = KeyCode.Q;

    private float rotationX;
    private float rotationY;

    public bool BlockMovement = false;
    
    private VisualElement _ui;
    private Keyboard _keyboard;

    void Start()
    {
        _keyboard = Keyboard.current;
        
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        if (BlockMovement)
            return;
        
        if (_keyboard != null && _keyboard.f11Key.wasPressedThisFrame)
        {
            BlockMovement = !BlockMovement;
        }

        HandleMouseLook();
        HandleMovement();
        
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity;

        rotationX += mouseX;
        rotationY -= mouseY;
        rotationY = Mathf.Clamp(rotationY, -89f, 89f);

        transform.rotation = Quaternion.Euler(rotationY, rotationX, 0f);
    }

    void HandleMovement()
    {
        float speed = moveSpeed;
        if (Input.GetKey(fastKey))
            speed *= fastSpeedMultiplier;

        Vector3 move = new Vector3(
            Input.GetAxis("Horizontal"),
            0,
            Input.GetAxis("Vertical")
        );

        Vector3 moveDir = transform.TransformDirection(move);

        if (Input.GetKey(upKey))
            moveDir.y += 1f;
        if (Input.GetKey(downKey))
            moveDir.y -= 1f;

        transform.position += moveDir * (speed * Time.deltaTime);
    }
}
