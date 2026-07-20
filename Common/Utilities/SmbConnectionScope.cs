using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    /// <summary>
    /// SMB 共享目录连接管理器。
    ///
    /// 设计原则：
    /// 1. 同一个远程服务器，在同一个 Windows 登录会话中只能使用一套凭据。
    /// 2. SMB 连接按共享目录缓存，避免每次文件操作重复连接、断开。
    /// 3. Dispose 只释放当前代码租约，不主动断开底层 SMB 连接。
    /// 4. 应用退出或人工维护时，可通过 DisconnectAll 统一断开。
    /// </summary>
    public sealed class SmbConnectionScope : IDisposable
    {
        private const int NoError = 0;
        private const int ErrorSessionCredentialConflict = 1219;
        private const int ErrorNotConnected = 2250;

        /// <summary>
        /// 按共享目录缓存连接，例如：
        /// \\10.9.85.35\检测结果
        /// </summary>
        private static readonly ConcurrentDictionary<string, ConnectionEntry>
            ShareConnections =
                new ConcurrentDictionary<string, ConnectionEntry>(
                    StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 按服务器登记凭据。
        ///
        /// Windows 的 1219 判断粒度是服务器，而不是共享目录。
        /// 因此：
        /// \\10.9.85.35\共享A
        /// \\10.9.85.35\共享B
        /// 必须使用同一套账号。
        /// </summary>
        private static readonly ConcurrentDictionary<string, CredentialDescriptor>
            ServerCredentials =
                new ConcurrentDictionary<string, CredentialDescriptor>(
                    StringComparer.OrdinalIgnoreCase);

        private readonly ConnectionEntry _entry;
        private int _disposed;

        private SmbConnectionScope(ConnectionEntry entry)
        {
            _entry = entry;

            if (_entry != null)
            {
                Interlocked.Increment(ref _entry.ActiveLeaseCount);
            }
        }

        /// <summary>
        /// 根据文件路径决定是否需要建立 SMB 连接。
        /// </summary>
        public static IDisposable ConnectIfNeeded(
            string path,
            string userName,
            string password)
        {
            if (!TryGetUncPathInfo(path, out UncPathInfo uncInfo))
            {
                return EmptyDisposable.Instance;
            }

            // Empty account means using the current Windows identity. Do not register
            // credentials or call WNetAddConnection2 in this mode.
            if (string.IsNullOrWhiteSpace(userName))
            {
                return EmptyDisposable.Instance;
            }

            CredentialDescriptor credential =
                CreateCredentialDescriptor(userName, password);

            // 先检查同一服务器是否配置了不同账号。
            RegisterServerCredential(uncInfo.ServerName, credential);

            /*
             * 未配置用户名时，不主动调用 WNetAddConnection2。
             * 后续文件访问将使用 WebApi 当前 Windows 身份。
             *
             * 但上面仍然登记了当前身份，防止另一个任务又使用 cim
             * 连接同一服务器，提前发现配置冲突。
             */
            if (!credential.IsExplicit)
            {
                return EmptyDisposable.Instance;
            }

            ConnectionEntry entry = ShareConnections.GetOrAdd(
                uncInfo.ShareRoot,
                _ => new ConnectionEntry
                {
                    ServerName = uncInfo.ServerName,
                    ShareRoot = uncInfo.ShareRoot,
                    UserName = credential.DisplayUserName,
                    CredentialKey = credential.CredentialKey
                });

            // 理论上服务器级校验已经覆盖，这里再做一次共享目录级保护。
            if (!string.Equals(
                    entry.CredentialKey,
                    credential.CredentialKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"共享目录 {uncInfo.ShareRoot} 已使用账号 " +
                    $"[{entry.UserName}] 初始化，不能再使用账号 " +
                    $"[{credential.DisplayUserName}]。");
            }

            EnsureConnected(entry, userName, password);

            return new SmbConnectionScope(entry);
        }

        /// <summary>
        /// 普通文件操作结束时，不断开 SMB 底层连接。
        ///
        /// 原实现每次 Dispose 都调用 WNetCancelConnection2(force=true)，
        /// 在并发任务中会把其他任务正在使用的连接强制断开。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_entry != null)
            {
                Interlocked.Decrement(ref _entry.ActiveLeaseCount);
            }
        }

        /// <summary>
        /// 断开当前进程登记过的全部 SMB 共享连接。
        ///
        /// 建议仅用于：
        /// 1. 应用停止；
        /// 2. 修改共享目录账号后人工重置；
        /// 3. 故障维护。
        ///
        /// 不要在每次文件读取结束后调用。
        /// </summary>
        public static IReadOnlyList<string> DisconnectAll(bool force = false)
        {
            var errors = new List<string>();

            foreach (KeyValuePair<string, ConnectionEntry> item
                     in ShareConnections.ToArray())
            {
                ConnectionEntry entry = item.Value;

                lock (entry.SyncRoot)
                {
                    if (!entry.IsConnected)
                    {
                        ShareConnections.TryRemove(item.Key, out _);
                        continue;
                    }

                    int result = WNetCancelConnection2(
                        entry.ShareRoot,
                        0,
                        force);

                    if (result == NoError || result == ErrorNotConnected)
                    {
                        entry.IsConnected = false;
                        ShareConnections.TryRemove(item.Key, out _);
                    }
                    else
                    {
                        errors.Add(
                            $"断开共享目录 {entry.ShareRoot} 失败，" +
                            $"Win32Error={result}，" +
                            $"Reason={new Win32Exception(result).Message}");
                    }
                }
            }

            RemoveUnusedServerCredentials();

            return errors;
        }

        /// <summary>
        /// 断开指定服务器下，由当前进程登记过的全部共享连接。
        ///
        /// pathOrServer 可传：
        /// \\10.9.85.35\检测结果
        /// \\10.9.85.35
        /// 10.9.85.35
        /// </summary>
        public static IReadOnlyList<string> DisconnectServer(
            string pathOrServer,
            bool force = false)
        {
            var errors = new List<string>();

            string serverName = GetServerName(pathOrServer);

            if (string.IsNullOrWhiteSpace(serverName))
            {
                errors.Add($"无法从 [{pathOrServer}] 解析服务器名称。");
                return errors;
            }

            List<KeyValuePair<string, ConnectionEntry>> connections =
                ShareConnections
                    .Where(x => string.Equals(
                        x.Value.ServerName,
                        serverName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

            foreach (KeyValuePair<string, ConnectionEntry> item in connections)
            {
                ConnectionEntry entry = item.Value;

                lock (entry.SyncRoot)
                {
                    if (!entry.IsConnected)
                    {
                        ShareConnections.TryRemove(item.Key, out _);
                        continue;
                    }

                    int result = WNetCancelConnection2(
                        entry.ShareRoot,
                        0,
                        force);

                    if (result == NoError || result == ErrorNotConnected)
                    {
                        entry.IsConnected = false;
                        ShareConnections.TryRemove(item.Key, out _);
                    }
                    else
                    {
                        errors.Add(
                            $"断开共享目录 {entry.ShareRoot} 失败，" +
                            $"Win32Error={result}，" +
                            $"Reason={new Win32Exception(result).Message}");
                    }
                }
            }

            bool stillHasConnection = ShareConnections.Values.Any(
                x => string.Equals(
                    x.ServerName,
                    serverName,
                    StringComparison.OrdinalIgnoreCase));

            if (!stillHasConnection)
            {
                ServerCredentials.TryRemove(serverName, out _);
            }

            return errors;
        }

        public static IReadOnlyList<SmbConnectionSnapshot> GetSnapshots()
        {
            var snapshots = ShareConnections.Values
                .Select(entry => new SmbConnectionSnapshot
                {
                    ServerName = entry.ServerName,
                    ShareRoot = entry.ShareRoot,
                    UserName = entry.UserName,
                    IsExplicitCredential = !string.IsNullOrWhiteSpace(entry.CredentialKey)
                        && entry.CredentialKey.StartsWith("EXPLICIT|", StringComparison.OrdinalIgnoreCase),
                    IsConnected = entry.IsConnected,
                    ConnectedTime = entry.ConnectedTime,
                    ActiveLeaseCount = entry.ActiveLeaseCount
                })
                .OrderBy(x => x.ServerName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ShareRoot, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (KeyValuePair<string, CredentialDescriptor> credential
                     in ServerCredentials.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                bool hasShare = snapshots.Any(
                    x => string.Equals(
                        x.ServerName,
                        credential.Key,
                        StringComparison.OrdinalIgnoreCase));

                if (hasShare)
                {
                    continue;
                }

                snapshots.Add(new SmbConnectionSnapshot
                {
                    ServerName = credential.Key,
                    ShareRoot = string.Empty,
                    UserName = credential.Value.DisplayUserName,
                    IsExplicitCredential = credential.Value.IsExplicit,
                    IsConnected = false,
                    ConnectedTime = null,
                    ActiveLeaseCount = 0
                });
            }

            return snapshots;
        }

        private static void EnsureConnected(
            ConnectionEntry entry,
            string userName,
            string password)
        {
            lock (entry.SyncRoot)
            {
                if (entry.IsConnected)
                {
                    return;
                }

                var resource = new NetResource
                {
                    Scope = 0,
                    Type = 1,
                    DisplayType = 0,
                    Usage = 0,
                    LocalName = null,
                    RemoteName = entry.ShareRoot,
                    Comment = null,
                    Provider = null
                };

                int result = WNetAddConnection2(
                    ref resource,
                    password,
                    userName,
                    0);

                if (result == NoError)
                {
                    entry.IsConnected = true;
                    entry.ConnectedTime = DateTime.Now;
                    return;
                }

                if (result == ErrorSessionCredentialConflict &&
                    !string.IsNullOrWhiteSpace(userName) &&
                    entry.ActiveLeaseCount == 0)
                {
                    int disconnectResult = WNetCancelConnection2(
                        entry.ShareRoot,
                        0,
                        false);

                    if (disconnectResult == NoError ||
                        disconnectResult == ErrorNotConnected)
                    {
                        entry.IsConnected = false;

                        result = WNetAddConnection2(
                            ref resource,
                            password,
                            userName,
                            0);

                        if (result == NoError)
                        {
                            entry.IsConnected = true;
                            entry.ConnectedTime = DateTime.Now;
                            return;
                        }

                        throw new Win32Exception(
                            result,
                            BuildErrorMessage(
                                entry.ShareRoot,
                                userName,
                                result) +
                            $" 同共享自动断开后重试失败，Win32Error={result}。");
                    }

                    throw new Win32Exception(
                        result,
                        BuildErrorMessage(
                            entry.ShareRoot,
                            userName,
                            result) +
                        $" 同共享自动断开失败，Win32Error={disconnectResult}，Reason={new Win32Exception(disconnectResult).Message}。");
                }

                throw new Win32Exception(
                    result,
                    BuildErrorMessage(
                        entry.ShareRoot,
                        userName,
                        result));
            }
        }

        private static void RegisterServerCredential(
            string serverName,
            CredentialDescriptor credential)
        {
            CredentialDescriptor existing =
                ServerCredentials.GetOrAdd(serverName, credential);

            if (string.Equals(
                    existing.CredentialKey,
                    credential.CredentialKey,
                    StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"SMB 凭据配置冲突：服务器 \\\\{serverName} " +
                $"已经登记账号 [{existing.DisplayUserName}]，" +
                $"当前又尝试使用账号 [{credential.DisplayUserName}]。" +
                Environment.NewLine +
                "Windows 不允许同一个运行用户同时使用不同账号连接同一服务器。" +
                Environment.NewLine +
                $"请检查所有以 \\\\{serverName}\\ 开头的文件配置，" +
                "确保它们全部使用相同账号；也不能混用“未配置账号”和显式账号。");
        }

        private static CredentialDescriptor CreateCredentialDescriptor(
            string userName,
            string password)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                string currentIdentity;

                try
                {
                    currentIdentity =
                        WindowsIdentity.GetCurrent()?.Name
                        ?? "未知 Windows 身份";
                }
                catch
                {
                    currentIdentity = "未知 Windows 身份";
                }

                return new CredentialDescriptor
                {
                    IsExplicit = false,
                    DisplayUserName = $"当前运行身份：{currentIdentity}",
                    CredentialKey =
                        "IMPLICIT|" + currentIdentity.Trim().ToUpperInvariant()
                };
            }

            string normalizedUserName = userName.Trim();
            string passwordHash = ComputeHash(password ?? string.Empty);

            return new CredentialDescriptor
            {
                IsExplicit = true,
                DisplayUserName = normalizedUserName,
                CredentialKey =
                    "EXPLICIT|" +
                    normalizedUserName.ToUpperInvariant() +
                    "|" +
                    passwordHash
            };
        }

        private static string ComputeHash(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private static bool TryGetUncPathInfo(
            string path,
            out UncPathInfo info)
        {
            info = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalizedPath = path.Trim();

            if (!normalizedPath.StartsWith(
                    @"\\",
                    StringComparison.Ordinal))
            {
                return false;
            }

            string trimmed = normalizedPath.TrimStart('\\');

            string[] parts = trimmed.Split(
                new[] { '\\' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return false;
            }

            info = new UncPathInfo
            {
                ServerName = parts[0],
                ShareName = parts[1],
                ShareRoot = @"\\" + parts[0] + @"\" + parts[1]
            };

            return true;
        }

        private static string GetServerName(string pathOrServer)
        {
            if (string.IsNullOrWhiteSpace(pathOrServer))
            {
                return string.Empty;
            }

            string value = pathOrServer.Trim();

            if (value.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string trimmed = value.TrimStart('\\');

                string[] parts = trimmed.Split(
                    new[] { '\\' },
                    StringSplitOptions.RemoveEmptyEntries);

                return parts.Length > 0 ? parts[0] : string.Empty;
            }

            int slashIndex = value.IndexOf('\\');

            return slashIndex >= 0
                ? value.Substring(0, slashIndex)
                : value;
        }

        private static void RemoveUnusedServerCredentials()
        {
            foreach (string serverName in ServerCredentials.Keys.ToArray())
            {
                bool stillUsed = ShareConnections.Values.Any(
                    x => string.Equals(
                        x.ServerName,
                        serverName,
                        StringComparison.OrdinalIgnoreCase));

                if (!stillUsed)
                {
                    ServerCredentials.TryRemove(serverName, out _);
                }
            }
        }

        private static string BuildErrorMessage(
            string remoteName,
            string userName,
            int errorCode)
        {
            string account = string.IsNullOrWhiteSpace(userName)
                ? "未配置账号"
                : userName;

            return
                $"无法连接共享目录 {remoteName}，" +
                $"账号 {account}，" +
                $"Win32Error={errorCode}。" +
                GetFriendlyReason(errorCode) +
                GetConnectionConflictHint(remoteName, errorCode);
        }

        private static string GetConnectionConflictHint(
            string remoteName,
            int errorCode)
        {
            if (errorCode != ErrorSessionCredentialConflict)
            {
                return string.Empty;
            }

            string serverName = GetServerName(remoteName);

            if (string.IsNullOrWhiteSpace(serverName))
            {
                return string.Empty;
            }

            List<string> accounts = ServerCredentials
                .Where(x => string.Equals(
                    x.Key,
                    serverName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Value.DisplayUserName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> shares = ShareConnections.Values
                .Where(x => string.Equals(
                    x.ServerName,
                    serverName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(x =>
                    $"{x.ShareRoot}(account={x.UserName}, connected={x.IsConnected}, activeLease={x.ActiveLeaseCount})")
                .ToList();

            if (accounts.Count == 0 && shares.Count == 0)
            {
                return
                    $" 当前 WebApi 进程没有登记 \\\\{serverName} 的 SMB 连接，" +
                    "更可能是资源管理器、其他进程、管理员/非管理员会话中已存在旧连接。";
            }

            return
                $" 当前 WebApi 进程已登记 \\\\{serverName} 的账号：{string.Join(", ", accounts)}；" +
                $"共享：{string.Join("; ", shares)}。";
        }

        private static string GetFriendlyReason(int errorCode)
        {
            switch (errorCode)
            {
                case 1219:
                    return
                        "同一 Windows 登录会话中，已经使用其他账号连接了该服务器。" +
                        "请停止 WebApi 或相关后台任务，在实际运行账号下执行 net use，" +
                        "断开指向该服务器的旧连接，并确保所有共享配置统一使用同一账号。";

                case 1326:
                    return
                        "用户名或密码错误，请检查 Domain、UserName、Password。";

                case 5:
                    return
                        "访问被拒绝，请同时检查共享权限和 NTFS 文件夹权限。";

                case 53:
                    return
                        "网络路径未找到，请检查 IP、共享名、网络连通性和防火墙。";

                case 67:
                    return
                        "网络名称找不到，请检查共享名称是否正确。";

                case 86:
                    return
                        "密码错误，请重新录入密码后保存配置。";

                case 64:
                    return
                        "指定网络名不再可用，请检查网络稳定性或 SMB 服务状态。";

                default:
                    return
                        "请检查共享路径、账号密码、网络连通性、共享权限、NTFS 权限，" +
                        "以及是否存在 Windows SMB 凭据冲突。";
            }
        }

        [DllImport(
            "mpr.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern int WNetAddConnection2(
            ref NetResource netResource,
            string password,
            string username,
            int flags);

        [DllImport(
            "mpr.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern int WNetCancelConnection2(
            string name,
            int flags,
            bool force);

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode)]
        private struct NetResource
        {
            public int Scope;
            public int Type;
            public int DisplayType;
            public int Usage;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string LocalName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string RemoteName;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Comment;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Provider;
        }

        private sealed class ConnectionEntry
        {
            public string ServerName { get; set; }

            public string ShareRoot { get; set; }

            public string UserName { get; set; }

            public string CredentialKey { get; set; }

            public bool IsConnected { get; set; }

            public DateTime? ConnectedTime { get; set; }

            public int ActiveLeaseCount;

            public object SyncRoot { get; } = new object();
        }

        public sealed class SmbConnectionSnapshot
        {
            public string ServerName { get; set; }

            public string ShareRoot { get; set; }

            public string UserName { get; set; }

            public bool IsExplicitCredential { get; set; }

            public bool IsConnected { get; set; }

            public DateTime? ConnectedTime { get; set; }

            public int ActiveLeaseCount { get; set; }
        }

        private sealed class CredentialDescriptor
        {
            public bool IsExplicit { get; set; }

            public string DisplayUserName { get; set; }

            public string CredentialKey { get; set; }
        }

        private sealed class UncPathInfo
        {
            public string ServerName { get; set; }

            public string ShareName { get; set; }

            public string ShareRoot { get; set; }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance =
                new EmptyDisposable();

            private EmptyDisposable()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
