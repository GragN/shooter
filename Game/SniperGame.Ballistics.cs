using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Sniper3D;

internal sealed partial class SniperGame
{
    private void UpdateAimSway(float dt, bool aiming)
    {
        if (_stabilizationEnabled)
        {
            _aimSwayBlend = 0f;
            _aimSwayYaw = 0f;
            _aimSwayPitch = 0f;
            return;
        }

        _aimSwayTime += dt;

        float blendTarget = aiming ? 1f : 0f;
        float blendAlpha = 1f - MathF.Exp(-AimSwayBlendResponse * dt);
        _aimSwayBlend += (blendTarget - _aimSwayBlend) * blendAlpha;

        // Несколько медленных гармоник с разными периодами дают плавное
        // неповторяющееся движение без резкого случайного джиттера.
        float t = _aimSwayTime;
        float yawSignal =
            0.62f * MathF.Sin(t * 0.62f + 0.35f) +
            0.25f * MathF.Sin(t * 1.07f + 2.10f) +
            0.13f * MathF.Sin(t * 0.21f + 4.40f);

        float pitchSignal =
            0.60f * MathF.Sin(t * 0.53f + 1.15f) +
            0.27f * MathF.Sin(t * 0.96f + 3.30f) +
            0.13f * MathF.Sin(t * 0.18f + 5.20f);

        _aimSwayYaw = AimSwayYawDegrees * yawSignal * _aimSwayBlend;
        _aimSwayPitch = AimSwayPitchDegrees * pitchSignal * _aimSwayBlend;
    }

    private void UpdateImpactPrediction(float dt, bool aiming)
    {
        if (!_impactPredictionEnabled || _impactPredictionLockedRangeMeters <= 0f ||
            !aiming || _distanceMenuOpen || TrackedBulletCamera is not null)
        {
            _hasPredictedImpact = false;
            _impactPredictionRefreshTimer = 0f;
            return;
        }

        _impactPredictionRefreshTimer += dt;
        if (_hasPredictedImpact && _impactPredictionRefreshTimer < ImpactPredictionRefreshSeconds)
            return;

        _impactPredictionRefreshTimer = 0f;
        _hasPredictedImpact = TryPredictImpactAtLockedRange(out _predictedImpactPoint);
    }

    /// <summary>
    /// При включении T один раз измеряет дистанцию по центральному лучу прицела.
    /// В дальнейшем эта дальность остаётся неизменной, пока пользователь не
    /// выключит T и не захватит новую цель. Это намеренно НЕ динамический
    /// дальномер.
    /// </summary>
    private bool TryAcquireImpactPredictionRange(out float lockedRangeMeters)
    {
        lockedRangeMeters = 0f;

        Vector3 aimDirection = AimFront;
        if (aimDirection.LengthSquared < 0.000001f)
            return false;
        aimDirection = Vector3.Normalize(aimDirection);

        Vector3 rayOrigin = _cameraPosition;
        const float maxLockDistanceMeters = 2600f;
        Vector3 rayEnd = rayOrigin + aimDirection * maxLockDistanceMeters;

        bool found = false;
        float nearestRayDistance = float.PositiveInfinity;
        Vector3 nearestPoint = default;

        void ConsiderPoint(Vector3 point)
        {
            float alongRay = Vector3.Dot(point - rayOrigin, aimDirection);
            if (alongRay <= 0.01f || alongRay >= nearestRayDistance)
                return;

            nearestRayDistance = alongRay;
            nearestPoint = point;
            found = true;
        }

        // Существующий материал. Разрушенное отверстие луч пропускает, поэтому
        // через него можно захватить более дальнюю стену.
        if (TryHitActiveDestructibleSegment(rayOrigin, rayEnd, out Vector3 concretePoint))
            ConsiderPoint(concretePoint);

        // Земля.
        if (aimDirection.Y < -0.000001f)
        {
            float groundT = -rayOrigin.Y / aimDirection.Y;
            if (groundT > 0f && groundT <= maxLockDistanceMeters)
                ConsiderPoint(rayOrigin + aimDirection * groundT);
        }

        if (!found)
            return false;

        // Реальная пуля стартует на 0,85 м впереди камеры. Храним именно
        // downrange-дистанцию от дульной точки до захваченной поверхности.
        Vector3 muzzlePosition = GetMuzzlePosition(aimDirection);
        lockedRangeMeters = Vector3.Dot(nearestPoint - muzzlePosition, aimDirection);
        lockedRangeMeters = MathF.Max(1f, lockedRangeMeters);
        return true;
    }

