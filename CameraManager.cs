using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;

namespace p4g64.mouselook;

public class CameraManager
{
    private readonly IReloadedHooks _hooks;
    private readonly Config _config;

    // Native addresses in P4G.exe x64 (ASLR is disabled in P4G)
    private const long FieldObjGlobalAddr          = 0x1411AB2C8L; // Field object (Yu / 3D map active)
    private const long TargetPosAddr               = 0x1411ACD10L; // Focus target position (X, Y, Z)
    private const long CamGlobalPtrAddr            = 0x1462487C0L; // Global camera context pointer
    private const long CamRotAddr                  = 0x1402D4C80L; // Horizontal yaw rotation
    private const long MoveTickAddr                = 0x1402D53A0L; // Field camera frame tick
    private const long CamLookAtAddr               = 0x1405143B0L; // Native Camera::LookAt(ctx, eye, target, up)
    private const long MovieObjPtrAddr             = 0x14624C7D0L; // Cutscene / Movie player object
    private const long InteractiveDialogFlagAddr   = 0x1451FE648L; // Bit 1 (0x2) is set ONLY for interactive story dialogues/choices (NOT dungeon banter)
    private const long CampMenuPtrAddr             = 0x140EC0A40L; // Camp menu object
    private const long BattleMgrPtrAddr            = 0x140EC08F0L; // Battle manager object

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CamRotDelegate(long fieldObj, float delta);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MoveTickDelegate(nint param1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CamLookAtDelegate(nint pCamGlobal, nint pEyePos, nint pTargetPos, nint pUpVector);

    private CamRotDelegate? _camRot;
    private CamLookAtDelegate? _camLookAt;
    private IHook<MoveTickDelegate>? _moveTickHook;

    private float _pendingDeltaX = 0f;
    private float _pendingDeltaY = 0f;
    private readonly object _deltaLock = new();

    // Pitch offset (in radians relative to base camera)
    private float _currentPitch = 0f;
    private const float MinPitchOffset = -0.08f; // Safe clamp: approx -4.6 degrees (tilt up)
    private const float MaxPitchOffset =  0.06f; // Safe clamp: approx +3.4 degrees (tilt down)

    private long _lastMoveTickTime = 0;
    private long _lastInteractiveDialogTick = 0;

    public bool IsInField
    {
        get
        {
            // If MoveTick hasn't executed in the last 150ms, the 3D field is NOT running!
            // (E.g. Title screen, Quick Save exit, Loading screens, etc.)
            if ((Environment.TickCount64 - _lastMoveTickTime) > 150)
                return false;

            try
            {
                unsafe
                {
                    long* ptr = (long*)FieldObjGlobalAddr;
                    return ptr != null && *ptr != 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsCutsceneOrMoviePlaying
    {
        get
        {
            try
            {
                unsafe
                {
                    long* ptr = (long*)MovieObjPtrAddr;
                    return ptr != null && *ptr != 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsDialogueActive
    {
        get
        {
            try
            {
                unsafe
                {
                    ushort* ptr = (ushort*)InteractiveDialogFlagAddr;
                    if (ptr != null && (*ptr & 2) != 0)
                    {
                        _lastInteractiveDialogTick = Environment.TickCount64;
                    }
                }
            }
            catch { }

            // 200ms hold debounce so microsecond redraws never cause cursor flicker
            return (Environment.TickCount64 - _lastInteractiveDialogTick) < 200;
        }
    }

    /// <summary>
    /// Checks if P4G's native field camera allows rotation right now.
    /// In native CamRot (0x1402D4C80): mode 0, 6, 7 are free exploration modes where camera rotation is allowed.
    /// During interactive events / story cutscenes / choice prompts, mode is an event mode (not 0, 6, 7).
    /// </summary>
    public bool IsNativeCameraFree
    {
        get
        {
            try
            {
                unsafe
                {
                    long* pFieldObj = (long*)FieldObjGlobalAddr;
                    if (pFieldObj == null || *pFieldObj == 0) return false;

                    nint fieldObj = (nint)(*pFieldObj);
                    nint camControl = *(nint*)(fieldObj + 0x48);
                    if (camControl == nint.Zero) return false;

                    int mode = *(int*)(camControl + 8);
                    return mode == 0 || mode == 6 || mode == 7;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Distinguishes non-blocking walking banter (Rise/Teddie voice lines, chest discovery)
    /// from real blocking story dialogues / choices / NPC conversations.
    /// When walking in dungeons, IsNativeCameraFree is TRUE, so this returns FALSE (mouse stays locked).
    /// In story events / choices, IsNativeCameraFree is FALSE, so this returns TRUE (mouse unlocks).
    /// </summary>
    public bool IsInteractiveDialogue
    {
        get
        {
            return IsDialogueActive && !IsNativeCameraFree;
        }
    }

    public bool IsCampMenuOpen
    {
        get
        {
            try
            {
                unsafe
                {
                    nint* ptr = (nint*)CampMenuPtrAddr;
                    return ptr != null && *ptr != 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsInBattle
    {
        get
        {
            try
            {
                unsafe
                {
                    nint* ptr = (nint*)BattleMgrPtrAddr;
                    return ptr != null && *ptr != 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public volatile bool IsLooking = false;
    private bool _mainThreadCursorHidden = false;

    public bool CanRotateCamera
    {
        get
        {
            if (!IsInField) return false;
            if (_config.AutoUnlockInCutscene && IsCutsceneOrMoviePlaying) return false;
            if (_config.AutoUnlockInDialogue && IsInteractiveDialogue) return false;
            if (_config.AutoUnlockInMenu && IsCampMenuOpen) return false;
            if (_config.AutoUnlockInBattle && IsInBattle) return false;
            return true;
        }
    }

    public CameraManager(IReloadedHooks hooks, Config config)
    {
        _hooks = hooks;
        _config = config;

        InitHooks();
    }

    private void InitHooks()
    {
        try
        {
            _camRot = _hooks.CreateWrapper<CamRotDelegate>(unchecked((nint)CamRotAddr), out _);
            _camLookAt = _hooks.CreateWrapper<CamLookAtDelegate>(unchecked((nint)CamLookAtAddr), out _);
            _moveTickHook = _hooks.CreateHook<MoveTickDelegate>(MoveTickDetour, unchecked((nint)MoveTickAddr)).Activate();
        }
        catch { }
    }

    public void AddMouseDelta(float deltaX, float deltaY)
    {
        lock (_deltaLock)
        {
            _pendingDeltaX += deltaX;
            _pendingDeltaY += deltaY;
        }
    }

    public void ResetCamera()
    {
        _currentPitch = 0f;
        try
        {
            // Simulate 'R' key tap (native camera recenter in P4G)
            Native.keybd_event(Native.VK_KEY_R, 0, 0, 0); // Down
            Native.keybd_event(Native.VK_KEY_R, 0, Native.KEYEVENTF_KEYUP, 0); // Up
        }
        catch { }
    }

    public void ResetPitch()
    {
        _currentPitch = 0f;
    }

    public void ResetMainThreadCursor()
    {
        _mainThreadCursorHidden = false;
    }

    private void MoveTickDetour(nint param1)
    {
        // Record field heartbeat so we know 3D field camera is actively ticking
        _lastMoveTickTime = Environment.TickCount64;

        // Synchronize cursor visibility on the game's main UI thread!
        bool shouldHide = CanRotateCamera && IsLooking;
        if (shouldHide && !_mainThreadCursorHidden)
        {
            Native.SetCursor(nint.Zero);
            while (Native.ShowCursor(false) >= 0) { }
            _mainThreadCursorHidden = true;
        }
        else if (!shouldHide && _mainThreadCursorHidden)
        {
            while (Native.ShowCursor(true) < 0) { }
            Native.SetCursor(Native.LoadCursor(nint.Zero, (nint)Native.IDC_ARROW));
            _mainThreadCursorHidden = false;
        }

        // Run original game movement & camera tick
        _moveTickHook?.OriginalFunction(param1);

        if (!IsInField)
        {
            _currentPitch = 0f;
            if (_mainThreadCursorHidden)
            {
                while (Native.ShowCursor(true) < 0) { }
                Native.SetCursor(Native.LoadCursor(nint.Zero, (nint)Native.IDC_ARROW));
                _mainThreadCursorHidden = false;
            }
            return;
        }

        if (!CanRotateCamera)
        {
            return;
        }

        float deltaX = 0f;
        float deltaY = 0f;
        lock (_deltaLock)
        {
            deltaX = _pendingDeltaX;
            _pendingDeltaX = 0f;

            deltaY = _pendingDeltaY;
            _pendingDeltaY = 0f;
        }

        // 1. Apply Horizontal Yaw rotation via native CamRot
        if (Math.Abs(deltaX) > 0.0001f)
        {
            try
            {
                unsafe
                {
                    long* ptr = (long*)FieldObjGlobalAddr;
                    if (ptr != null && *ptr != 0)
                    {
                        float signX = _config.InvertX ? 1.0f : -1.0f;
                        float rotDelta = deltaX * signX * _config.SensitivityX * 0.0035f;
                        _camRot?.Invoke(*ptr, rotDelta);
                    }
                }
            }
            catch { }
        }

        // 2. Apply Vertical Pitch rotation via Spherical LookAt offset
        if (_config.EnablePitch)
        {
            float signY = _config.InvertY ? -1.0f : 1.0f;
            if (Math.Abs(deltaY) > 0.0001f)
            {
                // Smooth, gentle pitch sensitivity (0.0008f)
                _currentPitch += deltaY * signY * _config.SensitivityY * 0.0008f;
                _currentPitch = Math.Clamp(_currentPitch, MinPitchOffset, MaxPitchOffset);
            }

            if (Math.Abs(_currentPitch) > 0.0005f)
            {
                ApplyPitch(param1, _currentPitch);
            }
        }
        else
        {
            _currentPitch = 0f;
        }
    }

    private unsafe void ApplyPitch(nint param1, float pitch)
    {
        try
        {
            if (float.IsNaN(pitch) || float.IsInfinity(pitch))
            {
                _currentPitch = 0f;
                return;
            }

            nint pCamGlobal = *(nint*)CamGlobalPtrAddr;
            if (pCamGlobal == nint.Zero) return;

            nint camObj = *(nint*)(pCamGlobal + 8);
            if (camObj == nint.Zero) return;

            // Target focus position (Player focus point)
            float* pTarget = (float*)TargetPosAddr;
            if (pTarget == null) return;
            float tx = pTarget[0];
            float ty = pTarget[1];
            float tz = pTarget[2];

            if (float.IsNaN(tx) || float.IsNaN(ty) || float.IsNaN(tz)) return;

            // Get clean base eye position computed by MoveTick for this frame
            // CRITICAL: camState + 0x30 MUST REMAIN READ-ONLY!
            // Writing to camState + 0x30 corrupts MoveTick's collision solver,
            // triggers compounding angles, and pushes camera outside the room (black screen).
            float bx, by, bz;
            nint camState = *(nint*)(param1 + 0x1248);
            if (camState != nint.Zero)
            {
                float* pBaseEye = (float*)(camState + 0x30);
                bx = pBaseEye[0];
                by = pBaseEye[1];
                bz = pBaseEye[2];
            }
            else
            {
                float* pEye = (float*)(camObj + 0x50);
                bx = pEye[0];
                by = pEye[1];
                bz = pEye[2];
            }

            if (float.IsNaN(bx) || float.IsNaN(by) || float.IsNaN(bz)) return;

            // Vector from target to base eye
            float vx = bx - tx;
            float vy = by - ty;
            float vz = bz - tz;

            float r0 = MathF.Sqrt(vx * vx + vz * vz);
            if (r0 < 10.0f || float.IsNaN(r0)) return;

            float radius = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            if (radius < 50.0f || float.IsNaN(radius)) return;

            // Native base pitch angle theta0
            float theta0 = MathF.Atan2(vy, r0);
            if (float.IsNaN(theta0)) return;

            // Total angle = native angle + user pitch offset
            float phi = theta0 + pitch;

            // Strict clamp to keep camera safely within dungeon room volume
            // Prevents clipping into low dungeon ceilings or solid floors (which causes portal culling / black screen)
            phi = Math.Clamp(phi, 0.22f, 0.44f);

            // Horizontal angle alpha from target to eye
            float alpha = MathF.Atan2(vz, vx);
            if (float.IsNaN(alpha)) return;

            float newHdist = radius * MathF.Cos(phi);
            float newHeight = radius * MathF.Sin(phi);

            float* newEye = stackalloc float[4];
            newEye[0] = tx + newHdist * MathF.Cos(alpha);
            newEye[1] = ty + newHeight;
            newEye[2] = tz + newHdist * MathF.Sin(alpha);
            newEye[3] = 1.0f;

            if (float.IsNaN(newEye[0]) || float.IsNaN(newEye[1]) || float.IsNaN(newEye[2]))
            {
                _currentPitch = 0f;
                return;
            }

            // Write new eye directly into camObj
            float* pCamObjEye = (float*)(camObj + 0x50);
            pCamObjEye[0] = newEye[0];
            pCamObjEye[1] = newEye[1];
            pCamObjEye[2] = newEye[2];

            // Call native Camera::LookAt to update orientation, frustum, and render pipeline
            _camLookAt?.Invoke(pCamGlobal, (nint)newEye, (nint)pTarget, nint.Zero);
        }
        catch { }
    }
}
