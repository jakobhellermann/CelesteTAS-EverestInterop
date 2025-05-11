using System;
using Newtonsoft.Json;

namespace Snapshots;

public abstract class NullableJsonConverter<T> : JsonConverter {
    public sealed override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
        if (value == null) {
            writer.WriteNull();
            return;
        }

        WriteJson(writer, (T)value, serializer);
    }

    protected abstract void WriteJson(JsonWriter writer, T? value, JsonSerializer serializer);

    public sealed override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer) {
        if (existingValue != null && existingValue is not T) {
            throw new JsonSerializationException(
                $"Converter cannot read JSON with the specified existing value. {typeof(T)} is required.");
        }

        return ReadJson(reader,
            objectType,
            existingValue == null ? default : (T)existingValue,
            existingValue != null,
            serializer);
    }

    protected abstract T? ReadJson(JsonReader reader, Type objectType, T? existingValue, bool hasExistingValue,
        JsonSerializer serializer);

    public sealed override bool CanConvert(Type objectType) {
        var underlying = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return typeof(T).IsAssignableFrom(underlying);
    }
}

public abstract class NullableJsonConverter : JsonConverter {
    public sealed override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
        if (value == null) {
            writer.WriteNull();
            return;
        }

        WriteJsonInner(writer, value, serializer);
    }

    protected abstract void WriteJsonInner(JsonWriter writer, object value, JsonSerializer serializer);

    public sealed override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer) {
        if (existingValue != null) {
            throw new JsonSerializationException(
                $"Converter cannot read JSON with the specified existing value.");
        }

        return ReadJsonInner(reader, objectType, existingValue, serializer);
    }

    protected abstract object? ReadJsonInner(JsonReader reader, Type objectType, object? existingValue,
        JsonSerializer serializer);

    protected abstract bool CanConvertInner(Type objectType);

    public sealed override bool CanConvert(Type objectType) {
        var underlying = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return CanConvertInner(underlying);
    }
}
