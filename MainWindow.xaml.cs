
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace BorderlessZones;

public partial class MainWindow : Window
{
    private readonly UserSettings _settings;
    private Process? _selectedProcess;

    public MainWindow()
    {
        _settings = UserSettings.Load();
        InitializeComponent();
        this.Topmost = true;

        SelectApplicationItem.SelectionChanged += SelectApplicationItem_SelectionChanged;
        SelectMonitorItem.DropDownOpened += (_, _) => LoadMonitors();

        LoadMonitors();
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
        if (int.TryParse(XOffsetInput.Text, out var x)) _settings.XOffset = x;
        if (int.TryParse(YOffsetInput.Text, out var y)) _settings.YOffset = y;
        if (int.TryParse(WidthInput.Text, out var w)) _settings.Width = w;
        if (int.TryParse(HeightInput.Text, out var h)) _settings.Height = h;
        _settings.Save();
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveUserSettings();
        base.OnClosed(e);
    }

    // --- Window Manipulation P/Invoke ---
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

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

    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref LPRECT monitorRect, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public LPRECT rcMonitor;
        public LPRECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string deviceName, uint deviceNum, ref DISPLAY_DEVICE displayDevice, uint flags);

    // --- Simplified Monitor Logic ---
    private sealed class MonitorOption
    {
        public IntPtr Handle { get; init; }
        public string DeviceName { get; init; } = string.Empty;
        public string FriendlyName { get; init; } = string.Empty;
        public LPRECT Bounds { get; init; }
        public bool IsPrimary { get; init; }
    }

    private static bool TryGetMonitorInfo(IntPtr hMonitor, out MONITORINFOEX info)
    {
        info = new MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<MONITORINFOEX>(),
            szDevice = string.Empty
        };
        return hMonitor != IntPtr.Zero && GetMonitorInfo(hMonitor, ref info);
    }

    private static string CleanMonitorName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        name = name.Trim();

        // Windows can report names such as "Generic Monitor (LS49AG95)".
        if (name.StartsWith("Generic ", StringComparison.OrdinalIgnoreCase))
        {
            int start = name.IndexOf('(');
            int end = name.IndexOf(')', start + 1);

            if (start >= 0 && end > start + 1)
                return name[(start + 1)..end].Trim();

            return string.Empty;
        }

        return name.Equals("Default Monitor", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : name;
    }

    private static string GetMonitorFriendlyName(string deviceName)
    {
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

        if (EnumDisplayDevices(deviceName, 0, ref device, 0))
        {
            string name = CleanMonitorName(device.DeviceString);

            // Query WMI if EnumDisplayDevices doesn't provide a usable model name.
            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(device.DeviceID))
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE PNPClass='Monitor'");

                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string pnpId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
                        string[] parts = pnpId.Split('\\');

                        // Match the physical monitor by its hardware ID.
                        if (parts.Length < 2 ||
                            !device.DeviceID.Contains(parts[1], StringComparison.OrdinalIgnoreCase))
                            continue;

                        string wmiName = CleanMonitorName(obj["Name"]?.ToString());
                        if (!string.IsNullOrEmpty(wmiName))
                            return wmiName;
                    }
                }
                catch (ManagementException) { /* WMI unavailable: use fallback name. */ }
                catch (COMException) { /* WMI unavailable: use fallback name. */ }
            }

            return name;
        }

        return string.Empty; // UI falls back to "Monitor X".
    }

    private static int GetMonitorNumber(string deviceName)
    {
        const string prefix = @"\\.\DISPLAY";
        return deviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(deviceName[prefix.Length..], out int number)
            ? number
            : int.MaxValue;
    }

    private void LoadMonitors()
    {
        string? previousDevice = (SelectMonitorItem.SelectedItem as ComboBoxItem)?.Tag is MonitorOption previous
            ? previous.DeviceName
            : null;

        var monitors = new List<MonitorOption>();

        bool CollectMonitor(IntPtr monitor, IntPtr hdc, ref LPRECT rect, IntPtr data)
        {
            if (TryGetMonitorInfo(monitor, out MONITORINFOEX info))
            {
                monitors.Add(new MonitorOption
                {
                    Handle = monitor,
                    DeviceName = info.szDevice,
                    FriendlyName = GetMonitorFriendlyName(info.szDevice),
                    Bounds = info.rcMonitor,
                    IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0
                });
            }
            return true;
        }

        bool enumerated = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, CollectMonitor, IntPtr.Zero);
        if (!enumerated || monitors.Count == 0) return;

        SelectMonitorItem.Items.Clear();
        ComboBoxItem? previousItem = null;
        ComboBoxItem? primaryItem = null;

        foreach (MonitorOption monitor in monitors
                     .OrderByDescending(m => m.IsPrimary)
                     .ThenBy(m => GetMonitorNumber(m.DeviceName))
                     .ThenBy(m => m.DeviceName, StringComparer.OrdinalIgnoreCase))
        {
            int width = monitor.Bounds.Right - monitor.Bounds.Left;
            int height = monitor.Bounds.Bottom - monitor.Bounds.Top;

            // Use FriendlyName if available, otherwise fallback to "Monitor X"
            string modelName = string.IsNullOrWhiteSpace(monitor.FriendlyName)
                ? $"Monitor {GetMonitorNumber(monitor.DeviceName)}"
                : monitor.FriendlyName;

            string primary = monitor.IsPrimary ? " (Primary)" : string.Empty;

            var item = new ComboBoxItem
            {
                // This creates the exact format: "LS49AG95 (Primary) - 5120 × 1440"
                Content = $"{modelName}{primary} - {width} × {height}",
                Tag = monitor
            };
            SelectMonitorItem.Items.Add(item);

            if (string.Equals(monitor.DeviceName, previousDevice, StringComparison.OrdinalIgnoreCase))
                previousItem = item;

            if (monitor.IsPrimary)
                primaryItem = item;
        }

        SelectMonitorItem.SelectedItem = previousItem ?? primaryItem ?? SelectMonitorItem.Items[0];
    }

    private bool TryGetTargetMonitorBounds(out LPRECT bounds)
    {
        if ((SelectMonitorItem.SelectedItem as ComboBoxItem)?.Tag is MonitorOption selected &&
            TryGetMonitorInfo(selected.Handle, out MONITORINFOEX info) &&
            string.Equals(selected.DeviceName, info.szDevice, StringComparison.OrdinalIgnoreCase))
        {
            bounds = info.rcMonitor;
            return true;
        }

        bounds = default;
        return false;
    }

    // --- Process & UI Logic ---
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

    private readonly Dictionary<int, ProcessUiState> _processUiStates = new();
    private readonly Dictionary<int, SavedWindowState> _savedOriginalWindowStates = new();

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int GWL_STYLE = -16;

    private List<Process> GetWindowsProcesses()
    {
        return Process.GetProcesses()
            .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle)
                        && p.MainWindowHandle != IntPtr.Zero
                        && p.Id != Environment.ProcessId)
            .OrderBy(p => p.MainWindowTitle)
            .ToList();
    }

    private void LoadProcessesForMenuItems()
    {
        int? selectedProcessId = _selectedProcess?.Id;
        SelectApplicationItem.SelectionChanged -= SelectApplicationItem_SelectionChanged;
        SelectApplicationItem.Items.Clear();

        SelectApplicationItem.Items.Add(new ComboBoxItem
        {
            Content = "Select Application...",
            Foreground = System.Windows.Media.Brushes.LightGray,
            Tag = null
        });

        foreach (Process process in GetWindowsProcesses())
        {
            string label = $"{process.MainWindowTitle} ({process.Id})";
            var item = new ComboBoxItem { Content = label, Tag = process };
            SelectApplicationItem.Items.Add(item);

            if (selectedProcessId == process.Id)
                SelectApplicationItem.SelectedItem = item;
        }

        if (SelectApplicationItem.SelectedItem is null || (SelectApplicationItem.SelectedItem as ComboBoxItem)?.Tag is null)
        {
            SelectApplicationItem.SelectedIndex = 0;
            _selectedProcess = null;
        }

        SelectApplicationItem.SelectionChanged += SelectApplicationItem_SelectionChanged;
    }

    private void SelectApplicationItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Process? newProcess = null;
        if (SelectApplicationItem.SelectedItem is ComboBoxItem item && item.Tag is Process process)
            newProcess = process;

        if (_selectedProcess?.Id != newProcess?.Id)
        {
            _selectedProcess = newProcess;

            if (_selectedProcess != null && _processUiStates.TryGetValue(_selectedProcess.Id, out var state))
            {
                StatusTextBlock.Text = state.StatusText;
                StatusTextBlock.Visibility = state.IsStatusVisible ? Visibility.Visible : Visibility.Hidden;
                HideTaskbarCheckBox.Visibility = state.IsBorderlessApplied ? Visibility.Visible : Visibility.Hidden;
                HideTaskbarCheckBox.IsChecked = state.IsTaskbarCovered;
            }
            else
            {
                StatusTextBlock.Text = string.Empty;
                StatusTextBlock.Visibility = Visibility.Hidden;
                HideTaskbarCheckBox.Visibility = Visibility.Hidden;
                HideTaskbarCheckBox.IsChecked = false;
            }
        }
    }

    private void SelectApplicationItem_OnSubmenuOpened(object sender, EventArgs e)
    {
        LoadProcessesForMenuItems();
    }

    private void BorderlessButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null)
        {
            StatusTextBlock.Text = "Select a process from the dropdown";
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

        if (!int.TryParse(XOffsetInput.Text, out int xOffset) ||
            !int.TryParse(YOffsetInput.Text, out int yOffset) ||
            !int.TryParse(WidthInput.Text, out int width) ||
            !int.TryParse(HeightInput.Text, out int height))
        {
            StatusTextBlock.Text = "Enter valid numbers for all fields";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (width <= 0 || height <= 0)
        {
            StatusTextBlock.Text = "Width and height must be greater than zero";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (!TryGetTargetMonitorBounds(out LPRECT monitorBounds))
        {
            StatusTextBlock.Text = "Unable to determine the target monitor";
            StatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        int actualX = monitorBounds.Left + xOffset;
        int actualY = monitorBounds.Top + yOffset;

        StatusTextBlock.Visibility = Visibility.Hidden;
        SaveUserSettings();

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

        IntPtr currentStyle = GetWindowLongPtr(hWnd, GWL_STYLE);
        IntPtr newStyle = new(currentStyle.ToInt64() & ~(WS_CAPTION | WS_THICKFRAME));

        SetWindowLongPtr(hWnd, GWL_STYLE, newStyle);
        SetWindowPos(hWnd, HWND_NOTOPMOST, actualX, actualY, width, height, SWP_FRAMECHANGED | SWP_SHOWWINDOW);

        _processUiStates[processId] = new ProcessUiState
        {
            IsBorderlessApplied = true,
            IsTaskbarCovered = false,
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

        SetWindowPos(hWnd, HWND_NOTOPMOST, saved.OriginalRect.Left, saved.OriginalRect.Top, width, height, SWP_FRAMECHANGED | SWP_SHOWWINDOW);

        _savedOriginalWindowStates.Remove(_selectedProcess.Id);
        _processUiStates.Remove(_selectedProcess.Id);

        StatusTextBlock.Text = "Window restored";
        StatusTextBlock.Visibility = Visibility.Visible;
        HideTaskbarCheckBox.Visibility = Visibility.Hidden;
        HideTaskbarCheckBox.IsChecked = false;
    }

    private void CoverTaskbarCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null) return;
        _selectedProcess.Refresh();
        IntPtr hWnd = _selectedProcess.MainWindowHandle;
        if (hWnd == IntPtr.Zero) return;

        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);
        if (_processUiStates.TryGetValue(_selectedProcess.Id, out var state))
            state.IsTaskbarCovered = true;
    }

    private void CoverTaskbarCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_selectedProcess is null) return;
        _selectedProcess.Refresh();
        IntPtr hWnd = _selectedProcess.MainWindowHandle;
        if (hWnd == IntPtr.Zero) return;

        SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);
        if (_processUiStates.TryGetValue(_selectedProcess.Id, out var state))
            state.IsTaskbarCovered = false;
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
        RestoreButton_OnClick(sender, e);
        Application.Current.Shutdown();
    }
}