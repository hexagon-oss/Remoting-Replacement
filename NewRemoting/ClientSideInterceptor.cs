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

// BinaryFormatter shouldn't be used
#pragma warning disable SYSLIB0011
namespace NewRemoting
{
	internal sealed class ClientSideInterceptor : IInterceptor, IProxyGenerationHook, IDisposable
	{
		private readonly string _side;
		private readonly TcpClient _serverLink;
		private readonly IInternalClient _remotingClient;
		private readonly MessageHandler _messageHandler;
		private int _sequence;
		private ConcurrentDictionary<int, (IInvocation, AutoResetEvent eventToTrigger)> _pendingInvocations;
		private Thread _receiverThread;
		private bool _receiving;

		public ClientSideInterceptor(String side, TcpClient serverLink, IInternalClient remotingClient, MessageHandler messageHandler)
		{
			DebuggerToStringBehavior = DebuggerToStringBehavior.ReturnProxyName;
			_sequence = side == "Client" ? 1 : 10000;
			_side = side;
			_serverLink = serverLink;
			_remotingClient = remotingClient;
			_messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));
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
			string methodName = invocation.Method.ToString();

			// Todo: Check this stuff
			if (methodName == "ToString()" && DebuggerToStringBehavior != DebuggerToStringBehavior.EvaluateRemotely)
			{
				invocation.ReturnValue = "Remote proxy";
				return;
			}

			int thisSeq;

			MemoryStream rawDataMessage = new MemoryStream(1024);
			BinaryWriter writer = new BinaryWriter(rawDataMessage, Encoding.Unicode);

			lock (_remotingClient.CommunicationLinkLock)
			{
				thisSeq = NextSequenceNumber();

				Debug.WriteLine($"{_side}: Intercepting {invocation.Method}, sequence {thisSeq}");

				// Console.WriteLine($"Here should be a call to {invocation.Method}");
				MethodInfo me = invocation.Method;
				RemotingCallHeader hd = new RemotingCallHeader(RemotingFunctionType.MethodCall, thisSeq);

				if (me.IsStatic)
				{
					throw new RemotingException("Remote-calling a static method? No.", RemotingExceptionKind.UnsupportedOperation);
				}

				if (!_remotingClient.TryGetRemoteInstance(invocation.Proxy, out var remoteInstanceId))
				{
					throw new RemotingException("Not a valid remoting proxy", RemotingExceptionKind.ProxyManagementError);
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
				rawDataMessage.CopyTo(_serverLink.GetStream());
			}

			WaitForReply(invocation, thisSeq);
		}

		internal void WaitForReply(IInvocation invocation, int thisSeq)
		{
			AutoResetEvent ev = new AutoResetEvent(false);
			if (!_pendingInvocations.TryAdd(thisSeq, (invocation, ev)))
			{
				// This really shouldn't happen
				throw new InvalidOperationException("A call with the same id is already being processed");
			}

			// The event is signaled by the receiver thread when the message was processed
			ev.WaitOne();
			ev.Dispose();
			Debug.WriteLine($"{_side}: {invocation.Method} done.");
		}

		private void ReceiverThread()
		{
			var reader = new BinaryReader(_serverLink.GetStream(), Encoding.Unicode);
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

					Debug.WriteLine($"{_side}: Decoding message {hdReturnValue.Sequence} of type {hdReturnValue.Function}");

					if (hdReturnValue.Function != RemotingFunctionType.MethodReply)
					{
						throw new RemotingException("I think only replies should end here", RemotingExceptionKind.ProtocolError);
					}

					if (_pendingInvocations.TryGetValue(hdReturnValue.Sequence, out var item))
					{
						Debug.WriteLine($"{_side}: Receiving reply for {item.Item1.Method}");
						_messageHandler.ProcessCallResponse(item.Item1, reader);
						_pendingInvocations.TryRemove(hdReturnValue.Sequence, out _);
						item.eventToTrigger.Set();
					}
					else
					{
						throw new RemotingException($"There's no pending call for sequence id {hdReturnValue.Sequence}", RemotingExceptionKind.ProtocolError);
					}
				}
			}
			catch (IOException x)
			{
				Console.WriteLine("Terminating client receiver thread - Communication Exception: " + x.Message);
				_receiving = false;
			}
		}

		public void MethodsInspected()
		{
		}

		public void NonProxyableMemberNotification(Type type, MemberInfo memberInfo)
		{
			Console.WriteLine($"Type {type} has non-virtual method {memberInfo} - cannot be used for proxying");
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
	}
}
