using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LibDmd.Common
{
	public static class InteropUtil
	{
		private static readonly Lazy<bool> _isRunningOnWine = new Lazy<bool>(DetectWine);

		/// <summary>
		/// True when running under Wine or Proton, where some window features don't
		/// behave like on Windows.
		/// </summary>
		public static bool IsRunningOnWine => _isRunningOnWine.Value;

		private static bool DetectWine()
		{
			try {
				var ntdll = GetModuleHandle("ntdll.dll");
				if (ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero) {
					return true;
				}
				using (var key = Registry.LocalMachine.OpenSubKey(@"Software\Wine")) {
					return key != null;
				}
			} catch (Exception) {
				return false;
			}
		}

		[DllImport("kernel32", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandle(string moduleName);

		[DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
		private static extern IntPtr GetProcAddress(IntPtr module, string procName);

		public static ushort[] ReadUInt16Array(IntPtr data, int length)
		{
			var buffer = new ushort[length];
			var uint16Buffer = new byte[length * 2];
			Marshal.Copy(data, uint16Buffer, 0, length * 2);
			var pos = 0;
			for (var i = 0; i < length * 2; i += 2) {
				buffer[pos++] = BitConverter.ToUInt16(uint16Buffer, i);
			}
			return buffer;
		}
	}
}