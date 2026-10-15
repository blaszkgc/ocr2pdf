using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
namespace OCR2PDF;

public static class DesktopAutomation
{
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    public static IntPtr WindowAt(Drawing.Point point) => GetAncestor(WindowFromPoint(new NativePoint { X = point.X, Y = point.Y }), 2);
    public static void Focus(IntPtr window) { if (GetForegroundWindow() != window) SetForegroundWindow(window); }
    public static void EnsureTarget(IntPtr window, Drawing.Point? point = null)
    {
        if (window == IntPtr.Zero || !IsWindow(window) || GetForegroundWindow() != window || (point.HasValue && WindowAt(point.Value) != window))
            throw new InvalidOperationException("The target window changed or lost focus. Automation stopped; captured pages are retained.");
    }
    public static void Advance(AutomationPlan plan, Drawing.Point point, IntPtr target)
    {
        EnsureTarget(target, plan.Mode == AdvanceMode.KeyPress ? null : point);
        if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0))
            throw new InvalidOperationException("Release Shift, Ctrl, Alt, and Windows keys before automating.");
        var inputs = new List<Input>();
        if (plan.Mode != AdvanceMode.KeyPress)
        {
            int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            int x = (int)Math.Round((point.X - GetSystemMetrics(76)) * 65535.0 / Math.Max(1, width - 1));
            int y = (int)Math.Round((point.Y - GetSystemMetrics(77)) * 65535.0 / Math.Max(1, height - 1));
            if (x is < 0 or > 65535 || y is < 0 or > 65535) throw new InvalidOperationException("The action location is outside the current displays.");
            inputs.Add(Mouse(0x8000 | 0x4000 | 0x0001, 0, x, y));
            if (plan.Mode == AdvanceMode.Click) { inputs.Add(Mouse(0x0002)); inputs.Add(Mouse(0x0004)); }
            else inputs.Add(Mouse(0x0800, unchecked((uint)(plan.ScrollNotches * -120))));
        }
        else
        {
            ushort key = (ushort)KeyInterop.VirtualKeyFromKey(plan.Key);
            uint extended = plan.Key is Key.Space or Key.Enter ? 0u : 1u;
            inputs.Add(Keyboard(key, extended)); inputs.Add(Keyboard(key, extended | 0x0002));
        }
        if (SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) != inputs.Count)
        {
            // Release an input if Windows accepted only part of the sequence.
            var release = plan.Mode == AdvanceMode.Click ? Mouse(0x0004) : Keyboard((ushort)KeyInterop.VirtualKeyFromKey(plan.Key), plan.Key is Key.Space or Key.Enter ? 2u : 3u);
            if (plan.Mode != AdvanceMode.Scroll) SendInput(1, new[] { release }, Marshal.SizeOf<Input>());
            throw new InvalidOperationException("Windows did not accept the automated input. Check whether the target app runs with elevated permissions.");
        }
    }
    private static Input Mouse(uint flags, uint data = 0, int x = 0, int y = 0) => new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = x, Y = y, Data = data, Flags = flags } } };
    private static Input Keyboard(ushort key, uint flags) => new() { Type = 1, Data = new InputUnion { Keyboard = new KeyInput { Key = key, Flags = flags } } };
    public static BitmapSource Capture(Drawing.Rectangle region)
    {
        var bounds = new Drawing.Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));
        if (region.Width < 10 || region.Height < 10 || !bounds.Contains(region)) throw new InvalidOperationException("The capture region is outside the current displays.");
        using var bitmap = new Drawing.Bitmap(region.Width, region.Height);
        using (var graphics = Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(region.Location, Drawing.Point.Empty, region.Size);
        using var stream = new MemoryStream(); bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
}
