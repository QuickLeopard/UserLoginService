using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Collections.Concurrent;
using CommandLine;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using UserLoginService.Protos;

namespace UserLoginClient
{
    class Program
    {
        // Command line options classes
        [Verb("interactive", HelpText = "Start in interactive mode")]
        public class InteractiveOptions
        {
            [Option('s', "server", Required = false, HelpText = "gRPC server URL", Default = null)]
            public string? ServerUrl { get; set; }
        }

        [Verb("login", HelpText = "Record a user login")]
        public class LoginOptions
        {
            [Option('s', "server", Required = false, HelpText = "gRPC server URL", Default = null)]
            public string? ServerUrl { get; set; }

            [Option('u', "user", Required = true, HelpText = "User ID")]
            public long UserId { get; set; }

            [Option('i', "ip", Required = true, HelpText = "IP Address")]
            public string IpAddress { get; set; } = string.Empty;

            [Option('r', "repeat", Required = false, HelpText = "Number of times to repeat the login", Default = 1)]
            public int RepeatCount { get; set; }

            [Option('d', "delay", Required = false, HelpText = "Delay in milliseconds between repeats", Default = 0)]
            public int DelayMs { get; set; }
        }

        [Verb("get-ips", HelpText = "Get all IP addresses for a user")]
        public class GetIpsOptions
        {
            [Option('s', "server", Required = false, HelpText = "gRPC server URL", Default = null)]
            public string? ServerUrl { get; set; }

            [Option('u', "user", Required = true, HelpText = "User ID")]
            public long UserId { get; set; }
        }

        [Verb("get-users", HelpText = "Get all users by IP address")]
        public class GetUsersOptions
        {
            [Option('s', "server", Required = false, HelpText = "gRPC server URL", Default = null)]
            public string? ServerUrl { get; set; }

            [Option('i', "ip", Required = true, HelpText = "IP Address")]
            public string IpAddress { get; set; } = string.Empty;
        }

        [Verb("get-last-login", HelpText = "Get last login for a user")]
        public class GetLastLoginOptions
        {
            [Option('u', "user-id", Required = true, HelpText = "User ID to retrieve last login for")]
            public long UserId { get; set; }

            [Option('a', "address", Required = false, Default = "http://envoy:8080", HelpText = "gRPC service address")]
            public string ServiceAddress { get; set; } = "http://envoy:8080";
        }

        [Verb("load-test", HelpText = "Run a load test")]
        public class LoadTestOptions
        {
            [Option('s', "server", Required = false, HelpText = "gRPC server URL", Default = null)]
            public string? ServerUrl { get; set; }

            [Option('u', "users", Required = false, HelpText = "Number of unique users", Default = 100)]
            public int UserCount { get; set; }

            [Option('i', "ips", Required = false, HelpText = "Number of unique IPs", Default = 10)]
            public int IpCount { get; set; }

            [Option('c', "count", Required = false, HelpText = "Number of login requests to send", Default = 1000)]
            public int RequestCount { get; set; }

            [Option('p', "parallel", Required = false, HelpText = "Number of parallel tasks", Default = 10)]
            public int ParallelTasks { get; set; }

            [Option('d', "delay", Required = false, HelpText = "Delay in milliseconds between requests", Default = 0)]
            public int DelayMs { get; set; }
            
            [Option('l', "log", Required = false, Default = false, HelpText = "Log detailed metrics to a file")]
            public bool LogToFile { get; set; }
            
            [Option("log-dir", Required = false, Default = "./logs", HelpText = "Directory for log files")]
            public string? LogDirectory { get; set; } = "./logs";
        }

        static async Task<int> Main(string[] args)
        {
            return await Parser.Default.ParseArguments<
                InteractiveOptions, 
                LoginOptions, 
                GetIpsOptions, 
                GetUsersOptions,
                GetLastLoginOptions,
                LoadTestOptions>(args)
                .MapResult(
                    (InteractiveOptions opts) => RunInteractiveMode(opts),
                    (LoginOptions opts) => RecordUserLogin(opts),
                    (GetIpsOptions opts) => GetAllUserIPs(opts),
                    (GetUsersOptions opts) => GetUsersByIP(opts),
                    (GetLastLoginOptions opts) => GetUserLastLogin(opts),
                    (LoadTestOptions opts) => RunLoadTest(opts),
                    errs => Task.FromResult(1)
                );
        }

        private static string GetServerUrl(string? serverOption)
        {
            // First check the command-line option
            if (!string.IsNullOrEmpty(serverOption))
            {
                return serverOption;
            }
            
            // Then check the environment variable
            var envUrl = Environment.GetEnvironmentVariable("GRPC_SERVICE_URL");
            if (!string.IsNullOrEmpty(envUrl))
            {
                return envUrl;
            }
            
            // Finally, use the default
            return "http://localhost:5001";
        }

