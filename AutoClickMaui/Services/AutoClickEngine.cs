using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Extensions;

#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
#endif

namespace AutoClickMaui.Services;

public class AutoClickEngine
{
    private CancellationTokenSource? _cts;
    private Task? _worker;
    public string LastStatus { get; private set; } = "Listo";
    public bool IsRunning => _cts is not null && _worker is not null && !_worker.IsCompleted;

#if WINDOWS
    private sealed class CompiledStep
    {
        public string Name { get; init; } = "";
        public Mat TemplateGray { get; init; } = new();
        public PointDto ClickPoint { get; init; } = new();
        public double Threshold { get; init; }
    }
#endif

#if WINDOWS
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RectStruct lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectStruct
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, InputStruct[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RectStruct lpRect);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct InputStruct
    {
        public uint Type;
        public MouseInputData Mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }
#endif

    public List<MonitorInfo> GetMonitors()
    {
#if WINDOWS
        var monitors = new List<MonitorInfo>();
        var index = 0;

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RectStruct lprcMonitor, IntPtr dwData) =>
        {
            var width = lprcMonitor.Right - lprcMonitor.Left;
            var height = lprcMonitor.Bottom - lprcMonitor.Top;

            if (width > 0 && height > 0)
            {
                monitors.Add(new MonitorInfo
                {
                    Id = index.ToString(),
                    Name = $"Pantalla {index + 1}: {width}x{height} @ ({lprcMonitor.Left},{lprcMonitor.Top})",
                    Left = lprcMonitor.Left,
                    Top = lprcMonitor.Top,
                    Width = width,
                    Height = height
                });

                index++;
            }

            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (width > 0 && height > 0)
            {
                monitors.Add(new MonitorInfo
                {
                    Id = "0",
                    Name = $"Pantalla virtual: {width}x{height} @ ({left},{top})",
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height
                });
            }
        }

        if (monitors.Count == 0)
        {
            monitors.Add(GetDesktopMonitorFallback());
        }

