using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace NewRemoting
{
	public static class Extensions
	{
		// .Net Standard 2.0 doesn't contain a clear on ConcurrentBag...
		public static void Clear<T>(this ConcurrentBag<T> bag)
		{
			while (!bag.IsEmpty)
			{
				bag.TryTake(out _);
			}
		}

		public static void AddOrUpdate<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> table, TKey key, TValue value)
		where TKey : class
		where TValue : class
		{
			// Remove existing key if present
			if (table.TryGetValue(key, out _))
			{
				table.Remove(key);
			}

			// Add the new key-value pair
			table.Add(key, value);
		}
	}
}