        private static GrpcChannel CreateChannel(string? serverUrl)
        {
            var url = GetServerUrl(serverUrl);
            Console.WriteLine($"Connecting to gRPC service at: {url}");
            
            // Configure gRPC client
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            
            // Configure channel options for better diagnostics
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning);
            });
            
            var channelOptions = new GrpcChannelOptions
            {
                LoggerFactory = loggerFactory
            };
            
            return GrpcChannel.ForAddress(url, channelOptions);
        }

        static async Task<int> RunInteractiveMode(InteractiveOptions opts)
        {
            using var channel = CreateChannel(opts.ServerUrl);
            var client = new UserLoginService.Protos.UserLoginService.UserLoginServiceClient(channel);

            Console.WriteLine("UserLogin gRPC Client - Interactive Mode");
            Console.WriteLine("========================================");
            Console.WriteLine();

            while (true)
            {
                DisplayMainMenu();
                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await RecordUserLoginInteractive(client);
                        break;
                    case "2":
                        await GetAllUserIPsInteractive(client);
                        break;
                    case "3":
                        await GetUsersByIPInteractive(client);
                        break;
                    case "4":
                        await GetUserLastLoginInteractive(client);
                        break;
                    case "5":
                        await RunLoadTestInteractive(client);
                        break;
                    case "6":
                        return 0;
                    default:
                        Console.WriteLine("Invalid option, please try again.");
                        break;
                }

                Console.WriteLine("\nPress Enter to continue...");
                Console.ReadLine();
                Console.Clear();
            }
        }

        static async Task<int> RecordUserLogin(LoginOptions opts)
        {
            using var channel = CreateChannel(opts.ServerUrl);
            var client = new UserLoginService.Protos.UserLoginService.UserLoginServiceClient(channel);

            try
            {
                Console.WriteLine($"Recording {opts.RepeatCount} login(s) for user {opts.UserId} from IP {opts.IpAddress}");
                
                if (opts.UserId <= 0)
                {
                    Console.WriteLine("Error: User ID must be a positive number");
                    return 1;
                }

                if (!IsValidIpAddress(opts.IpAddress))
                {
                    Console.WriteLine($"Error: Invalid IP address format: {opts.IpAddress}");
                    Console.WriteLine("Please provide a valid IP address (e.g., 192.168.1.1 or 2001:0db8:85a3:0000:0000:8a2e:0370:7334)");
                    return 1;
                }
                
                for (int i = 0; i < opts.RepeatCount; i++)
                {
                    var request = new UserLoginRequest
                    {
                        UserId = opts.UserId,
                        IpAddress = opts.IpAddress,
                        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                    };

                    var response = await client.UserLoginConnectAsync(request);
                    Console.WriteLine($"Login {i+1}/{opts.RepeatCount}: {(response.Success ? "Success" : "Failed")} - {response.Message}");
                    
                    if (opts.DelayMs > 0 && i < opts.RepeatCount - 1)
                    {
                        await Task.Delay(opts.DelayMs);
                    }
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static async Task<int> GetAllUserIPs(GetIpsOptions opts)
        {
            using var channel = CreateChannel(opts.ServerUrl);
            var client = new UserLoginService.Protos.UserLoginService.UserLoginServiceClient(channel);

            try
            {
                var request = new UserIdRequest
                {
                    UserId = opts.UserId
                };

                var response = await client.GetAllUserIPsAsync(request);
                
                Console.WriteLine($"Found {response.IpAddresses.Count} IP addresses for user {opts.UserId}:");
                foreach (var ip in response.IpAddresses)
                {
                    Console.WriteLine($"- {ip.IpAddress} (Last login: {ip.LastLogin.ToDateTime():yyyy-MM-dd HH:mm:ss UTC})");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static async Task<int> GetUsersByIP(GetUsersOptions opts)
        {
            using var channel = CreateChannel(opts.ServerUrl);
            var client = new UserLoginService.Protos.UserLoginService.UserLoginServiceClient(channel);

            try
            {
                if (!IsValidIpPattern(opts.IpAddress))
                {
                    Console.WriteLine($"Error: Invalid IP address pattern: {opts.IpAddress}");
                    Console.WriteLine("Please provide a valid IP address or pattern (e.g., 192.168.1.1, 2001:0db8:85a3:0000:0000:8a2e:0370:7334, 192.168, or 2001:0db8:85a3)");
                    return 1;
                }

                var request = new IPAddressRequest
                {
                    IpAddress = opts.IpAddress
                };

                var response = await client.GetUsersByIPAsync(request);
                
                Console.WriteLine($"Found {response.Users.Count} users for IP pattern {opts.IpAddress}:");
                foreach (var user in response.Users)
                {
                    Console.WriteLine($"- User ID: {user.UserId} IP: {user.IpAddress} (Last login: {user.LastLogin.ToDateTime():yyyy-MM-dd HH:mm:ss UTC})");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        static async Task<int> RunLoadTest(LoadTestOptions opts)
        {
            using var channel = CreateChannel(opts.ServerUrl);
            var client = new UserLoginService.Protos.UserLoginService.UserLoginServiceClient(channel);

            try
            {
                Console.WriteLine($"Starting load test with {opts.RequestCount} requests using {opts.ParallelTasks} parallel tasks...");
                Console.WriteLine($"Using {opts.UserCount} unique users and {opts.IpCount} unique IPs");
                
                var startTime = DateTime.Now;
                var random = new Random();
                
                int successCount = 0;
                int failureCount = 0;
                
                // Create a semaphore to limit parallelism
                using var semaphore = new SemaphoreSlim(opts.ParallelTasks);
                var tasks = new List<Task>();
                object consoleLock = new object();
                
                // Dictionary to track error types and their frequency
                var errorStats = new Dictionary<string, int>();
                var errorStatsLock = new object();
                
                // Track response time performance
                var responseTimes = new ConcurrentBag<double>();
                var errorDetails = new ConcurrentBag<string>();
                
                // For detailed logging
                var detailedErrorLogs = new ConcurrentBag<(long UserId, string IpAddress, DateTime Timestamp, string ErrorType, string ErrorMessage, double ElapsedMs)>();
                var detailedSuccessLogs = new ConcurrentBag<(long UserId, string IpAddress, DateTime Timestamp, double ElapsedMs)>();
                
                // Track database performance
                int dbTimeoutsCount = 0;
                int networkErrorsCount = 0;
                int serviceErrorsCount = 0;
                int deadlineExceededCount = 0;
                
                // Track response time buckets for histogram
                var responseTimeBuckets = new ConcurrentDictionary<string, int>();
                string[] buckets = { "0-10ms", "10-50ms", "50-100ms", "100-250ms", "250-500ms", "500-1000ms", "1s+" };
                foreach (var bucket in buckets)
                {
                    responseTimeBuckets[bucket] = 0;
                }
                
                for (int i = 0; i < opts.RequestCount; i++)
                {
                    // Wait until we can enter the semaphore
                    await semaphore.WaitAsync();
                    
                    // Generate random user and IP
                    long userId = random.Next(1, opts.UserCount + 1);
                    string ip = random.Next(2) == 0 ? 
                        //$"192.168.1.{random.Next(1, opts.IpCount + 1)}" : 
                        $"{new IPAddress (random.Next(1, opts.IpCount + 1))}" :
                        $"2001:0db8:85a3:0000:0000:8a2e:0370:{random.Next(1, opts.IpCount + 1):x4}";
                    
                    // Start a new task
                    tasks.Add(Task.Run(async () =>
                    {
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        string errorCategory = string.Empty;
                        int retryCount = 0;
                        const int maxRetries = 3;
                        
                        while (retryCount <= maxRetries)
                        {
                            try
                            {
                                var request = new UserLoginRequest
                                {
                                    UserId = userId,
                                    IpAddress = ip,
                                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                                };

                                // Add deadline for the request
                                var options = new CallOptions(
                                    deadline: DateTime.UtcNow.AddSeconds(120),
                                    headers: new Metadata
                                    {
                                        { "x-request-attempt", retryCount.ToString() }
                                    });
                                
                                var response = await client.UserLoginConnectAsync(request, options);
                                
                                // Record response time
                                stopwatch.Stop();
                                double responseTimeMs = stopwatch.ElapsedMilliseconds;
                                responseTimes.Add(responseTimeMs);
                                
                                // Categorize response time
                                string responseBucket = CategorizeResponseTime(responseTimeMs);
                                responseTimeBuckets.AddOrUpdate(responseBucket, 1, (key, count) => count + 1);
                                
                                if (response.Success)
                                {
                                    Interlocked.Increment(ref successCount);
                                    detailedSuccessLogs.Add((userId, ip, DateTime.UtcNow, responseTimeMs));
                                    break; // Exit retry loop on success
                                }
                                else
                                {
                                    Interlocked.Increment(ref failureCount);
                                    errorCategory = "ApplicationError";
                                    Interlocked.Increment(ref serviceErrorsCount);
                                    
                                    // Log the application-level failure
                                    string errorMsg = $"Request failed (app-level): {response.Message}";
                                    errorDetails.Add(errorMsg);
                                    break; // Application error doesn't need retry
                                }
                            }
                            catch (RpcException ex)
                            {
                                // Only retry on certain status codes that might be transient
                                if (retryCount < maxRetries && 
                                    (ex.StatusCode == StatusCode.Unavailable || 
                                     ex.StatusCode == StatusCode.ResourceExhausted ||
                                     ex.StatusCode == StatusCode.DeadlineExceeded))
                                {
                                    retryCount++;
                                    // Exponential backoff
                                    int backoffMs = (int)Math.Min(100 * Math.Pow(2, retryCount), 1000);
                                    await Task.Delay(backoffMs);
                                    continue;
                                }
                                
                                // If we've exhausted retries or it's not a retryable error
                                stopwatch.Stop();
                                double responseTimeMs = stopwatch.ElapsedMilliseconds;
                                
                                Interlocked.Increment(ref failureCount);
                                errorCategory = ex.GetType().Name;
                                if (errorCategory.Contains("RpcException"))
                                {
                                    var rpcEx = ex as RpcException;
                                    if (rpcEx != null)
                                    {
                                        if (rpcEx.StatusCode == StatusCode.DeadlineExceeded)
                                        {
                                            errorCategory = "Timeout";
                                            Interlocked.Increment(ref deadlineExceededCount);
                                        }
                                        else if (rpcEx.StatusCode == StatusCode.Unavailable)
                                        {
                                            errorCategory = "ServiceUnavailable";
                                            Interlocked.Increment(ref networkErrorsCount);
                                        }
                                        else
                                        {
                                            errorCategory = $"RPC_{rpcEx.StatusCode}";
                                            Interlocked.Increment(ref serviceErrorsCount);
                                        }
                                    }
                                }
                                else if (errorCategory.Contains("Timeout") || errorCategory.Contains("TimeoutException"))
                                {
                                    errorCategory = "DbTimeout";
                                    Interlocked.Increment(ref dbTimeoutsCount);
                                }
                                else if (errorCategory.Contains("Socket") || errorCategory.Contains("Connection"))
                                {
                                    errorCategory = "NetworkError";
                                    Interlocked.Increment(ref networkErrorsCount);
                                }
                                else
                                {
                                    errorCategory = "OtherError";
                                    Interlocked.Increment(ref serviceErrorsCount);
                                }
                                
                                // Include inner exception details if available
                                if (ex.InnerException != null)
                                {
                                    errorDetails.Add($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                                }
                                
                                // Track error type statistics
                                lock (errorStatsLock)
                                {
                                    if (errorStats.ContainsKey(errorCategory))
                                        errorStats[errorCategory]++;
                                    else
                                        errorStats[errorCategory] = 1;
                                }
                                
                                // Log to console with thread-safe lock to prevent mixed output
                                lock (consoleLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"Request failed: {ex.Message}");
                                    Console.ResetColor();
                                }
                                detailedErrorLogs.Add((userId, ip, DateTime.UtcNow, errorCategory, ex.Message, stopwatch.ElapsedMilliseconds));
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }
                    }));
                    
                    // Simple progress reporting
                    if (i % 100 == 0 && i > 0)
                    {
                        Console.WriteLine($"Started {i} requests...");
                    }
                }
                
                // Wait for all tasks to complete
                await Task.WhenAll(tasks);
                
                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                
                Console.WriteLine("\nLoad test results:");
                Console.WriteLine($"Total requests: {opts.RequestCount}");
                Console.WriteLine($"Successful: {successCount}");
                Console.WriteLine($"Failed: {failureCount}");
                Console.WriteLine($"Duration: {duration.TotalSeconds:F2} seconds");
                Console.WriteLine($"Requests per second: {opts.RequestCount / duration.TotalSeconds:F2}");
                
                // Print performance statistics
                if (responseTimes.Count > 0)
                {
                    var allTimes = responseTimes.ToArray();
                    Array.Sort(allTimes);
                    
                    double avgResponseTime = allTimes.Average();
                    double p50 = allTimes[(int)(allTimes.Length * 0.5)];
                    double p90 = allTimes[(int)(allTimes.Length * 0.9)];
                    double p99 = allTimes[(int)(allTimes.Length * 0.99)];
                    
                    Console.WriteLine("\nResponse time statistics:");
                    Console.WriteLine($"Average: {avgResponseTime:F2} ms");
                    Console.WriteLine($"Median (P50): {p50:F2} ms");
                    Console.WriteLine($"P90: {p90:F2} ms");
                    Console.WriteLine($"P99: {p99:F2} ms");
                    Console.WriteLine($"Min: {allTimes.Min():F2} ms");
                    Console.WriteLine($"Max: {allTimes.Max():F2} ms");
                    
                    Console.WriteLine("\nResponse time distribution:");
                    foreach (var bucket in buckets)
                    {
                        int count = responseTimeBuckets[bucket];
                        double percentage = (double)count / opts.RequestCount * 100;
                        string bar = new string('#', (int)(percentage / 2));
                        Console.WriteLine($"{bucket,-10}: {count,5} ({percentage,5:F1}%) {bar}");
                    }
                }
                
                // Print error statistics if there were any failures
                if (failureCount > 0)
                {
                    Console.WriteLine("\nError statistics by category:");
                    if (networkErrorsCount > 0)
                        Console.WriteLine($"- Network errors: {networkErrorsCount} ({(double)networkErrorsCount / failureCount:P1} of failures)");
                    if (dbTimeoutsCount > 0)
                        Console.WriteLine($"- Database timeouts: {dbTimeoutsCount} ({(double)dbTimeoutsCount / failureCount:P1} of failures)");
                    if (deadlineExceededCount > 0)
                        Console.WriteLine($"- Deadline exceeded: {deadlineExceededCount} ({(double)deadlineExceededCount / failureCount:P1} of failures)");
                    if (serviceErrorsCount > 0)
                        Console.WriteLine($"- Service errors: {serviceErrorsCount} ({(double)serviceErrorsCount / failureCount:P1} of failures)");
                    
                    if (errorStats.Count > 0)
                    {
                        Console.WriteLine("\nDetailed error statistics:");
                        foreach (var error in errorStats.OrderByDescending(e => e.Value))
                        {
                            Console.WriteLine($"- {error.Key}: {error.Value} occurrences ({(double)error.Value / failureCount:P1} of failures)");
                        }
                    }
                }
                
                if (opts.LogToFile)
                {
                    var logDir = opts.LogDirectory;
                    if (!Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
                    var logFile = Path.Combine(logDir, $"load-test-{timestamp}.log");
                    var errorsCsvFile = Path.Combine(logDir, $"errors-{timestamp}.csv");
                    var successCsvFile = Path.Combine(logDir, $"success-{timestamp}.csv");
                    
                    // Save summary log file
                    using (var writer = new StreamWriter(logFile))
                    {
                        writer.WriteLine($"Load Test Results - {DateTime.Now}");
                        writer.WriteLine($"==============================================");
                        writer.WriteLine($"Configuration:");
                        writer.WriteLine($"- Total requests: {opts.RequestCount}");
                        writer.WriteLine($"- Parallel tasks: {opts.ParallelTasks}");
                        writer.WriteLine($"- Unique users: {opts.UserCount}");
                        writer.WriteLine($"- Unique IPs: {opts.IpCount}");
                        writer.WriteLine($"- Server URL: {GetServerUrl(opts.ServerUrl)}");
                        writer.WriteLine($"- Request delay: {opts.DelayMs}ms");
                        writer.WriteLine();
                        
                        writer.WriteLine("Summary:");
                        writer.WriteLine($"- Successful: {successCount}");
                        writer.WriteLine($"- Failed: {failureCount}");
                        writer.WriteLine($"- Success rate: {(double)successCount / opts.RequestCount:P1}");
                        writer.WriteLine($"- Duration: {duration.TotalSeconds:F2} seconds");
                        writer.WriteLine($"- Requests per second: {opts.RequestCount / duration.TotalSeconds:F2}");
                        
                        // Print performance statistics
                        if (responseTimes.Count > 0)
                        {
                            var allTimes = responseTimes.ToArray();
                            Array.Sort(allTimes);
                            
                            double avgResponseTime = allTimes.Average();
                            double p50 = allTimes[(int)(allTimes.Length * 0.5)];
                            double p90 = allTimes[(int)(allTimes.Length * 0.9)];
                            double p99 = allTimes[(int)(allTimes.Length * 0.99)];
                            
                            writer.WriteLine("\nResponse time statistics:");
                            writer.WriteLine($"- Average: {avgResponseTime:F2} ms");
                            writer.WriteLine($"- Median (P50): {p50:F2} ms");
                            writer.WriteLine($"- P90: {p90:F2} ms");
                            writer.WriteLine($"- P99: {p99:F2} ms");
                            writer.WriteLine($"- Min: {allTimes.Min():F2} ms");
                            writer.WriteLine($"- Max: {allTimes.Max():F2} ms");
                            
                            writer.WriteLine("\nResponse time distribution:");
                            foreach (var bucket in buckets)
                            {
                                int count = responseTimeBuckets[bucket];
                                double percentage = (double)count / opts.RequestCount * 100;
                                string bar = new string('#', (int)(percentage / 2));
                                writer.WriteLine($"{bucket,-10}: {count,5} ({percentage,5:F1}%) {bar}");
                            }
                        }
                        
                        // Print error statistics if there were any failures
                        if (failureCount > 0)
                        {
                            writer.WriteLine("\nError statistics by category:");
                            if (networkErrorsCount > 0)
                                writer.WriteLine($"- Network errors: {networkErrorsCount} ({(double)networkErrorsCount / failureCount:P1} of failures)");
                            if (dbTimeoutsCount > 0)
                                writer.WriteLine($"- Database timeouts: {dbTimeoutsCount} ({(double)dbTimeoutsCount / failureCount:P1} of failures)");
                            if (deadlineExceededCount > 0)
                                writer.WriteLine($"- Deadline exceeded: {deadlineExceededCount} ({(double)deadlineExceededCount / failureCount:P1} of failures)");
                            if (serviceErrorsCount > 0)
                                writer.WriteLine($"- Service errors: {serviceErrorsCount} ({(double)serviceErrorsCount / failureCount:P1} of failures)");
                            
                            if (errorStats.Count > 0)
                            {
                                writer.WriteLine("\nDetailed error statistics:");
                                foreach (var error in errorStats.OrderByDescending(e => e.Value))
                                {
                                    writer.WriteLine($"- {error.Key}: {error.Value} occurrences ({(double)error.Value / failureCount:P1} of failures)");
                                }
                            }
                        }
                    }
                    
                    // Export detailed errors to CSV
                    if (detailedErrorLogs.Count > 0)
                    {
                        using var errorWriter = new StreamWriter(errorsCsvFile);
                        errorWriter.WriteLine("UserId,IpAddress,Timestamp,ErrorType,ErrorMessage,ResponseTimeMs");
                        
                        foreach (var error in detailedErrorLogs)
                        {
                            // Escape any commas in the error message to maintain CSV format
                            string safeErrorMessage = error.ErrorMessage.Replace("\"", "\"\"");
                            if (safeErrorMessage.Contains(","))
                            {
                                safeErrorMessage = $"\"{safeErrorMessage}\"";
                            }
                            
                            errorWriter.WriteLine($"{error.UserId},{error.IpAddress},{error.Timestamp:o},{error.ErrorType},{safeErrorMessage},{error.ElapsedMs:F2}");
                        }
                    }
                    
                    // Export successful requests to CSV
                    if (detailedSuccessLogs.Count > 0)
                    {
                        using var successWriter = new StreamWriter(successCsvFile);
                        successWriter.WriteLine("UserId,IpAddress,Timestamp,ResponseTimeMs");
                        
                        foreach (var success in detailedSuccessLogs)
                        {
                            successWriter.WriteLine($"{success.UserId},{success.IpAddress},{success.Timestamp:o},{success.ElapsedMs:F2}");
                        }
                    }
                    
                    Console.WriteLine("\nLog files saved:");
                    Console.WriteLine($"- Summary: {logFile}");
                    if (detailedErrorLogs.Count > 0)
                        Console.WriteLine($"- Error details: {errorsCsvFile}");
                    if (detailedSuccessLogs.Count > 0)
                        Console.WriteLine($"- Success details: {successCsvFile}");
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Load test error: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Console.ResetColor();
                return 1;
            }
        }
        
        // Helper method to categorize response times into buckets
        private static string CategorizeResponseTime(double responseTimeMs)
        {
            if (responseTimeMs < 10) return "0-10ms";
            if (responseTimeMs < 50) return "10-50ms";
            if (responseTimeMs < 100) return "50-100ms";
            if (responseTimeMs < 250) return "100-250ms";
            if (responseTimeMs < 500) return "250-500ms";
            if (responseTimeMs < 1000) return "500-1000ms";
            return "1s+";
        }

        static async Task RecordUserLoginInteractive(UserLoginService.Protos.UserLoginService.UserLoginServiceClient client)
        {
            Console.Write("Enter user ID: ");
            if (!long.TryParse(Console.ReadLine(), out long userId))
            {
                Console.WriteLine("Invalid user ID format.");
                return;
            }

            Console.Write("Enter IP address: ");
            string ipAddress = Console.ReadLine();
            
            if (!IsValidIpAddress(ipAddress))
            {
                Console.WriteLine($"Invalid IP address format: {ipAddress}");
                Console.WriteLine("Please enter a valid IP address (e.g., 192.168.1.1 or 2001:0db8:85a3:0000:0000:8a2e:0370:7334)");
                return;
            }

            try
            {
                var request = new UserLoginRequest
                {
                    UserId = userId,
                    IpAddress = ipAddress,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };

                var response = await client.UserLoginConnectAsync(request);
                Console.WriteLine($"Response: {response.Success}, Message: {response.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static async Task GetAllUserIPsInteractive(UserLoginService.Protos.UserLoginService.UserLoginServiceClient client)
        {
            Console.Write("Enter user ID: ");
            if (!long.TryParse(Console.ReadLine(), out long userId))
            {
                Console.WriteLine("Invalid user ID format.");
                return;
            }

            try
            {
                var request = new UserIdRequest
                {
                    UserId = userId
                };

                var response = await client.GetAllUserIPsAsync(request);
                
                Console.WriteLine($"Found {response.IpAddresses.Count} IP addresses for user {userId}:");
                foreach (var ip in response.IpAddresses)
                {
                    Console.WriteLine($"- {ip.IpAddress} (Last login: {ip.LastLogin.ToDateTime():yyyy-MM-dd HH:mm:ss UTC})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static async Task GetUsersByIPInteractive(UserLoginService.Protos.UserLoginService.UserLoginServiceClient client)
        {
            Console.Write("Enter IP address pattern: ");
            string ipPattern = Console.ReadLine();
            
            if (!IsValidIpPattern(ipPattern))
            {
                Console.WriteLine($"Invalid IP address pattern: {ipPattern}");
                Console.WriteLine("Please enter a valid IP address or pattern (e.g., 192.168.1.1, 2001:0db8:85a3:0000:0000:8a2e:0370:7334, 192.168, or 2001:0db8:85a3)");
                return;
            }

            try
            {
                var request = new IPAddressRequest
                {
                    IpAddress = ipPattern
                };

                var response = await client.GetUsersByIPAsync(request);
                
                Console.WriteLine($"Found {response.Users.Count} users for IP pattern {ipPattern}:");
                foreach (var user in response.Users)
                {
                    Console.WriteLine($"- User ID: {user.UserId} (Last login: {user.LastLogin.ToDateTime():yyyy-MM-dd HH:mm:ss UTC})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static async Task GetUserLastLoginInteractive(UserLoginService.Protos.UserLoginService.UserLoginServiceClient client)
        {
            try
            {
                Console.Write("Enter user ID: ");
                if (!long.TryParse(Console.ReadLine(), out long userId))
                {
                    Console.WriteLine("Invalid user ID. Please enter a number.");
                    return;
                }

                Console.WriteLine($"Retrieving last login for user ID: {userId}");
                
                var request = new UserIdRequest { UserId = userId };
                
                var sw = Stopwatch.StartNew();
                var response = await client.UserLastLoginAsync(request);
                sw.Stop();
                
                Console.WriteLine($"Request completed in {sw.ElapsedMilliseconds}ms");
                
                if (response.Found)
                {
                    DateTime loginTime = response.LastLogin.ToDateTime();
                    Console.WriteLine($"User {userId} last logged in at: {loginTime} from IP: {response.IpAddress}");
                }
                else
                {
                    Console.WriteLine($"No login records found for user ID {userId}");
                }
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Error: {ex.Status.Detail}");
                Console.WriteLine($"Status code: {ex.Status.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
        }

        static async Task RunLoadTestInteractive(UserLoginService.Protos.UserLoginService.UserLoginServiceClient client)
        {
            Console.Write("Number of unique users (default 100): ");
            var userInput = Console.ReadLine();
            int userCount = string.IsNullOrWhiteSpace(userInput) ? 100 : int.Parse(userInput);

            Console.Write("Number of unique IPs (default 10): ");
            userInput = Console.ReadLine();
            int ipCount = string.IsNullOrWhiteSpace(userInput) ? 10 : int.Parse(userInput);

            Console.Write("Number of login requests to send (default 1000): ");
            userInput = Console.ReadLine();
            int requestCount = string.IsNullOrWhiteSpace(userInput) ? 1000 : int.Parse(userInput);

            Console.Write("Number of parallel tasks (default 10): ");
            userInput = Console.ReadLine();
            int parallelTasks = string.IsNullOrWhiteSpace(userInput) ? 10 : int.Parse(userInput);

            Console.Write("Delay in milliseconds between requests (default 0): ");
            userInput = Console.ReadLine();
            int delayMs = string.IsNullOrWhiteSpace(userInput) ? 0 : int.Parse(userInput);

            await RunLoadTest(new LoadTestOptions 
            { 
                UserCount = userCount,
                IpCount = ipCount,
                RequestCount = requestCount,
                ParallelTasks = parallelTasks,
                DelayMs = delayMs
            });
        }

        static void DisplayMainMenu()
        {
            Console.WriteLine("\n===== User Login Client =====");
            Console.WriteLine("1. Record a new login");
            Console.WriteLine("2. Get IPs for a user");
            Console.WriteLine("3. Get users for an IP");
            Console.WriteLine("4. Get last login for a user");
            Console.WriteLine("5. Run load test");
            Console.WriteLine("6. Exit");
            Console.Write("\nSelect an option: ");
        }

        static async Task<int> GetUserLastLogin(GetLastLoginOptions opts)
        {
            Console.WriteLine($"Retrieving last login for user ID: {opts.UserId}");
            Console.WriteLine($"Connecting to gRPC service at: {opts.ServiceAddress}");
            
            var channelOptions = new GrpcChannelOptions
            {
                MaxRetryAttempts = 5,
                MaxReceiveMessageSize = 16 * 1024 * 1024 // 16 MB
            };

            using var channel = GrpcChannel.ForAddress(opts.ServiceAddress, channelOptions);
            var client = new UserLoginService.Protos.UserLoginService.UserLoginServiceClient(channel);
            
            try
            {
                var request = new UserIdRequest { UserId = opts.UserId };
                
                var sw = Stopwatch.StartNew();
                var response = await client.UserLastLoginAsync(request);
                sw.Stop();
                
                Console.WriteLine($"Request completed in {sw.ElapsedMilliseconds}ms");
                
                if (response.Found)
                {
                    DateTime loginTime = response.LastLogin.ToDateTime();
                    Console.WriteLine($"User {opts.UserId} last logged in at: {loginTime} from IP: {response.IpAddress}");
                }
                else
                {
                    Console.WriteLine($"No login records found for user ID {opts.UserId}");
                }
                
                return 0;
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"Error: {ex.Status.Detail}");
                Console.WriteLine($"Status code: {ex.Status.StatusCode}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                return 1;
            }
        }

        // IP validation patterns
        private static readonly Regex FullIpv4Regex = new Regex(
            @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$",
            RegexOptions.Compiled);

        private static readonly Regex PartialIpv4Regex = new Regex(
            @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){0,3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)?$",
            RegexOptions.Compiled);

        // Regex for validating IPv6 addresses
        private static readonly Regex Ipv6Regex = new Regex(
            @"^(([0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]+|::(ffff(:0{1,4})?:)?((25[0-5]|(2[0-4]|1?[0-9])?[0-9])\.){3}(25[0-5]|(2[0-4]|1?[0-9])?[0-9])|([0-9a-fA-F]{1,4}:){1,4}:((25[0-5]|(2[0-4]|1?[0-9])?[0-9])\.){3}(25[0-5]|(2[0-4]|1?[0-9])?[0-9]))$",
            RegexOptions.Compiled);

        // Regex for validating partial IPv6 patterns
        private static readonly Regex PartialIpv6Regex = new Regex(
            @"^([0-9a-fA-F]{1,4}:){0,7}([0-9a-fA-F]{0,4})?$", 
            RegexOptions.Compiled);

        // Utility methods for IP validation
        private static bool IsValidIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // Try to parse with the built-in IPAddress class which supports both IPv4 and IPv6
            return IPAddress.TryParse(ipAddress, out var _);
        }

        private static bool IsValidIpv4Address(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // Check against regex pattern
            if (!FullIpv4Regex.IsMatch(ipAddress))
                return false;

            // Additional validation using built-in IPAddress class
            return IPAddress.TryParse(ipAddress, out var parsedIp) && 
                   parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }

        private static bool IsValidIpv6Address(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // Check against regex pattern for common formats
            if (!Ipv6Regex.IsMatch(ipAddress))
                return false;

            // Additional validation by trying to parse with the built-in IPAddress class
            return IPAddress.TryParse(ipAddress, out var parsedIp) && 
                   parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
        }

        private static bool IsValidIpv4Pattern(string ipPattern)
        {
            if (string.IsNullOrWhiteSpace(ipPattern))
                return false;

            // If it's a complete IPv4 address, it's also a valid pattern
            if (IsValidIpv4Address(ipPattern))
                return true;

            // Check if it's a valid partial IPv4 pattern
            if (!PartialIpv4Regex.IsMatch(ipPattern))
                return false;

            // Split by dots and check each octet
            string[] octets = ipPattern.Split('.');
            
            // Each octet must be a valid number between 0-255
            foreach (var octet in octets)
            {
                // Skip empty trailing octet (e.g., "192.168.")
                if (string.IsNullOrEmpty(octet))
                    continue;

                if (!int.TryParse(octet, out int value) || value < 0 || value > 255)
                    return false;
            }

            return true;
        }

        private static bool IsValidIpv6Pattern(string ipPattern)
        {
            if (string.IsNullOrWhiteSpace(ipPattern))
                return false;

            // If it's a complete IPv6 address, it's also a valid pattern
            if (IsValidIpv6Address(ipPattern))
                return true;

            // Check if it's a valid partial IPv6 pattern
            if (!PartialIpv6Regex.IsMatch(ipPattern))
                return false;

            // Split by colons and check each hextet
            string[] hextets = ipPattern.Split(':');
            
            // Each hextet must be a valid hexadecimal number between 0 and FFFF
            foreach (var hextet in hextets)
            {
                // Skip empty trailing hextet or consecutive colons (::)
                if (string.IsNullOrEmpty(hextet))
                    continue;

                // Check if the hextet is a valid hexadecimal number (0-FFFF)
                if (!uint.TryParse(hextet, System.Globalization.NumberStyles.HexNumber, null, out uint value) || 
                    value > 0xFFFF)
                    return false;
            }

            return true;
        }

        private static bool IsValidIpPattern(string ipPattern)
        {
            if (string.IsNullOrWhiteSpace(ipPattern))
                return false;

            // Check if it's a valid IPv4 pattern
            if (IsValidIpv4Pattern(ipPattern))
                return true;

            // Check if it's a valid IPv6 pattern
            if (IsValidIpv6Pattern(ipPattern))
                return true;

            return false;
        }
    }
}
