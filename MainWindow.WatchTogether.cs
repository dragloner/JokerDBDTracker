using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using JokerDBDTracker.Models;
using JokerDBDTracker.Services;

namespace JokerDBDTracker
{
    public partial class MainWindow
    {
        private WatchTogetherService? _watchTogetherService;
        private bool _isWatchTogetherActive;
        private bool _watchTogetherFirewallAccepted;

        private void RefreshWatchTogetherPanel()
        {
            DetectLocalIpAddresses();
            UpdateWatchTogetherUiState();
        }

        private void InitializeWatchTogether()
        {
            _watchTogetherService = new WatchTogetherService();
            _watchTogetherService.MessageReceived += WatchTogether_MessageReceived;
            _watchTogetherService.PeerConnected += WatchTogether_PeerConnected;
            _watchTogetherService.PeerDisconnected += WatchTogether_PeerDisconnected;
            _watchTogetherService.Error += WatchTogether_Error;
            _watchTogetherService.Stopped += WatchTogether_Stopped;
        }

        private void UpdateWatchTogetherUiState()
        {
            if (WtStatusText is null)
            {
                return;
            }

            var isConnected = _isWatchTogetherActive &&
                              _watchTogetherService is not null &&
                              _watchTogetherService.IsConnected;

            // ── Not connected state ──
            if (!isConnected)
            {
                WtStatusText.Text = T("● Сервер не запущен", "● Server not running");
                WtStatusText.Foreground = BrushFromHex("#F4D89B");
                WtRoleText.Text = T(
                    "Создайте комнату или подключитесь к хосту.",
                    "Create a room or connect to a host.");
                WtPortInfoText.Text = string.Empty;

                WtHostButton.IsEnabled = true;
                WtHostButton.Content = T("Создать комнату (Хост)", "Create room (Host)");
                WtConnectButton.IsEnabled = true;
                WtConnectButton.Content = T("Подключиться (Гость)", "Connect (Guest)");
                WtIpBox.IsEnabled = true;
                WtPortBox.IsEnabled = true;
                WtDisconnectButton.Visibility = Visibility.Collapsed;
                WtHostButton.Visibility = Visibility.Visible;
                WtConnectButton.Visibility = Visibility.Visible;

                WtHostIpPanel.Visibility = Visibility.Collapsed;
                WtPeersPanel.Visibility = Visibility.Collapsed;
                WtGuestInfoPanel.Visibility = Visibility.Collapsed;
                WtInstructionsPanel.Visibility = Visibility.Visible;
                return;
            }

            // ── Connected ──
            WtHostButton.IsEnabled = false;
            WtConnectButton.IsEnabled = false;
            WtIpBox.IsEnabled = false;
            WtPortBox.IsEnabled = false;
            WtDisconnectButton.Visibility = Visibility.Visible;
            WtHostButton.Visibility = Visibility.Collapsed;
            WtConnectButton.Visibility = Visibility.Collapsed;
            WtInstructionsPanel.Visibility = Visibility.Collapsed;

            if (_watchTogetherService!.IsHost)
            {
                // ── Host mode ──
                var peerCount = _watchTogetherService.PeerCount;
                var port = ReadWtPort();

                WtStatusText.Text = T("● Сервер запущен — ожидание гостей", "● Server running — waiting for guests");
                WtStatusText.Foreground = BrushFromHex("#7BC8F4");
                WtRoleText.Text = T("Вы — Хост", "You are the Host");
                WtPortInfoText.Text = T($"Порт: {port}", $"Port: {port}");

                // Show host IP panel.
                WtHostIpPanel.Visibility = Visibility.Visible;
                DetectLocalIpAddresses();
                WtMyIpHintText.Text = T(
                    "Друг должен ввести этот IP в поле «IP адрес хоста» и нажать «Подключиться».\n" +
                    $"Если не подключается — разрешите порт {port} в брандмауэре Windows.",
                    "Your friend should enter this IP in the \"Host IP address\" field and click \"Connect\".\n" +
                    $"If connection fails — allow port {port} in Windows Firewall.");

                // Show peers panel.
                WtPeersPanel.Visibility = Visibility.Visible;
                WtGuestInfoPanel.Visibility = Visibility.Collapsed;

                if (peerCount == 0)
                {
                    WtPeersCountText.Text = T("Пока никто не подключился", "No one connected yet");
                    WtPeersListText.Text = T("—  ожидание...", "—  waiting...");
                }
                else
                {
                    WtPeersCountText.Text = T(
                        $"Подключено: {peerCount} {GetPeerWord(peerCount)}",
                        $"Connected: {peerCount} {(peerCount == 1 ? "guest" : "guests")}");

                    var endpoints = _watchTogetherService.GetPeerEndpoints();
                    var lines = new List<string>();
                    for (var i = 0; i < endpoints.Count; i++)
                    {
                        lines.Add($"  {i + 1}. {endpoints[i]}");
                    }

                    WtPeersListText.Text = string.Join("\n", lines);
                }

                // Update main status to reflect peer count.
                if (peerCount > 0)
                {
                    WtStatusText.Text = T(
                        $"● Сервер запущен — {peerCount} {GetPeerWord(peerCount)} онлайн",
                        $"● Server running — {peerCount} {(peerCount == 1 ? "guest" : "guests")} online");
                    WtStatusText.Foreground = BrushFromHex("#7BF4A3");
                }
            }
            else
            {
                // ── Guest mode ──
                WtStatusText.Text = T("● Подключено к хосту", "● Connected to host");
                WtStatusText.Foreground = BrushFromHex("#7BF4A3");
                WtRoleText.Text = T("Вы — Гость", "You are a Guest");
                WtPortInfoText.Text = string.Empty;

                WtHostIpPanel.Visibility = Visibility.Collapsed;
                WtPeersPanel.Visibility = Visibility.Collapsed;
                WtGuestInfoPanel.Visibility = Visibility.Visible;

                var hostAddr = WtIpBox.Text.Trim();
                WtGuestHostAddrText.Text = T(
                    $"Хост: {hostAddr}:{ReadWtPort()}",
                    $"Host: {hostAddr}:{ReadWtPort()}");
                WtGuestSyncHintText.Text = T(
                    "Ожидайте — хост откроет видео и управление будет синхронизировано.",
                    "Wait — the host will open a video and controls will be synced.");
            }
        }

