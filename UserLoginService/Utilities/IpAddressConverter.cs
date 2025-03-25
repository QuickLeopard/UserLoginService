using System;
using System.Net;
using System.Numerics;

namespace UserLoginService.Utilities
{
    public static class IpAddressConverter
    {
        /// <summary>
        /// Converts an IP address string to two ulong values for efficient storage and pattern matching
        /// For IPv4: High = 0, Low = the numeric value
        /// For IPv6: Split into high and low 64-bit values
        /// </summary>
        /// <param name="ipAddress">IP address string (IPv4 or IPv6)</param>
        /// <param name="highBits">Output parameter for high 64 bits (0 for IPv4)</param>
        /// <param name="lowBits">Output parameter for low 64 bits (or full IPv4 value)</param>
        /// <returns>True if conversion successful, false otherwise</returns>
        public static bool TryConvertIpToNumbers(string ipAddress, out Int64 highBits, out Int64 lowBits)
        {
            highBits = 0;
            lowBits = 0;

            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // Try to parse as IP address
            if (!IPAddress.TryParse(ipAddress, out IPAddress? parsedIp))
                return false;

            // Get the bytes representation
            byte[] addressBytes = parsedIp.GetAddressBytes();

            // For IPv4 (4 bytes)
            if (addressBytes.Length == 4)
            {
                // For IPv4, we store the full address in the low bits
                // Ensure proper network byte order (big-endian)
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(addressBytes);

                lowBits = BitConverter.ToUInt32(addressBytes.Concat(new byte[4]).ToArray(), 0);
                highBits = 0; // No high bits for IPv4
                return true;
            }
            // For IPv6 (16 bytes)
            else if (addressBytes.Length == 16)
            {
                // Split into high 64 bits and low 64 bits
                // Ensure proper network byte order (big-endian)
                if (BitConverter.IsLittleEndian)
                {
                    var highBytes = addressBytes.Take(8).Reverse().ToArray();
                    var lowBytes = addressBytes.Skip(8).Take(8).Reverse().ToArray();
                    
                    highBits = BitConverter.ToInt64(highBytes, 0);
                    lowBits = BitConverter.ToInt64(lowBytes, 0);
                }
                else
                {
                    highBits = BitConverter.ToInt64(addressBytes, 0);
                    lowBits = BitConverter.ToInt64(addressBytes, 8);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Converts numeric IP representation back to string form
        /// </summary>
        /// <param name="highBits">High 64 bits (0 for IPv4)</param>
        /// <param name="lowBits">Low 64 bits (or full IPv4 value)</param>
        /// <returns>IP address string or empty string if conversion fails</returns>
        public static string ConvertNumbersToIp(ulong highBits, ulong lowBits)
        {
            // If highBits is 0, this is an IPv4 address
            if (highBits == 0)
            {
                // Extract the 4 bytes from the lower 32 bits
                byte[] bytes = BitConverter.GetBytes((uint)lowBits);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                
                return new IPAddress(bytes).ToString();
            }
            else
            {
                // This is an IPv6 address
                byte[] bytes = new byte[16];
                
                // Convert high bits to bytes
                byte[] highBytes = BitConverter.GetBytes(highBits);
                // Convert low bits to bytes
                byte[] lowBytes = BitConverter.GetBytes(lowBits);
                
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(highBytes);
                    Array.Reverse(lowBytes);
                }
                
                // Combine high and low bytes
                Array.Copy(highBytes, 0, bytes, 0, 8);
                Array.Copy(lowBytes, 0, bytes, 8, 8);
                
                return new IPAddress(bytes).ToString();
            }
        }
        
        /// <summary>
        /// Checks if a given IP pattern (with wildcards) matches a target IP
        /// </summary>
        /// <param name="patternHighBits">High 64 bits of the pattern</param>
        /// <param name="patternLowBits">Low 64 bits of the pattern</param>
        /// <param name="targetHighBits">High 64 bits of the target</param>
        /// <param name="targetLowBits">Low 64 bits of the target</param>
        /// <param name="maskBits">Number of bits to match (from left to right)</param>
        /// <returns>True if the pattern matches the target</returns>
        public static bool MatchIpPattern(ulong patternHighBits, ulong patternLowBits, 
                                         ulong targetHighBits, ulong targetLowBits, 
                                         int maskBits = 128)
        {
            // IPv4 has only 32 bits
            if (patternHighBits == 0 && targetHighBits == 0)
                maskBits = Math.Min(maskBits, 32);
            
            // Apply mask to high bits if needed
            if (maskBits > 64)
            {
                ulong highMask = ulong.MaxValue << (128 - maskBits);
                if ((patternHighBits & highMask) != (targetHighBits & highMask))
                    return false;
                
                // All high bits match, check low bits with remaining mask
                ulong lowMask = ulong.MaxValue << (128 - maskBits + 64);
                return (patternLowBits & lowMask) == (targetLowBits & lowMask);
            }
            else if (maskBits == 64)
            {
                // Only check high bits
                return patternHighBits == targetHighBits;
            }
            else
            {
                // Only check part of high bits
                ulong highMask = ulong.MaxValue << (64 - maskBits);
                return (patternHighBits & highMask) == (targetHighBits & highMask);
            }
        }

        public static ulong GetPrefixBitMask(string ipAddressPrefix)
        {
            if (ipAddressPrefix.EndsWith('.') == false)
            {
                ipAddressPrefix = ipAddressPrefix + ".";
            }

            var segments = ipAddressPrefix.Count(c => c == '.');
            var shift = Math.Max(0, 4 - segments) * 8;

            ulong bitmask = ulong.MaxValue << shift;

            return bitmask;
        }
    }
}
