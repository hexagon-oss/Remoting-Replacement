using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace RemotingServer
{
    /// <summary>
    /// This represents a dictionary of object types, which uses Object.ReferenceEquals() to check for hits
    /// </summary>
    /// <typeparam name="T">The type of associated value</typeparam>
    public class ObjectDictionary<T> : IDictionary<object, T>
    {
        private Dictionary<int, List<(object, T)>> _keyValuePairs;
        private Func<object, int> _keyCreationFunc;

        public ObjectDictionary()
        {
            _keyValuePairs = new Dictionary<int, List<(object, T)>>();
            _keyCreationFunc = RuntimeHelpers.GetHashCode;
        }

        public ObjectDictionary(Func<object, int> keyCreationFunc)
        {
            _keyCreationFunc = keyCreationFunc;
        }

        public IEnumerator<KeyValuePair<object, T>> GetEnumerator()
        {
            return new ObjectDictionaryEnumerator(_keyValuePairs);
        }

        public class ObjectDictionaryEnumerator : IEnumerator<KeyValuePair<object, T>>
        {
            private readonly Dictionary<int, List<(object, T)>> _keyValuePairs;
            private Dictionary<int, List<(object, T)>>.Enumerator _internalEnumerator;
            private List<(object, T)> _currentList;
            private int _currentListIndex;

            public ObjectDictionaryEnumerator(Dictionary<int, List<(object, T)>> keyValuePairs)
            {
                _keyValuePairs = keyValuePairs;
                _internalEnumerator = keyValuePairs.GetEnumerator();
                _currentListIndex = -1;
            }

            public bool MoveNext()
            {
                _currentListIndex++;

                if (_currentList == null || _currentListIndex >= _currentList.Count)
                {
                    if (!_internalEnumerator.MoveNext())
                    {
                        return false;
                    }
                    _currentList = _internalEnumerator.Current.Value;
                    _currentListIndex = 0;
                    return true;
                }

                return true;
            }

            public void Reset()
            {
                _internalEnumerator = _keyValuePairs.GetEnumerator();
                _currentList = null;
                _currentListIndex = -1;
            }

            public KeyValuePair<object, T> Current
            {
                get
                {
                    var elem = _currentList[_currentListIndex];
                    return new KeyValuePair<object, T>(elem.Item1, elem.Item2);
                }
            }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(KeyValuePair<object, T> item)
        {
            Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _keyValuePairs.Clear();
        }

        public bool Contains(KeyValuePair<object, T> item)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<object, T>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<object, T> item)
        {
            throw new NotImplementedException();
        }

        public int Count { get; }
        public bool IsReadOnly { get; }
        public void Add(object key, T value)
        {
            throw new NotImplementedException();
        }

        public bool ContainsKey(object key)
        {
            throw new NotImplementedException();
        }

        public bool Remove(object key)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(object key, out T value)
        {
            throw new NotImplementedException();
        }

        public T this[object key]
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public ICollection<object> Keys { get; }
        public ICollection<T> Values { get; }
    }
}