        private static string GetPeerWord(int count)
        {
            var mod10 = count % 10;
            var mod100 = count % 100;
            if (mod10 == 1 && mod100 != 11)
            {
                return "гость";
            }

            if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            {
                return "гостя";
            }

            return "гостей";
        }

        private int ReadWtPort()
        {
            if (int.TryParse(WtPortBox.Text.Trim(), out var port) && port is >= 1 and <= 65535)
            {
                return port;
            }

            return WatchTogetherService.DefaultPort;
        }

        private async void WtHostButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show progress.
                WtHostButton.IsEnabled = false;
                WtConnectButton.IsEnabled = false;
                WtHostButton.Content = T("Запуск сервера...", "Starting server...");
                WtStatusText.Text = T("● Запуск сервера...", "● Starting server...");
                WtStatusText.Foreground = BrushFromHex("#E2C17A");
                WtRoleText.Text = T("Инициализация TCP сервера...", "Initializing TCP server...");
                WtPortInfoText.Text = string.Empty;

                if (_watchTogetherService is null)
                {
                    InitializeWatchTogether();
                }

                var port = ReadWtPort();

                // Small delay so user sees the progress state.
                await Task.Delay(150);

                _watchTogetherService!.StartHost(port);
                _isWatchTogetherActive = true;

                // Brief confirmation flash.
                WtStatusText.Text = T("● Сервер успешно запущен!", "● Server started successfully!");
                WtStatusText.Foreground = BrushFromHex("#7BF4A3");
                WtRoleText.Text = T("Привязка к порту завершена.", "Port binding complete.");
                await Task.Delay(400);

