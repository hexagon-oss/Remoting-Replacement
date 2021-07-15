using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;

// BinaryFormatter shouldn't be used
#pragma warning disable SYSLIB0011
namespace NewRemoting
{
	internal sealed class ClientSideInterceptor : IInterceptor, IProxyGenerationHook, IDisposable
	{
		private readonly string _side;
		private readonly Stream _serverLink;
		private readonly MessageHandler _messageHandler;
		private readonly ILogger _logger;
		private int _sequence;
		private ConcurrentDictionary<int, CallContext> _pendingInvocations;
		private Thread _receiverThread;
		private bool _receiving;
		private int _numberOfCallsInspected;

		public ClientSideInterceptor(String side, Stream serverLink, MessageHandler messageHandler, ILogger logger)
		{
			DebuggerToStringBehavior = DebuggerToStringBehavior.ReturnProxyName;
			_sequence = side == "Client" ? 1 : 10000;
			_side = side;
			_serverLink = serverLink;
			_numberOfCallsInspected = 0;
			_messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
			_logger = logger;
			_pendingInvocations = new();
			_receiverThread = new Thread(ReceiverThread);
			_receiverThread.Name = "ClientSideInterceptor - Receiver - " + side;
			_receiving = true;
			_receiverThread.Start();
		}

		public DebuggerToStringBehavior DebuggerToStringBehavior
		{
			get;
			set;
		}

		internal int NextSequenceNumber()
		{
			return Interlocked.Increment(ref _sequence);
		}

		public void Intercept(IInvocation invocation)
		{
			if (_receiverThread == null)
			{
				throw new ObjectDisposedException("Remoting infrastructure has been shut down. Remote proxies are no longer valid");
			}

			string methodName = invocation.Method.ToString();

			// Todo: Check this stuff
			if (methodName == "ToString()" && DebuggerToStringBehavior != DebuggerToStringBehavior.EvaluateRemotely)
			{
				invocation.ReturnValue = "Remote proxy";
				return;
			}

			CleanStaleReferences();

			int thisSeq = NextSequenceNumber();

			using MemoryStream rawDataMessage = new MemoryStream(1024);
			using BinaryWriter writer = new BinaryWriter(rawDataMessage, Encoding.Unicode);

			using CallContext ctx = CreateCallContext(invocation, thisSeq);

			lock (_messageHandler.CommunicationLinkLock)
			{
				_logger.Log(LogLevel.Debug, $"{_side}: Intercepting {invocation.Method}, sequence {thisSeq}");

				// Console.WriteLine($"Here should be a call to {invocation.Method}");
				MethodInfo me = invocation.Method;
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.MethodCall, thisSeq);

				if (me.IsStatic)
				{
					throw new RemotingException("Remote-calling a static method? No.", RemotingExceptionKind.UnsupportedOperation);
				}

				if (!_messageHandler.InstanceManager.TryGetObjectId(invocation.Proxy, out var remoteInstanceId))
				{
					// One valid case when we may get here is when the proxy is just being created (as a class proxy) and within that ctor,
					// a virtual member function is called. So we can execute the call locally (the object should be in an useful state, since its default ctor
					// has been called)
					_logger.Log(LogLevel.Debug, "Not a valid remoting proxy. Assuming within ctor of class proxy");
					invocation.Proceed();
					_pendingInvocations.TryRemove(thisSeq, out _);
					return;
				}

				hd.WriteTo(writer);
				writer.Write(remoteInstanceId);
				// Also transmit the type of the calling object (if the method is called on an interface, this is different from the actual object)
				if (me.DeclaringType != null)
				{
					writer.Write(me.DeclaringType.AssemblyQualifiedName);
				}
				else
				{
					writer.Write(string.Empty);
				}

				writer.Write(me.MetadataToken);
				if (me.ContainsGenericParameters)
				{
					// This should never happen (or the compiler has done something wrong)
					throw new RemotingException("Cannot call methods with open generic arguments", RemotingExceptionKind.UnsupportedOperation);
				}

				var genericArgs = me.GetGenericArguments();
				writer.Write((int)genericArgs.Length);
				foreach (var genericType in genericArgs)
				{
					string arg = genericType.AssemblyQualifiedName;
					if (arg == null)
					{
						throw new RemotingException("Unresolved generic type or some other undefined case", RemotingExceptionKind.UnsupportedOperation);
					}

					writer.Write(arg);
				}

				writer.Write(invocation.Arguments.Length);

				foreach (var argument in invocation.Arguments)
				{
					_messageHandler.WriteArgumentToStream(writer, argument);
				}

				// now finally write the stream to the network. That way, we don't send incomplete messages if an exception happens encoding a parameter.
				rawDataMessage.Position = 0;
				rawDataMessage.CopyTo(_serverLink);
			}

