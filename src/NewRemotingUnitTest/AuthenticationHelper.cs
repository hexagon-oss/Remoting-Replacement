using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Security;
using NewRemoting;
using NUnit.Framework;

namespace NewRemotingUnitTest
{
	internal class AuthenticationHelper
	{
		private string _mCertificateFileName;
		private string _mConfigFile;

		public string ConfigFile
		{
			get => _mConfigFile;
			set => _mConfigFile = value;
		}

		public string CertificateFileName
		{
			get => _mCertificateFileName;
			set => _mCertificateFileName = value;
		}

		public string CertificatePassword { get; set; } = "unittests";

		public AuthenticationHelper()
		{
		}

		public static X509Certificate2 CreateCertificate(string exportFileName, string password, DateTimeOffset notBefore, bool addStore = true, bool export = true, bool rsa = true)
		{
			try
			{
				// create a key pair using the default parameters
				CertificateRequest certificateRequest = null;
				AsymmetricAlgorithm algorithm = null;
				if (rsa)
				{
					algorithm = RSA.Create();
					certificateRequest = new CertificateRequest("cn=localhost", algorithm as RSA, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
				}
				else
				{
					algorithm = ECDsa.Create();
					certificateRequest = new CertificateRequest("cn=localhost", algorithm as ECDsa, HashAlgorithmName.SHA256);
				}

				X509Certificate2 cert = certificateRequest.CreateSelfSigned(notBefore, DateTimeOffset.UtcNow.AddDays(365 * 100));
				cert.FriendlyName = "RemoteLoaderClientTest";
				if (export)
				{
					var bytearray = cert.Export(X509ContentType.Pfx, password);
					File.WriteAllBytes(exportFileName, bytearray);
				}

				if (addStore)
				{
					X509Store store = new X509Store("MY", StoreLocation.CurrentUser);
					store.Open(OpenFlags.ReadWrite);
					store.Add(cert);
					store.Close();
				}

				return cert;
			}
			catch (Exception exception) when (exception is CryptographicException or
												  SecurityException)
			{
				throw;
			}
		}

		public static void UpdateConfigurationFile(string dllConfig, string exportFileName, string password)
		{
			var baseName = Path.GetFileNameWithoutExtension(dllConfig);
			var configuration = System.Configuration.ConfigurationManager.OpenExeConfiguration(Path.Join(AppDomain.CurrentDomain.BaseDirectory, baseName));
			var appSettings = configuration.AppSettings;
			var keys = appSettings.Settings.AllKeys;
			Tuple<string, string>[] expectedkeys = { new Tuple<string, string>("CertificateFileName", exportFileName), new Tuple<string, string>("CertificatePassword", password) };
			foreach (var key in expectedkeys)
			{
				if (keys.Contains(key.Item1))
				{
					configuration.AppSettings.Settings[key.Item1].Value = key.Item2;
				}
				else
				{
					throw new InvalidDataException();
				}
			}

			configuration.Save();
		}

		public static Tuple<string, AuthenticationInformation> CreateAuthenticationInfo(bool addStore = true, bool export = true, bool rsa = true)
		{
			var filename = Path.GetTempFileName();
			var password = "blabla";
			CreateCertificate(filename, password, DateTimeOffset.UtcNow, addStore, export, rsa);
			var pathName = AppDomain.CurrentDomain.BaseDirectory;
			var configFile = Directory.EnumerateFiles(pathName, "RemotingServer.dll.config");
			var enumerable = configFile.ToList();
			if (!enumerable.Any())
			{
				throw new InvalidDataException($"Config file not found in {pathName}");
			}

			var configurationFile = enumerable.ToList().First();
			UpdateConfigurationFile(configurationFile, filename, password);
			return new Tuple<string, AuthenticationInformation>(configurationFile, new AuthenticationInformation(filename, password));
		}

		public static void CleanupCertificate(string dllConfig)
		{
			UpdateConfigurationFile(dllConfig, string.Empty, string.Empty);
			X509Store store = new X509Store("MY", StoreLocation.CurrentUser);
			var ourCertificates = store.Certificates.Where(x => x.FriendlyName == "RemotingUnitTests");
			store.Open(OpenFlags.ReadWrite);
			foreach (var cert in ourCertificates)
			{
				store.Remove(cert);
			}

			store.Close();
		}

		public void SetUp()
		{
			_mCertificateFileName = Path.GetTempFileName();
			CreateCertificate(_mCertificateFileName, CertificatePassword, DateTimeOffset.UtcNow);

			var configFile = Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, "RemotingServer.dll.config").ToList();
			Assert.IsTrue(configFile.Any(), "Remotingserver.dll.config missing");

			_mConfigFile = configFile.First();
			UpdateConfigurationFile(_mConfigFile, _mCertificateFileName, CertificatePassword);
		}

		public void UpdateFile()
		{
			UpdateConfigurationFile(_mConfigFile, _mCertificateFileName, CertificatePassword);
		}

		public void TearDown()
		{
			CleanupCertificate(ConfigFile);
		}
	}
}
