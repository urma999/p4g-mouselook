using System.Diagnostics;
using Reloaded.Mod.Interfaces;

namespace p4g64.mouselook;

public class MouseController
{
    private readonly CameraManager _camera;
    private readonly Config _config;
    private readonly ILogger? _logger;
    private readonly Thread _pollThread;
    private volatile bool _running = true;

    private bool _isToggledOn = true;
    private bool _toggleKeyDownLast = false;
    private bool _sensUpLast = false;
    private bool _sensDownLast = false;
    private bool _mmbDownLast = false;
    private bool _wasLookingLast = false;
    private uint _myPid;

    public MouseController(CameraManager camera, Config config, ILogger? logger = null)
    {
        _camera = camera;
        _config = config;
        _logger = logger;
        _myPid = Native.GetCurrentProcessId();

        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "P4G_MouseLook_Poller"
        };
        _pollThread.Start();
    }

    private bool _cursorHidden = false;

    public void Stop()
    {
        _running = false;
        SetCursorVisibility(true);
        Native.SetCursor(Native.LoadCursor(nint.Zero, (nint)Native.IDC_ARROW));
        _cursorHidden = false;
        _camera.ResetMainThreadCursor();
    }

    private void EnsureCursor(bool visible)
    {
        if (visible)
        {
            SetCursorVisibility(true);
            Native.SetCursor(Native.LoadCursor(nint.Zero, (nint)Native.IDC_ARROW));
            _cursorHidden = false;
            _camera.ResetMainThreadCursor();
        }
        else if (!_cursorHidden)
        {
            Native.SetCursor(nint.Zero);
            SetCursorVisibility(false);
            _cursorHidden = true;
        }
    }

    private static void SetCursorVisibility(bool visible)
    {
        try
        {
            if (visible)
            {
                int count = Native.ShowCursor(true);
                while (count < 0)
                {
                    count = Native.ShowCursor(true);
                }
                while (count > 0)
                {
                    count = Native.ShowCursor(false);
                }
            }
            else
            {
                int count = Native.ShowCursor(false);
                while (count >= 0)
                {
                    count = Native.ShowCursor(false);
                }
                while (count < -1)
                {
                    count = Native.ShowCursor(true);
                }
            }
        }
        catch { }
    }

    private void PollLoop()
    {
        while (_running)
        {
            try
            {
                Tick();
            }
            catch { }

            Thread.Sleep(8); // ~120Hz
        }

        SetCursorVisibility(true);
    }

    private void Tick()
    {
        nint fgWnd = Native.GetForegroundWindow();
        if (fgWnd == nint.Zero)
        {
            _camera.IsLooking = false;
            EnsureCursor(true);
            _wasLookingLast = false;
            return;
        }

        Native.GetWindowThreadProcessId(fgWnd, out uint fgPid);
        if (fgPid != _myPid)
        {
            _camera.IsLooking = false;
            EnsureCursor(true);
            _wasLookingLast = false;
            return;
        }

        uint mainThreadId = Native.GetWindowThreadProcessId(fgWnd, out _);
        Native.AttachThreadInput(Native.GetCurrentThreadId(), mainThreadId, true);

        // Check Toggle Key (e.g. ~ or F1)
        bool toggleKeyDown = (Native.GetAsyncKeyState(_config.ToggleKey) & 0x8000) != 0;
        if (toggleKeyDown && !_toggleKeyDownLast)
        {
            _isToggledOn = !_isToggledOn;
            _logger?.WriteLine($"[P4G Mouse-Look] Mouse-look toggled {(_isToggledOn ? "ON" : "OFF")}", _logger.ColorYellow);
        }
        _toggleKeyDownLast = toggleKeyDown;

        // Check Middle Mouse Button (VK_MBUTTON = 0x04) to Reset Camera
        bool mmbDown = (Native.GetAsyncKeyState(0x04) & 0x8000) != 0;
        if (mmbDown && !_mmbDownLast)
        {
            if (_config.MiddleMouseResetCamera && _camera.CanRotateCamera)
            {
                _camera.ResetCamera();
                _logger?.WriteLine("[P4G Mouse-Look] Camera reset behind player (MMB)", _logger.ColorLightBlue);
            }
        }
        _mmbDownLast = mmbDown;

        // In-game sensitivity adjustment hotkeys:
        // Increase: ']' (0xDD) or '+' (0xBB)
        bool sensUpDown = ((Native.GetAsyncKeyState(_config.SensitivityUpKey) & 0x8000) != 0) ||
                          ((Native.GetAsyncKeyState(0xBB) & 0x8000) != 0); // VK_OEM_PLUS
        if (sensUpDown && !_sensUpLast)
        {
            _config.SensitivityX = MathF.Round(Math.Clamp(_config.SensitivityX + 0.5f, 0.5f, 50.0f), 1);
            _config.SensitivityY = MathF.Round(Math.Clamp(_config.SensitivityY + 0.5f, 0.5f, 50.0f), 1);
            _config.SaveConfig();
            _logger?.WriteLine($"[P4G Mouse-Look] Mouse Sensitivity: X={_config.SensitivityX:F1}, Y={_config.SensitivityY:F1}", _logger.ColorYellow);
        }
        _sensUpLast = sensUpDown;

        // Decrease: '[' (0xDB) or '-' (0xBD)
        bool sensDownDown = ((Native.GetAsyncKeyState(_config.SensitivityDownKey) & 0x8000) != 0) ||
                            ((Native.GetAsyncKeyState(0xBD) & 0x8000) != 0); // VK_OEM_MINUS
        if (sensDownDown && !_sensDownLast)
        {
            _config.SensitivityX = MathF.Round(Math.Clamp(_config.SensitivityX - 0.5f, 0.5f, 50.0f), 1);
            _config.SensitivityY = MathF.Round(Math.Clamp(_config.SensitivityY - 0.5f, 0.5f, 50.0f), 1);
            _config.SaveConfig();
            _logger?.WriteLine($"[P4G Mouse-Look] Mouse Sensitivity: X={_config.SensitivityX:F1}, Y={_config.SensitivityY:F1}", _logger.ColorYellow);
        }
        _sensDownLast = sensDownDown;

        // Check Genshin-style Alt hold (VK_MENU = 0x12)
        bool isAltHeld = _config.HoldAltToUnlock && ((Native.GetAsyncKeyState(0x12) & 0x8000) != 0);

        // Check Right Mouse Button (VK_RBUTTON = 0x02)
        bool rmbDown = (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;

        // Decide if camera should be actively looking:
        bool shouldLook;
        if (isAltHeld)
        {
            // Genshin style: holding Alt temporarily frees the cursor!
            shouldLook = false;
        }
        else if (!_camera.CanRotateCamera)
        {
            // In menu, dialogue, cutscene, battle, or non-field screen -> free the cursor!
            shouldLook = false;
        }
        else
        {
            // In normal 3D gameplay: look if toggled on, or if holding RMB
            shouldLook = _isToggledOn || rmbDown;
        }

        _camera.IsLooking = shouldLook;

        if (shouldLook)
        {
            EnsureCursor(false);

            if (!Native.GetClientRect(fgWnd, out Native.RECT clientRect))
                return;

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;
            if (width <= 0 || height <= 0) return;

            Native.POINT center = new Native.POINT
            {
                X = width / 2,
                Y = height / 2
            };
            Native.ClientToScreen(fgWnd, ref center);

            if (!_wasLookingLast)
            {
                // Just entered look mode (e.g. released Alt, closed menu/dialogue, toggled on)
                // Center cursor immediately and discard any old cursor delta so camera never jumps!
                Native.SetCursorPos(center.X, center.Y);
                _wasLookingLast = true;
                return;
            }

            if (Native.GetCursorPos(out Native.POINT current))
            {
                int dx = current.X - center.X;
                int dy = current.Y - center.Y;

                if (dx != 0 || dy != 0)
                {
                    _camera.AddMouseDelta(dx, dy);
                }

                // Re-center cursor so mouse never hits screen edge
                Native.SetCursorPos(center.X, center.Y);
            }
        }
        else
        {
            EnsureCursor(true);
            _wasLookingLast = false;
        }
    }
}