    /// <summary>
    /// Прогнозирует положение пули на ЗАРАНЕЕ ЗАХВАЧЕННОЙ дистанции.
    /// Геометрия мира больше не определяет дальность при каждом обновлении.
    /// При этом ветер семплируется заново при каждом обновлении прогноза и на
    /// каждом RK4-подшаге, поэтому изменение ветра постоянно двигает красную
    /// точку. Используется то же оружие, сопротивление и гравитация, что и у
    /// настоящего выстрела.
    /// </summary>
    private bool TryPredictImpactAtLockedRange(out Vector3 impactPoint)
    {
        impactPoint = default;
        if (_impactPredictionLockedRangeMeters <= 0f)
            return false;

        Vector3 aimDirection = AimFront;
        if (aimDirection.LengthSquared < 0.000001f)
            return false;
        aimDirection = Vector3.Normalize(aimDirection);

        Vector3 muzzlePosition = GetMuzzlePosition(aimDirection);
        Vector3 shotDirection = ShotFront;
        BallisticProfile ballistics = ActiveBallistics;

        var simulatedBullet = new Bullet(
            muzzlePosition,
            shotDirection * ballistics.MuzzleVelocityMps,
            ballistics);

        float simulationTime = _wind.TimeSeconds;
        int maxSteps = (int)MathF.Ceiling(20f / BallisticStepSeconds);

        for (int i = 0; i < maxSteps; i++)
        {
            simulatedBullet.Step(
                BallisticStepSeconds,
                _wind,
                simulationTime,
                _activeWindLimitMps);

            simulationTime += BallisticStepSeconds;

            Vector3 from = simulatedBullet.PreviousPosition;
            Vector3 to = simulatedBullet.Position;

            float fromDownrange = Vector3.Dot(from - muzzlePosition, aimDirection);
            float toDownrange = Vector3.Dot(to - muzzlePosition, aimDirection);

            if (toDownrange >= _impactPredictionLockedRangeMeters &&
                toDownrange > fromDownrange + 0.000001f)
            {
                float t = (_impactPredictionLockedRangeMeters - fromDownrange)
                    / (toDownrange - fromDownrange);
                t = MathHelper.Clamp(t, 0f, 1f);
                impactPoint = from + (to - from) * t;
                return true;
            }

            if (!simulatedBullet.IsAlive)
                break;
        }

        return false;
    }

    private void UpdateAutomaticFire(float dt)
    {
        // В режиме камеры пули после первого выстрела автоматический огонь
        // приостанавливается, чтобы камера не перескакивала между очередью.
        if (TrackedBulletCamera is not null)
        {
            _automaticFireCooldown = 0f;
            return;
        }

        if (!ActiveWeapon.IsAutomatic || ActiveWeapon.RateOfFireRpm <= 0f)
        {
            _automaticFireCooldown = 0f;
            return;
        }

        if (!MouseState.IsButtonDown(MouseButton.Left))
        {
            // После отпускания ЛКМ следующий зажим начинается с мгновенного выстрела.
            _automaticFireCooldown = 0f;
            return;
        }

        _automaticFireCooldown -= dt;
        float secondsPerShot = 60f / ActiveWeapon.RateOfFireRpm;

        // Safety ограничивает число выстрелов за один кадр после сильного лага.
        int safety = 0;
        while (_automaticFireCooldown <= 0f && safety < 3)
        {
            Fire();
            _automaticFireCooldown += secondsPerShot;
            safety++;
        }
    }

