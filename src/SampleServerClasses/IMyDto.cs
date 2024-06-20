using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleServerClasses
{
	public interface IMyDto
	{
		string Name { get; }
		int Id { get; }

		IMyDto Next { get; set; }
	}
}
