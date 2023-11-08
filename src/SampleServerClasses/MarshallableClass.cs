using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SampleServerClasses
{
	public class MarshallableClass : MarshalByRefObject, IMarshallInterface
	{
		private ReferencedComponent _component;
		private ICallbackInterface _cb;
		private string _name;
		private byte[] _someData;

		private string _callbackData;
		private SimpleCalc _calculator;
		private Action<int> _progressFeedback;

		public event Action AnEvent0;
		public event Action<string> AnEvent1;
		public event Action<string, string> AnEvent2;
		public event Action<string, string, string> AnEvent3;
		public event Action<string, string, string, string> AnEvent4;
		public event Action<string, string, string, string, string> AnEvent5;

		private ICallbackInterface _instanceForCallback;

		public MarshallableClass()
		{
			_component = new ReferencedComponent();
			_cb = null;
			Name = "Unnamed" + GetHashCode();
			_callbackData = null;
			_instanceForCallback = null;
			_someData = new byte[100];
			_someData[1] = 5;
			_someData[2] = 6;
		}

		public MarshallableClass(string name)
		{
			_component = new ReferencedComponent();
			_cb = null;
			_name = name;
			_callbackData = null;
		}

		public virtual string Name
		{
			get => _name;
			set => _name = value;
		}

		public virtual int GetSomeData()
		{
			return 4711;
		}

		public virtual int GetCurrentProcessId()
		{
			Process ps = Process.GetCurrentProcess();
			return ps.Id;
		}

		public virtual int AddValues(int a, int b)
		{
			return a + b;
		}

		public virtual bool TryParseInt(string input, out int value)
		{
			return Int32.TryParse(input, out value);
		}

		public virtual void UpdateArgument(ref int value)
		{
			value += 2;
		}

		string IMarshallInterface.StringProcessId()
		{
			return GetCurrentProcessId().ToString();
		}

		public virtual void RegisterCallback(ICallbackInterface cb)
		{
			_cb = cb;
		}

		public virtual void DoCallback()
		{
			if (_cb != null)
			{
				_cb.FireSomeAction("Hello again!");
			}
		}

		public virtual void DoCallbackOnEvent(string msg)
		{
			AnEvent4?.Invoke(msg, msg, msg, Name);
			AnEvent3?.Invoke(msg, msg, Name);
			AnEvent2?.Invoke(msg, Name);
			AnEvent1?.Invoke(msg);
			AnEvent0?.Invoke();
		}

		public void DoCallbackOnEvent5(string msg)
		{
			AnEvent5?.Invoke(msg, msg, msg, msg, msg);
		}

		public virtual void CleanEvents()
		{
			AnEvent4 = null;
			AnEvent3 = null;
			AnEvent2 = null;
			AnEvent1 = null;
			AnEvent0 = null;
		}

		public virtual ReferencedComponent GetComponent()
		{
			return _component;
		}

		public virtual T GetInterface<T>()
			where T : class
		{
			return _component as T;
		}

		public virtual string GetTypeName(Type t)
		{
			return t.FullName;
		}

		public virtual IList<ReferencedComponent> GetSomeComponents()
		{
			List<ReferencedComponent> list = new List<ReferencedComponent>();
			list.Add(new ReferencedComponent());
			list.Add(new ReferencedComponent());
			return list;
		}

		public virtual int MaybeThrowException(int mustBeZero)
		{
			return DoIntegerDivide(10, mustBeZero);
		}

		private int DoIntegerDivide(int a, int b)
		{
			return a / b;
		}

		public virtual int UseMixedArgument(SerializableClassWithMarshallableMembers sc)
		{
			return sc.Component.Data;
		}

		/// <summary>
		/// This is not remote-callable, because the argument is not serializable
		/// </summary>
		public virtual bool CallerError(UnserializableObject arg)
		{
			return arg != null;
		}

		/// <summary>
		/// This is not remote-callable, because the return value is not serializable
		/// Here, the error happens on the server only.
		/// </summary>
		public virtual UnserializableObject ServerError()
		{
			return new UnserializableObject();
		}

		public virtual bool StreamDataContains(Stream stream, byte b)
		{
			stream.Position = 0;
			int data = stream.ReadByte();
			while (data != -1)
			{
				if (data == b)
				{
					return true;
				}

				data = stream.ReadByte();
			}

			return false;
		}

		public virtual Stream GetFileStream(string fileName)
		{
			return new FileStream(fileName, FileMode.Open, FileAccess.Read);
		}

		public virtual Stream OpenStream(string fileName, FileMode mode, FileAccess access)
		{
			return new FileStream(fileName, mode, access);
		}

		public virtual void DeleteFile(string fileName)
		{
			File.Delete(fileName);
		}

		public void CloseStream(Stream stream)
		{
			if (stream != null)
			{
				stream.Close();
				stream.Dispose();
			}
		}

		public int ListCount<T>(IEnumerable<T> intList)
		{
			return intList.Count();
		}

		public virtual bool CheckStreamEqualToFile(string fileToOpen, long length, Stream fileStream)
		{
			// First, try to just read the file
			var sreader = new StreamReader(fileStream, null, true, -1, false);
			string strContent = sreader.ReadToEnd();

			if (strContent.All(x => x == '\0'))
			{
				throw new InvalidDataException("The stream data is empty");
			}

			if (fileStream.Length != length)
			{
				throw new InvalidDataException("Data length does not match");
			}

			fileStream.Position = 0;
			using FileStream fsLocal = new FileStream(fileToOpen, FileMode.Open, FileAccess.Read);
			fsLocal.Position = 0;
			var sreader2 = new StreamReader(fileStream, null, true, -1, false);
			string strContent2 = sreader2.ReadToEnd();

			if (strContent != strContent2)
			{
				throw new InvalidDataException("The data is not equal");
			}

			return true;
		}

		public void EnsureCallbackWasUsed()
		{
			if (_callbackData == null)
			{
				throw new InvalidOperationException("The callback value was still null");
			}
		}

		public void InverseCallback(string data)
		{
			_callbackData = data;
		}

		public void RegisterForCallback(ICallbackInterface callbackInterface)
		{
			callbackInterface.Callback += InverseCallback;
		}

		public void RegisterEvent(Action<int> progressFeedback)
		{
			_progressFeedback = progressFeedback;
		}

		public void SetProgress(int progress)
		{
			_progressFeedback.Invoke(progress);
		}

		public void RegisterEventOnCallback(ICallbackInterface cb)
		{
			_instanceForCallback = cb;
			if (_instanceForCallback != null)
			{
				_instanceForCallback.Callback += InverseCallback;
			}
		}

		public string DeregisterEvent()
		{
			if (_instanceForCallback != null)
			{
				_instanceForCallback.Callback -= InverseCallback;
			}

			return _callbackData;
		}

		public virtual void CreateCalc()
		{
			_calculator = new SimpleCalc();
		}

		public virtual SimpleCalc DetermineCalc()
		{
			return _calculator;
		}

		public virtual object GetSealedClass()
		{
			return new SealedClass();
		}

		public virtual bool TakeSomeArguments(int a, Int16 b, UInt16 c, double d)
		{
			return Math.Abs(a + b - (c + d)) < 1E-12;
		}

		public virtual bool TakeSomeMoreArguments(byte a, short b, ushort c, float d)
		{
			return Math.Abs(a + b - (c + d)) < 1E-12;
		}

		public virtual string ProcessListOfTypes(params Type[] types)
		{
			return string.Join(", ", types.Select(x => x?.ToString()));
		}

		public virtual CustomSerializableObject SomeStructOperation(CustomSerializableObject sent)
		{
			return new CustomSerializableObject() { Time = DateTime.Now, Value = 10 };
		}

		public virtual ManualSerializableLargeObject GetSerializedObject()
		{
			return new ManualSerializableLargeObject(new Memory<byte>(_someData, 0, 10));
		}

		public virtual (ManualSerializableLargeObject A, ManualSerializableLargeObject B, int C) GetSerializedObjects()
		{
			return (new ManualSerializableLargeObject(new Memory<byte>(_someData, 2, 5)), new ManualSerializableLargeObject(_someData), 33);
		}
	}
}
