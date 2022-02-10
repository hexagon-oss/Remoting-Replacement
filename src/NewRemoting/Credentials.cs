using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
	/// <summary>
	/// Encapsulates user information for accessing remote computers or resources of different users.
	/// </summary>
	public class Credentials : IEquatable<Credentials>
	{
		/// <summary>
		/// Constant for no credentials
		/// </summary>
		public static readonly Credentials None = new Credentials(null, null, null);

		/// <summary>
		/// For marshalling.
		/// </summary>
		private Credentials()
		{
		}

		/// <summary>
		/// Constructor, use factory methods to create instances of that type.
		/// </summary>
		private Credentials(string username, string password, string domainName)
		{
			Username = username;
			Password = password;
			DomainName = domainName;
		}

		/// <summary>
		/// The username. NULL if not set.
		/// </summary>
		public string? Username
		{
			get;
			private set;
		}

		/// <summary>
		/// Returns the domain qualified user name "Domain\UserName"
		/// </summary>
		public string DomainQualifiedUsername
		{
			get
			{
				return Equals(None) ? string.Empty : string.Format(CultureInfo.InvariantCulture, "{0}\\{1}", DomainName, Username);
			}
		}

		/// <summary>
		/// The corresponding password. NULL if not set.
		/// </summary>
		public string? Password
		{
			get;
			private set;
		}

		/// <summary>
		/// The domain / computer name of the user account.
		/// </summary>
		public string? DomainName
		{
			get;
			private set;
		}

		/// <summary>
		/// Creates remote credentials for a username and a corresponding password
		/// </summary>
		public static Credentials CreateLocal(string username, string password)
		{
			return Create(username, password, Environment.MachineName);
		}

		/// <summary>
		/// Creates remote credentials for a username and a corresponding password, also supply domain or computer name of user account
		/// </summary>
		/// <exception cref="ArgumentNullException">Parameter was null</exception>
		public static Credentials Create(string username, string password, string domainName)
		{
			return new Credentials(username ?? string.Empty, password ?? string.Empty, domainName ?? string.Empty);
		}

		public bool Equals(Credentials? other)
		{
			if (other == null)
			{
				return false;
			}

			return string.Equals(Password, other.Password) && string.Equals(DomainName, other.DomainName) && string.Equals(Username, other.Username);
		}

		public override bool Equals(object? obj)
		{
			if (ReferenceEquals(null, obj))
			{
				return false;
			}

			if (ReferenceEquals(this, obj))
			{
				return true;
			}

			if (obj.GetType() != GetType())
			{
				return false;
			}

			return Equals((Credentials)obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = Password != null ? Password.GetHashCode() : 0;
				hashCode = (hashCode * 397) ^ (DomainName != null ? DomainName.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (Username != null ? Username.GetHashCode() : 0);
				return hashCode;
			}
		}

		public void Impersonate(Action task)
		{
			if (Equals(None))
			{
				task.Invoke();
				return;
			}

			Impersonator.RunImpersonated(Username, DomainName, Password, task);
		}
	}
}
