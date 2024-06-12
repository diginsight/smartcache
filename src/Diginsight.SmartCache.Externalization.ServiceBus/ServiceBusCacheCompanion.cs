using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Diginsight.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Diginsight.SmartCache.Externalization.ServiceBus;

internal sealed class ServiceBusCacheCompanion : BackgroundService, ICacheCompanion
{
    private const string SourcePropertyName = "source";
    private const string DestinationPropertyName = "destination";
    private const string ChunkIndexPropertyName = "chunkIndex";
    private const string ChunkCountPropertyName = "chunkCount";

    private const string GetRequestSubject = "get?";
    private const string GetResponseMessageSubject = "get!";
    private const string CacheMissMessageSubject = "cachemiss";
    private const string InvalidateMessageSubject = "invalidate";

    private readonly ILogger logger;
    private readonly Lazy<ISmartCache> smartCacheLazy;
    private readonly IServiceProvider serviceProvider;
    private readonly ISmartCacheServiceBusOptions serviceBusOptions;

    private readonly MreUtils mreUtils;

    private readonly ClientHolder clientHolder;

    private readonly CommandDictionary getRequestDictionary;
    private readonly QueryDictionary getResponseDictionary;
    private readonly CommandDictionary cacheMissDictionary;
    private readonly CommandDictionary invalidateDictionary;

    private readonly ManualResetEventSlim executionMre = new ();
    private readonly ManualResetEventSlim uninstallationMre = new ();

    private readonly ObjectFactory<ServiceBusCacheLocation> makeLocation =
        ActivatorUtilities.CreateFactory<ServiceBusCacheLocation>([ typeof(string) ]);

    private IEnumerable<CacheEventNotifier>? eventNotifiers;

    private volatile bool disposed = false;

    public string SelfLocationId => serviceBusOptions.SubscriptionName;

    public IEnumerable<PassiveCacheLocation> PassiveLocations { get; }

    private ISmartCache SmartCache => smartCacheLazy.Value;

    public ServiceBusCacheCompanion(
        ILogger<ServiceBusCacheCompanion> logger,
        Lazy<ISmartCache> smartCacheLazy,
        IServiceProvider serviceProvider,
        IOptions<SmartCacheServiceBusOptions> serviceBusOptions,
        IEnumerable<PassiveCacheLocation> passiveLocations,
        TimeProvider? timeProvider = null
    )
    {
        this.logger = logger;
        this.smartCacheLazy = smartCacheLazy;
        this.serviceProvider = serviceProvider;
        this.serviceBusOptions = serviceBusOptions.Value;

        PassiveLocations = passiveLocations;

        mreUtils = ActivatorUtilities.CreateInstance<MreUtils>(serviceProvider);

        clientHolder = ActivatorUtilities.CreateInstance<ClientHolder>(serviceProvider, mreUtils);

        timeProvider ??= TimeProvider.System;
        getRequestDictionary = new CommandDictionary(timeProvider);
        getResponseDictionary = new QueryDictionary(timeProvider);
        cacheMissDictionary = new CommandDictionary(timeProvider);
        invalidateDictionary = new CommandDictionary(timeProvider);
    }

    private sealed class MreUtils
    {
        private readonly ILogger logger;

        public MreUtils(ILogger<MreUtils> logger)
        {
            this.logger = logger;
        }

        public void Wait(ManualResetEventSlim mre, CancellationToken cancellationToken, string description)
        {
            logger.LogTrace("Waiting for {Description}", description);
            mre.Wait(cancellationToken);
        }

        public bool Wait(ManualResetEventSlim mre, TimeSpan timeout, CancellationToken cancellationToken, string description)
        {
            logger.LogTrace("Waiting for {Description}", description);
            return mre.Wait(timeout, cancellationToken);
        }

        public void Reset(ManualResetEventSlim? mre, string description)
        {
            if (mre is null)
                return;
            logger.LogTrace("Resetting {Description}", description);
            mre.Reset();
        }

        public void Set(ManualResetEventSlim mre, string description)
        {
            logger.LogTrace("Setting {Description}", description);
            mre.Set();
        }

        public void Dispose(ManualResetEventSlim? mre, string description)
        {
            if (mre is null)
                return;
            logger.LogTrace("Disposing {Description}", description);
            mre.Dispose();
        }
    }

