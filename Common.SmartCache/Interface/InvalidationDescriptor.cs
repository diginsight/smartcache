#nullable enable

namespace Common.SmartCache;

public sealed record InvalidationDescriptor(byte[] RawRule, string PodIp);