			WaitForReply(invocation, ctx);
		}

		/// <summary>
		/// Informs the server about stale object references.
		/// This method must only be called when the connection is in idle state!
		/// </summary>
		private void CleanStaleReferences()
		{
			if (_numberOfCallsInspected++ % 100 == 0)
			{
				using MemoryStream rawDataMessage = new MemoryStream(1024);
				using BinaryWriter writer = new BinaryWriter(rawDataMessage, Encoding.Unicode);
				_messageHandler.InstanceManager.PerformGc(writer);
				rawDataMessage.CopyTo(_serverLink);
			}
		}

		internal CallContext CreateCallContext(IInvocation invocation, int thisSeq)
		{
			CallContext ctx = new CallContext(invocation);
			if (!_pendingInvocations.TryAdd(thisSeq, ctx))
			{
				// This really shouldn't happen
				throw new InvalidOperationException("A call with the same id is already being processed");
			}

			return ctx;
		}

		internal void WaitForReply(IInvocation invocation, CallContext ctx)
		{
			// The event is signaled by the receiver thread when the message was processed
			ctx.Wait();
			_logger.Log(LogLevel.Debug, $"{_side}: {invocation.Method} done.");
			if (ctx.Exception != null)
			{
				// Rethrow remote exception
				throw ctx.Exception;
			}
		}

		private void ReceiverThread()
		{
			var reader = new BinaryReader(_serverLink, Encoding.Unicode);
			try
			{
				while (_receiving)
				{
					RemotingCallHeader hdReturnValue = default;
					// This read is blocking
					if (!hdReturnValue.ReadFrom(reader))
					{
						throw new RemotingException("Unexpected reply or stream out of sync", RemotingExceptionKind.ProtocolError);
					}

					_logger.Log(LogLevel.Debug, $"{_side}: Decoding message {hdReturnValue.Sequence} of type {hdReturnValue.Function}");

					if (hdReturnValue.Function != RemotingFunctionType.MethodReply && hdReturnValue.Function != RemotingFunctionType.ExceptionReturn)
					{
						throw new RemotingException("Only replies or exceptions should end here", RemotingExceptionKind.ProtocolError);
					}

					if (_pendingInvocations.TryGetValue(hdReturnValue.Sequence, out var item))
					{
						if (hdReturnValue.Function == RemotingFunctionType.ExceptionReturn)
						{
							_logger.Log(LogLevel.Debug, $"{_side}: Receiving exception in reply to {item.Invocation.Method}");
							var exception = _messageHandler.DecodeException(reader);
							_pendingInvocations.TryRemove(hdReturnValue.Sequence, out var ctx);
							ctx.Exception = exception;
							item.Set();
						}
						else
						{
							_logger.Log(LogLevel.Debug, $"{_side}: Receiving reply for {item.Invocation.Method}");
							_messageHandler.ProcessCallResponse(item.Invocation, reader);
							_pendingInvocations.TryRemove(hdReturnValue.Sequence, out _);
							item.Set();
						}
					}
					else
					{
						throw new RemotingException($"There's no pending call for sequence id {hdReturnValue.Sequence}", RemotingExceptionKind.ProtocolError);
					}
				}
			}
			catch (IOException x)
			{
				_logger.Log(LogLevel.Error, "Terminating client receiver thread - Communication Exception: " + x.Message);
				_receiving = false;
			}
		}

		public void MethodsInspected()
		{
		}

		public void NonProxyableMemberNotification(Type type, MemberInfo memberInfo)
		{
			_logger.Log(LogLevel.Error, $"Type {type} has non-virtual method {memberInfo} - cannot be used for proxying");
		}

		public bool ShouldInterceptMethod(Type type, MethodInfo methodInfo)
		{
			return true;
		}

		public void Dispose()
		{
			_receiving = false;
			_receiverThread?.Join();
			_receiverThread = null;
		}

		internal sealed class CallContext : IDisposable
		{
			public CallContext(IInvocation invocation)
			{
				Invocation = invocation;
				EventToTrigger = new AutoResetEvent(false);
				Exception = null;
			}

			public IInvocation Invocation
			{
				get;
			}

			public AutoResetEvent EventToTrigger
			{
				get;
			}

			public Exception Exception
			{
				get;
				set;
			}

			public void Wait()
			{
				EventToTrigger.WaitOne();
			}

			public void Set()
			{
				EventToTrigger.Set();
			}

			public void Dispose()
			{
				EventToTrigger.Dispose();
			}
		}
	}
}