    private sealed class ClientHolder : IDisposable
    {
        private static readonly TimeSpan SenderTimeout = TimeSpan.FromSeconds(5);

        private readonly ILogger<ClientHolder> logger;
        private readonly MreUtils mreUtils;
        private readonly ISmartCacheServiceBusOptions serviceBusOptions;

        private ManualResetEventSlim? mre = new ();

        private ServiceBusClient? client;
        private ServiceBusSender? sender;

        public ClientHolder(
            ILogger<ClientHolder> logger,
            MreUtils mreUtils,
            IOptions<SmartCacheServiceBusOptions> serviceBusOptions
        )
        {
            this.logger = logger;
            this.mreUtils = mreUtils;
            this.serviceBusOptions = serviceBusOptions.Value;
        }

        public ServiceBusClient GetClient(CancellationToken cancellationToken)
        {
            mreUtils.Wait(mre ?? throw new ObjectDisposedException(nameof(ClientHolder)), cancellationToken, "Client holder - Client");
            return client!;
        }

        public async Task<bool> SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken)
        {
            // ReSharper disable once LocalVariableHidesMember
            ManualResetEventSlim mre = this.mre ?? throw new ObjectDisposedException(nameof(ClientHolder));

            try
            {
                if (!mreUtils.Wait(mre, SenderTimeout, cancellationToken, "Client holder - Sender"))
                {
                    logger.LogWarning("Message not sent due to initialization timeout");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Message not sent due to stopping");
                return false;
            }

            await sender!.SendMessageAsync(message, CancellationToken.None);
            return true;
        }

        public void Invalidate()
        {
            mreUtils.Reset(mre, "Client holder");
            client = null;
            sender = null;
        }

        public void Initialize()
        {
            if (mre is null)
            {
                throw new ObjectDisposedException(nameof(ClientHolder));
            }

            client = new ServiceBusClient(serviceBusOptions.ConnectionString);
            sender = client.CreateSender(serviceBusOptions.TopicName);
            mreUtils.Set(mre, "Client holder");
        }

        public void Dispose()
        {
            Invalidate();
            mreUtils.Dispose(Interlocked.Exchange(ref mre, null), "Client holder");
        }
    }

    private abstract class ChunkedBodyDictionary : IDisposable
    {
        private readonly TimeProvider timeProvider;
        private readonly ConcurrentDictionary<string, ChunkedBody> underlying = new ();
        private readonly Timer cleanupTimer;

        private volatile bool disposed = false;

        protected bool Disposed => disposed;

        protected ChunkedBodyDictionary(TimeProvider timeProvider)
        {
            this.timeProvider = timeProvider;
            TimeSpan cleanupPeriod = TimeSpan.FromMinutes(1);
            cleanupTimer = new Timer(x => Cleanup((TimeSpan)x!), cleanupPeriod, cleanupPeriod, cleanupPeriod);
        }

        private void Cleanup(TimeSpan? maybeAge)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();

            foreach ((string messageId, ChunkedBody chunkedBody) in underlying)
            {
                if (maybeAge is not { } age || now - chunkedBody.Timestamp <= age)
                    continue;

                underlying.TryRemove(messageId, out _);
                chunkedBody.Dispose();
            }
        }

