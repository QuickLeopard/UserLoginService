using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.EntityFrameworkCore;
using UserLoginService.Data;
using UserLoginService.Models;
using UserLoginService.Protos;
using UserLoginService.Utilities;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.FileSystemGlobbing.Internal.PatternContexts;

namespace UserLoginService.Services
{
    public class UserLoginServiceImpl : Protos.UserLoginService.UserLoginServiceBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<UserLoginServiceImpl> _logger;
        private readonly ICacheService _cacheService;
        private readonly IRedisStreamService _streamService;
        private static readonly SemaphoreSlim _dbSemaphore = new SemaphoreSlim(10, 10); // Limit concurrent DB operations
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public UserLoginServiceImpl(
            ApplicationDbContext dbContext, 
            ILogger<UserLoginServiceImpl> logger,
            ICacheService cacheService,
            IRedisStreamService streamService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _cacheService = cacheService;
            _streamService = streamService;
        }

        public override async Task<UserLoginResponse> UserLoginConnect(UserLoginRequest request, ServerCallContext context)
        {
            // Get user-specific lock using the UserId as a string key
            var userLock = _userLocks.GetOrAdd(request.UserId.ToString(), _ => new SemaphoreSlim(1, 1));
            
            try
            {
                //_logger.LogInformation("Received login request for user {UserId} from IP {IpAddress}", 
                //    request.UserId, request.IpAddress);

                // Validate IP address
                if (string.IsNullOrWhiteSpace(request.IpAddress))
                {
                    _logger.LogWarning("Invalid empty IP address for user {UserId}", request.UserId);
                    return new UserLoginResponse
                    {
                        Success = false,
                        Message = "Invalid IP address: IP address cannot be empty"
                    };
                }

                if (!IpAddressValidator.IsValidIpAddress(request.IpAddress))
                {
                    _logger.LogWarning("Invalid IP address format {IpAddress} for user {UserId}", request.IpAddress, request.UserId);
                    return new UserLoginResponse
                    {
                        Success = false,
                        Message = $"Invalid IP address format: {request.IpAddress}\nPlease provide a valid IPv4 or IPv6 address"
                    };
                }

                // Convert IP address to numeric representation
                bool convertSuccess = IpAddressConverter.TryConvertIpToNumbers(
                    request.IpAddress, 
                    out Int64 ipNumericHigh, 
                    out Int64 ipNumericLow);
                
                if (!convertSuccess)
                {
                    _logger.LogWarning("Failed to convert IP {IpAddress} to numeric format for user {UserId}", 
                        request.IpAddress, request.UserId);
                    return new UserLoginResponse
                    {
                        Success = false,
                        Message = $"Failed to process IP address: {request.IpAddress}"
                    };
                }

                // Acquire user specific lock to prevent race conditions
                await userLock.WaitAsync(TimeSpan.FromSeconds(2));

                // Create new user login record
                var loginRecord = new UserLoginRecord
                {
                    UserId = request.UserId,
                    IpAddress = request.IpAddress,
                    LoginTimestamp = request.Timestamp.ToDateTime(),
                    IpNumericHigh = ipNumericHigh,
                    IpNumericLow = ipNumericLow
                };

                try
                {
                    // Publish to Redis Stream instead of directly writing to database
                    await _streamService.PublishUserLoginAsync(loginRecord);
                    //_logger.LogInformation("Successfully published login record to Redis Stream for user {UserId}", request.UserId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error publishing login to Redis Stream for user {UserId}", request.UserId);
                    return new UserLoginResponse
                    {
                        Success = false,
                        Message = "Error publishing login record: " + ex.Message
                    };
                }

                // Perform cache operations asynchronously to improve response time
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _cacheService.RemoveAsync($"user_ips_{request.UserId}");
                        await _cacheService.RemoveAsync($"users_by_ip_{request.IpAddress}");
                        //_logger.LogInformation("Successfully invalidated cache for user {UserId}", request.UserId);
                    }
                    catch (Exception cacheEx)
                    {
                        _logger.LogWarning(cacheEx, "Cache invalidation failed for user {UserId}, but stream write succeeded", request.UserId);
                    }
                });

                return new UserLoginResponse
                {
                    Success = true,
                    Message = "Login recorded successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording login for user {UserId}", request.UserId);
                
                return new UserLoginResponse
                {
                    Success = false,
                    Message = $"Error recording login: {ex.Message}"
                };
            }
            finally
            {
                if (userLock.CurrentCount == 0)
                {
                    userLock.Release();
                }
            }
        }

        public override async Task<IPAddressListResponse> GetAllUserIPs(UserIdRequest request, ServerCallContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                _logger.LogInformation("Retrieving all IP addresses for user {UserId}", request.UserId);

                // Try to get from cache first
                IPAddressListResponse? cachedResponse = null;
                string cacheKey = $"user_ips_{request.UserId}";
                
                try 
                {
                    cachedResponse = await _cacheService.GetAsync<IPAddressListResponse>(cacheKey);
                    
                    if (cachedResponse != null)
                    {
                        _logger.LogInformation("Retrieved IP addresses for user {UserId} from cache", request.UserId);
                        return cachedResponse;
                    }
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Cache retrieval failed for user {UserId}, falling back to database", request.UserId);
                }

                // Acquire semaphore to limit concurrent DB operations
                bool dbSemaphoreAcquired = await _dbSemaphore.WaitAsync(TimeSpan.FromSeconds(3));
                
                try
                {
                    if (!dbSemaphoreAcquired)
                    {
                        _logger.LogWarning("Database semaphore timeout for user {UserId}, service under high load", request.UserId);
                        throw new RpcException(new Status(StatusCode.ResourceExhausted, "Service is currently experiencing high load. Please try again."));
                    }

                    // Use a more efficient query with indexing hints
                    var ipAddresses = await _dbContext.UserLoginRecords
                        .AsNoTracking() // Performance improvement for read-only queries
                        .Where(r => r.UserId == request.UserId)
                        .TagWith("GetAllUserIPs_Query") // For query identification in logs
                        .GroupBy(r => r.IpAddress)
                        .Select(g => new 
                        { 
                            IpAddress = g.Key, 
                            LastLogin = g.Max(r => r.LoginTimestamp),
                            IpNumericHigh = g.First().IpNumericHigh,
                            IpNumericLow = g.First().IpNumericLow
                        })
                        .ToListAsync(context.CancellationToken);

                    var response = new IPAddressListResponse();
                    
                    foreach (var ip in ipAddresses)
                    {
                        response.IpAddresses.Add(new IPAddressInfo 
                        { 
                            IpAddress = ip.IpAddress,
                            LastLogin = Timestamp.FromDateTime(DateTime.SpecifyKind(ip.LastLogin, DateTimeKind.Utc)),
                            IpNumericHigh = (ulong)ip.IpNumericHigh,
                            IpNumericLow = (ulong)ip.IpNumericLow
                        });
                    }

                    _logger.LogInformation("Found {Count} IP addresses for user {UserId}", 
                        response.IpAddresses.Count, request.UserId);

                    // Cache the result asynchronously 
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));
                            _logger.LogInformation("Successfully cached IP addresses for user {UserId}", request.UserId);
                        }
                        catch (Exception cacheEx)
                        {
                            _logger.LogWarning(cacheEx, "Failed to cache IP addresses for user {UserId}", request.UserId);
                        }
                    });

                    return response;
                }
                finally
                {
                    if (dbSemaphoreAcquired)
                    {
                        _dbSemaphore.Release();
                    }
                }
            }
            catch (Exception ex) when (!(ex is RpcException))
            {
                _logger.LogError(ex, "Error retrieving IP addresses for user {UserId}", request.UserId);
                throw new RpcException(new Status(StatusCode.Internal, $"Error retrieving IP addresses: {ex.Message}"));
            }
        }

        public override async Task<UserListResponse> GetUsersByIP(IPAddressRequest request, ServerCallContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                _logger.LogInformation("Retrieving users for IP address pattern {IpAddress}", request.IpAddress);

                // Validate IP address pattern
                if (string.IsNullOrWhiteSpace(request.IpAddress))
                {
                    _logger.LogWarning("Invalid empty IP address pattern");
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "IP address pattern cannot be empty"));
                }

                var isValidIp = IpAddressValidator.IsValidIpv4Address(request.IpAddress) || IpAddressValidator.IsValidIpv6Address(request.IpAddress);

                var isValidIpPattern = IpAddressValidator.IsValidIpPattern(request.IpAddress);
        
                if (!isValidIpPattern)
                {
                    _logger.LogWarning("Invalid IP address pattern format {IpAddress}", request.IpAddress);
                    throw new RpcException(new Status(StatusCode.InvalidArgument, 
                        $"Invalid IP address pattern: {request.IpAddress}\nPlease provide a valid IPv4 address or pattern (e.g., 192.168.1.1 or 192.168) or IPv6 address or pattern"));
                }

                // Convert the IP address pattern to numeric representation
                bool isExactIp = isValidIp;// && !isValidIpPattern;
                Int64 patternHighBits = 0;
                Int64 patternLowBits = 0;
                int cidrPrefixLength = 128; // Default for exact match

                _logger.LogWarning ($"isExactIp: {isExactIp}");
                
                // For exact IP address matching, convert to numeric
                if (isExactIp)
                {
                    bool convertSuccess = IpAddressConverter.TryConvertIpToNumbers(
                        request.IpAddress, 
                        out patternHighBits, 
                        out patternLowBits);
                        
                    if (!convertSuccess)
                    {
                        _logger.LogWarning("Failed to convert IP pattern {IpAddress} to numeric format", request.IpAddress);
                        throw new RpcException(new Status(StatusCode.InvalidArgument, 
                            $"Failed to process IP address pattern: {request.IpAddress}"));
                    }
                }
                // For pattern matching, calculate prefix
                else
                {
                    if (!request.IpAddress.EndsWith("."))
                        request.IpAddress = request.IpAddress + ".";

                    // Try to extract a CIDR prefix length from pattern
                    string ipPart = request.IpAddress;
                    if (request.IpAddress.Contains("/"))
                    {
                        var parts = request.IpAddress.Split('/');
                        ipPart = parts[0];
                        if (int.TryParse(parts[1], out int prefixLen))
                        {
                            cidrPrefixLength = prefixLen;
                        }
                    }
                    else if (request.IpAddress.EndsWith("."))
                    {
                        // IPv4 with dot at end - calculate prefix based on segments
                        int segments = request.IpAddress.Count(c => c == '.');
                        cidrPrefixLength = segments * 8;
                    }

                    // Convert the prefix part to numeric for pattern matching
                    var patternIp = ipPart.TrimEnd('*', '.');
                    if (patternIp.EndsWith(".")) patternIp = patternIp.TrimEnd('.');
                    
                    // Add zeroes to make a valid IP for conversion
                    if (patternIp.Count(c => c == '.') >= 0 && patternIp.Count(c => c == '.') < 3)
                    {
                        // IPv4 prefix - pad with zeros
                        int missingSegments = 3 - patternIp.Count(c => c == '.');
                        for (int i = 0; i < missingSegments; i++)
                        {
                            patternIp += ".0";
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(patternIp) && IpAddressValidator.IsValidIpAddress(patternIp))
                    {
                        IpAddressConverter.TryConvertIpToNumbers(patternIp, out patternHighBits, out patternLowBits);
                    }

                    //_logger.LogWarning($"{request.IpAddress} {patternIp} {patternHighBits}:{patternLowBits}");
                }

                // Try to get from cache first
                string cacheKey = $"users_by_ip_{request}"; //.IpAddress.TrimEnd('*', '.')}";
                /*UserListResponse? cachedResponse = null;
                
                try
                {
                    cachedResponse = await _cacheService.GetAsync<UserListResponse>(cacheKey);
                    
                    if (cachedResponse != null)
                    {
                        _logger.LogInformation("Retrieved users for IP address pattern {IpAddress} from cache", request.IpAddress);
                        return cachedResponse;
                    }
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Cache retrieval failed for IP pattern {IpAddress}, falling back to database", request.IpAddress);
                }*/

                // Acquire semaphore to limit concurrent DB operations
                bool dbSemaphoreAcquired = await _dbSemaphore.WaitAsync(TimeSpan.FromSeconds(3));

                try
                {
                    if (!dbSemaphoreAcquired)
                    {
                        _logger.LogWarning("Database semaphore timeout for IP pattern {IpAddress}, service under high load", request.IpAddress);
                        throw new RpcException(new Status(StatusCode.ResourceExhausted, "Service is currently experiencing high load. Please try again."));
                    }

                    // Use numeric pattern matching for faster query
                    var queryable = _dbContext.UserLoginRecords.AsNoTracking();

                    // Different query approach based on exact vs pattern matching
                    if (isExactIp)
                    {
                        // For exact match, use equality on both high and low bits
                        queryable = queryable.Where(r => 
                            r.IpNumericHigh == patternHighBits && 
                            r.IpNumericLow == patternLowBits);
                    }
                    else
                    {
                        

                        //_logger.LogWarning ($"{request.IpAddress} {patternHighBits}:{patternLowBits}");

                        queryable = queryable.Where(r => 
                            (r.IpNumericHigh & patternHighBits) == patternHighBits &&
                            (r.IpNumericLow  & patternLowBits)  == patternLowBits);

                        // For CIDR-like prefix matching
                        /*if (patternHighBits == 0 && request.IpAddress.Contains("."))
                        {
                            // IPv4 pattern matching - simplified approach using string prefix
                            var ipPattern = request.IpAddress.TrimEnd('*', '.');

                            _logger.LogWarning ($"{patternHighBits}:{patternLowBits}");

                            queryable = queryable.Where(r => r.IpAddress.StartsWith(ipPattern));
                            //queryable = queryable.Where(r => 
                            //    r.IpNumericHigh == patternHighBits && 
                            //    (r.IpNumericLow & patternLowBits) == patternLowBits);
                        }
                        else
                        {
                            // We'll still use string prefix matching for simplicity in this implementation
                            // In a production system, you would implement bit-masking here based on cidrPrefixLength
                            var ipPattern = request.IpAddress.TrimEnd('*', '.');
                            queryable = queryable.Where(r => r.IpAddress.StartsWith(ipPattern));
                        }*/
                    }
                
                    // Complete the query with tagging, grouping, and projection
                    queryable = queryable.TagWith("GetUsersByIP_NumericQuery");
                    
                    var users = await queryable
                        .GroupBy(r => r.UserId)
                        .Select(g => new 
                        { 
                            UserId = g.Key, 
                            LastLogin = g.Max(r => r.LoginTimestamp),
                            IpAddress = g.OrderByDescending(r => r.LoginTimestamp).First().IpAddress,
                            IpNumericHigh = g.OrderByDescending(r => r.LoginTimestamp).First().IpNumericHigh,
                            IpNumericLow = g.OrderByDescending(r => r.LoginTimestamp).First().IpNumericLow
                        })
                        .ToListAsync(context.CancellationToken);

                    var response = new UserListResponse();
                    
                    foreach (var user in users)
                    {
                        response.Users.Add(new UserInfo 
                        { 
                            UserId = user.UserId,
                            LastLogin = Timestamp.FromDateTime(DateTime.SpecifyKind(user.LastLogin, DateTimeKind.Utc)),
                            IpAddress = user.IpAddress,
                            IpNumericHigh = (ulong)user.IpNumericHigh,
                            IpNumericLow = (ulong)user.IpNumericLow
                        });
                    }

                    _logger.LogInformation("Found {Count} users for IP address pattern {IpAddress}", 
                        response.Users.Count, request.IpAddress);

                    // Cache the result asynchronously
                    /*_ = Task.Run(async () => 
                    {
                        try
                        {
                            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));
                            _logger.LogInformation("Successfully cached users for IP address pattern {IpAddress}", request.IpAddress);
                        }
                        catch (Exception cacheEx)
                        {
                            _logger.LogWarning(cacheEx, "Failed to cache users for IP address pattern {IpAddress}", request.IpAddress);
                        }
                    });*/

                    return response;
                }
                finally
                {
                    if (dbSemaphoreAcquired)
                    {
                        _dbSemaphore.Release();
                    }
                }
            }
            catch (Exception ex) when (!(ex is RpcException))
            {
                _logger.LogError(ex, "Error retrieving users for IP address {IpAddress}", request.IpAddress);
                throw new RpcException(new Status(StatusCode.Internal, $"Error retrieving users: {ex.Message}"));
            }
        }

        public override async Task<UserLastLoginResponse> UserLastLogin(UserIdRequest request, ServerCallContext context)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                _logger.LogInformation("Retrieving last login for user ID {UserId}", request.UserId);

                // Validate user ID
                if (request.UserId <= 0)
                {
                    _logger.LogWarning("Invalid user ID: {UserId}", request.UserId);
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "User ID must be a positive number"));
                }

                // Try to get from cache first
                UserLastLoginResponse? cachedResponse = null;
                string cacheKey = $"user_last_login_{request.UserId}";
                
                try
                {
                    cachedResponse = await _cacheService.GetAsync<UserLastLoginResponse>(cacheKey);
                    
                    if (cachedResponse != null)
                    {
                        _logger.LogInformation("Retrieved last login for user ID {UserId} from cache", request.UserId);
                        return cachedResponse;
                    }
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Cache retrieval failed for user ID {UserId}, falling back to database", request.UserId);
                }

                // Acquire semaphore to limit concurrent DB operations
                bool dbSemaphoreAcquired = await _dbSemaphore.WaitAsync(TimeSpan.FromSeconds(3));

                try
                {
                    if (!dbSemaphoreAcquired)
                    {
                        _logger.LogWarning("Database semaphore timeout for user ID {UserId}, service under high load", request.UserId);
                        throw new RpcException(new Status(StatusCode.ResourceExhausted, "Service is currently experiencing high load. Please try again."));
                    }

                    // Find the most recent login for this user
                    var userLogin = await _dbContext.UserLoginRecords
                        .AsNoTracking()
                        .Where(r => r.UserId == request.UserId)
                        .OrderByDescending(r => r.LoginTimestamp)
                        .FirstOrDefaultAsync(context.CancellationToken);

                    var response = new UserLastLoginResponse
                    {
                        Found = userLogin != null,
                        UserId = request.UserId
                    };

                    if (userLogin != null)
                    {
                        response.LastLogin = Timestamp.FromDateTime(DateTime.SpecifyKind(userLogin.LoginTimestamp, DateTimeKind.Utc));
                        response.IpAddress = userLogin.IpAddress;
                        response.IpNumericHigh = (ulong)userLogin.IpNumericHigh;
                        response.IpNumericLow = (ulong)userLogin.IpNumericLow;
                    }

                    _logger.LogInformation("Found last login for user ID {UserId} at {Timestamp} from IP {IpAddress}", 
                        request.UserId, userLogin?.LoginTimestamp, userLogin?.IpAddress);

                    // Cache the result asynchronously
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromMinutes(5));
                            _logger.LogInformation("Successfully cached last login for user ID {UserId}", request.UserId);
                        }
                        catch (Exception cacheEx)
                        {
                            _logger.LogWarning(cacheEx, "Failed to cache last login for user ID {UserId}", request.UserId);
                        }
                    });

                    return response;
                }
                catch (Exception ex) when (ex is not RpcException)
                {
                    _logger.LogError(ex, "Error retrieving last login for user ID {UserId}", request.UserId);
                    throw new RpcException(new Status(StatusCode.Internal, "An error occurred while retrieving user last login data."));
                }
                finally
                {
                    if (dbSemaphoreAcquired)
                    {
                        _dbSemaphore.Release();
                    }
                }
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in UserLastLogin for user ID {UserId}", request.UserId);
                throw new RpcException(new Status(StatusCode.Internal, "An unexpected error occurred."));
            }
        }
    }
}
