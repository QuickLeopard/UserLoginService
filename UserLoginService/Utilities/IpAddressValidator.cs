using System.Net;
using System.Text.RegularExpressions;

namespace UserLoginService.Utilities
{
    public static class IpAddressValidator
    {
        // Regex for validating full IPv4 addresses (e.g., 192.168.1.1)
        private static readonly Regex FullIpv4Regex = new Regex(
            @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$",
            RegexOptions.Compiled);

        // Regex for validating partial IPv4 patterns (e.g., 192.168. or 192.168)
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

        /// <summary>
        /// Validates if a string is a valid IP address (IPv4 or IPv6)
        /// </summary>
        /// <param name="ipAddress">IP address to validate</param>
        /// <returns>True if valid IP address, false otherwise</returns>
        public static bool IsValidIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // Try to parse with the built-in IPAddress class which supports both IPv4 and IPv6
            return IPAddress.TryParse(ipAddress, out var _);
        }

        /// <summary>
        /// Validates if a string is a valid complete IPv4 address
        /// </summary>
        /// <param name="ipAddress">IP address to validate</param>
        /// <returns>True if valid IPv4 address, false otherwise</returns>
        public static bool IsValidIpv4Address(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // Check against regex pattern
            if (!FullIpv4Regex.IsMatch(ipAddress))
                return false;

            // Additional validation by trying to parse with the built-in IPAddress class
            return IPAddress.TryParse(ipAddress, out var parsedIp) && 
                   parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }

        /// <summary>
        /// Validates if a string is a valid IPv6 address
        /// </summary>
        /// <param name="ipAddress">IP address to validate</param>
        /// <returns>True if valid IPv6 address, false otherwise</returns>
        public static bool IsValidIpv6Address(string ipAddress)
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

        /// <summary>
        /// Validates if a string is a valid IPv4 address prefix pattern
        /// </summary>
        /// <param name="ipPattern">IP pattern to validate</param>
        /// <returns>True if valid IPv4 pattern, false otherwise</returns>
        public static bool IsValidIpv4Pattern(string ipPattern)
        {
            if (string.IsNullOrWhiteSpace(ipPattern))
                return false;

            // If it's a complete IP address, it's also a valid pattern
            if (IsValidIpv4Address(ipPattern))
                return true;

            // Check if it's a valid partial IP (prefix)
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

        /// <summary>
        /// Validates if a string is a valid IPv6 prefix pattern
        /// </summary>
        /// <param name="ipPattern">IP pattern to validate</param>
        /// <returns>True if valid IPv6 pattern, false otherwise</returns>
        public static bool IsValidIpv6Pattern(string ipPattern)
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

        /// <summary>
        /// Validates if a string is a valid IP pattern (IPv4 or IPv6)
        /// </summary>
        /// <param name="ipPattern">IP pattern to validate</param>
        /// <returns>True if valid IP pattern, false otherwise</returns>
        public static bool IsValidIpPattern(string ipPattern)
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