        protected ChunkedBody InnerGetOrAdd(string messageId)
        {
            return underlying.GetOrAdd(
#if NET || NETSTANDARD2_1_OR_GREATER
                messageId, static (_, a) => new ChunkedBody(a.GetUtcNow()), timeProvider
#else
                messageId, _ => new ChunkedBody(timeProvider.GetUtcNow())
#endif
            );
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            cleanupTimer.Dispose();
            Cleanup(null);
            underlying.Clear();
        }
    }

    private sealed class CommandDictionary : ChunkedBodyDictionary
    {
        public CommandDictionary(TimeProvider timeProvider)
            : base(timeProvider) { }

        public byte[]? Set(string messageId, byte[] body, int chunkIndex, int chunkCount)
        {
            if (Disposed)
                return null;

            ChunkedBody chunkedBody = InnerGetOrAdd(messageId);
            return chunkedBody.Set(body, chunkIndex, chunkCount) ? chunkedBody.Get(CancellationToken.None) : null;
        }
    }

    private sealed class QueryDictionary : ChunkedBodyDictionary
    {
        public QueryDictionary(TimeProvider timeProvider)
            : base(timeProvider) { }

        public byte[] Get(string messageId, CancellationToken cancellationToken)
        {
            return Disposed ? [ ] : InnerGetOrAdd(messageId).Get(cancellationToken);
        }

        public void Set(string messageId, byte[] body, int chunkIndex, int chunkCount)
        {
            if (Disposed)
                return;

            _ = InnerGetOrAdd(messageId).Set(body, chunkIndex, chunkCount);
        }
    }

    private async Task InstallAsync(CancellationToken cancellationToken)
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(logger);

        string topicName = serviceBusOptions.TopicName;
        string subscriptionName = serviceBusOptions.SubscriptionName;
        const string ruleName = "$Default";

        ServiceBusAdministrationClient administrationClient = new (serviceBusOptions.ConnectionString);

        RetryStrategyOptions retryStrategyOptions = new ()
        {
            ShouldHandle = static args =>
            {
                Exception exception = args.Outcome.Exception!;
                bool shouldHandle = exception is ServiceBusException sbException &&
                    (sbException.IsTransient ||
                        sbException.Reason is ServiceBusFailureReason.MessagingEntityAlreadyExists or ServiceBusFailureReason.MessagingEntityNotFound);
                return new ValueTask<bool>(shouldHandle);
            },
        };

        ResiliencePipeline resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(retryStrategyOptions)
            .Build();

        await resiliencePipeline.ExecuteAsync(InstallTopicAsync, cancellationToken);
        await resiliencePipeline.ExecuteAsync(InstallSubscriptionAsync, cancellationToken);
        await resiliencePipeline.ExecuteAsync(InstallRuleAsync, cancellationToken);

        clientHolder.Initialize();

        async ValueTask InstallTopicAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            logger.LogDebug("Installing topic '{TopicName}'", topicName);
            if (await administrationClient.TopicExistsAsync(topicName, CancellationToken.None))
            {
                TopicProperties topicProperties = await administrationClient.GetTopicAsync(topicName, CancellationToken.None);
                topicProperties.AutoDeleteOnIdle = TimeSpan.FromDays(7);
                topicProperties.DefaultMessageTimeToLive = TimeSpan.FromHours(1);
                topicProperties.EnableBatchedOperations = true;

                await administrationClient.UpdateTopicAsync(topicProperties, CancellationToken.None);
            }
            else
            {
                await administrationClient.CreateTopicAsync(
                    new CreateTopicOptions(topicName)
                    {
                        AutoDeleteOnIdle = TimeSpan.FromDays(7),
                        DefaultMessageTimeToLive = TimeSpan.FromHours(1),
                        EnableBatchedOperations = true,
                        EnablePartitioning = true,
                    },
                    CancellationToken.None
                );
            }
        }

        async ValueTask InstallSubscriptionAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            logger.LogDebug("Installing subscription '{SubscriptionName}'", subscriptionName);

            if (await administrationClient.SubscriptionExistsAsync(topicName, subscriptionName, CancellationToken.None))
            {
                SubscriptionProperties subscriptionProperties = await administrationClient.GetSubscriptionAsync(topicName, subscriptionName, CancellationToken.None);
                subscriptionProperties.AutoDeleteOnIdle = TimeSpan.FromDays(1);
                subscriptionProperties.DefaultMessageTimeToLive = TimeSpan.FromHours(1);
                subscriptionProperties.LockDuration = TimeSpan.FromSeconds(30);
                subscriptionProperties.DeadLetteringOnMessageExpiration = false;
                subscriptionProperties.EnableBatchedOperations = true;
                subscriptionProperties.EnableDeadLetteringOnFilterEvaluationExceptions = true;
                subscriptionProperties.MaxDeliveryCount = 2;

                await administrationClient.UpdateSubscriptionAsync(subscriptionProperties, CancellationToken.None);
            }
            else
            {
                await administrationClient.CreateSubscriptionAsync(
                    new CreateSubscriptionOptions(topicName, subscriptionName)
                    {
                        AutoDeleteOnIdle = TimeSpan.FromDays(1),
                        DefaultMessageTimeToLive = TimeSpan.FromHours(1),
                        LockDuration = TimeSpan.FromSeconds(30),
                        DeadLetteringOnMessageExpiration = false,
                        EnableBatchedOperations = true,
                        EnableDeadLetteringOnFilterEvaluationExceptions = true,
                        MaxDeliveryCount = 2,
                    },
                    CancellationToken.None
                );
            }
        }

        async ValueTask InstallRuleAsync(CancellationToken ct)
        {
            logger.LogDebug("Installing subscription rule");

            string filterExpression = $"[{SourcePropertyName}] != '{subscriptionName}' AND ((NOT EXISTS ([{DestinationPropertyName}])) OR [{DestinationPropertyName}] = '{subscriptionName}')";
            bool createRule = true;
            await foreach (RuleProperties ruleProperties in administrationClient.GetRulesAsync(topicName, subscriptionName, ct))
            {
                if (ruleProperties.Name == ruleName && ruleProperties.Filter is SqlRuleFilter filter && filter.SqlExpression == filterExpression)
                {
                    createRule = false;
                }
                else
                {
                    await administrationClient.DeleteRuleAsync(topicName, subscriptionName, ruleProperties.Name, CancellationToken.None);
                }
            }

            ct.ThrowIfCancellationRequested();

            if (createRule)
            {
                await administrationClient.CreateRuleAsync(
                    topicName,
                    subscriptionName,
                    new CreateRuleOptions(ruleName, new SqlRuleFilter(filterExpression)),
                    CancellationToken.None
                );
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string topicName = serviceBusOptions.TopicName;
        string subscriptionName = serviceBusOptions.SubscriptionName;
        TimeSpan receiveWaitTime = TimeSpan.FromSeconds(10);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await InstallAsync(stoppingToken);

                logger.LogDebug("Starting messages listen loop");

                try
                {
                    await using ServiceBusClient client = clientHolder.GetClient(stoppingToken);
                    await using ServiceBusReceiver receiver = client.CreateReceiver(topicName, subscriptionName);

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        ServiceBusReceivedMessage? receivedMessage;
                        try
                        {
                            receivedMessage = await receiver.ReceiveMessageAsync(receiveWaitTime, stoppingToken);
                        }
                        catch (Exception exception)
                        {
                            if (exception is not OperationCanceledException)
                            {
                                logger.LogWarning(exception, "Error receiving companion message");
                            }
                            break;
                        }

                        if (receivedMessage is null)
                            continue;

                        await ProcessAsync(receiver, receivedMessage, stoppingToken);
                    }
                }
                finally
                {
                    clientHolder.Invalidate();
                }
            }
        }
        finally
        {
            mreUtils.Set(executionMre, "Execution");
        }
    }

    private async Task ProcessAsync(ServiceBusReceiver receiver, ServiceBusReceivedMessage receivedMessage, CancellationToken stoppingToken)
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(logger);

        if (receivedMessage.ApplicationProperties.GetValueOrDefault(SourcePropertyName) is not string emitter)
        {
            logger.LogDebug("Received message without emitter; will be discarded");
            await receiver.AbandonMessageAsync(receivedMessage, cancellationToken: CancellationToken.None);
            return;
        }

        string subject = receivedMessage.Subject?.ToLowerInvariant() ?? "";
        logger.LogDebug("Received message from '{Emitter}' with subject '{Subject}'", emitter, subject);

        StrongBox<bool> completedBox = new (false);

        try
        {
            async Task CompleteMessageAsync()
            {
                await receiver.CompleteMessageAsync(receivedMessage, cancellationToken: CancellationToken.None);
                completedBox.Value = true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int GetChunkPValue(string name)
            {
                return receivedMessage.ApplicationProperties.TryGetValue(name, out object? rawValue) && rawValue is int value ? value : 1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int GetChunkIndex() => GetChunkPValue(ChunkIndexPropertyName) - 1;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int GetChunkCount() => GetChunkPValue(ChunkCountPropertyName);

            async Task<Func<Task>> CoreProcessQueryAsync(QueryDictionary queryDictionary)
            {
                string messageId = receivedMessage.CorrelationId;
                byte[] body = receivedMessage.Body.ToArray();
                int chunkIndex = GetChunkIndex();
                int chunkCount = GetChunkCount();
                await CompleteMessageAsync();

                return () =>
                {
                    queryDictionary.Set(messageId, body, chunkIndex, chunkCount);
                    return Task.CompletedTask;
                };
            }

            async Task<Func<Task>> CoreProcessCommandAsync(CommandDictionary commandDictionary, Func<byte[], Task> finishAsync)
            {
                int chunkCount = GetChunkCount();
                byte[]? body = chunkCount == 1
                    ? receivedMessage.Body.ToArray()
                    : commandDictionary.Set(receivedMessage.MessageId, receivedMessage.Body.ToArray(), GetChunkIndex(), chunkCount);
                await CompleteMessageAsync();

                return () => body is null ? Task.CompletedTask : finishAsync(body);
            }

            Task<Func<Task>> ProcessGetAsync()
            {
                return CoreProcessCommandAsync(getRequestDictionary, FinishAsync);

                async Task FinishAsync(byte[] incomingBody)
                {
                    byte[]? outgoingBody;
                    using (TimerLap lap = SmartCacheObservability.Instruments.FetchDuration.StartLap(SmartCacheObservability.Tags.Type.Direct))
                    {
                        object key = SmartCacheSerialization.Deserialize<object>(incomingBody);

                        if (SmartCache.TryGetDirectFromMemory(key, out Type? type, out object? value))
                        {
                            lap.AddTags(SmartCacheObservability.Tags.Found.True);
                            outgoingBody = SmartCacheSerialization.SerializeToBytes(value, type);
                        }
                        else
                        {
                            lap.AddTags(SmartCacheObservability.Tags.Found.False);
                            outgoingBody = null;
                        }
                    }

                    _ = await SendMessageAsync(
                        outgoingBody,
                        GetResponseMessageSubject,
                        emitter,
                        () => new ServiceBusMessage() { CorrelationId = receivedMessage.MessageId },
                        stoppingToken
                    );
                }
            }

            Task<Func<Task>> ProcessGetReplyAsync() => CoreProcessQueryAsync(getResponseDictionary);

            Task<Func<Task>> ProcessCacheMissAsync()
            {
                return CoreProcessCommandAsync(cacheMissDictionary, FinishAsync);

                Task FinishAsync(byte[] body)
                {
                    CacheMissDescriptor descriptor = SmartCacheSerialization.Deserialize<CacheMissDescriptor>(body.ToArray());
                    SmartCache.AddExternalMiss(descriptor);
                    return Task.CompletedTask;
                }
            }

            Task<Func<Task>> ProcessInvalidateAsync()
            {
                return CoreProcessCommandAsync(invalidateDictionary, FinishAsync);

                Task FinishAsync(byte[] body)
                {
                    InvalidationDescriptor descriptor = SmartCacheSerialization.Deserialize<InvalidationDescriptor>(body.ToArray());
                    SmartCache.Invalidate(descriptor);
                    return Task.CompletedTask;
                }
            }

            Func<Task>? finishAsync = subject switch
            {
                GetRequestSubject => await ProcessGetAsync(),
                GetResponseMessageSubject => await ProcessGetReplyAsync(),
                CacheMissMessageSubject => await ProcessCacheMissAsync(),
                InvalidateMessageSubject => await ProcessInvalidateAsync(),
                _ => null,
            };

            if (finishAsync is not null)
            {
                TaskUtils.RunAndForget(finishAsync, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Error processing '{Subject}' message", subject);
            if (!completedBox.Value)
            {
                await receiver.DeadLetterMessageAsync(receivedMessage, "ExceptionProcessing", exception.Message, CancellationToken.None);
                completedBox.Value = true;
            }
        }
        finally
        {
            if (!completedBox.Value)
            {
                await receiver.AbandonMessageAsync(receivedMessage, cancellationToken: CancellationToken.None);
            }
        }
    }

    private async Task<bool> SendMessageAsync(
        byte[]? body, string subject, string? destination, Func<ServiceBusMessage>? makeMessage, CancellationToken cancellationToken
    )
    {
        ServiceBusMessage MakeMessage()
        {
            ServiceBusMessage message = makeMessage?.Invoke() ?? new ServiceBusMessage();
            message.Subject = subject;
            message.ApplicationProperties[SourcePropertyName] = SelfLocationId;
            if (destination is not null)
            {
                message.ApplicationProperties[DestinationPropertyName] = destination;
            }
            return message;
        }

        void LogSending(int chunkIndex, int chunkCount)
        {
            if (destination is null)
                logger.LogDebug("Sending message {ChunkIndex}/{ChunkCount} for '{Subject}'", chunkIndex + 1, chunkCount, subject);
            else
                logger.LogDebug("Sending message {ChunkIndex}/{ChunkCount} for '{Subject}' to '{Destination}'", chunkIndex + 1, chunkCount, subject, destination);
        }

        if (body is null)
        {
            LogSending(0, 1);
            return await clientHolder.SendMessageAsync(MakeMessage(), cancellationToken);
        }

        const int chunkLength = 200 << 10;
        int bodyLength = body.Length;
        int chunkCount = bodyLength / chunkLength + 1;

        if (chunkCount == 1)
        {
            ServiceBusMessage message = MakeMessage();
            message.Body = BinaryData.FromBytes(body);

            LogSending(0, 1);
            return await clientHolder.SendMessageAsync(message, cancellationToken);
        }

        async Task<bool> SendChunkAsync(int chunkIndex)
        {
            ServiceBusMessage message = MakeMessage();
            message.ApplicationProperties[ChunkIndexPropertyName] = chunkIndex + 1;
            message.ApplicationProperties[ChunkCountPropertyName] = chunkCount;

            Range range = (chunkLength * chunkIndex)..Math.Min(chunkLength * (chunkIndex + 1), bodyLength);
            (int rangeOffset, int rangeLength) = range.GetOffsetAndLength(bodyLength);
            message.Body = BinaryData.FromBytes(body.AsMemory(rangeOffset, rangeLength));

            LogSending(chunkIndex, chunkCount);
            return await clientHolder.SendMessageAsync(message, cancellationToken);
        }

        return (await Task.WhenAll(Enumerable.Range(0, chunkCount).Select(SendChunkAsync)))
            .Any(static sent => !sent);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            await UninstallAsync();
        }
    }

    private async Task UninstallAsync()
    {
        using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(logger);

        try
        {
            mreUtils.Wait(executionMre, CancellationToken.None, "Execution");

            clientHolder.Invalidate();

            ServiceBusAdministrationClient administrationClient = new (serviceBusOptions.ConnectionString);

            string topicName = serviceBusOptions.TopicName;
            string subscriptionName = serviceBusOptions.SubscriptionName;

            logger.LogDebug("Deleting subscription '{SubscriptionName}'", subscriptionName);
            if (await administrationClient.SubscriptionExistsAsync(topicName, subscriptionName))
            {
                await administrationClient.DeleteSubscriptionAsync(topicName, subscriptionName);
            }
        }
        finally
        {
            mreUtils.Set(executionMre, "Uninstallation");
        }
    }

    public override void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        base.Dispose();

        getRequestDictionary.Dispose();
        getResponseDictionary.Dispose();
        cacheMissDictionary.Dispose();
        invalidateDictionary.Dispose();

        clientHolder.Dispose();

        mreUtils.Set(uninstallationMre, "Uninstallation");
        mreUtils.Dispose(uninstallationMre, "Uninstallation");
    }

    public Task<IEnumerable<ActiveCacheLocation>> GetActiveLocationsAsync(IEnumerable<string> locationIds)
    {
        return Task.FromResult(
            locationIds
                .Select(x => makeLocation(serviceProvider, [ x ]))
                .ToArray<ActiveCacheLocation>()
                .AsEnumerable()
        );
    }

    public Task<IEnumerable<CacheEventNotifier>> GetAllEventNotifiersAsync()
    {
        return Task.FromResult(eventNotifiers ??= [ ActivatorUtilities.CreateInstance<ServiceBusCacheEventNotifier>(serviceProvider) ]);
    }

    private sealed class ServiceBusCacheLocation : ActiveCacheLocation
    {
        private readonly ILogger logger;
        private readonly ServiceBusCacheCompanion companion;

        public override KeyValuePair<string, object?> MetricTag => SmartCacheObservability.Tags.Type.Distributed;

        public ServiceBusCacheLocation(
            string subscriptionName,
            ILogger<ServiceBusCacheLocation> logger,
            ServiceBusCacheCompanion companion
        )
            : base(subscriptionName)
        {
            this.logger = logger;
            this.companion = companion;
        }

        public override async Task<CacheLocationOutput<TValue>?> GetAsync<TValue>(
            CachePayloadHolder<object> keyHolder, DateTimeOffset minimumCreationDate, Action markInvalid, CancellationToken cancellationToken
        )
        {
            using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(logger, () => new { key = keyHolder.Payload, minimumCreationDate });
            logger.LogDebug("Sending message for get request to '{Destination}'", Id);

            using TimerLap lap = SmartCacheObservability.Instruments.FetchDuration.CreateLap(SmartCacheObservability.Tags.Type.Distributed);

            string messageId = Guid.NewGuid().ToString("N");

            byte[] body;
            using (lap.Start())
            {
                bool sent = await companion.SendMessageAsync(
                    keyHolder.GetAsBytes(),
                    GetRequestSubject,
                    Id,
                    () => new ServiceBusMessage() { MessageId = messageId },
                    cancellationToken
                );

                if (sent)
                {
                    try
                    {
                        CancellationToken combinedCancellationToken = CancellationTokenSource
                            .CreateLinkedTokenSource(cancellationToken, new CancellationTokenSource(companion.serviceBusOptions.RequestTimeout).Token)
                            .Token;
                        body = companion.getResponseDictionary.Get(messageId, combinedCancellationToken);
                    }
                    catch (OperationCanceledException oce) when (oce.CancellationToken != cancellationToken)
                    {
                        body = [ ];
                    }
                }
                else
                {
                    body = [ ];
                }
            }

            long valueSerializedSize = body.LongLength;
            if (!(valueSerializedSize > 0))
            {
                markInvalid();
                logger.LogDebug("Partial cache miss: Failed to retrieve value from peer '{PeerId}'", Id);

                lap.AddTags(SmartCacheObservability.Tags.Found.False);
                return null;
            }

            TValue item;
            using (SmartCacheObservability.Instruments.SerializationDuration.StartLap(SmartCacheObservability.Tags.Operation.Deserialization, SmartCacheObservability.Tags.Subject.Value))
            {
                item = SmartCacheSerialization.Deserialize<TValue>(body);
            }

            double latencyMsecD = lap.ElapsedMilliseconds;
            long latencyMsecL = (long)latencyMsecD;

            logger.LogDebug("Cache hit (latency: {LatencyMsec} ms): Returning up-to-date value from peer '{PeerId}'", latencyMsecL, Id);

            lap.AddTags(SmartCacheObservability.Tags.Found.True);
            return new CacheLocationOutput<TValue>(item, valueSerializedSize, latencyMsecD);
        }
    }

    private sealed class ServiceBusCacheEventNotifier : CacheEventNotifier
    {
        private readonly ILogger logger;
        private readonly IHostApplicationLifetime applicationLifetime;
        private readonly ServiceBusCacheCompanion companion;

        public ServiceBusCacheEventNotifier(
            ILogger<ServiceBusCacheEventNotifier> logger,
            IHostApplicationLifetime applicationLifetime,
            ServiceBusCacheCompanion companion
        )
        {
            this.logger = logger;
            this.applicationLifetime = applicationLifetime;
            this.companion = companion;
        }

        protected override Task NotifyCacheMissAsync(CachePayloadHolder<CacheMissDescriptor> descriptorHolder)
        {
            return NotifyAsync(descriptorHolder, CacheMissMessageSubject);
        }

        protected override Task NotifyInvalidationAsync(CachePayloadHolder<InvalidationDescriptor> descriptorHolder)
        {
            return NotifyAsync(descriptorHolder, InvalidateMessageSubject);
        }

        private async Task NotifyAsync(ICachePayloadHolder descriptorHolder, string subject)
        {
            logger.LogDebug("Sending message for '{Subject}' event notification", subject);

            _ = await companion.SendMessageAsync(descriptorHolder.GetAsBytes(), subject, null, null, applicationLifetime.ApplicationStopping);
        }
    }
}
