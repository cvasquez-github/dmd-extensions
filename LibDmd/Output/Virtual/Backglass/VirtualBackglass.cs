using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibDmd.Common;
using NLog;

namespace LibDmd.Output.Virtual.Backglass
{
	/// <summary>
	/// A borderless window showing a still image, e.g. the backglass of the running game.
	/// </summary>
	///
	/// <remarks>
	/// The window runs on its own UI thread and never takes the focus from the game. It
	/// isn't kept on top, so a virtual DMD with "stay on top" can be placed over it.
	/// </remarks>
	public class VirtualBackglass : IDisposable
	{
		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

		private Window _window;
		private Image _image;

		// incremented by every SetImage(), so only the image of the latest call is shown
		private int _imageVersion;

		public VirtualBackglass(double left, double top, double width, double height)
		{
			// Wine marks a borderless window covering a whole monitor as full screen, and KWin
			// keeps full screen windows above "stay on top" windows like the virtual DMD while the
			// active window (the game) is on another monitor. One pixel less keeps it a normal window.
			if (InteropUtil.IsRunningOnWine && GetCoveredMonitor(left, top, width, height) is Rect32 monitor) {
				height = monitor.Bottom - 1 - top;
				Logger.Info("Wine detected, leaving out the last row of the monitor so the backglass window stays below the virtual DMD.");
			}

			using (var ready = new ManualResetEventSlim()) {
				var thread = new Thread(() => {
					try {
						SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
						_image = new Image { Stretch = Stretch.Uniform };
						RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
						_window = new Window {
							Title = "Backglass",
							WindowStyle = WindowStyle.None,
							ResizeMode = ResizeMode.NoResize,
							WindowStartupLocation = WindowStartupLocation.Manual,
							ShowInTaskbar = false,
							ShowActivated = false,
							Background = Brushes.Black,
							Left = left,
							Top = top,
							Width = width,
							Height = height,
							Content = _image
						};
						_window.SourceInitialized += (sender, e) => PreventActivation(_window);
						_window.Show();
					} catch (Exception e) {
						Logger.Error(e, "Could not open backglass window.");
						_window = null;
					} finally {
						ready.Set();
					}
					if (_window != null) {
						Dispatcher.Run();
					}
				});
				thread.Name = "Backglass";
				thread.IsBackground = true; // don't keep dmdext running
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();
				ready.Wait();
			}
			if (_window != null) {
				Logger.Info("Opened backglass window at {0}x{1}@{2}/{3}.", width, height, left, top);
			}
		}

		/// <summary>
		/// Shows the image at the given path, or nothing (black) if the path is null.
		/// </summary>
		///
		/// <remarks>
		/// The image is loaded in the background, so this doesn't block the caller.
		/// </remarks>
		public void SetImage(string path)
		{
			if (_window == null) {
				return;
			}
			var version = Interlocked.Increment(ref _imageVersion);
			ThreadPool.QueueUserWorkItem(_ => {
				var bitmap = path == null ? null : LoadImage(path);
				_window.Dispatcher.BeginInvoke(new Action(() => {
					if (version == Volatile.Read(ref _imageVersion)) {
						_image.Source = bitmap;
					}
				}));
			});
		}

		private static BitmapSource LoadImage(string path)
		{
			try {
				var bitmap = new BitmapImage();
				bitmap.BeginInit();
				// load right away and freeze, so the window's thread can use it
				bitmap.CacheOption = BitmapCacheOption.OnLoad;
				bitmap.UriSource = new Uri(path);
				bitmap.EndInit();
				bitmap.Freeze();
				return bitmap;

			} catch (Exception e) {
				Logger.Warn("Could not load backglass image {0}: {1}", path, e.Message);
				return null;
			}
		}

		/// <summary>
		/// Keeps the window from being activated, e.g. when clicked, so the game keeps the
		/// focus. Also hides it from the task switcher.
		/// </summary>
		private static void PreventActivation(Window window)
		{
			var hwnd = new WindowInteropHelper(window).Handle;
			var style = GetWindowLong(hwnd, GWL_EXSTYLE);
			SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
		}

		public void Dispose()
		{
			_window?.Dispatcher.BeginInvoke(new Action(() => {
				_window.Close();
				Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
			}));
		}

		/// <summary>
		/// Returns the bounds of the monitor the given rectangle fully covers, or null if it
		/// doesn't cover one.
		/// </summary>
		private static Rect32? GetCoveredMonitor(double left, double top, double width, double height)
		{
			var rect = new Rect32 {
				Left = (int)left,
				Top = (int)top,
				Right = (int)(left + width),
				Bottom = (int)(top + height)
			};
			var handle = MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL);
			if (handle == IntPtr.Zero) {
				return null;
			}
			var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
			if (!GetMonitorInfo(handle, ref info)) {
				return null;
			}
			var monitor = info.Monitor;
			var covers = rect.Left <= monitor.Left && rect.Top <= monitor.Top && rect.Right >= monitor.Right && rect.Bottom >= monitor.Bottom;
			return covers ? monitor : (Rect32?)null;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct Rect32
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct MonitorInfo
		{
			public int Size;
			public Rect32 Monitor;
			public Rect32 WorkArea;
			public uint Flags;
		}

		private const int MONITOR_DEFAULTTONULL = 0;

		[DllImport("user32.dll")]
		private static extern IntPtr MonitorFromRect(ref Rect32 rect, int flags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

		private const int GWL_EXSTYLE = -20;
		private const int WS_EX_TOOLWINDOW = 0x00000080;
		private const int WS_EX_NOACTIVATE = 0x08000000;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
	}
}
