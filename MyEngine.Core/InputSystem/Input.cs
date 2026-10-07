using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace MyEngine.Core.InputSystem;

public enum MouseButton { Left, Right, Middle }

/// <summary>
/// Static, per-frame-polled input state for keyboard, mouse, and touch. Call <see cref="Update"/> once
/// per frame — the host (the Editor while in Play Mode, or the standalone Runtime) is responsible for
/// this, the same way it's responsible for calling Scene.Update(); scripts never call Update() themselves.
///
/// Mouse and touch are unified under "Pointer" (IsPointerDown, PointerPosition, ...) for code that
/// doesn't care which one the player is using — write your player-tap-to-move logic once against
/// Pointer and it works with a mouse today and a finger on Android tomorrow, unchanged. Reach for the
/// Mouse-prefixed or Touch-prefixed members only when you specifically need mouse-only or multi-touch behavior.
/// </summary>
public static class Input
{
    private static KeyboardState _keyboard;
    private static KeyboardState _previousKeyboard;
    private static MouseState _mouse;
    private static MouseState _previousMouse;
    private static TouchCollection _touches;

    /// <summary>Refreshes all input state for this frame. Must be called exactly once per frame, before
    /// any script's OnUpdate runs, so IsKeyPressed/IsPointerPressed-style "this frame only" queries work.</summary>
    public static void Update()
    {
        _previousKeyboard = _keyboard;
        _keyboard = Keyboard.GetState();

        _previousMouse = _mouse;
        _mouse = Mouse.GetState();

        _touches = TouchPanel.GetState();
    }

    // ---------------------------------------------------------------- keyboard

    public static bool IsKeyDown(Keys key) => _keyboard.IsKeyDown(key);
    public static bool IsKeyUp(Keys key) => _keyboard.IsKeyUp(key);
    public static bool IsKeyPressed(Keys key) => _keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
    public static bool IsKeyReleased(Keys key) => _keyboard.IsKeyUp(key) && _previousKeyboard.IsKeyDown(key);

    // ---------------------------------------------------------------- pointer (mouse OR touch, unified)

    /// <summary>True while a touch is active — when true, Pointer* members read from the primary touch;
    /// otherwise they read from the mouse.</summary>
    public static bool HasActiveTouch => _touches.Count > 0;

    public static Vector2 PointerPosition => HasActiveTouch ? _touches[0].Position : MousePosition;

    public static bool IsPointerDown => HasActiveTouch || _mouse.LeftButton == ButtonState.Pressed;

    public static bool IsPointerPressed =>
        HasActiveTouch ? _touches[0].State == TouchLocationState.Pressed : IsMouseButtonPressed(MouseButton.Left);

    public static bool IsPointerReleased =>
        (HasActiveTouch && _touches[0].State == TouchLocationState.Released) ||
        (!HasActiveTouch && IsMouseButtonReleased(MouseButton.Left));

    // ---------------------------------------------------------------- mouse

    public static Vector2 MousePosition => new(_mouse.X, _mouse.Y);
    public static Vector2 MouseDelta => new(_mouse.X - _previousMouse.X, _mouse.Y - _previousMouse.Y);

    /// <summary>Scroll wheel change this frame, in "notches" (120 raw units each — one typical wheel click).</summary>
    public static float ScrollDelta => (_mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue) / 120f;

    public static bool IsMouseButtonDown(MouseButton button) => GetButtonState(_mouse, button) == ButtonState.Pressed;
    public static bool IsMouseButtonUp(MouseButton button) => GetButtonState(_mouse, button) == ButtonState.Released;

    public static bool IsMouseButtonPressed(MouseButton button) =>
        GetButtonState(_mouse, button) == ButtonState.Pressed && GetButtonState(_previousMouse, button) == ButtonState.Released;

    public static bool IsMouseButtonReleased(MouseButton button) =>
        GetButtonState(_mouse, button) == ButtonState.Released && GetButtonState(_previousMouse, button) == ButtonState.Pressed;

    private static ButtonState GetButtonState(in MouseState state, MouseButton button) => button switch
    {
        MouseButton.Left => state.LeftButton,
        MouseButton.Right => state.RightButton,
        MouseButton.Middle => state.MiddleButton,
        _ => ButtonState.Released,
    };

    // ---------------------------------------------------------------- touch (multi-touch access)

    public static int TouchCount => _touches.Count;

    public static Vector2 GetTouchPosition(int index) => _touches[index].Position;

    public static TouchLocationState GetTouchState(int index) => _touches[index].State;
}
