using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;



namespace PdfReader
{
    internal sealed class ReaderWindowPlacement
    {
        private readonly Window _window;
        private Rect _bounds;
        private WindowState _state;
        private bool _showInTaskbar;
        private bool _wasTopmost;
        private HwndSource _readingSource;
        private bool _captured, _changed;
        private readonly Action _close;
        private bool _nativeCaption;
        private Panel _controlsParent;
        private StackPanel _fallbackControls;
        private HwndSource _workAreaSource;
        private WindowStyle _style;
        private ResizeMode _resize;
        private System.Windows.Shell.WindowChrome _chrome;

        private FrameworkElement _toolbar;
        private Thickness _toolbarMargin;
        private bool _windowed;
        internal ReaderWindowPlacement(Window window, Action close = null)
        { _window = window; _close = close; }
        internal void Capture()
        {
            if (_window == null || _captured) return;
            _state = _window.WindowState;
            _showInTaskbar = _window.ShowInTaskbar;
            _wasTopmost = _window.Topmost;
            _style = _window.WindowStyle; _resize = _window.ResizeMode;
            _chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(_window);
            _bounds = _state == WindowState.Normal ? new Rect(_window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight) : _window.RestoreBounds;
            _captured = true;
            _toolbar = _window.FindName("ViewboxFloatingBar") as FrameworkElement;
            if (_toolbar != null) _toolbarMargin = _toolbar.Margin;
        }

        internal void KeepReadingWindowNonTopmost()
        {
            if (!_captured || _readingSource != null) return;
            _readingSource = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
            if (_readingSource == null) return;
            _readingSource.AddHook(ReadingZOrderHook);
            _window.SetCurrentValue(Window.TopmostProperty, false);
            SetWindowPos(_readingSource.Handle, HwndNotTopmost, 0, 0, 0, 0, ZOrderOnly);
        }

        private static IntPtr ReadingZOrderHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmWindowPosChanging = 0x0046;
            const int WmStyleChanging = 0x007C;
            const int GwlExStyle = -20;
            const uint WsExTopmost = 0x00000008;
            const uint SwpNoZOrder = 0x0004;
            if (lParam == IntPtr.Zero) return IntPtr.Zero;