        return monitors;
#else
        return new List<MonitorInfo>();
#endif
    }

    public string CaptureMonitorPngBase64(string monitorId)
    {
#if WINDOWS
        var monitor = GetMonitorOrThrow(monitorId);
        using var bmp = CaptureScreen(monitor);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
#else
        throw new PlatformNotSupportedException("Solo Windows está soportado en esta versión.");
#endif
    }

    public string CaptureMonitorJpegBase64(string monitorId, long quality = 75L)
    {
#if WINDOWS
        var monitor = GetMonitorOrThrow(monitorId);
        using var bmp = CaptureScreen(monitor);
        using var ms = new MemoryStream();

        var encoder = ImageCodecInfo.GetImageDecoders().FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
        if (encoder is null)
        {
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            return Convert.ToBase64String(ms.ToArray());
        }

        using var qualityParams = new EncoderParameters(1);
        qualityParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        bmp.Save(ms, encoder, qualityParams);
        return Convert.ToBase64String(ms.ToArray());
#else
        throw new PlatformNotSupportedException("Solo Windows está soportado en esta versión.");
#endif
    }

    public void Start(StartDetectionRequest request)
    {
#if WINDOWS
        Stop();

        if (request.Actions is null || request.Actions.Count == 0)
        {
            throw new InvalidOperationException("Debes configurar al menos una acción.");
        }

        var monitor = GetMonitorOrThrow(request.MonitorId);

        var compiledSteps = new List<CompiledStep>();
        foreach (var step in request.Actions)
        {
            if (string.IsNullOrWhiteSpace(step.TemplateBase64))
            {
                throw new InvalidOperationException($"La acción '{step.Name}' no tiene plantilla.");
            }

            var templateBytes = Convert.FromBase64String(ExtractBase64Payload(step.TemplateBase64));
            using var templateMatColor = Cv2.ImDecode(templateBytes, ImreadModes.Color);
            if (templateMatColor.Empty())
            {
                throw new InvalidOperationException($"No se pudo decodificar la plantilla de '{step.Name}'.");
            }

            var templateGray = new Mat();
            ConvertToGray(templateMatColor, templateGray);

            compiledSteps.Add(new CompiledStep
            {
                Name = string.IsNullOrWhiteSpace(step.Name) ? "Acción" : step.Name,
                TemplateGray = templateGray,
                ClickPoint = step.ClickPoint,
                Threshold = step.Threshold
            });
        }

        var orderedMode = string.Equals(request.ExecutionMode, "ordered", StringComparison.OrdinalIgnoreCase);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _worker = Task.Run(async () =>
        {
            DateTime lastClick = DateTime.MinValue;
            var routeIndex = 0;
            Mat? lastClickSnapshot = null;
            var waitingForScreenChange = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var bmp = CaptureScreen(monitor);
                    using var frameMat = BitmapConverter.ToMat(bmp);
                    using var frameGray = new Mat();
                    ConvertToGray(frameMat, frameGray);
                    using var currentSnapshot = CreateChangeSnapshot(frameGray);

                    if (request.RequireScreenChangeAfterClick && waitingForScreenChange)
                    {
                        if (lastClickSnapshot is not null && HasScreenChanged(lastClickSnapshot, currentSnapshot))
                        {
                            waitingForScreenChange = false;
                            lastClickSnapshot.Dispose();
                            lastClickSnapshot = null;
                            LastStatus = "Pantalla cambió: clic reactivado";
                        }
                        else
                        {
                            LastStatus = "Esperando cambio de pantalla para reactivar clic";
                            await Task.Delay(Math.Max(30, request.IntervalMs), token);
                            continue;
                        }
                    }

                    var now = DateTime.UtcNow;
                    if ((now - lastClick).TotalMilliseconds < request.CooldownMs)
                    {
                        LastStatus = "Esperando cooldown";
                    }
                    else if (orderedMode)
                    {
                        var currentStep = compiledSteps[routeIndex];
                        if (currentStep.TemplateGray.Width > frameGray.Width || currentStep.TemplateGray.Height > frameGray.Height)
                        {
                            LastStatus = $"Ruta: '{currentStep.Name}' plantilla más grande que pantalla";
                            await Task.Delay(Math.Max(30, request.IntervalMs), token);
                            continue;
                        }

                        using var result = new Mat();
                        Cv2.MatchTemplate(frameGray, currentStep.TemplateGray, result, TemplateMatchModes.CCoeffNormed);
                        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                        if (maxVal >= currentStep.Threshold)
                        {
                            var offsetX = Math.Clamp(currentStep.ClickPoint.X, 0, Math.Max(0, currentStep.TemplateGray.Width - 1));
                            var offsetY = Math.Clamp(currentStep.ClickPoint.Y, 0, Math.Max(0, currentStep.TemplateGray.Height - 1));
                            var clickX = monitor.Left + maxLoc.X + offsetX;
                            var clickY = monitor.Top + maxLoc.Y + offsetY;
                            if (TryDoClick(clickX, clickY, out var clickError))
                            {
                                lastClick = DateTime.UtcNow;
                                if (request.RequireScreenChangeAfterClick)
                                {
                                    lastClickSnapshot?.Dispose();
                                    lastClickSnapshot = currentSnapshot.Clone();
                                    waitingForScreenChange = true;
                                    LastStatus = $"Ruta: '{currentStep.Name}' score={maxVal:F3} -> clic (esperando cambio de pantalla)";
                                }
                                else
                                {
                                    LastStatus = $"Ruta: '{currentStep.Name}' score={maxVal:F3} -> clic";
                                }
                                routeIndex = (routeIndex + 1) % compiledSteps.Count;
                            }
                            else
                            {
                                LastStatus = $"Ruta: '{currentStep.Name}' detectada pero clic falló: {clickError}";
                            }
                        }
                        else
                        {
                            LastStatus = $"Ruta paso {routeIndex + 1}/{compiledSteps.Count}: esperando '{currentStep.Name}' score={maxVal:F3}";
                        }
                    }
                    else
                    {
                        CompiledStep? bestStep = null;
                        double bestValue = 0;
                        OpenCvSharp.Point bestLoc = default;

                        foreach (var step in compiledSteps)
                        {
                            if (step.TemplateGray.Width > frameGray.Width || step.TemplateGray.Height > frameGray.Height)
                            {
                                continue;
                            }

                            using var result = new Mat();
                            Cv2.MatchTemplate(frameGray, step.TemplateGray, result, TemplateMatchModes.CCoeffNormed);
                            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

                            if (maxVal >= step.Threshold && maxVal > bestValue)
                            {
                                bestValue = maxVal;
                                bestStep = step;
                                bestLoc = maxLoc;
                            }
                        }

                        if (bestStep is not null)
                        {
                            var offsetX = Math.Clamp(bestStep.ClickPoint.X, 0, Math.Max(0, bestStep.TemplateGray.Width - 1));
                            var offsetY = Math.Clamp(bestStep.ClickPoint.Y, 0, Math.Max(0, bestStep.TemplateGray.Height - 1));
                            var clickX = monitor.Left + bestLoc.X + offsetX;
                            var clickY = monitor.Top + bestLoc.Y + offsetY;
                            if (TryDoClick(clickX, clickY, out var clickError))
                            {
                                lastClick = DateTime.UtcNow;
                                if (request.RequireScreenChangeAfterClick)
                                {
                                    lastClickSnapshot?.Dispose();
                                    lastClickSnapshot = currentSnapshot.Clone();
                                    waitingForScreenChange = true;
                                    LastStatus = $"Sin orden: '{bestStep.Name}' score={bestValue:F3} en ({bestLoc.X},{bestLoc.Y}) -> clic (esperando cambio de pantalla)";
                                }
                                else
                                {
                                    LastStatus = $"Sin orden: '{bestStep.Name}' score={bestValue:F3} en ({bestLoc.X},{bestLoc.Y}) -> clic";
                                }
                            }
                            else
                            {
                                LastStatus = $"Sin orden: '{bestStep.Name}' detectada pero clic falló: {clickError}";
                            }
                        }
                        else
                        {
                            LastStatus = $"Sin orden: esperando cualquier imagen (mejor score={bestValue:F3})";
                        }
                    }
                }
                catch (Exception ex)
                {
                    LastStatus = $"Error: {ex.Message}";
                }

                await Task.Delay(Math.Max(30, request.IntervalMs), token);
            }

            foreach (var step in compiledSteps)
            {
                step.TemplateGray.Dispose();
            }

            lastClickSnapshot?.Dispose();
        }, token);

        LastStatus = orderedMode ? "Detección activa (ruta ordenada)" : "Detección activa (sin orden)";
