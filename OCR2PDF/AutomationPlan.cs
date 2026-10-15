using System.Windows.Input;
namespace OCR2PDF;

public enum AdvanceMode { Click, Scroll, KeyPress }
public sealed record AutomationPlan(AdvanceMode Mode, int PageCount, double WaitSeconds, int ScrollNotches = 3, Key Key = Key.PageDown)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || PageCount is < 1 or > 1000 || !double.IsFinite(WaitSeconds) || WaitSeconds is < .2 or > 60)
            throw new ArgumentException("Choose 1–1000 captures and a wait between 0.2 and 60 seconds.");
        if (Mode == AdvanceMode.Scroll && (ScrollNotches == 0 || ScrollNotches is < -100 or > 100)) throw new ArgumentException("Choose 1–100 scroll notches.");
        if (Mode == AdvanceMode.KeyPress && Key is not (Key.PageDown or Key.PageUp or Key.Right or Key.Left or Key.Down or Key.Up or Key.Space or Key.Enter)) throw new ArgumentException("Choose a supported navigation key.");
    }
}

public static class AutomationRunner
{
    public static async Task RunAsync(AutomationPlan plan, Func<int, CancellationToken, Task> capture, Func<CancellationToken, Task> advance, CancellationToken token)
    {
        plan.Validate();
        for (int i = 0; i < plan.PageCount; i++)
        {
            token.ThrowIfCancellationRequested();
            if (i > 0)
            {
                await advance(token);
                await Task.Delay(TimeSpan.FromSeconds(plan.WaitSeconds), token);
            }
            token.ThrowIfCancellationRequested();
            await capture(i + 1, token);
        }
    }
}
