using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Snapshots;

namespace TAS.Tracer;

// Generic serialization + comparison plumbing for the tracer. Game-agnostic.

internal class WritableOnlyResolver : DefaultContractResolver {
    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
        var property = base.CreateProperty(member, memberSerialization);
        property.ShouldSerialize = _ => property.Writable;
        return property;
    }
}

internal class ToStringConverter<T> : NullableJsonConverter<T> {
    protected override void WriteJson(JsonWriter writer, T? value, JsonSerializer serializer) {
        if (value == null) {
            writer.WriteNull();
            return;
        }

        writer.WriteValue(value.ToString());
    }

    protected override T? ReadJson(JsonReader reader, Type objectType, T? existingValue, bool hasExistingValue,
        JsonSerializer serializer) => throw new NotImplementedException();
}

internal class FuncConverter<T>(Func<T, object?> func) : JsonConverter {
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
        if (value == null) {
            writer.WriteNull();
            return;
        }

        serializer.Serialize(writer, func((T)value));
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer) => throw new NotImplementedException();

    public override bool CanConvert(Type objectType) {
        var underlying = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return typeof(T).IsAssignableFrom(underlying);
    }
}

internal static class EqualsHelper {
    public static bool CompareDeep(
        object? obj1,
        object? obj2,
        [NotNullWhen(true)] out string? failurePath,
        [NotNullWhen(true)] out object? left,
        [NotNullWhen(true)] out object? right
    ) {
        var fp = "";
        var ret = CompareDeepInner(obj1, obj2, ref fp, out left, out right);
        failurePath = fp;
        return ret;
    }

    private static bool CompareDeepInner(object? obj1, object? obj2, ref string? failurePath, out object? left,
        out object? right) {
        if (obj1 == null) {
            if (obj2 == null) {
                left = right = null;
                return true;
            }

            left = obj1;
            right = obj2;
            return false;
        }

        if (obj2 == null) {
            left = obj1;
            right = obj2;
            return false;
        }

        var type1 = obj1.GetType();
        var type2 = obj2.GetType();

        if (type1 != type2) {
            left = obj1;
            right = obj2;
            return false;
        }

        if (type1.IsPrimitive || obj1 is string) {
            var nativeEq = obj1.Equals(obj2);
            if (nativeEq) {
                left = right = null;
                return true;
            }

            left = obj1;
            right = obj2;
            return false;
        }

        if (type1.IsArray) {
            var first = (obj1 as Array)!;
            var second = (obj2 as Array)!;

            var en = first.GetEnumerator();
            var i = 0;
            while (en.MoveNext()) {
                if (!CompareDeep(en.Current, second.GetValue(i), out failurePath, out left, out right)) {
                    failurePath = $"[{i}]" + failurePath;
                    return false;
                }

                i++;
            }

            if (first.Length != second.Length) {
                failurePath = "<array_length_mismatch>" + failurePath;
                left = first.Length;
                right = second.Length;
                return false;
            }


            left = right = null;
            return true;
        }

        if (typeof(System.Collections.IDictionary).IsAssignableFrom(type1)) {
            var dict1 = (System.Collections.IDictionary)obj1;
            var dict2 = (System.Collections.IDictionary)obj2;

            if (dict1.Count != dict2.Count) {
                failurePath = "<dict_count>" + failurePath;
                left = dict1.Count;
                right = dict2.Count;
                return false;
            }

            foreach (var key in dict1.Keys) {
                if (!dict2.Contains(key)) {
                    failurePath = $"<missing_key:{key}>" + failurePath;
                    left = key;
                    right = null;
                    return false;
                }

                if (!CompareDeep(dict1[key], dict2[key], out failurePath, out left, out right)) {
                    failurePath = $".{key}" + failurePath;
                    return false;
                }
            }

            left = right = null;
            return true;
        }

        if (obj1 is IComparable comparable) {
            if (comparable.CompareTo(obj2) == 0) {
                left = right = null;
                return true;
            } else {
                left = obj1;
                right = obj2;
                return false;
            }
        }

        foreach (var fi in type1.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)) {
            var val = fi.GetValue(obj1);
            var tval = fi.GetValue(obj2);

            if (!CompareDeep(val, tval, out failurePath, out left, out right)) {
                failurePath = $".{fi.Name}" + failurePath;
                return false;
            }
        }

        left = right = null;
        return true;
    }
}
