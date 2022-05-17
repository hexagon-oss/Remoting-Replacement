using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public class MarshallableClass : MarshalByRefObject, IMarshallInterface
	{
		private ReferencedComponent _component;
		private ICallbackInterface _cb;
		private string _name;

		public MarshallableClass()
		{
			_component = new ReferencedComponent();
			_cb = null;
			Name = "Unnamed" + GetHashCode();
		}

		public MarshallableClass(string name)
		{
			_component = new ReferencedComponent();
			_cb = null;
			_name = name;
		}

		public virtual event Action<string, string> AnEvent;
		public virtual event Action<string> EventTwo;
		public virtual event Action<string> EventThree;

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
			AnEvent?.Invoke(msg, Name);
		}

		public virtual void DoCallbackOnOtherEvents(string msg)
		{
			EventTwo?.Invoke(msg);
			EventThree?.Invoke(msg);
		}

		public virtual void CleanEvents()
		{
			AnEvent = null;
			EventTwo = null;
			EventThree = null;
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
	}
}
