using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class FreeCameraController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 12f;
    [SerializeField] private float sprintMultiplier = 3f;
    [SerializeField] private float lookSensitivity = 2f;
    [SerializeField] private Key toggleMouseCaptureKey = Key.Escape;

    private float _yaw;
    private float _pitch;
    private bool _isMouseCaptured;
    private bool _ignoreNextMouseDelta;

    private void OnEnable()
    {
        Vector3 eulerAngles = transform.eulerAngles;
        _yaw = eulerAngles.y;
        _pitch = NormalizeAngle(eulerAngles.x);
        SetMouseCapture(true);
    }

    private void OnDisable()
    {
        SetMouseCapture(false);
    }

    private void Update()
    {
        UpdateMouseCaptureToggle();
        UpdateRotation();
        UpdateMovement();
    }

    private void UpdateMouseCaptureToggle()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        KeyControl toggleKey = keyboard[toggleMouseCaptureKey];
        if (toggleKey != null && toggleKey.wasPressedThisFrame)
        {
            SetMouseCapture(!_isMouseCaptured);
        }
    }

    private void UpdateRotation()
    {
        if (!_isMouseCaptured)
        {
            return;
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 mouseDelta = mouse.delta.ReadValue();
        if (_ignoreNextMouseDelta)
        {
            _ignoreNextMouseDelta = false;
            return;
        }

        float mouseX = mouseDelta.x;
        float mouseY = mouseDelta.y;

        _yaw += mouseX * lookSensitivity;
        _pitch -= mouseY * lookSensitivity;
        _pitch = Mathf.Clamp(_pitch, -89f, 89f);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void UpdateMovement()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        float currentSpeed = moveSpeed;
        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
        {
            currentSpeed *= sprintMultiplier;
        }

        Vector3 moveDirection = Vector3.zero;
        if (keyboard.wKey.isPressed)
        {
            moveDirection += transform.forward;
        }

        if (keyboard.sKey.isPressed)
        {
            moveDirection -= transform.forward;
        }

        if (keyboard.dKey.isPressed)
        {
            moveDirection += transform.right;
        }

        if (keyboard.aKey.isPressed)
        {
            moveDirection -= transform.right;
        }

        if (keyboard.spaceKey.isPressed)
        {
            moveDirection += Vector3.up;
        }

        if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed)
        {
            moveDirection -= Vector3.up;
        }

        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        transform.position += moveDirection * (currentSpeed * Time.deltaTime);
    }

    private static float NormalizeAngle(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }

    private void SetMouseCapture(bool captureMouse)
    {
        _isMouseCaptured = captureMouse;
        _ignoreNextMouseDelta = captureMouse;
        Cursor.lockState = captureMouse ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captureMouse;
    }
}
