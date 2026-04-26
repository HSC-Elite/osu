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
        public static string GenerateClientHashes(string? osuFileMd5 = null)
        {
            const string default_md5 = "cd4a25cbe1d5793e8bf577217497c701";
            string macAddressesStr = getMacAddressesString();
            string macAddressesMd5 = getMd5(macAddressesStr);
            string uninstallIdMd5 = getMd5(getUninstallId());
            string diskSignatureMd5 = getMd5(getDiskSignature());

            return $"{osuFileMd5 ?? default_md5}:{macAddressesStr}:{macAddressesMd5}:{uninstallIdMd5}:{diskSignatureMd5}";
        }

        private static string getMacAddressesString()
        {
            var macs = NetworkInterface.GetAllNetworkInterfaces()
                                       .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                                   n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                                       .Select(n => n.GetPhysicalAddress().ToString())
                                       .Where(s => !string.IsNullOrEmpty(s))
                                       .ToList();

            if (macs.Count == 0) return "runningunderwine.";

            return string.Join(".", macs) + ".";
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
