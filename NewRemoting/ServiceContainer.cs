using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewRemoting
{
    public sealed class ServiceContainer
    {
        private static Dictionary<Type, object> _serviceDictionary;

        static ServiceContainer()
        {
            _serviceDictionary = new Dictionary<Type, object>();
        }

        public static void AddService<T>(object instance)
        {
            AddService(typeof(T), instance);
        }

        public static void AddService(Type typeOfService, object instance)
        {
            _serviceDictionary.Add(typeOfService, instance);
        }

        public static T GetService<T>()
        {
            return (T)GetService(typeof(T));
        }

        public static object GetService(Type typeOfService)
        {
            if (_serviceDictionary.TryGetValue(typeOfService, out var instance))
            {
                return instance;
            }

            return null;
        }
    }
}
