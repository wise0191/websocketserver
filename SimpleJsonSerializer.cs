using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace DrugInfoWebSocketServer
{
    public class SimpleJsonSerializer
    {
        public object DeserializeObject(string json)
        {
            return MiniJSON.Json.Deserialize(json);
        }

        public T Deserialize<T>(string json) where T : new()
        {
            object obj = MiniJSON.Json.Deserialize(json);
            return (T)ConvertToType(typeof(T), obj);
        }

        private object ConvertToType(Type type, object value)
        {
            if (value == null)
                return null;

            if (typeof(IDictionary).IsAssignableFrom(value.GetType()))
            {
                IDictionary<string, object> dict = (IDictionary<string, object>)value;
                object instance = Activator.CreateInstance(type);
                foreach (PropertyInfo prop in type.GetProperties())
                {
                    if (!prop.CanWrite) continue;

                    object val = null;
                    string matchedKey = null;
                    foreach (string k in dict.Keys)
                    {
                        if (string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedKey = k;
                            break;
                        }
                    }
                    if (matchedKey != null)
                    {
                        val = dict[matchedKey];
                    }
                    else
                    {
                        JsonAliasAttribute aliasAttr = (JsonAliasAttribute)Attribute.GetCustomAttribute(prop, typeof(JsonAliasAttribute));
                        if (aliasAttr != null)
                        {
                            foreach (string alias in aliasAttr.Aliases)
                            {
                                foreach (string k in dict.Keys)
                                {
                                    if (string.Equals(k, alias, StringComparison.OrdinalIgnoreCase))
                                    {
                                        matchedKey = k;
                                        break;
                                    }
                                }
                                if (matchedKey != null)
                                {
                                    val = dict[matchedKey];
                                    break;
                                }
                            }
                        }
                    }

                    if (val != null)
                    {
                        object converted = ConvertValue(prop.PropertyType, val);
                        prop.SetValue(instance, converted, null);
                    }
                }
                return instance;
            }
            return ConvertValue(type, value);
        }

        private object ConvertValue(Type type, object value)
        {
            if (value == null)
                return null;

            if (type == typeof(string))
                return value.ToString();
            if (type == typeof(int) || type == typeof(int?))
                return Convert.ToInt32(value);
            if (type == typeof(long) || type == typeof(long?))
                return Convert.ToInt64(value);
            if (type == typeof(bool) || type == typeof(bool?))
                return Convert.ToBoolean(value);
            if (type == typeof(double) || type == typeof(double?))
                return Convert.ToDouble(value);
            if (type == typeof(float) || type == typeof(float?))
                return Convert.ToSingle(value);
            if (type == typeof(DateTime) || type == typeof(DateTime?))
                return Convert.ToDateTime(value);

            if (typeof(IList).IsAssignableFrom(type) && value is IList<object>)
            {
                Type elemType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType));
                foreach (object item in (IList<object>)value)
                {
                    list.Add(ConvertValue(elemType, item));
                }
                if (type.IsArray)
                {
                    Array arr = Array.CreateInstance(elemType, list.Count);
                    list.CopyTo(arr, 0);
                    return arr;
                }
                return list;
            }

            if (value is IDictionary<string, object>)
            {
                return ConvertToType(type, value);
            }

            return Convert.ChangeType(value, type);
        }

        public string Serialize(object obj)
        {
            object prepared = PrepareForSerialize(obj);
            return MiniJSON.Json.Serialize(prepared);
        }

        private object PrepareForSerialize(object obj)
        {
            if (obj == null)
                return null;

            Type t = obj.GetType();
            if (obj is string || t.IsPrimitive)
                return obj;

            if (obj is IDictionary dict)
            {
                Dictionary<string, object> nd = new Dictionary<string, object>();
                foreach (DictionaryEntry kv in dict)
                {
                    nd[kv.Key.ToString()] = PrepareForSerialize(kv.Value);
                }
                return nd;
            }

            if (obj is IEnumerable && !(obj is string))
            {
                List<object> list = new List<object>();
                foreach (object item in (IEnumerable)obj)
                {
                    list.Add(PrepareForSerialize(item));
                }
                return list;
            }

            Dictionary<string, object> result = new Dictionary<string, object>();
            foreach (PropertyInfo prop in t.GetProperties())
            {
                if (!prop.CanRead) continue;
                object val = prop.GetValue(obj, null);
                val = PrepareForSerialize(val);
                string key = prop.Name;
                JsonAliasAttribute aliasAttr = (JsonAliasAttribute)Attribute.GetCustomAttribute(prop, typeof(JsonAliasAttribute));
                if (aliasAttr != null && aliasAttr.Aliases.Length > 0)
                    key = aliasAttr.Aliases[0];
                result[key] = val;
            }
            return result;
        }
    }
}
