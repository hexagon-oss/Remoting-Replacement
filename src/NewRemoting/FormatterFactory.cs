﻿using System;
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
		private readonly IInstanceManager _instanceManager;
		private readonly ConcurrentDictionary<string, JsonSerializerOptions> _cusBinaryFormatters;

		public FormatterFactory(IInstanceManager instanceManager)
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
					new ManualSerializerSurrogate(),
					new CultureInfoSerializerSurrogate(),
					new ByteArraySurrogate(),
					new SystemTypeSurrogate(),
					new IpAddressSurrogate(),
				},
				ReferenceHandler = ReferenceHandler.Preserve,
				NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
			};

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
