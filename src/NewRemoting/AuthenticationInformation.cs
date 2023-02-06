using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	/// <summary>
	/// Container for authentication information such as certificate name and password.
	/// This can gather the authentication informations for a client or a server.
	///
	public class AuthenticationInformation
	{
		private string _certificateFileName;
		private string _certificatePassword;

		public AuthenticationInformation(string certificateFileName, string certificatePassword)
		{
			CertificateFileName = certificateFileName;
			CertificatePassword = certificatePassword;
		}

		public string CertificateFileName { get => _certificateFileName; set => _certificateFileName = value; }
		public string CertificatePassword { get => _certificatePassword; set => _certificatePassword = value; }
	}
}
