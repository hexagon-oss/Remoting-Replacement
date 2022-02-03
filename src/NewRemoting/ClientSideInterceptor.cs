using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
		private readonly Stream _serverLink;
		private readonly MessageHandler _messageHandler;
		private readonly ILogger _logger;
		private int _sequence;
		private ConcurrentDictionary<int, CallContext> _pendingInvocations;
		private Thread _receiverThread;
		private bool _receiving;
		private int _numberOfCallsInspected;
		private CancellationTokenSource _terminator;

		/// <summary>
		/// This is only used withhin the receiver thread, but must be global so it can be closed on dispose (to
		/// force falling out of the blocking Read call)
		/// </summary>
		private BinaryReader _reader;

		public ClientSideInterceptor(string otherSideInstanceId, string thisSideInstanceId, bool clientSide, Stream serverLink, MessageHandler messageHandler, ILogger logger)
		{
			OtherSideInstanceId = otherSideInstanceId;
			ThisSideInstanceId = thisSideInstanceId;
			DebuggerToStringBehavior = DebuggerToStringBehavior.ReturnProxyName;
			_sequence = clientSide ? 1 : 10000;
			_serverLink = serverLink;
			_numberOfCallsInspected = 0;
			_messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
			_logger = logger;
			_pendingInvocations = new();
			_terminator = new CancellationTokenSource();
			_reader = new BinaryReader(_serverLink, Encoding.Unicode);
			_receiverThread = new Thread(ReceiverThread);
			_receiverThread.Name = "ClientSideInterceptor - " + thisSideInstanceId;
			_receiving = true;
			_receiverThread.Start();
		}

		public DebuggerToStringBehavior DebuggerToStringBehavior
		{
			get;
			set;
		}

		public string OtherSideInstanceId { get; }
		public string ThisSideInstanceId { get; }

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
				_logger.Log(LogLevel.Debug, $"{ThisSideInstanceId}: Intercepting {invocation.Method}, sequence {thisSeq}");

				MethodInfo me = invocation.Method;
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.MethodCall, thisSeq);

				if (me.IsStatic)
				{
					throw new RemotingException("Remote-calling a static method? No.");
				}

				if (!_messageHandler.InstanceManager.TryGetObjectId(invocation.Proxy, out var remoteInstanceId))
				{
					// One valid case when we may get here is when the proxy is just being created (as a class proxy) and within that ctor,
					// a virtual member function is called. So we can execute the call locally (the object should be in an useful state, since its default ctor
					// has been called)
					// Another possible reason to end here is when a class proxy gets its Dispose(false) method called by the local finalizer thread.
					// The remote reference is long gone and the local destructor may not work as expected, because the object is not
					// in a valid state.
					_logger.Log(LogLevel.Debug, "Not a valid remoting proxy. Assuming within ctor of class proxy");
					try
					{
						invocation.Proceed();
					}
					catch (NotImplementedException x)
					{
						_logger.LogError(x, "Unable to proceed on suspected class ctor. Assuming disconnected interface instead");
						throw new RemotingException("Unable to call method on remote object. Instance not found.");
					}

					_pendingInvocations.TryRemove(thisSeq, out _);
					return;
				}

				if (invocation.Proxy is DelegateInternalSink di)
				{
					// Need the source reference for this one. There's something fishy here, as this sometimes is ok, sometimes not.
					remoteInstanceId = di.RemoteObjectReference;
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
					throw new RemotingException("Cannot call methods with open generic arguments");
				}

				var genericArgs = me.GetGenericArguments();
				writer.Write((int)genericArgs.Length);
				foreach (var genericType in genericArgs)
				{
					string arg = genericType.AssemblyQualifiedName;
					if (arg == null)
					{
						throw new RemotingException("Unresolved generic type or some other undefined case");
					}

					writer.Write(arg);
				}

				writer.Write(invocation.Arguments.Length);

				foreach (var argument in invocation.Arguments)
				{
					_messageHandler.WriteArgumentToStream(writer, argument);
				}

				// now finally write the stream to the network. That way, we don't send incomplete messages if an exception happens encoding a parameter.
				SafeSendToServer(rawDataMessage);
			}

			WaitForReply(invocation, ctx);
		}

		private void SafeSendToServer(MemoryStream rawDataMessage)
		{
			try
			{
				rawDataMessage.Position = 0;
				rawDataMessage.CopyTo(_serverLink);
			}
			catch (IOException x)
			{
				throw new RemotingException("Error sending data to server. Link down?", x);
			}
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
				SafeSendToServer(rawDataMessage);
			}
		}

		internal CallContext CreateCallContext(IInvocation invocation, int thisSeq)
		{
			CallContext ctx = new CallContext(invocation, thisSeq, _terminator.Token);
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
			_logger.Log(LogLevel.Debug, $"{ThisSideInstanceId}: {invocation.Method} done.");
			if (ctx.Exception != null)
			{
				if (ctx.IsInTerminationMethod())
				{
					return;
				}

				// Rethrow remote exception
				ExceptionDispatchInfo.Capture(ctx.Exception).Throw();
			}
		}

		private void ReceiverThread()
		{
			try
			{
				while (_receiving && !_terminator.IsCancellationRequested)
				{
					RemotingCallHeader hdReturnValue = default;
					// This read is blocking
					if (!hdReturnValue.ReadFrom(_reader))
					{
						throw new RemotingException("Unexpected reply or stream out of sync");
					}

					_logger.Log(LogLevel.Debug, $"{ThisSideInstanceId}: Decoding message {hdReturnValue.Sequence} of type {hdReturnValue.Function}");

					if (hdReturnValue.Function == RemotingFunctionType.ServerShuttingDown)
					{
						// Quit here.
						foreach (var inv in _pendingInvocations)
						{
							inv.Value.Exception = new RemotingException("Server terminated itself. Call aborted");
							inv.Value.Set();
						}

						_pendingInvocations.Clear();
						_terminator.Cancel();
						return;
					}

					if (hdReturnValue.Function != RemotingFunctionType.MethodReply && hdReturnValue.Function != RemotingFunctionType.ExceptionReturn)
					{
						throw new RemotingException("Only replies or exceptions should end here");
					}

					if (_pendingInvocations.TryRemove(hdReturnValue.Sequence, out var ctx))
					{
						if (hdReturnValue.Function == RemotingFunctionType.ExceptionReturn)
						{
							_logger.Log(LogLevel.Debug, $"{ThisSideInstanceId}: Receiving exception in reply to {ctx.Invocation.Method}");
							var exception = _messageHandler.DecodeException(_reader);
							ctx.Exception = exception;
							// Hack to move the remote stack trace to the correct field.
							var remoteField = typeof(Exception).GetField("_remoteStackTraceString", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
							remoteField.SetValue(exception, exception.StackTrace);
							ctx.Set();
						}
						else
						{
							_logger.Log(LogLevel.Debug, $"{ThisSideInstanceId}: Receiving reply for {ctx.Invocation.Method}");
							_messageHandler.ProcessCallResponse(ctx.Invocation, _reader);
							ctx.Set();
						}
					}
					else
					{
						throw new RemotingException($"There's no pending call for sequence id {hdReturnValue.Sequence}");
					}
				}
			}
			catch (Exception x) when (x is IOException || x is ObjectDisposedException)
			{
				_logger.Log(LogLevel.Error, "Terminating client receiver thread - Communication Exception: " + x.Message);
				_receiving = false;
				_terminator.Cancel();
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
			_terminator.Cancel();
			_receiving = false;
			_reader.Dispose();
			_receiverThread?.Join();
			_receiverThread = null;
		}

		internal sealed class CallContext : IDisposable
		{
			private static readonly MethodInfo TerminationMethod =
				typeof(RemoteServerService).GetMethod(nameof(RemoteServerService.TerminateRemoteServerService));

			private readonly CancellationToken _externalTerminator;

			public CallContext(IInvocation invocation, int sequence, CancellationToken externalTerminator)
			{
				_externalTerminator = externalTerminator;
				Invocation = invocation;
				SequenceNumber = sequence;
				EventToTrigger = new AutoResetEvent(false);
				Exception = null;
			}

			public IInvocation Invocation
			{
				get;
			}

			public int SequenceNumber { get; }

			private AutoResetEvent EventToTrigger
			{
				get;
				set;
			}

			public Exception Exception
			{
				get;
				set;
			}

			public void Wait()
			{
				WaitHandle[] handles = new WaitHandle[] { EventToTrigger, _externalTerminator.WaitHandle };
				if (handles.All(x => x != null))
				{
					WaitHandle.WaitAny(handles);
				}

				if (IsInTerminationMethod())
				{
					// Report, but don't throw (special handling by parent)
					Exception = new RemotingException("Error executing remote call: Link is going down");
					return;
				}

				if (_externalTerminator.IsCancellationRequested)
				{
					throw new RemotingException("Error executing remote call: Link is going down");
				}
			}

			public bool IsInTerminationMethod()
			{
				if (Invocation.Method != null && Invocation.Method.Name == TerminationMethod.Name && Invocation.Method.DeclaringType == typeof(IRemoteServerService))
				{
					// If this is a call to the above method, we drop the exception, because it is expected.
					return true;
				}

				return false;
			}

			public void Set()
			{
				EventToTrigger?.Set();
			}

			public void Dispose()
			{
				if (EventToTrigger != null)
				{
					EventToTrigger.Dispose();
					EventToTrigger = null;
				}
			}
		}
	}
}
