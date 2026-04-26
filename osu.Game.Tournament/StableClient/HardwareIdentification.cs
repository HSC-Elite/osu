// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using Microsoft.Win32;
using System.Management;

namespace osu.Game.Tournament.StableClient
{
    [SupportedOSPlatform("windows")]
    public static class HardwareIdentification
    {
        private const string default_osu_path_md5 = "cd4a25cbe1d5793e8bf577217497c701";

        public static string GenerateClientHashes(string? osuPath = null)
        {
            string macAddressesStr = getMacAddressesString();
            string macAddressesMd5 = getMd5(macAddressesStr);
            string uninstallIdMd5 = getMd5(getMd5(getUninstallId()));
            string diskSignatureMd5 = getMd5(getMd5(getDiskSignature()));
            string osuPathMd5 = osuPath ?? default_osu_path_md5;

            // Stable sends a trailing ':' here; some bancho implementations rely on it when parsing.
            return $"{osuPathMd5}:{macAddressesStr}:{macAddressesMd5}:{uninstallIdMd5}:{diskSignatureMd5}:";
        }

        private static string getMacAddressesString()
        {
            var macs = NetworkInterface.GetAllNetworkInterfaces()
                                       .Where(n => !n.Name.Contains('-') &&
                                                   n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                                   n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                                                   n.OperationalStatus != OperationalStatus.NotPresent &&
                                                   n.OperationalStatus != OperationalStatus.Unknown)
                                       .Select(n => n.GetPhysicalAddress()?.ToString() ?? string.Empty);

            return string.Concat(macs.Select(m => $"{m}."));
        }

        [SupportedOSPlatform("windows")]
        private static string getUninstallId()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\osu!"))
                {
                    return key?.GetValue("UninstallID")?.ToString() ?? Guid.NewGuid().ToString();
                }
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        [SupportedOSPlatform("windows")]
        private static string getDiskSignature()
        {
            string[] properties = { "Signature", "SerialNumber" };
            const string path = "Win32_DiskDrive";

            try
            {
                foreach (var o in new ManagementClass(path).GetInstances())
                {
                    var instance = (ManagementObject)o;

                    foreach (string propertyName in properties)
                    {
                        try
                        {
                            object? val = instance[propertyName];
                            if (val != null)
                                return val.ToString() ?? string.Empty;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string getFileMd5(string filePath)
        {
            if (!File.Exists(filePath)) return getMd5("dummy");

            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        private static string getMd5(string input)
        {
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
