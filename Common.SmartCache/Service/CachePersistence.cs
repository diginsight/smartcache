#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using Microsoft.Extensions.FileProviders;

namespace Common;

public sealed class CachePersistence : ICachePersistence
{
    private const string RootFileName = "root.txt";
    private const string RootTempFileName = "root.tmp.txt";

    private readonly IFileProvider fileProvider;
    private readonly SemaphoreSlim semaphore = new (1, 1);

    public CachePersistence(ICachePersistenceFileProvider fileProvider)
    {
        this.fileProvider = fileProvider;
    }

    public Task PersistAsync(ICacheKey key, IValueEntry valueEntry)
    {
        return GeneralWriteAsync(key, (k, r, w) => { PersistCore(k, valueEntry, r, w); });
    }

    public Task<Optional<T>> TryRetrieveAsync<T>(ICacheKey key, DateTime? minimumCreationDate)
    {
        return GeneralReadAsync(key, (k, r) => TryRetrieveCore<T>(k, minimumCreationDate, r));
    }

    public Task RemoveAsync(ICacheKey key)
    {
        return GeneralWriteAsync(key, RemoveCore);
    }

    private async Task GeneralWriteAsync(ICacheKey key, Action<string, BlockReader, BlockWriter> write)
    {
        await semaphore.WaitAsync();
        try
        {
            using (TextReader reader = OpenRead())
            await using (TextWriter writer = OpenWrite())
            {
                write(CacheSerialization.SerializeToString(key), MakeBlockReader(reader), MakeBlockWriter(writer));
            }

            File.Move(
                fileProvider.GetFileInfo(RootTempFileName).PhysicalPath,
                fileProvider.GetFileInfo(RootFileName).PhysicalPath,
                true);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<TResult> GeneralReadAsync<TResult>(ICacheKey key, Func<string, BlockReader, TResult> read)
    {
        await semaphore.WaitAsync();
        try
        {
            using TextReader reader = OpenRead();

            return read(CacheSerialization.SerializeToString(key), MakeBlockReader(reader));
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static void PersistCore(string rawKey, IValueEntry valueEntry, BlockReader tryReadBlock, BlockWriter writeBlock)
    {
        void WriteTargetBlock()
        {
            string rawValue = CacheSerialization.SerializeToString(valueEntry.Data, valueEntry.Type);
            writeBlock(rawKey, valueEntry.CreationDate, rawValue);
        }

        bool found = false;
        while (tryReadBlock() is var (otherRawKey, otherCreationDateTicks, otherRawValue))
        {
            if (!found)
            {
                int compare = string.Compare(rawKey, otherRawKey, StringComparison.Ordinal);

                if (compare <= 0)
                {
                    WriteTargetBlock();
                    found = true;
                }

                if (compare == 0)
                {
                    continue;
                }
            }

            writeBlock(otherRawKey, otherCreationDateTicks, otherRawValue);
        }

        if (!found)
        {
            WriteTargetBlock();
        }
    }

    private static Optional<T> TryRetrieveCore<T>(string rawKey, DateTime? minimumCreationDate, BlockReader tryReadBlock)
    {
        while (tryReadBlock() is var (otherRawKey, creationDate, rawValue))
        {
            int compare = string.Compare(rawKey, otherRawKey, StringComparison.Ordinal);

            if (compare < 0)
            {
                continue;
            }

            if (compare > 0 || minimumCreationDate > creationDate)
            {
                return default;
            }

            return new Optional<T>(CacheSerialization.Deserialize<T>(rawValue));
        }

        return default;
    }

    private static void RemoveCore(string rawKey, BlockReader tryReadBlock, BlockWriter writeBlock)
    {
        bool found = false;
        while (tryReadBlock() is var (otherRawKey, otherCreationDateTicks, otherRawValue))
        {
            if (!found)
            {
                int compare = string.Compare(rawKey, otherRawKey, StringComparison.Ordinal);

                if (compare <= 0)
                {
                    found = true;
                }

                if (compare == 0)
                {
                    continue;
                }
            }

            writeBlock(otherRawKey, otherCreationDateTicks, otherRawValue);
        }
    }

    private TextReader OpenRead() => new StreamReader(OpenFile(false), CacheSerialization.InnerEncoding);

    private TextWriter OpenWrite() => new StreamWriter(OpenFile(true), CacheSerialization.InnerEncoding);

    private FileStream OpenFile(bool write)
    {
        string fullPath = fileProvider.GetFileInfo(write ? RootTempFileName : RootFileName).PhysicalPath;

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        return File.Open(
            fullPath,
            new FileStreamOptions()
            {
                Mode = FileMode.OpenOrCreate,
                Access = write ? FileAccess.ReadWrite : FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous,
            });
    }

    private static BlockReader MakeBlockReader(TextReader reader)
    {
        return () =>
        {
            if (reader.ReadLine() is not { } line)
            {
                return null;
            }

            string rawKey = line;
            DateTime creationDate = DateTime.ParseExact(
                reader.ReadLine() ?? throw new EndOfStreamException(),
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            string rawValue = reader.ReadLine() ?? throw new EndOfStreamException();

            return (rawKey, creationDate, rawValue);
        };
    }

    private static BlockWriter MakeBlockWriter(TextWriter writer)
    {
        return (rawKey, creationDate, rawValue) =>
        {
            writer.WriteLine(rawKey);
            writer.WriteLine(creationDate.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteLine(rawValue);
        };
    }

    private delegate (string RawKey, DateTime CreationDate, string RawValue)? BlockReader();

    private delegate void BlockWriter(string rawKey, DateTime creationDate, string rawValue);
}