#else
        throw new PlatformNotSupportedException("Solo Windows está soportado en esta versión.");
#endif
    }

    public void Stop()
    {
        if (_cts is null)
        {
            LastStatus = "Detenido";
            return;
        }

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
        _worker = null;
        LastStatus = "Detenido";
    }

#if WINDOWS
    private static string ExtractBase64Payload(string value)
    {
        var marker = "base64,";
        var idx = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? value[(idx + marker.Length)..] : value;
    }

    private MonitorInfo GetMonitorOrThrow(string? monitorId)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return GetDesktopMonitorFallback();
        }

        if (string.IsNullOrWhiteSpace(monitorId))
        {
            return monitors[0];
        }

        var monitor = monitors.FirstOrDefault(m => m.Id == monitorId);
        return monitor ?? monitors[0];
    }

    private MonitorInfo GetDesktopMonitorFallback()
    {
        var desktop = GetDesktopWindow();
        if (desktop != IntPtr.Zero && GetWindowRect(desktop, out var rect))
        {
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width > 0 && height > 0)
            {
                return new MonitorInfo
                {
                    Id = "desktop",
                    Name = $"Escritorio: {width}x{height} @ ({rect.Left},{rect.Top})",
                    Left = rect.Left,
                    Top = rect.Top,
                    Width = width,
                    Height = height
                };
            }
        }

        var fallbackWidth = Math.Max(1, GetSystemMetrics(SM_CXSCREEN));
        var fallbackHeight = Math.Max(1, GetSystemMetrics(SM_CYSCREEN));
        return new MonitorInfo
        {
            Id = "desktop",
            Name = $"Escritorio primario: {fallbackWidth}x{fallbackHeight} @ (0,0)",
            Left = 0,
            Top = 0,
            Width = fallbackWidth,
            Height = fallbackHeight
        };
    }

    private static Bitmap CaptureScreen(MonitorInfo monitor)
    {
        var bmp = new Bitmap(monitor.Width, monitor.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(monitor.Left, monitor.Top, 0, 0, new System.Drawing.Size(monitor.Width, monitor.Height));
        return bmp;
    }

    private static bool TryDoClick(int x, int y, out string error)
    {
        error = string.Empty;

        var moved = SetCursorPos(x, y);
        if (!moved)
        {
            var winErr = Marshal.GetLastWin32Error();
            error = $"SetCursorPos error={winErr}";
            return false;
        }

        var inputs = new[]
        {
            new InputStruct
            {
                Type = INPUT_MOUSE,
                Mi = new MouseInputData
                {
                    Dx = 0,
                    Dy = 0,
                    MouseData = 0,
                    DwFlags = MOUSEEVENTF_LEFTDOWN,
                    Time = 0,
                    DwExtraInfo = UIntPtr.Zero
                }
            },
            new InputStruct
            {
                Type = INPUT_MOUSE,
                Mi = new MouseInputData
                {
                    Dx = 0,
                    Dy = 0,
                    MouseData = 0,
                    DwFlags = MOUSEEVENTF_LEFTUP,
                    Time = 0,
                    DwExtraInfo = UIntPtr.Zero
                }
            }
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<InputStruct>());
        if (sent == inputs.Length)
        {
            return true;
        }

        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        error = $"SendInput sent={sent}/{inputs.Length}, fallback mouse_event aplicado";
        return true;
    }

    private static void ConvertToGray(Mat source, Mat destination)
    {
        if (source.Empty())
        {
            throw new InvalidOperationException("La imagen de entrada está vacía.");
        }

        switch (source.Channels())
        {
            case 1:
                source.CopyTo(destination);
                break;
            case 3:
                Cv2.CvtColor(source, destination, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                Cv2.CvtColor(source, destination, ColorConversionCodes.BGRA2GRAY);
                break;
            default:
                throw new InvalidOperationException($"Formato de imagen no soportado: {source.Channels()} canales.");
        }
    }

    private static Mat CreateChangeSnapshot(Mat frameGray)
    {
        var snapshot = new Mat();
        Cv2.Resize(frameGray, snapshot, new OpenCvSharp.Size(160, 90), 0, 0, InterpolationFlags.Area);
        Cv2.GaussianBlur(snapshot, snapshot, new OpenCvSharp.Size(3, 3), 0);
        return snapshot;
    }

    private static bool HasScreenChanged(Mat previousSnapshot, Mat currentSnapshot)
    {
        if (previousSnapshot.Empty() || currentSnapshot.Empty())
        {
            return true;
        }

        using var diff = new Mat();
        Cv2.Absdiff(previousSnapshot, currentSnapshot, diff);
        var mean = Cv2.Mean(diff);
        return mean.Val0 >= 2.0;
    }
#endif
}
