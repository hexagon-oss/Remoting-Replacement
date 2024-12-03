using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting.Toolkit
{
	/// <summary>
	/// Allows controlling the serialization for remoting.
	/// Rarely used, but may be helpful in serializing Memory{T} or similiar constructs
	/// </summary>
	public interface IManualSerialization
	{
		/// <summary>
		/// Serialize to the given target.
		/// </summary>
		/// <param name="serializerTarget">Target object</param>
		void Serialize(BinaryWriter serializerTarget);

		/// <summary>
		/// Deserializing operation. The constructor has probably not been called prior to this (and all fields are still in the default state)
		/// </summary>
		/// <param name="serializerSource">The serialization source. The object must make sure it reads exactly as many bytes as had been
		/// written during serialization.</param>
		void Deserialize(BinaryReader serializerSource);
	}
}
