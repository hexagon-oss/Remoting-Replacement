using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace NewRemoting
{
	/// <summary>
	/// Exception for all remote access related stuff.
	/// </summary>
	[ExcludeFromCodeCoverage]
	[Serializable]
	public sealed class RemoteAccessException : Exception
	{
		private ErrorReason _errorReason = ErrorReason.Unknown;

		public RemoteAccessException()
		{
		}

		public RemoteAccessException(ErrorReason reason)
		{
			_errorReason = reason;
		}

		public RemoteAccessException(string message)
			: base(message)
		{
		}

		public RemoteAccessException(string message, Exception inner)
			: this(message, inner, ErrorReason.Unknown)
		{
		}

		public RemoteAccessException(string message, Exception inner, ErrorReason errorReason)
			: base(message, inner)
		{
			_errorReason = errorReason;
		}

		private RemoteAccessException(SerializationInfo info, StreamingContext context)
			: base(info, context)
		{
			ErrorCode = (ErrorReason)info.GetValue(nameof(ErrorCode), typeof(ErrorReason));
		}

		public enum ErrorReason
		{
			Unknown,
			Timeout,
			WmiProviderNotInstalled,
			WmiConnectionFailed
		}

		public ErrorReason ErrorCode
		{
			get
			{
				return _errorReason;
			}
			private set
			{
				_errorReason = value;
			}
		}

		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue(nameof(ErrorCode), ErrorCode);
			base.GetObjectData(info, context);
		}
	}
}
