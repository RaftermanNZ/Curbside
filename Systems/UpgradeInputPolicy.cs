namespace MedianGrassReplacement.Systems
{
    internal static class UpgradeInputPolicy
    {
        internal static bool PointerBlocked => Game.Input.InputManager.instance.mouseOverUI || Game.Input.InputManager.instance.hasInputFieldFocus;
        internal static bool CancelPressed(bool action) => UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true || action && !RemovePressed(false);
        internal static bool RemovePressed(bool secondary) => secondary || UnityEngine.InputSystem.Mouse.current?.rightButton.wasPressedThisFrame == true;
    }
}