            // Older ICCCE hosts maintain topmost through both SetWindowPos
            // and extended styles. Filter only those requests on the reader HWND.
            if (message == WmWindowPosChanging)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                if (position.InsertAfter == HwndTopmost && (position.Flags & SwpNoZOrder) == 0)
                {
                    position.Flags |= SwpNoZOrder;
                    Marshal.StructureToPtr(position, lParam, false);
                }
            }
            else if (message == WmStyleChanging && wParam.ToInt64() == GwlExStyle)
            {
                var styles = Marshal.PtrToStructure<WindowStyles>(lParam);
                if ((styles.NewStyle & WsExTopmost) != 0)
                {
                    styles.NewStyle &= ~WsExTopmost;
                    Marshal.StructureToPtr(styles, lParam, false);
                }
            }
            return IntPtr.Zero;
        }
        internal void ShowWindowed(bool maximize = false)
        {
            if (!_captured || _window == null) return;
            var handle = new WindowInteropHelper(_window).Handle;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
            var source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget == null) return;
            var transform = source.CompositionTarget.TransformFromDevice;
            var area = new Rect(transform.Transform(new Point(monitor.Work.Left, monitor.Work.Top)),
                transform.Transform(new Point(monitor.Work.Right, monitor.Work.Bottom)));
            _changed = true;
            Apply(new Rect(area.X + area.Width * .05, area.Y + area.Height * .05, area.Width * .9, area.Height * .9));
            _windowed = true;
            ShowControls();
            _window.SetCurrentValue(Window.ShowInTaskbarProperty, true);
            _window.LayoutUpdated -= KeepToolbarVisible;
            _window.LayoutUpdated += KeepToolbarVisible;
            if (maximize) _window.WindowState = WindowState.Maximized;
            KeepToolbarVisible(null, EventArgs.Empty);
        }
        private void ShowControls()
        {
            if (_nativeCaption || _fallbackControls != null) return;
            if (_window.AllowsTransparency)
            {
                AttachWorkAreaLimit();
                ShowTransparentWindowControls();
                return;
            }
            _nativeCaption = true;
            System.Windows.Shell.WindowChrome.SetWindowChrome(_window, null);
            _window.ResizeMode = ResizeMode.CanResize;
            _window.WindowStyle = WindowStyle.SingleBorderWindow;
            _window.Closing += WindowClosing;
        }
        private void AttachWorkAreaLimit()
        {
            if (_workAreaSource != null) return;
            _workAreaSource = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
            _workAreaSource?.AddHook(WindowedWorkAreaHook);
        }

        // Borderless layered windows otherwise maximize over the taskbar. Keep
        // this limit local to windowed PDF reading; fullscreen and the desktop
        // overlay must recover their original monitor-sized bounds.
        private IntPtr WindowedWorkAreaHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmGetMinMaxInfo = 0x0024;
            if (!_windowed || message != WmGetMinMaxInfo || lParam == IntPtr.Zero) return IntPtr.Zero;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor)) return IntPtr.Zero;
            int width = monitor.Work.Right - monitor.Work.Left;
            int height = monitor.Work.Bottom - monitor.Work.Top;
            if (width <= 0 || height <= 0) return IntPtr.Zero;
            var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            // Win32 expects physical pixels relative to this monitor, not WPF
            // DIPs or absolute desktop coordinates (which may be negative).
            limits.MaxPosition = new NativePoint {
                X = monitor.Work.Left - monitor.Monitor.Left,
                Y = monitor.Work.Top - monitor.Monitor.Top };
            limits.MaxSize = new NativePoint { X = width, Y = height };
            Marshal.StructureToPtr(limits, lParam, false);
            handled = true;
            return IntPtr.Zero;
        }
        // A WPF per-pixel window cannot have a native caption. Never ask the
        // user to enable a compositor that can block all other applications.
        // Keep window operations available on existing transparent hosts.
        private void ShowTransparentWindowControls()
        {
            if (!(_window.Content is Panel parent)) return;
            _controlsParent = parent;
            _fallbackControls = new StackPanel {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8), Background = Brushes.DimGray };
            void Add(string symbol, string label, Action action)
            {
                var button = new Button { Content = symbol, ToolTip = label,
                    Width = 42, Height = 32, Foreground = Brushes.White, Background = Brushes.Transparent };
                System.Windows.Automation.AutomationProperties.SetName(button, label);
                button.Click += (_, __) => action();
                _fallbackControls.Children.Add(button);
            }
            Add("−", Strings.MinimizeWindow, () => _window.WindowState = WindowState.Minimized);
            Add("□", Strings.MaximizeRestoreWindow, () => _window.WindowState =
                _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            Add("×", Strings.Close, () => _close?.Invoke());
            Panel.SetZIndex(_fallbackControls, int.MaxValue);
            parent.Children.Add(_fallbackControls);
        }
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_windowed || _close == null) return;
            e.Cancel = true;
            _close();
        }
        private void KeepToolbarVisible(object sender, EventArgs e)
        {
            if (!_windowed || _toolbar == null || _toolbar.Visibility != Visibility.Visible
                || !(_toolbar.Parent is FrameworkElement parent) || parent.ActualWidth <= 0 || parent.ActualHeight <= 0) return;
            var margin = _toolbar.Margin;
            double x = Math.Max(0, (parent.ActualWidth - _toolbar.ActualWidth) / 2);
            double y = Math.Max(0, Math.Min(margin.Top, Math.Max(0, parent.ActualHeight - _toolbar.ActualHeight - 8)));
            if (Math.Abs(x - margin.Left) < .1 && Math.Abs(y - margin.Top) < .1) return;
            _toolbar.BeginAnimation(FrameworkElement.MarginProperty, null);
            _toolbar.SetCurrentValue(FrameworkElement.MarginProperty, new Thickness(x, y, margin.Right, margin.Bottom));
        }
        internal void HideWindowedControls()
        {
            bool wasWindowed = _windowed;
            _windowed = false;
            _workAreaSource?.RemoveHook(WindowedWorkAreaHook);
            _workAreaSource = null;
            if (_window != null) _window.LayoutUpdated -= KeepToolbarVisible;
            if (_fallbackControls != null) _controlsParent?.Children.Remove(_fallbackControls);
            _fallbackControls = null;
            _controlsParent = null;
            RestoreCaption();
            if (wasWindowed) _window.SetCurrentValue(Window.ShowInTaskbarProperty, _showInTaskbar);
            if (_toolbar != null) _toolbar.SetCurrentValue(FrameworkElement.MarginProperty, _toolbarMargin);
        }
        private void RestoreCaption()
        {
            if (!_nativeCaption) return;
            _window.Closing -= WindowClosing;
            _window.WindowStyle = _style; _window.ResizeMode = _resize;
            System.Windows.Shell.WindowChrome.SetWindowChrome(_window, _chrome);
            _nativeCaption = false;
        }
        private void Apply(Rect bounds)
        {
            _window.WindowState = WindowState.Normal;
            _window.Left = bounds.X; _window.Top = bounds.Y;
            _window.Width = bounds.Width; _window.Height = bounds.Height;
        }
        internal void Restore()
        {
            try
            {
                HideWindowedControls();
                if (_changed && !_bounds.IsEmpty && _bounds.Width > 0 && _bounds.Height > 0)
                { Apply(_bounds); _window.WindowState = _state; }
            }
            finally
            {
                var source = _readingSource;
                _readingSource = null;
                _captured = _changed = false;
                if (source != null && !source.IsDisposed)
                {
                    source.RemoveHook(ReadingZOrderHook);
                    _window.SetCurrentValue(Window.TopmostProperty, _wasTopmost);
                    SetWindowPos(source.Handle, _window.Topmost ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0, ZOrderOnly);
                }
            }
        }
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly IntPtr HwndNotTopmost = new IntPtr(-2);
        private const uint ZOrderOnly = 0x0001 | 0x0002 | 0x0010 | 0x0200; // No size, move, activation, or owner Z-order change.
        [StructLayout(LayoutKind.Sequential)] private struct WindowPosition
        {
            public IntPtr Window, InsertAfter;
            public int X, Y, Width, Height;
            public uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)] private struct WindowStyles { public uint OldStyle, NewStyle; }
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
        {
            public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }
}


