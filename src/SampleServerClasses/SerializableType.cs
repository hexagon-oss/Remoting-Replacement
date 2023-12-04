using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public class SerializableType : IMyDto
	{
		public SerializableType(string name, int id)
		{
			Name = name;
			Id = id;
		}

		public string Name { get; }
		public int Id { get; }

		public IMyDto Next { get; set; }
	}
}
