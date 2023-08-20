#nullable enable

using System;

namespace Common.SmartCache;

public sealed record CacheMissDescriptor(
    byte[] RawKey,
    DateTime Timestamp,
    string Location,
    byte[]? RawValue,
    string? TypeName);