    private void Fire()
    {
        _shots++;

        // Пристрелка не двигает перекрестие: она меняет угол ствола относительно
        // линии визирования. На выбранной ZERO-дистанции без ветра траектория
        // пересекает центральную линию прицела.
        Vector3 sightDirection = AimFront;
        Vector3 shotDirection = ShotFront;
        Vector3 muzzlePosition = GetMuzzlePosition(sightDirection);
        BallisticProfile ballistics = ActiveBallistics;

        // Каждый реальный патрон немного отличается по начальной скорости.
        // ZERO/T/риски прицела остаются откалиброваны по номинальной средней,
        // а конкретный выстрел получает собственную гауссову вариацию.
        float actualMuzzleVelocityMps = ballistics.SampleMuzzleVelocity(_random);
        Vector3 muzzleVelocity = shotDirection * actualMuzzleVelocityMps;

        var bullet = new Bullet(muzzlePosition, muzzleVelocity, ballistics);
        _bullets.Add(bullet);

        if (_cameraMode == CameraMode.Bullet)
            _trackedBullet = bullet;

        _lastShot = "BULLET IN FLIGHT";

        // В обычном режиме отдача подбрасывает прицел вверх. В режиме O
        // отдача полностью отключена — ни одного углового смещения не добавляется.
        if (!_stabilizationEnabled)
            _targetPitch = MathHelper.Clamp(_targetPitch + ActiveWeapon.RecoilPitchDegrees, -75f, 75f);

        UpdateWindowTitle();
    }

    private void UpdateBullets(float frameDt)
    {
        _ballisticAccumulator += frameDt;

        // Ограничение защищает от спирали накопления после остановки в отладчике.
        int safetySteps = 0;
        while (_ballisticAccumulator >= BallisticStepSeconds && safetySteps < 80)
        {
            StepBallistics(BallisticStepSeconds);
            _ballisticAccumulator -= BallisticStepSeconds;
            safetySteps++;
        }

        if (safetySteps == 80)
            _ballisticAccumulator = 0f;

        _bullets.RemoveAll(b => !b.IsAlive);

        if (_trackedBullet is not null && (!_trackedBullet.IsAlive || _trackedBullet.HasImpacted))
            _trackedBullet = null;
    }

    private void StepBallistics(float dt)
    {
        // Ветер обновляется тем же фиксированным физическим шагом, что и пули.
        // Это делает порывы независимыми от FPS.
        float stepStartTime = _wind.TimeSeconds;
        _wind.Update(dt);

        foreach (Bullet bullet in _bullets)
        {
            if (!bullet.IsAlive)
                continue;

            bullet.Step(dt, _wind, stepStartTime, _activeWindLimitMps);

            if (bullet.HasImpacted)
                continue;

            ResolveBulletCollisions(bullet);
        }
    }

    private void ResolveBulletCollisions(Bullet bullet)
    {
        Vector3 from = bullet.PreviousPosition;
        Vector3 to = bullet.Position;

        // CCD: сначала находим ВСЕ потенциальные столкновения на последнем
        // отрезке физического шага, затем выбираем самое раннее по hitT.
        float concreteT = float.PositiveInfinity;
        Vector3 concreteImpact = default;
        bool hasConcrete = TryHitActiveDestructibleSegment(from, to, out concreteT, out concreteImpact,
            bullet.Profile.DiameterMeters * 0.5f);

        bool hasGround = false;
        float groundT = float.PositiveInfinity;
        Vector3 groundImpact = default;
        if (from.Y > 0f && to.Y <= 0f)
        {
            groundT = from.Y / (from.Y - to.Y);
            if (groundT >= 0f && groundT <= 1f)
            {
                hasGround = true;
                groundImpact = from + (to - from) * groundT;
            }
        }

        if (!hasConcrete && !hasGround)
            return;

        if (hasConcrete && (!hasGround || concreteT <= groundT))
        {
            // Возвращаем состояние пули к реальному моменту контакта, а не
            // используем скорость/время в конце 1-мс физического шага.
            bullet.MoveToSegmentFraction(concreteT);
            concreteImpact = bullet.Position;
            bool firstRegisteredHit = !bullet.ResultRegistered;
            bullet.ResultRegistered = true;

            ProjectileImpactData projectile = ProjectileImpactData.From(
                bullet.Profile,
                bullet.Velocity,
                bullet.MassKg);
            if (_activeScene == SceneType.SteelPlate)
            {
                RegisterSteelHit(bullet, concreteImpact, projectile, firstRegisteredHit);
                return;
            }
            ConcreteImpactResult impact = ApplyImpactToActiveDestructible(
                concreteImpact,
                projectile);

            RegisterConcreteHit(
                bullet,
                impact,
                concreteImpact,
                projectile.Direction,
                firstRegisteredHit);
            return;
        }

        // Земля оказалась ближе по тому же continuous segment.
        bullet.MoveToSegmentFraction(groundT);
        groundImpact = bullet.Position;
        if (!bullet.ResultRegistered)
        {
            bullet.ResultRegistered = true;
            RegisterMiss(bullet);
        }
        else
        {
            CaptureImpactTelemetry(bullet);
        }

        bullet.Impact(groundImpact);
        UpdateWindowTitle();
    }

