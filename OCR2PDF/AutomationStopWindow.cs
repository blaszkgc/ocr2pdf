using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
namespace OCR2PDF;

public sealed class AutomationStopWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    private readonly CancellationTokenSource cancellation;
    private readonly TextBlock status = new() { Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private HwndSource? source;
    private bool finished, registered;
    public AutomationStopWindow(CancellationTokenSource cancellation)
    {
        this.cancellation = cancellation;
        Title = "Automated snipping"; Width = 310; Height = 130; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowActivated = false; ShowInTaskbar = false; WindowStyle = WindowStyle.ToolWindow;
        Left = SystemParameters.WorkArea.Right - Width - 16; Top = SystemParameters.WorkArea.Bottom - Height - 16;
        var panel = new StackPanel { Margin = new Thickness(12) }; panel.Children.Add(status);
        var stop = new Button { Content = "Stop (Esc)", Padding = new Thickness(8, 5, 8, 5) }; panel.Children.Add(stop); Content = panel;
        stop.Click += (_, _) => { cancellation.Cancel(); stop.IsEnabled = false; status.Text = "Stopping…"; };
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source.AddHook(OnMessage);
            registered = RegisterHotKey(source.Handle, 7421, 0x4000, 0x1B);
        };
        Closing += (_, e) => { if (!finished) { e.Cancel = true; cancellation.Cancel(); status.Text = "Stopping…"; } };
        Closed += (_, _) => { if (registered && source != null) UnregisterHotKey(source.Handle, 7421); source?.RemoveHook(OnMessage); };
    }
    public void CheckStopKey() { if (!registered) throw new InvalidOperationException("Esc is reserved by another application. Close that shortcut owner before starting automation."); }
    public void SetStatus(string text) => status.Text = text;
    public void Finish() { finished = true; Close(); }
    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == 7421) { cancellation.Cancel(); handled = true; }
        return IntPtr.Zero;
    }
}
