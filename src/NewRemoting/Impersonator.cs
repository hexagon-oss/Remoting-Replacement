using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NewRemoting
{
	/// <summary>
	/// Impersonation of a user. Allows to execute code under another
	/// user context.
	/// Please note that the account that instantiates the Impersonator class
	/// needs to have the 'Act as part of operating system' privilege set.
	/// </summary>
	/// <remarks>
	/// This class is based on the information in the Microsoft knowledge base
	/// article http://support.microsoft.com/default.aspx?scid=kb;en-us;Q306158
	///
	/// </remarks>
	internal static class Impersonator
	{
		/// <summary>
		/// Executes the given task with the credentials of another user.
		/// </summary>
		/// <param name="userName">The name of the user to act as.</param>
		/// <param name="domain">The domain name of the user to act as.</param>
		/// <param name="password">The password of the user to act as.</param>
		/// <param name="task">The action to execute</param>
		/// <exception cref="Win32Exception">Something went wrong (i.e wrong password)</exception>
		public static void RunImpersonated(string? userName, string? domain, string? password, Action task)
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("This is currently only supported on Windows");
			}

			SafeAccessTokenHandle token = SafeAccessTokenHandle.InvalidHandle;

			try
			{
				if (NativeMethods.RevertToSelf())
				{
					if (NativeMethods.LogonUser(
						userName,
						domain,
						password,
						NativeMethods.LOGON32_LOGON_NEW_CREDENTIALS,
						NativeMethods.LOGON32_PROVIDER_DEFAULT,
						ref token) != 0)
					{
						WindowsIdentity.RunImpersonated(token, task);
					}
					else
					{
						throw new Win32Exception(Marshal.GetLastWin32Error());
					}
				}
				else
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
			}
			finally
			{
				if (token != SafeAccessTokenHandle.InvalidHandle)
				{
					token.Dispose();
				}
			}
		}
	}
}
