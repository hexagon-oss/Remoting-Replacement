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
using NewRemoting.Surrogates;
using NewRemoting.Toolkit;

namespace NewRemoting
{
	/// <summary>
	/// Provides the factory for the internal Json serializer options and custom serialization.
	/// This class is public to provide an easy means of verifying proper (de)serialization of complex
	/// user types in unit tests.
	/// </summary>
	public class FormatterFactory
	{
		private static readonly List<JsonConverter> ExternalSurrogateList = new List<JsonConverter>();
		private readonly IInstanceManager _instanceManager;
		private readonly ConcurrentDictionary<string, JsonSerializerOptions> _cusBinaryFormatters;

		public FormatterFactory(IInstanceManager instanceManager)
		{
			_instanceManager = instanceManager;
			_cusBinaryFormatters = new ConcurrentDictionary<string, JsonSerializerOptions>();
		}

		/// <summary>
		/// Allows manually setting up a converter that is to be included in the list of internal converters.
		/// This should be called before the formatter is used the first time. It should only be used for types that need serialization
		/// and cannot implement <see cref="IManualSerialization"/> (e.g. because they're imported from a library)
		/// </summary>
		/// <param name="surrogate"></param>
		public static void AddSurrogate(JsonConverter surrogate)
		{
			ExternalSurrogateList.Add(surrogate);
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
					new ManualSerializerSurrogate(),
					new CultureInfoSerializerSurrogate(),
					new ByteArraySurrogate(),
					new SystemTypeSurrogate(),
					new IpAddressSurrogate(),
				},
				ReferenceHandler = ReferenceHandler.Preserve,
				NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
			};

			foreach (var converter in ExternalSurrogateList)
			{
				options.Converters.Add(converter);
			}

			_cusBinaryFormatters.TryAdd(otherSideProcessId, options);
			return options;
		}

		public void FinalizeSerialization(BinaryWriter w, JsonSerializerOptions formatter)
		{
			foreach (IInternalManualSerializerSurrogate manualSerializer in formatter.Converters.Where(x => x is IInternalManualSerializerSurrogate))
			{
				manualSerializer.PerformManualSerialization(w);
			}
		}

		public void FinalizeDeserialization(BinaryReader r, JsonSerializerOptions formatter)
		{
			foreach (IInternalManualSerializerSurrogate manualSerializer in formatter.Converters.Where(x => x is IInternalManualSerializerSurrogate))
			{
				manualSerializer.PerformManualDeserialization(r);
			}
		}
	}
}
