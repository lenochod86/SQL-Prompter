using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Logs the public properties of an object. Values that look like secrets
    /// (password, token, …) are never logged, only whether they are set.
    /// </summary>
    internal static class ObjectDumper
    {
        private static readonly Regex Secret = new Regex(
            "password|pwd|token|secret|credential",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsSecretName(string name) => name != null && Secret.IsMatch(name);

        public static void Dump(string area, string label, object value, int maxDepth)
        {
            Dump(area, label, value, 0, maxDepth, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static void Dump(string area, string label, object value, int depth, int maxDepth, HashSet<object> visited)
        {
            var indent = new string(' ', 2 + depth * 2);
            if (value == null)
            {
                Log.Write(area, indent + label + " = (null)");
                return;
            }

            var type = value.GetType();
            if (IsSecretName(label))
            {
                Log.Write(area, indent + label + " = [hidden, set: " + IsFilled(value) + "] (" + type.FullName + ")");
                return;
            }

            if (IsScalar(type))
            {
                Log.Write(area, indent + label + " = " + value + " (" + type.Name + ")");
                return;
            }

            if (!visited.Add(value))
            {
                Log.Write(area, indent + label + " = (already dumped) " + type.FullName);
                return;
            }

            Log.Write(area, indent + label + " : " + type.FullName + " z " + type.Assembly.GetName().Name);
            if (depth >= maxDepth)
            {
                return;
            }

            if (value is NameValueCollection nvc)
            {
                foreach (string key in nvc.AllKeys)
                {
                    Dump(area, "[" + key + "]", nvc[key], depth + 1, maxDepth, visited);
                }

                return;
            }

            if (value is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    Dump(area, "[" + entry.Key + "]", entry.Value, depth + 1, maxDepth, visited);
                }

                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                         .OrderBy(p => p.Name))
            {
                object propertyValue;
                try
                {
                    propertyValue = property.GetValue(value);
                }
                catch (Exception ex)
                {
                    var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                    Log.Write(area, indent + "  " + property.Name + " = (exception " + inner.GetType().Name + ")");
                    continue;
                }

                Dump(area, property.Name, propertyValue, depth + 1, maxDepth, visited);
            }
        }

        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                || type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid);
        }

        private static bool IsFilled(object value)
        {
            switch (value)
            {
                case string s: return s.Length > 0;
                case System.Security.SecureString ss: return ss.Length > 0;
                default: return true;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
