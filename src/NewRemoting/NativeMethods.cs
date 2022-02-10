using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace NewRemoting
{
	internal static class NativeMethods
	{
		public const int LOGON32_PROVIDER_DEFAULT = 0;
		public const int LOGON32_LOGON_NEW_CREDENTIALS = 9;
		public const int SECURITY_IMPERSONATION_LEVEL_IMPERSONATION = 2;

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		public static extern int LogonUser(string? lpszUserName, string? lpszDomain, string? lpszPassword, int dwLogonType,
			int dwLogonProvider, ref SafeAccessTokenHandle phToken);

		[DllImport("advapi32.dll", SetLastError = true)]
		public static extern int DuplicateToken(IntPtr hToken, int impersonationLevel, ref IntPtr hNewToken);

		[DllImport("advapi32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool RevertToSelf();
	}
}