    private void RegisterConcreteHit(
        Bullet bullet,
        ConcreteImpactResult impact,
        Vector3 impactPoint,
        Vector3 incomingDirection,
        bool countAsNewHit)
    {
        if (countAsNewHit)
            _hits++;
        CaptureImpactTelemetry(bullet);

        float integrityPercent = ActiveSceneIntegrity01 * 100f;
        string objectName = _activeScene == SceneType.RangeCube ? "CUBE" : "HOUSE";

        if (impact.Perforated && impact.ResidualSpeedMps > 5f)
        {
            _lastShot =
                $"{objectName} PERFORATED | PEN {impact.PenetrationDepthMeters * 100f:0.0} CM | " +
                $"EXIT {impact.ResidualSpeedMps:0} M/S | {integrityPercent:0}% LEFT";

            // Пуля не умирает: она выходит из бетона с остаточной энергией
            // и может попасть во вторую стену/перегородку/землю.
            bullet.ContinueAfterPenetration(incomingDirection, impact.ResidualSpeedMps);
        }
        else
        {
            _lastShot = impact.RemovedCellCount > 0
                ? $"{objectName} -{impact.RemovedCellCount} | PEN {impact.PenetrationDepthMeters * 100f:0.0} CM | DEBRIS {impact.SpawnedFragmentCount} | {integrityPercent:0}% LEFT"
                : $"{objectName} DAMAGED | PEN {impact.PenetrationDepthMeters * 100f:0.0} CM | {integrityPercent:0}% LEFT";

            bullet.Impact(impactPoint);
        }

        // Основной массив остаётся на месте; отделившиеся куски живут своей физикой.
        UpdateWindowTitle();
    }

    private void RegisterMiss(Bullet bullet)
    {
        CaptureImpactTelemetry(bullet);
        _lastShot = "MISS";
    }

    private void CaptureImpactTelemetry(Bullet bullet)
    {
        _lastMuzzleVelocity = bullet.InitialSpeedMps;
        _lastFlightTime = bullet.AgeSeconds;
        _lastImpactSpeed = bullet.SpeedMps;
        _lastImpactEnergy = bullet.KineticEnergyJoules;
    }

    private bool TryHitActiveDestructibleSegment(Vector3 from, Vector3 to, out Vector3 hitPoint)
        => TryHitActiveDestructibleSegment(from, to, out _, out hitPoint);

    private bool TryHitActiveDestructibleSegment(
        Vector3 from,
        Vector3 to,
        out float hitT,
        out Vector3 hitPoint,
        float bulletRadius = 0f)
    {
        return _activeScene switch
        {
            SceneType.RangeCube => _target.TryHitSegment(from, to, out hitT, out hitPoint),
            SceneType.DestructibleHouse => _house.TryHitSegment(from, to, out hitT, out hitPoint),
            _ => _steel.TryHitSegment(from, to, out hitT, out hitPoint, bulletRadius)
        };
    }

    private ConcreteImpactResult ApplyImpactToActiveDestructible(
        Vector3 hitPoint,
        ProjectileImpactData projectile)
    {
        return _activeScene == SceneType.RangeCube
            ? _target.ApplyImpact(hitPoint, projectile)
            : _house.ApplyImpact(hitPoint, projectile);
    }

    private void SetActiveSceneDistance(float distanceMeters)
    {
        if (_activeScene == SceneType.RangeCube)
            _target.SetDistance(distanceMeters, _cameraPosition.Z);
        else if (_activeScene == SceneType.DestructibleHouse)
            _house.SetDistance(distanceMeters, _cameraPosition.Z);
        else
            _steel.SetDistance(distanceMeters, _cameraPosition.Z);
    }

    private void ResetActiveDestructibleDamage()
    {
        if (_activeScene == SceneType.RangeCube)
            _target.ResetDamage();
        else if (_activeScene == SceneType.DestructibleHouse)
            _house.ResetDamage();
        else
            _steel.ResetDamage();
    }
}
