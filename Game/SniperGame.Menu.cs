using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void OpenDistanceMenu()
    {
        _distanceMenuOpen = true;
        _draggingDistanceSlider = false;
        _draggingScopeSlider = false;
        _draggingWindSlider = false;
        _menuDistanceMeters = (int)ActiveSceneDistanceMeters;
        _menuScene = _activeScene;
        _menuScopeMagnificationIndex = _scopeMagnificationIndex;
        _menuWindLimitMps = _activeWindLimitMps;
        _menuWeaponIndex = _activeWeaponIndex;
        _ignoreNextMouseMove = true;
        CursorState = CursorState.Normal;

        // Ставим видимый курсор на текущее положение бегунка.
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = (_menuDistanceMeters - MinTargetDistance) / (float)(MaxTargetDistance - MinTargetDistance);
        float knobX = layout.SliderLeft + (layout.SliderRight - layout.SliderLeft) * t;
        MousePosition = new Vector2(knobX, layout.DistanceSliderY);

        UpdateWindowTitle();
    }

    private void CloseDistanceMenu()
    {
        _distanceMenuOpen = false;
        _draggingDistanceSlider = false;
        _draggingScopeSlider = false;
        _draggingWindSlider = false;
        _ignoreNextMouseMove = true;
        CursorState = CursorState.Grabbed;
        UpdateWindowTitle();
    }

    private void HandleDistanceMenuMouseDown(Vector2 mouse)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();

        if (PointInRect(mouse, layout.Scene1Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight))
        {
            _menuScene = SceneType.RangeCube;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Scene2Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight))
        {
            _menuScene = SceneType.DestructibleHouse;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Scene3Left, layout.SceneTop, layout.SceneButtonWidth, layout.SceneButtonHeight))
        {
            _menuScene = SceneType.SteelPlate;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon1Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 0;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon2Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 1;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon3Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 2;
            UpdateWindowTitle();
            return;
        }

        if (PointInRect(mouse, layout.Weapon4Left, layout.WeaponTop, layout.WeaponButtonWidth, layout.WeaponButtonHeight))
        {
            _menuWeaponIndex = 3;
            UpdateWindowTitle();
            return;
        }

        // Увеличенная зона клика делает ползунки удобными даже на высоком DPI.
        if (mouse.X >= layout.SliderLeft - 14f && mouse.X <= layout.SliderRight + 14f &&
            mouse.Y >= layout.DistanceSliderY - 22f && mouse.Y <= layout.DistanceSliderY + 22f)
        {
            _draggingDistanceSlider = true;
            _draggingScopeSlider = false;
            _draggingWindSlider = false;
            UpdateMenuDistanceFromMouse(mouse.X);
            return;
        }

        if (mouse.X >= layout.SliderLeft - 14f && mouse.X <= layout.SliderRight + 14f &&
            mouse.Y >= layout.ScopeSliderY - 22f && mouse.Y <= layout.ScopeSliderY + 22f)
        {
            _draggingScopeSlider = true;
            _draggingDistanceSlider = false;
            _draggingWindSlider = false;
            UpdateMenuScopeFromMouse(mouse.X);
            return;
        }

        if (mouse.X >= layout.SliderLeft - 14f && mouse.X <= layout.SliderRight + 14f &&
            mouse.Y >= layout.WindSliderY - 22f && mouse.Y <= layout.WindSliderY + 22f)
        {
            _draggingWindSlider = true;
            _draggingDistanceSlider = false;
            _draggingScopeSlider = false;
            UpdateMenuWindFromMouse(mouse.X);
            return;
        }

        if (PointInRect(mouse, layout.ApplyLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight))
        {
            ApplyDistanceMenu();
            return;
        }

        if (PointInRect(mouse, layout.CancelLeft, layout.ButtonTop, layout.ButtonWidth, layout.ButtonHeight))
        {
            CloseDistanceMenu();
        }
    }

    private void UpdateMenuDistanceFromMouse(float mouseX)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = MathHelper.Clamp(
            (mouseX - layout.SliderLeft) / (layout.SliderRight - layout.SliderLeft),
            0f,
            1f);

        float rawDistance = MinTargetDistance + t * (MaxTargetDistance - MinTargetDistance);
        int snapped = (int)MathF.Round(rawDistance / TargetDistanceStep) * TargetDistanceStep;
        _menuDistanceMeters = Math.Clamp(snapped, MinTargetDistance, MaxTargetDistance);
        UpdateWindowTitle();
    }

    private void UpdateMenuScopeFromMouse(float mouseX)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = MathHelper.Clamp(
            (mouseX - layout.SliderLeft) / (layout.SliderRight - layout.SliderLeft),
            0f,
            1f);

        int index = (int)MathF.Round(t * (ScopeMagnifications.Length - 1));
        _menuScopeMagnificationIndex = Math.Clamp(index, 0, ScopeMagnifications.Length - 1);
        UpdateWindowTitle();
    }

    private void UpdateMenuWindFromMouse(float mouseX)
    {
        DistanceMenuLayout layout = GetDistanceMenuLayout();
        float t = MathHelper.Clamp(
            (mouseX - layout.SliderLeft) / (layout.SliderRight - layout.SliderLeft),
            0f,
            1f);

        float rawWind = MinWindLimitMps + t * (MaxWindLimitMps - MinWindLimitMps);
        int snapped = (int)MathF.Round(rawWind / WindLimitStepMps) * WindLimitStepMps;
        _menuWindLimitMps = Math.Clamp(snapped, MinWindLimitMps, MaxWindLimitMps);

        // Ползунок меняет только значение в меню. Физический лимит становится
        // активным только после APPLY, поэтому M/Esc/CANCEL не создают
        // скрытого возврата к старому ограничению.
        UpdateWindowTitle();
    }

    private static bool PointInRect(Vector2 p, float x, float y, float width, float height)
    {
        return p.X >= x && p.X <= x + width && p.Y >= y && p.Y <= y + height;
    }

    private DistanceMenuLayout GetDistanceMenuLayout()
    {
        float panelWidth = MathF.Min(650f, ClientSize.X - 40f);
        float panelHeight = MathF.Min(690f, ClientSize.Y - 20f);
        float left = (ClientSize.X - panelWidth) * 0.5f;
        float top = (ClientSize.Y - panelHeight) * 0.5f;
        float right = left + panelWidth;
        float bottom = top + panelHeight;

        float sliderLeft = left + 72f;
        float sliderRight = right - 72f;

        const float sceneGap = 14f;
        float sceneButtonWidth = (sliderRight - sliderLeft - sceneGap * 2f) / 3f;
        const float sceneButtonHeight = 36f;
        float scene1Left = sliderLeft;
        float scene2Left = scene1Left + sceneButtonWidth + sceneGap;
        float scene3Left = scene2Left + sceneButtonWidth + sceneGap;
        float sceneTop = top + 62f;

        const float weaponGap = 10f;
        float weaponButtonWidth = (sliderRight - sliderLeft - weaponGap * 3f) / 4f;
        const float weaponButtonHeight = 38f;
        float weapon1Left = sliderLeft;
        float weapon2Left = weapon1Left + weaponButtonWidth + weaponGap;
        float weapon3Left = weapon2Left + weaponButtonWidth + weaponGap;
        float weapon4Left = weapon3Left + weaponButtonWidth + weaponGap;
        float weaponTop = top + 145f;

        float distanceSliderY = top + 300f;
        float scopeSliderY = top + 410f;
        float windSliderY = top + 520f;

        const float buttonWidth = 150f;
        const float buttonHeight = 36f;
        const float buttonGap = 22f;
        float buttonsTotal = buttonWidth * 2f + buttonGap;
        float applyLeft = (ClientSize.X - buttonsTotal) * 0.5f;
        float cancelLeft = applyLeft + buttonWidth + buttonGap;
        float buttonTop = bottom - 48f;

        return new DistanceMenuLayout(
            left, top, right, bottom,
            sliderLeft, sliderRight,
            scene1Left, scene2Left, scene3Left, sceneTop, sceneButtonWidth, sceneButtonHeight,
            weapon1Left, weapon2Left, weapon3Left, weapon4Left, weaponTop, weaponButtonWidth, weaponButtonHeight,
            distanceSliderY, scopeSliderY, windSliderY,
            applyLeft, cancelLeft, buttonTop, buttonWidth, buttonHeight);
    }

    private readonly record struct DistanceMenuLayout(
        float Left,
        float Top,
        float Right,
        float Bottom,
        float SliderLeft,
        float SliderRight,
        float Scene1Left,
        float Scene2Left,
        float Scene3Left,
        float SceneTop,
        float SceneButtonWidth,
        float SceneButtonHeight,
        float Weapon1Left,
        float Weapon2Left,
        float Weapon3Left,
        float Weapon4Left,
        float WeaponTop,
        float WeaponButtonWidth,
        float WeaponButtonHeight,
        float DistanceSliderY,
        float ScopeSliderY,
        float WindSliderY,
        float ApplyLeft,
        float CancelLeft,
        float ButtonTop,
        float ButtonWidth,
        float ButtonHeight);

    private void ApplyDistanceMenu()
    {
        _menuDistanceMeters = Math.Clamp(
            (_menuDistanceMeters / TargetDistanceStep) * TargetDistanceStep,
            MinTargetDistance,
            MaxTargetDistance);

        bool sceneChanged = _activeScene != _menuScene;
        _activeScene = _menuScene;
        SetActiveSceneDistance(_menuDistanceMeters);
        if (sceneChanged)
            ResetActiveDestructibleDamage();

        _scopeMagnificationIndex = Math.Clamp(_menuScopeMagnificationIndex, 0, ScopeMagnifications.Length - 1);
        _menuWindLimitMps = Math.Clamp(_menuWindLimitMps, MinWindLimitMps, MaxWindLimitMps);
        _activeWindLimitMps = _menuWindLimitMps;
        _menuWeaponIndex = Math.Clamp(_menuWeaponIndex, 0, _weapons.Length - 1);
        _activeWeaponIndex = _menuWeaponIndex;
        RecalculateZeroing();
        RecalculateScopeRangeMarks();

        // Старые пули относятся к прежней геометрии полигона или оружию.
        _bullets.Clear();
        _trackedBullet = null;
        _ballisticAccumulator = 0f;
        _automaticFireCooldown = 0f;
        _lastMuzzleVelocity = 0f;
        _lastFlightTime = 0f;
        _lastImpactSpeed = 0f;
        _lastImpactEnergy = 0f;
        _impactPredictionEnabled = false;
        _impactPredictionLockedRangeMeters = 0f;
        _hasPredictedImpact = false;
        _lastShot = $"{ActiveWeapon.DisplayName} | {ActiveSceneHudName} | RANGE {_menuDistanceMeters} M";

        CloseDistanceMenu();
    }

    private void RestoreActiveDestructible()
    {
        // Полностью восстанавливаем текущий разрушаемый объект и удаляем его обломки.
        ResetActiveDestructibleDamage();

        // Пули, уже находящиеся в воздухе, удаляем, чтобы сразу после восстановления
        // они не повредили свежий куб старым выстрелом.
        _bullets.Clear();
        _trackedBullet = null;
        _ballisticAccumulator = 0f;
        _automaticFireCooldown = 0f;

        _lastMuzzleVelocity = 0f;
        _lastFlightTime = 0f;
        _lastImpactSpeed = 0f;
        _lastImpactEnergy = 0f;
        _lastShot = $"{ActiveSceneHudName} RESTORED";
        _hasPredictedImpact = false;
        _impactPredictionRefreshTimer = ImpactPredictionRefreshSeconds;

        UpdateWindowTitle();
    }
}
