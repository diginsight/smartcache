namespace Diginsight.SmartCache;

public interface IManualSize
{
    SizeResult GetSize(SizeGetter innerGetSize);

    public delegate SizeResult SizeGetter(object? obj);
}
