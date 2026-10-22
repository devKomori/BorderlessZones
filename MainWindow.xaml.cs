using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace BorderlessZones;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly UserSettings _settings;
    private Process? _selectedProcess;

    public MainWindow()
    {
        _settings = UserSettings.Load();
        InitializeComponent();
        this.Topmost = true; // Ensure the main window is always on top of other windows
        // Handles the SelectionChanged event for the ComboBox, without needing additional event attributes in XAML.
        SelectApplicationItem.SelectionChanged += SelectApplicationItem_SelectionChanged;
        LoadUserSettings();
        LoadProcessesForMenuItems();
    }


    private void LoadUserSettings()
    {
        XOffsetInput.Text = _settings.XOffset.ToString();
        YOffsetInput.Text = _settings.YOffset.ToString();
        WidthInput.Text = _settings.Width.ToString();
        HeightInput.Text = _settings.Height.ToString();
    }


    private void SaveUserSettings()
    {
        if (int.TryParse(XOffsetInput.Text, out var x))
            _settings.XOffset = x;

        if (int.TryParse(YOffsetInput.Text, out var y))
            _settings.YOffset = y;

        if (int.TryParse(WidthInput.Text, out var w))
            _settings.Width = w;

        if (int.TryParse(HeightInput.Text, out var h))
            _settings.Height = h;

        _settings.Save();
    }


    // Save settings when the main window closes.
    protected override void OnClosed(EventArgs e)
    {
        SaveUserSettings();
        base.OnClosed(e);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    // Retrieves the dimensions of the bounding rectangle of the specified window
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out LPRECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct LPRECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly struct SavedWindowState(IntPtr style, LPRECT originalRect)
    {
        public IntPtr OriginalStyle { get; } = style;
        public LPRECT OriginalRect { get; } = originalRect;
    }

    private class ProcessUiState
    {
        public bool IsBorderlessApplied { get; set; }
        public bool IsTaskbarCovered { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public bool IsStatusVisible { get; set; }
    }
    // Stores the UI state for each process, keyed by process ID.
    private readonly Dictionary<int, ProcessUiState> _processUiStates = new();

    // Stores original window styles and positions, keyed by process ID.
    private readonly Dictionary<int, SavedWindowState> _savedOriginalWindowStates = new();
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private static readonly IntPtr HWND_TOPMOST = new(-1); // Bring window to top of the Z order (covers taskbar)
    private const int WS_CAPTION = 0x00C00000; // Removes windowed title bar
    private const int WS_THICKFRAME = 0x00040000; // Removes the windowed border
    private const int GWL_STYLE = -16; // Retrieves the window style info


    private List<Process> GetWindowsProcesses()
    {
        return Process.GetProcesses()
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle)
                        && p.MainWindowHandle != IntPtr.Zero
                        && p.Id != Environment.ProcessId)
            .OrderBy(p => p.MainWindowTitle)
            .ToList();
    }


    // Populate the ComboBox Menu with applications that have a main window.
    private void LoadProcessesForMenuItems()
    {
        int? selectedProcessId = _selectedProcess?.Id;
        // Temporarily detach the event handler to avoid triggering UI resets when the ComboBox items are updated.
        SelectApplicationItem.SelectionChanged -= SelectApplicationItem_SelectionChanged;

        SelectApplicationItem.Items.Clear();

        // create placeholder that is not treated as a selectable process.
        SelectApplicationItem.Items.Add(new ComboBoxItem
        {
            Content = "Select Application...",
            Foreground = System.Windows.Media.Brushes.LightGray,
            Tag = null
        });

        foreach (Process process in GetWindowsProcesses())
        {
            string label = $"{process.MainWindowTitle} ({process.Id})";

            var item = new ComboBoxItem
            {
                Content = label,
                Tag = process
            };

            SelectApplicationItem.Items.Add(item);

            // Keep the previously selected process selected after refreshing.
            if (selectedProcessId == process.Id)
            {
                SelectApplicationItem.SelectedItem = item;
            }
        }
        // If the previously selected process is no longer running, reset to placeholder
        if (SelectApplicationItem.SelectedItem is null || (SelectApplicationItem.SelectedItem as ComboBoxItem)?.Tag is null)
        {
            SelectApplicationItem.SelectedIndex = 0;
            _selectedProcess = null;
        }
        // Reattach the event handler after updating the items in the dropdown.
        SelectApplicationItem.SelectionChanged += SelectApplicationItem_SelectionChanged;
    }


    private void SelectApplicationItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Process? newProcess = null;
        if (SelectApplicationItem.SelectedItem is ComboBoxItem item && item.Tag is Process process)
        {
            newProcess = process;
        }

        // Reset the UI if the selected process has changed
        if (_selectedProcess?.Id != newProcess?.Id)
        {
            _selectedProcess = newProcess;

            if (_selectedProcess != null && _processUiStates.TryGetValue(_selectedProcess.Id, out var state))
            {
                // Restore saved UI state for this specific process
                StatusTextBlock.Text = state.StatusText;
                StatusTextBlock.Visibility = state.IsStatusVisible ? Visibility.Visible : Visibility.Hidden;
                HideTaskbarCheckBox.Visibility = state.IsBorderlessApplied ? Visibility.Visible : Visibility.Hidden;
                HideTaskbarCheckBox.IsChecked = state.IsTaskbarCovered;
            }
            else
            {
                // Default state for unmodified or new processes
                StatusTextBlock.Text = string.Empty;
                StatusTextBlock.Visibility = Visibility.Hidden;
                HideTaskbarCheckBox.Visibility = Visibility.Hidden;
                HideTaskbarCheckBox.IsChecked = false;
            }
        }
    }


    // Refresh the application list whenever the dropdown is opened.
    private void SelectApplicationItem_OnSubmenuOpened(object sender, EventArgs e)
    {

        LoadProcessesForMenuItems();
    }


    private void BorderlessButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null)
        {
            StatusTextBlock.Text = "You must select a process from the dropdown";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        _selectedProcess.Refresh();
        IntPtr hWnd = _selectedProcess.MainWindowHandle;

        if (hWnd == IntPtr.Zero)
        {
            StatusTextBlock.Text = "The selected process does not have a main window";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }
        // Validate user input for offsets and dimensions
        if (!int.TryParse(XOffsetInput.Text, out int xOffset) ||
            !int.TryParse(YOffsetInput.Text, out int yOffset) ||
            !int.TryParse(WidthInput.Text, out int width) ||
            !int.TryParse(HeightInput.Text, out int height))
        {
            StatusTextBlock.Text = "Please enter valid numbers for all fields";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }
        StatusTextBlock.Visibility = Visibility.Hidden;
        SaveUserSettings();

        // Save the original window state if it hasn't been saved yet
        int processId = _selectedProcess.Id;
        if (!_savedOriginalWindowStates.ContainsKey(processId))
        {
            IntPtr originalStyle = GetWindowLongPtr(hWnd, GWL_STYLE);
            if (!GetWindowRect(hWnd, out LPRECT originalRect))
            {
                StatusTextBlock.Text = "Failed to read the current window bounds";
                StatusTextBlock.Visibility = Visibility.Visible;
                return;
            }
            _savedOriginalWindowStates[processId] = new SavedWindowState(originalStyle, originalRect);
        }
        // Remove the title bar (WS_CAPTION) and thick frame (WS_THICKFRAME).
        IntPtr currentStyle = GetWindowLongPtr(hWnd, GWL_STYLE);
        IntPtr newStyle = new(currentStyle.ToInt64() & ~(WS_CAPTION | WS_THICKFRAME));
        // Apply the new style and reposition the window.
        SetWindowLongPtr(hWnd, GWL_STYLE, newStyle);
        SetWindowPos(
            hWnd,
            HWND_NOTOPMOST,
            xOffset,
            yOffset,
            width,
            height,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW);

        _processUiStates[processId] = new ProcessUiState
        {
            IsBorderlessApplied = true,
            IsTaskbarCovered = false, // Reset topmost when re-applying borderless
            StatusText = "Borderless mode applied",
            IsStatusVisible = true
        };

        StatusTextBlock.Text = "Borderless mode applied";
        StatusTextBlock.Visibility = Visibility.Visible;
        HideTaskbarCheckBox.Visibility = Visibility.Visible;
    }


    private void RestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null)
        {
            StatusTextBlock.Text = "You must select a process from the dropdown";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }
        _selectedProcess.Refresh();

        IntPtr hWnd = _selectedProcess.MainWindowHandle;
        if (hWnd == IntPtr.Zero)
        {
            StatusTextBlock.Text = "The selected process does not have a main window";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (!_savedOriginalWindowStates.TryGetValue(_selectedProcess.Id, out SavedWindowState saved))
        {
            StatusTextBlock.Text = "No saved window state exists for this process";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        SetWindowLongPtr(hWnd, GWL_STYLE, saved.OriginalStyle);

        int width = saved.OriginalRect.Right - saved.OriginalRect.Left;
        int height = saved.OriginalRect.Bottom - saved.OriginalRect.Top;

        SetWindowPos(
            hWnd,
            HWND_NOTOPMOST,
            saved.OriginalRect.Left,
            saved.OriginalRect.Top,
            width,
            height,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW);

        _savedOriginalWindowStates.Remove(_selectedProcess.Id);
        _processUiStates.Remove(_selectedProcess.Id);

        StatusTextBlock.Text = "Window restored";
        StatusTextBlock.Visibility = Visibility.Visible;
        HideTaskbarCheckBox.Visibility = Visibility.Hidden;
        HideTaskbarCheckBox.IsChecked = false;
    }


    private void CoverTaskbarCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null)
            return;

        _selectedProcess.Refresh();
        IntPtr hWnd = _selectedProcess.MainWindowHandle;

        if (hWnd == IntPtr.Zero)
            return;

        SetWindowPos(
            hWnd,
            HWND_TOPMOST,
            0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);

        if (_processUiStates.TryGetValue(_selectedProcess.Id, out var state))
        {
            state.IsTaskbarCovered = true;
        }
    }


    private void CoverTaskbarCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null)
            return;

        _selectedProcess.Refresh();
        IntPtr hWnd = _selectedProcess.MainWindowHandle;

        if (hWnd == IntPtr.Zero)
            return;

        SetWindowPos(
            hWnd,
            HWND_NOTOPMOST,
            0, 0, 0, 0,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);

        if (_processUiStates.TryGetValue(_selectedProcess.Id, out var state))
        {
            state.IsTaskbarCovered = false;
        }
    }


    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreButton_OnClick(sender, e); // Restore the selected process before exiting
        Application.Current.Shutdown();
    }
}
