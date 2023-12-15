using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	[Serializable]
	public struct CustomSerializableObject : IEquatable<CustomSerializableObject>
	{
		public CustomSerializableObject(int value, DateTime time, double anotherValue, IMyDto dto)
		{
			Value = value;
			Time = time;
			AnotherValue = anotherValue;
			MyDto = dto;
			ReadOnlyObject = new SerializableType(dto.Name + " Copy", dto.Id * 22);
		}

		public int Value { get; set; }

		public DateTime Time { get; set; }

		public double AnotherValue { get; private set; }

		public IMyDto MyDto { get; set; }

		public IMyDto ReadOnlyObject { get; }

		public bool Equals(CustomSerializableObject other)
		{
			return Value == other.Value && Time.Equals(other.Time) && AnotherValue.Equals(other.AnotherValue) && Equals(MyDto, other.MyDto);
		}

		public override bool Equals(object obj)
		{
			return obj is CustomSerializableObject other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Value, Time, AnotherValue, MyDto);
		}

		public static bool operator ==(CustomSerializableObject left, CustomSerializableObject right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(CustomSerializableObject left, CustomSerializableObject right)
		{
			return !left.Equals(right);
		}
	}
}