                UpdateWatchTogetherUiState();
            }
            catch (Exception ex)
            {
                WtHostButton.IsEnabled = true;
                WtConnectButton.IsEnabled = true;
                WtHostButton.Content = T("Создать комнату (Хост)", "Create room (Host)");
                WtStatusText.Text = T("● Ошибка запуска сервера", "● Server start failed");
                WtStatusText.Foreground = BrushFromHex("#F47B7B");
                WtRoleText.Text = ex.Message;
                WtPortInfoText.Text = string.Empty;

                MessageBox.Show(
                    $"{T("Не удалось создать комнату:", "Failed to create room:")}\n{ex.Message}",
                    T("Watch Together", "Watch Together"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void WtConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var ip = WtIpBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show(
                    T("Введите IP адрес хоста.", "Enter the host IP address."),
                    T("Watch Together", "Watch Together"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                if (_watchTogetherService is null)
                {
                    InitializeWatchTogether();
                }

                var port = ReadWtPort();

                // Show progress.
                WtConnectButton.IsEnabled = false;
                WtHostButton.IsEnabled = false;
                WtConnectButton.Content = T("Подключение...", "Connecting...");
                WtStatusText.Text = T($"● Подключение к {ip}:{port}...", $"● Connecting to {ip}:{port}...");
                WtStatusText.Foreground = BrushFromHex("#E2C17A");
                WtRoleText.Text = T("Устанавливается TCP соединение...", "Establishing TCP connection...");
                WtPortInfoText.Text = string.Empty;

                await _watchTogetherService!.ConnectAsync(ip, port);
                _isWatchTogetherActive = true;

                // Brief success flash.
                WtStatusText.Text = T("● Успешно подключено!", "● Connected successfully!");
                WtStatusText.Foreground = BrushFromHex("#7BF4A3");
                await Task.Delay(400);

                UpdateWatchTogetherUiState();
            }
            catch (Exception ex)
            {
                WtConnectButton.IsEnabled = true;
                WtHostButton.IsEnabled = true;
                WtConnectButton.Content = T("Подключиться (Гость)", "Connect (Guest)");
                WtStatusText.Text = T("● Ошибка подключения", "● Connection failed");
                WtStatusText.Foreground = BrushFromHex("#F47B7B");
                WtRoleText.Text = ex.Message;
                WtPortInfoText.Text = string.Empty;

                MessageBox.Show(
                    $"{T("Не удалось подключиться:", "Failed to connect:")}\n{ex.Message}\n\n" +
                    T("Убедитесь, что хост разрешил порт в брандмауэре Windows и Radmin VPN запущен у обоих.",
                      "Make sure the host has allowed the port in Windows Firewall and Radmin VPN is running on both sides."),
                    T("Watch Together", "Watch Together"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void WtDisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            _watchTogetherService?.Stop();
            _isWatchTogetherActive = false;
            UpdateWatchTogetherUiState();
        }

        // ── Events from WatchTogetherService ──

        private void WatchTogether_MessageReceived(WatchTogetherMessage message)
        {
            switch (message.Type)
            {
                case "open_video":
                    HandleWtOpenVideo(message);
                    break;
            }
        }

        private void HandleWtOpenVideo(WatchTogetherMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.VideoId))
            {
                return;
            }

            var video = _allVideos.FirstOrDefault(v =>
                string.Equals(v.VideoId, message.VideoId, StringComparison.OrdinalIgnoreCase));
            if (video is null)
            {
                return;
            }

            _ = OpenVideoAsync(video);
        }

        private void WatchTogether_PeerConnected(string endpoint)
        {
            UpdateWatchTogetherUiState();
        }

        private void WatchTogether_PeerDisconnected(string endpoint)
        {
            UpdateWatchTogetherUiState();
        }

        private void WatchTogether_Error(string errorMessage)
        {
            WtStatusText.Text = T($"● Ошибка: {errorMessage}", $"● Error: {errorMessage}");
            WtStatusText.Foreground = BrushFromHex("#F47B7B");
        }

        private void WatchTogether_Stopped()
        {
            _isWatchTogetherActive = false;
            UpdateWatchTogetherUiState();
        }

        // ── Helpers ──

        /// <summary>
        /// Detects local IP addresses for display, helping the user share their IP with friends.
        /// Prioritizes Radmin VPN addresses (26.x.x.x range).
        /// </summary>
        private void DetectLocalIpAddresses()
        {
            if (WtMyIpText is null)
            {
                return;
            }

            try
            {
                var addresses = new List<string>();
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (iface.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    foreach (var addr in iface.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }

                        var ip = addr.Address.ToString();
                        if (ip.StartsWith("127.", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Radmin VPN uses 26.x.x.x range — show it first.
                        var label = ip.StartsWith("26.", StringComparison.Ordinal)
                            ? $"{ip}  (Radmin VPN)"
                            : ip;
                        addresses.Insert(
                            ip.StartsWith("26.", StringComparison.Ordinal) ? 0 : addresses.Count,
                            label);
                    }
                }

                WtMyIpText.Text = addresses.Count > 0
                    ? string.Join("\n", addresses)
                    : T("Не найдено сетевых адресов", "No network addresses found");
            }
            catch
            {
                WtMyIpText.Text = T("Не удалось определить IP", "Failed to detect IP");
            }
        }

        /// <summary>
        /// Sends an "open_video" command to all Watch Together peers when the host opens a video.
        /// </summary>
        private void NotifyWatchTogetherVideoOpened(YouTubeVideo video)
        {
            if (_watchTogetherService is null || !_watchTogetherService.IsConnected)
            {
                return;
            }

            _watchTogetherService.Send(new WatchTogetherMessage
            {
                Type = "open_video",
                VideoId = video.VideoId,
                VideoTitle = video.Title
            });
        }

        /// <summary>
        /// Gets the current Watch Together service for the PlayerWindow to use for sync.
        /// </summary>
        internal WatchTogetherService? GetWatchTogetherService()
        {
            return _isWatchTogetherActive ? _watchTogetherService : null;
        }

        /// <summary>
        /// Loads the firewall consent flag from saved settings.
        /// Called during MainWindow initialization.
        /// </summary>
        internal void LoadWatchTogetherFirewallConsent(bool accepted)
        {
            _watchTogetherFirewallAccepted = accepted;
        }

        /// <summary>
        /// Shows a first-time dialog explaining Watch Together network requirements.
        /// Returns true if user accepted, false if declined.
        /// </summary>
        private async Task<bool> ShowWatchTogetherFirstTimeDialogAsync()
        {
            var title = T("Watch Together — Настройка сети", "Watch Together — Network Setup");
            var message = T(
                "Функция Watch Together позволяет смотреть стримы вместе с друзьями через локальную сеть (Radmin VPN).\n\n" +
                "Для работы необходимо:\n" +
                "• Открыть TCP порт в брандмауэре Windows (по умолчанию 7777)\n" +
                "• Запустить Radmin VPN на обоих компьютерах\n\n" +
                "Что произойдёт:\n" +
                "• Программа автоматически создаст правило в брандмауэре Windows для порта 7777\n" +
                "• Может появиться запрос на повышение прав (UAC) — это нормально\n" +
                "• Правило разрешает входящие TCP-подключения только в частных сетях\n\n" +
                "Это безопасно — порт используется только для синхронизации воспроизведения между участниками.\n" +
                "Данные не отправляются в интернет.\n\n" +
                "Продолжить?",

                "Watch Together lets you watch streams with friends over a local network (Radmin VPN).\n\n" +
                "Requirements:\n" +
                "• Open a TCP port in Windows Firewall (default 7777)\n" +
                "• Run Radmin VPN on both computers\n\n" +
                "What will happen:\n" +
                "• The app will automatically create a Windows Firewall rule for port 7777\n" +
                "• A UAC elevation prompt may appear — this is normal\n" +
                "• The rule allows inbound TCP connections on private networks only\n\n" +
                "This is safe — the port is used only for playback sync between participants.\n" +
                "No data is sent to the internet.\n\n" +
                "Continue?");

            var result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                return false;
            }

            // Try to create the firewall rule automatically.
            var port = ReadWtPort();
            var firewallOk = await FirewallService.EnsureRuleAsync(port);

            if (firewallOk)
            {
                MessageBox.Show(
                    T($"Правило брандмауэра для порта {port} успешно создано!",
                      $"Firewall rule for port {port} created successfully!"),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var psCommand = $"New-NetFirewallRule -DisplayName \"JokerDBDTracker Watch Together\" -Direction Inbound -Protocol TCP -LocalPort {port} -Action Allow";

                // Auto-copy to clipboard so user can paste immediately.
                try { System.Windows.Clipboard.SetText(psCommand); } catch { /* Ignore clipboard errors */ }

                MessageBox.Show(
                    T($"Не удалось автоматически создать правило брандмауэра.\n\n" +
                      "Возможные причины:\n" +
                      "• Нет прав администратора (UAC отклонён)\n" +
                      "• Антивирус или сторонний файрвол блокирует изменения\n\n" +
                      "Что сделать:\n" +
                      "1. Откройте PowerShell от имени администратора\n" +
                      "   (Win+X → «Windows PowerShell (администратор)»)\n" +
                      "2. Вставьте и выполните команду (уже скопирована в буфер обмена):\n\n" +
                      $"{psCommand}\n\n" +
                      "Watch Together всё равно доступен — просто подключение с другого ПК может не работать без этого правила.",

                      $"Could not create firewall rule automatically.\n\n" +
                      "Possible reasons:\n" +
                      "• No admin rights (UAC was declined)\n" +
                      "• Antivirus or third-party firewall blocks changes\n\n" +
                      "What to do:\n" +
                      "1. Open PowerShell as Administrator\n" +
                      "   (Win+X → \"Windows PowerShell (Administrator)\")\n" +
                      "2. Paste and run the command (already copied to clipboard):\n\n" +
                      $"{psCommand}\n\n" +
                      "Watch Together is still available — connections from other PCs may just not work without this rule."),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Save consent regardless of firewall result.
            _watchTogetherFirewallAccepted = true;

            try
            {
                var settings = await _settingsService.LoadAsync();
                settings.WatchTogetherFirewallAccepted = true;
                await _settingsService.SaveAsync(settings);
            }
            catch
            {
                // Best-effort save.
            }

            return true;
        }

        private void CleanupWatchTogether()
        {
            _watchTogetherService?.Dispose();
            _watchTogetherService = null;
        }
    }
}
