#nullable enable

using System;
using System.Threading.Tasks;

namespace Common.SmartCache;

public interface IInvalidatable
{
    bool IsInvalidatedBy(IInvalidationRule invalidationRule, out Func<Task>? invalidationCallback);
}
