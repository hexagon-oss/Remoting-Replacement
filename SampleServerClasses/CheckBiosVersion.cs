using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	/// <summary>
	/// This class uses a reference to System.Management, which is very complex to resolve
	/// (because it has an OS-dependent implementation). This test validates that this works.
	/// </summary>
	public class CheckBiosVersion : MarshalByRefObject, IDisposable
	{
		private readonly ManagementObjectSearcher m_managementInfo;

		public CheckBiosVersion()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				throw new PlatformNotSupportedException("This works on Windows only");
			}

			m_managementInfo = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
		}

		public CheckBiosVersion(ManagementObjectSearcher searcher)
		{
			m_managementInfo = searcher ?? throw new ArgumentNullException(nameof(searcher));
		}

		public virtual string[] GetBiosVersions()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				throw new PlatformNotSupportedException("This works on Windows only");
			}

			var collection = m_managementInfo.Get();
			foreach (var obj in collection)
			{
				if (obj["BIOSVersion"] is string[] versionStrings && versionStrings.Any())
				{
					return versionStrings;
				}
			}

			return null;
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				m_managementInfo?.Dispose();
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}
}
