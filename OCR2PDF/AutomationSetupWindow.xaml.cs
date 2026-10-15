using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
namespace OCR2PDF;

public partial class AutomationSetupWindow : Window
{
    public AutomationPlan? Plan { get; private set; }
    public AutomationSetupWindow()
    {
        InitializeComponent();
        NavigationKey.ItemsSource = new[] { Key.PageDown, Key.PageUp, Key.Right, Key.Left, Key.Down, Key.Up, Key.Space, Key.Enter };
        NavigationKey.SelectedIndex = 0;
    }
    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ScrollOptions == null) return;
        ScrollOptions.Visibility = Mode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        KeyOptions.Visibility = Mode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Count.Text, out int count) || !double.TryParse(Wait.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double wait) || !int.TryParse(Notches.Text, out int notches))
        { Error.Text = "Enter valid numbers for captures, wait time, and scroll notches."; return; }
        var plan = new AutomationPlan((AdvanceMode)Mode.SelectedIndex, count, wait, Direction.SelectedIndex == 0 ? notches : -notches, (Key)NavigationKey.SelectedItem);
        try { plan.Validate(); Plan = plan; DialogResult = true; }
        catch (ArgumentException ex) { Error.Text = ex.Message; }
    }
}
