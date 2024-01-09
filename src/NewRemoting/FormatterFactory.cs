using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	internal class FormatterFactory
	{
		private readonly InstanceManager _instanceManager;
		private readonly ConcurrentDictionary<string, JsonSerializerOptions> _cusBinaryFormatters;

		public FormatterFactory(InstanceManager instanceManager)
		{
			_instanceManager = instanceManager;
			_cusBinaryFormatters = new ConcurrentDictionary<string, JsonSerializerOptions>();
		}

		public JsonSerializerOptions CreateOrGetFormatter(string otherSideProcessId)
		{
			if (_cusBinaryFormatters.TryGetValue(otherSideProcessId, out var formatter))
			{
				return formatter;
			}

			// Doing this twice doesn't hurt (except for a very minor performance penalty)
			JsonSerializerOptions options = new JsonSerializerOptions()
			{
				IncludeFields = true,
				Converters =
				{
					new ProxySurrogate(_instanceManager, otherSideProcessId),
					new ManualSerializerSurrogate(_instanceManager),
					new CultureInfoSerializerSurrogate(),
					new InterfaceInstantiationSurrogate(),
				},
				ReferenceHandler = ReferenceHandler.Preserve
			};

			_cusBinaryFormatters.TryAdd(otherSideProcessId, options);
			return options;
		}

		public void FinalizeSerialization(BinaryWriter w, JsonSerializerOptions formatter)
		{
			var manualSerializer = (ManualSerializerSurrogate)formatter.Converters.First(x => x is ManualSerializerSurrogate);
			manualSerializer.PerformManualSerialization(w);
		}

		public void FinalizeDeserialization(BinaryReader r, JsonSerializerOptions formatter)
		{
			var manualSerializer = (ManualSerializerSurrogate)formatter.Converters.First(x => x is ManualSerializerSurrogate);
			manualSerializer.PerformManualDeserialization(r);
		}
	}
}
