#nullable enable

namespace Common;

public sealed record InvalidationDescriptor(byte[] RawRule, string PodIp);
