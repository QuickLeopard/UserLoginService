using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using UserLoginService.Data;
using UserLoginService.Models;
using UserLoginService.Utilities;

namespace UserLoginService.Services
{
    public interface IRedisStreamService
    {
        Task PublishUserLoginAsync(UserLoginRecord loginRecord);
        Task StartConsumingAsync(CancellationToken cancellationToken);
    }

    public class RedisStreamService : IRedisStreamService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisStreamService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _streamKey = "user-login-stream";
        private readonly string _consumerGroup = "login-processors";
        private readonly string _consumerName;
        // Increase semaphore limit to process more records concurrently
        private static readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(50, 50);
        // Add batch size for more efficient database operations
        private const int _batchSize = 100;
        // Increase message read count
        private const int _readCount = 100;
        // Add maximum pending time for messages
        private static readonly TimeSpan _pendingMessageTimeout = TimeSpan.FromMinutes(30);

        public RedisStreamService(
            IConnectionMultiplexer redis,
            ILogger<RedisStreamService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _redis = redis;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _consumerName = $"consumer-{Environment.MachineName}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        public async Task PublishUserLoginAsync(UserLoginRecord loginRecord)
        {
            try
            {
                var db = _redis.GetDatabase();
                var values = new NameValueEntry[]
                {
                    new NameValueEntry("userId", loginRecord.UserId),
                    new NameValueEntry("ipAddress", loginRecord.IpAddress),
                    new NameValueEntry("loginTimestamp", loginRecord.LoginTimestamp.ToString("o")),
                    new NameValueEntry("recordId", loginRecord.Id),
                    new NameValueEntry("ipNumericHigh", loginRecord.IpNumericHigh.ToString()),
                    new NameValueEntry("ipNumericLow", loginRecord.IpNumericLow.ToString())
                };

                var messageId = await db.StreamAddAsync(_streamKey, values);
                //_logger.LogInformation("Published login record to Redis Stream with ID {MessageId}", messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing login record to Redis Stream");
                throw;
            }
        }

        public async Task StartConsumingAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Redis Stream consumer: {ConsumerName}", _consumerName);
            
            try
            {
                var db = _redis.GetDatabase();
                
                // Create consumer group if it doesn't exist
                try
                {
                    await db.StreamCreateConsumerGroupAsync(_streamKey, _consumerGroup, StreamPosition.NewMessages);
                    _logger.LogInformation("Created new consumer group: {ConsumerGroup}", _consumerGroup);
                }
                catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
                {
                    _logger.LogInformation("Consumer group already exists: {ConsumerGroup}", _consumerGroup);
                    
                    // Add code to handle pending messages on startup
                    _logger.LogInformation("Checking for pending messages on startup...");
                    var pendingInfo = await db.StreamPendingAsync(_streamKey, _consumerGroup);
                    _logger.LogInformation("Found {Count} pending messages in consumer group", pendingInfo.PendingMessageCount);
                    
                    // If there are pending messages, initiate an immediate claim on startup
                    if (pendingInfo.PendingMessageCount > 0)
                    {
                        // Run a special task to handle all the pending messages at startup
                        _ = Task.Run(() => ReprocessAllPendingMessagesAsync(db, cancellationToken));
                    }
                }
                catch (RedisServerException ex) when (ex.Message.Contains("ERR no such key"))
                {
                    await db.StreamCreateConsumerGroupAsync(_streamKey, _consumerGroup, StreamPosition.Beginning);
                    _logger.LogInformation("Created stream and consumer group: {ConsumerGroup}", _consumerGroup);
                }

                // Start multiple processing tasks to increase throughput
                var processingTasks = new List<Task>();
                for (int i = 0; i < 5; i++)
                {
                    processingTasks.Add(Task.Run(() => ProcessStreamMessagesAsync(db, cancellationToken)));
                }

                // Also periodically check for pending messages that might have timed out
                processingTasks.Add(Task.Run(() => ProcessPendingMessagesAsync(db, cancellationToken)));

                // Wait for all processing tasks to complete
                await Task.WhenAll(processingTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in Redis Stream consumer");
                throw;
            }
        }

        private async Task ProcessStreamMessagesAsync(IDatabase db, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting stream processing task for consumer: {ConsumerName}", _consumerName);
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Read new messages from the stream
                    var streamEntries = await db.StreamReadGroupAsync(
                        _streamKey,
                        _consumerGroup,
                        _consumerName,
                        count: _readCount,
                        noAck: false);

                    if (streamEntries.Length > 0)
                    {
                        //_logger.LogInformation("Read {Count} messages from stream for consumer {ConsumerName}", 
                        //    streamEntries.Length, _consumerName);
                    }

                    if (streamEntries.Length == 0)
                    {
                        // No new messages, sleep and then check again
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    // Group messages into batches for bulk processing
                    var recordBatches = new List<List<UserLoginRecord>>();
                    var currentBatch = new List<UserLoginRecord>();
                    var messageIds = new List<RedisValue>();

                    foreach (var entry in streamEntries)
                    {
                        messageIds.Add(entry.Id);
                        var values = entry.Values;
                        
                        if (values.FirstOrDefault(v => v.Name == "userId").Value.HasValue)
                        {
                            try
                            {
                                var userLogin = new UserLoginRecord
                                {
                                    UserId = long.Parse((string)values.FirstOrDefault(v => v.Name == "userId").Value),
                                    IpAddress = (string)values.FirstOrDefault(v => v.Name == "ipAddress").Value,
                                    LoginTimestamp = DateTime.Parse((string)values.FirstOrDefault(v => v.Name == "loginTimestamp").Value).ToUniversalTime(),
                                    CreatedAt = DateTime.UtcNow
                                };

                                // Get numeric IP values if available in the stream
                                string ipNumericHighStr = (string)values.FirstOrDefault(v => v.Name == "ipNumericHigh").Value;
                                string ipNumericLowStr = (string)values.FirstOrDefault(v => v.Name == "ipNumericLow").Value;
                                
                                // Try to get values from dictionary
                                //values.TryGetValue("ipNumericHigh", out ipNumericHighStr);
                                //values.TryGetValue("ipNumericLow", out ipNumericLowStr);
                                
                                if (!string.IsNullOrEmpty(ipNumericHighStr) && !string.IsNullOrEmpty(ipNumericLowStr))
                                {
                                    // Use the saved numeric values from Redis
                                    userLogin.IpNumericHigh = long.Parse(ipNumericHighStr);
                                    userLogin.IpNumericLow = long.Parse(ipNumericLowStr);
                                }
                                else
                                {
                                    // Convert IP string to numeric for legacy records without numeric values
                                    IpAddressConverter.TryConvertIpToNumbers(
                                        userLogin.IpAddress, 
                                        out long ipNumericHigh, 
                                        out long ipNumericLow);
                                        
                                    userLogin.IpNumericHigh = ipNumericHigh;
                                    userLogin.IpNumericLow = ipNumericLow;
                                }

                                currentBatch.Add(userLogin);

                                // If we've reached the batch size, start a new batch
                                if (currentBatch.Count >= _batchSize / 2)
                                {
                                    recordBatches.Add(currentBatch);
                                    currentBatch = new List<UserLoginRecord>();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error parsing message from Redis Stream: {MessageId}", entry.Id);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Received malformed message from Redis Stream: {MessageId}", entry.Id);
                        }
                    }

                    // Add the last batch if it has any records
                    if (currentBatch.Count > 0)
                    {
                        recordBatches.Add(currentBatch);
                    }

                    //_logger.LogInformation("Processing {BatchCount} batches with total {RecordCount} records", 
                    //    recordBatches.Count, 
                    //    recordBatches.Sum(b => b.Count));

                    // Process all batches
                    if (recordBatches.Count > 0)
                    {
                        await ProcessBatchesAsync(db, recordBatches, messageIds);
                    }
                    else
                    {
                        // No valid records to process, just acknowledge the messages
                        foreach (var id in messageIds)
                        {
                            await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, id);
                        }
                        _logger.LogWarning("Acknowledged {Count} messages with no valid records", messageIds.Count);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Application shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing messages from Redis Stream");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }

        private async Task ProcessBatchesAsync(IDatabase db, List<List<UserLoginRecord>> recordBatches, List<RedisValue> messageIds)
        {
            // Get a semaphore to limit concurrent database operations
            bool semaphoreAcquired = await _dbSemaphore.WaitAsync(TimeSpan.FromSeconds(10));
            
            try
            {
                if (!semaphoreAcquired)
                {
                    _logger.LogWarning("Database semaphore timeout for batch processing, will retry later");
                    return; // Don't acknowledge, will be reprocessed
                }

                //_logger.LogInformation("Acquired semaphore for batch processing, saving {Count} batches", recordBatches.Count);

                // Use a new scope to get a fresh DbContext
                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    
                    // Process each batch
                    foreach (var batch in recordBatches)
                    {
                        // Add all records to the context
                        foreach (var record in batch)
                        {

                            var r = 
                                
                                await dbContext
                                .UserLoginRecords
                                .AsNoTracking()
                                .FirstOrDefaultAsync(
                                    x => x.UserId == record.UserId && x.IpAddress == record.IpAddress
                                );

                            if ( r == null)
                                dbContext.UserLoginRecords.Add(record);
                            else
                                dbContext.UserLoginRecords.Update(record);                           
                        }

                        // Save the batch to the database
                        var savedCount = await dbContext.SaveChangesAsync();
                      
                        //_logger.LogInformation("Successfully saved {Count} login records to database", savedCount);
                    }

                    // Acknowledge all messages after successfully saving them
                    foreach (var id in messageIds)
                    {
                        await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, id);
                    }
                    //_logger.LogInformation("Acknowledged {Count} messages after successful processing", messageIds.Count);
                }
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _dbSemaphore.Release();
                }
            }
        }

        private async Task ProcessPendingMessagesAsync(IDatabase db, CancellationToken cancellationToken)
        {
            // Check for pending messages every 1 minute
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    
                    // Get pending messages that have been idle for too long
                    var pendingMessages = await db.StreamPendingMessagesAsync(
                        _streamKey, 
                        _consumerGroup, 
                        count: 100, 
                        consumerName: "*");   // Use "*" wildcard instead of null to get messages from all consumers

                    if (pendingMessages.Length == 0)
                    {
                        continue;
                    }

                    var idsToProcess = new List<RedisValue>();
                    
                    foreach (var message in pendingMessages)
                    {
                        // Only reclaim messages that have been pending for longer than the timeout
                        if (message.IdleTimeInMilliseconds > _pendingMessageTimeout.TotalMilliseconds)
                        {
                            idsToProcess.Add(message.MessageId);
                        }
                    }

                    if (idsToProcess.Count > 0)
                    {
                        // Claim the timed-out messages for this consumer
                        var claimedMessages = await db.StreamClaimAsync(
                            _streamKey,
                            _consumerGroup,
                            _consumerName, 
                            (long)_pendingMessageTimeout.TotalMilliseconds,
                            idsToProcess.ToArray());

                        _logger.LogInformation("Claimed {Count} timed-out pending messages for reprocessing", claimedMessages.Length);
                        
                        // Process the claimed messages
                        if (claimedMessages.Length > 0)
                        {
                            var recordBatches = new List<List<UserLoginRecord>>();
                            var currentBatch = new List<UserLoginRecord>();
                            var messageIds = new List<RedisValue>();

                            foreach (var message in claimedMessages)
                            {
                                try
                                {
                                    var values = message.Values.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
                                    
                                    if (!values.TryGetValue("userId", out var userIdStr) || 
                                        !values.TryGetValue("ipAddress", out var ipAddress) ||
                                        !values.TryGetValue("loginTimestamp", out var loginTimestampStr))
                                    {
                                        _logger.LogWarning("Invalid message format in claimed message: {MessageId}", message.Id);
                                        await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, message.Id);
                                        continue;
                                    }

                                    if (!long.TryParse(userIdStr, out var userId) || 
                                        !DateTime.TryParse(loginTimestampStr, out var loginTimestamp))
                                    {
                                        _logger.LogWarning("Invalid data types in claimed message: {MessageId}", message.Id);
                                        await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, message.Id);
                                        continue;
                                    }

                                    var loginRecord = new UserLoginRecord
                                    {
                                        UserId = userId,
                                        IpAddress = ipAddress,
                                        LoginTimestamp = loginTimestamp.ToUniversalTime()
                                    };

                                    // Get numeric IP values if available in the stream
                                    string? ipNumericHighStr = null;
                                    string? ipNumericLowStr = null;
                                    
                                    // Try to get values from dictionary
                                    var ipNumericHighEntry = values.FirstOrDefault(v => v.Key == "ipNumericHigh");
                                    var ipNumericLowEntry = values.FirstOrDefault(v => v.Key == "ipNumericLow");
                                    
                                    if (ipNumericHighEntry.Value != null && ipNumericLowEntry.Value != null)
                                    {
                                        // Use the saved numeric values from Redis
                                        loginRecord.IpNumericHigh = long.Parse(ipNumericHighEntry.Value);
                                        loginRecord.IpNumericLow = long.Parse(ipNumericLowEntry.Value);
                                    }
                                    else
                                    {
                                        // Convert IP string to numeric for legacy records without numeric values
                                        IpAddressConverter.TryConvertIpToNumbers(
                                            loginRecord.IpAddress, 
                                            out long ipNumericHigh, 
                                            out long ipNumericLow);
                                            
                                        loginRecord.IpNumericHigh = ipNumericHigh;
                                        loginRecord.IpNumericLow = ipNumericLow;
                                    }

                                    currentBatch.Add(loginRecord);
                                    messageIds.Add(message.Id);

                                    // When batch is full, add it to the batch list and start a new one
                                    if (currentBatch.Count >= _batchSize / 2)
                                    {
                                        recordBatches.Add(currentBatch);
                                        currentBatch = new List<UserLoginRecord>();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error processing claimed message {MessageId}", message.Id);
                                }
                            }

                            // Add the last batch if it has any records
                            if (currentBatch.Count > 0)
                            {
                                recordBatches.Add(currentBatch);
                            }

                            // Process all batches of claimed messages
                            if (recordBatches.Count > 0)
                            {
                                await ProcessBatchesAsync(db, recordBatches, messageIds);
                            }
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // Application shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing pending messages from Redis Stream");
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
        }

        private async Task ReprocessAllPendingMessagesAsync(IDatabase db, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting to reprocess all pending messages...");
                
                // Get all pending messages information
                var pendingInfo = await db.StreamPendingAsync(_streamKey, _consumerGroup);
                if (pendingInfo.PendingMessageCount == 0)
                {
                    _logger.LogInformation("No pending messages to process");
                    return;
                }
                
                _logger.LogInformation("Found {Count} pending messages in consumer group", pendingInfo.PendingMessageCount);
                
                // Get pending messages for all consumers in smaller batches
                long batchSize = 100;
                long processedTotal = 0;
                
                // Process each consumer's pending messages
                foreach (var consumer in pendingInfo.Consumers)
                {
                    string consumerName = consumer.Name;
                    long pendingCount = consumer.PendingMessageCount;
                    
                    //_logger.LogInformation("Processing {Count} pending messages for consumer {Consumer}", 
                    //    pendingCount, consumerName);
                    
                    // Process in batches
                    long processed = 0;
                    while (processed < pendingCount && !cancellationToken.IsCancellationRequested)
                    {
                        // Get a batch of pending message details
                        var pendingMessages = await db.StreamPendingMessagesAsync(
                            _streamKey, 
                            _consumerGroup,
                            count: (int)Math.Min(batchSize, pendingCount - processed),
                            consumerName: consumerName);
                        
                        if (pendingMessages.Length == 0)
                        {
                            _logger.LogInformation("No more pending messages found for consumer {Consumer}", consumerName);
                            break;
                        }
                        
                        // Get the message IDs to claim
                        var messageIds = pendingMessages.Select(p => p.MessageId).ToArray();
                        
                        // Claim these messages for our consumer
                        var claimedEntries = await db.StreamClaimAsync(
                            _streamKey,
                            _consumerGroup,
                            _consumerName,
                            0, // min idle time - claim all messages
                            messageIds);
                        
                        if (claimedEntries.Length == 0)
                        {
                            _logger.LogWarning("Failed to claim pending messages for consumer {Consumer}", consumerName);
                            processed += messageIds.Length; // Skip these and continue
                            continue;
                        }
                        
                        //_logger.LogInformation("Successfully claimed {Count} messages from consumer {Consumer}", 
                        //    claimedEntries.Length, consumerName);
                        
                        // Process the claimed messages
                        var recordBatches = new List<List<UserLoginRecord>>();
                        var currentBatch = new List<UserLoginRecord>();
                        var processedIds = new List<RedisValue>();
                        
                        foreach (var entry in claimedEntries)
                        {
                            processedIds.Add(entry.Id);
                            var values = entry.Values;
                            
                            try
                            {
                                if (values.FirstOrDefault(v => v.Name == "userId").Value.HasValue)
                                {
                                    var userLogin = new UserLoginRecord
                                    {
                                        UserId = long.Parse((string)values.FirstOrDefault(v => v.Name == "userId").Value),
                                        IpAddress = (string)values.FirstOrDefault(v => v.Name == "ipAddress").Value,
                                        LoginTimestamp = DateTime.Parse((string)values.FirstOrDefault(v => v.Name == "loginTimestamp").Value).ToUniversalTime(),
                                        CreatedAt = DateTime.UtcNow
                                    };

                                    // Get numeric IP values if available in the stream
                                    var ipNumericHighValue = values.FirstOrDefault(v => v.Name == "ipNumericHigh");
                                    var ipNumericLowValue = values.FirstOrDefault(v => v.Name == "ipNumericLow");
                                    
                                    if (ipNumericHighValue.Value.HasValue && ipNumericLowValue.Value.HasValue)
                                    {
                                        // Use the saved numeric values from Redis
                                        userLogin.IpNumericHigh = long.Parse((string)ipNumericHighValue.Value);
                                        userLogin.IpNumericLow = long.Parse((string)ipNumericLowValue.Value);
                                    }
                                    else
                                    {
                                        // Convert IP string to numeric for legacy records without numeric values
                                        IpAddressConverter.TryConvertIpToNumbers(
                                            userLogin.IpAddress, 
                                            out long ipNumericHigh, 
                                            out long ipNumericLow);
                                            
                                        userLogin.IpNumericHigh = ipNumericHigh;
                                        userLogin.IpNumericLow = ipNumericLow;
                                    }

                                    currentBatch.Add(userLogin);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error parsing pending message: {MessageId}", entry.Id);
                            }
                        }
                        
                        if (currentBatch.Count > 0)
                        {
                            recordBatches.Add(currentBatch);
                        }

                        // Process all batches
                        if (recordBatches.Count > 0)
                        {
                            //_logger.LogInformation("Processing {Count} record batches with total {RecordCount} records", 
                            //    recordBatches.Count, recordBatches.Sum(b => b.Count));
                                
                            await ProcessBatchesAsync(db, recordBatches, processedIds);
                            processed += processedIds.Count;
                            processedTotal += processedIds.Count;
                            
                            //_logger.LogInformation("Processed {Count} pending messages from consumer {Consumer} ({Total} total)", 
                            //    processedIds.Count, consumerName, processedTotal);
                        }
                        else if (processedIds.Count > 0)
                        {
                            // No valid records to process, just acknowledge the messages
                            foreach (var id in processedIds)
                            {
                                await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, id);
                            }
                            
                            _logger.LogWarning("Acknowledged {Count} invalid pending messages from consumer {Consumer}", 
                                processedIds.Count, consumerName);
                                
                            processed += processedIds.Count;
                            processedTotal += processedIds.Count;
                        }
                        
                        await Task.Delay(100, cancellationToken); // Avoid overwhelming Redis
                    }
                }
                
                _logger.LogInformation("Completed reprocessing pending messages, processed {Count} total messages", processedTotal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reprocessing pending messages");
            }
        }
    }
}
