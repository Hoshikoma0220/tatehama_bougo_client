using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;

namespace tatehama_bougo_client
{
    /// <summary>
    /// 防護無線サーバーとのSignalR通信クライアント
    /// </summary>
    public class BougoServerClient
    {
        private HubConnection _connection;
        private bool _isConnected = false;
        private string _serverUrl = "http://localhost:5000/bougohub";
        private System.Threading.Timer _heartbeatTimer;
        private readonly object _lock = new object();

        // イベント
        public event Action<bool> OnConnectionChanged;
        public event Action<string> OnBougoFired;
        public event Action<object> OnZoneBougoWarning;
        public event Action<string> OnError;

        // 現在の状態
        private string _currentTrainNumber = "";
        private string _currentZone = "";
        private bool _isBougoActive = false;

        public bool IsConnected => _isConnected;
        public string CurrentTrainNumber => _currentTrainNumber;
        public string CurrentZone => _currentZone;
        public bool IsBougoActive => _isBougoActive;

        /// <summary>
        /// サーバーに接続
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                if (_connection != null)
                {
                    await DisconnectAsync();
                }

                System.Diagnostics.Debug.WriteLine($"🔗 SignalR接続開始: {_serverUrl}");

                _connection = new HubConnectionBuilder()
                    .WithUrl(_serverUrl)
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                    .Build();

                // イベントハンドラーを設定
                SetupEventHandlers();

                // 接続開始
                await _connection.StartAsync();
                
                lock (_lock)
                {
                    _isConnected = true;
                }

                System.Diagnostics.Debug.WriteLine("✅ SignalR接続成功");
                OnConnectionChanged?.Invoke(true);

                // ハートビートタイマー開始
                StartHeartbeat();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR接続エラー: {ex.Message}");
                OnError?.Invoke($"接続エラー: {ex.Message}");
                
                lock (_lock)
                {
                    _isConnected = false;
                }
                OnConnectionChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// サーバーから切断
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;

                if (_connection != null)
                {
                    await _connection.DisposeAsync();
                    _connection = null;
                }

                lock (_lock)
                {
                    _isConnected = false;
                }

                System.Diagnostics.Debug.WriteLine("🔌 SignalR切断完了");
                OnConnectionChanged?.Invoke(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR切断エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 防護無線状態を更新
        /// </summary>
        public async Task UpdateBougoStatusAsync(string trainNumber, string zone, bool isBougoActive)
        {
            if (!_isConnected || _connection == null) 
            {
                System.Diagnostics.Debug.WriteLine("⚠️ SignalR未接続 - 状態更新スキップ");
                return;
            }

            try
            {
                var status = new
                {
                    TrainNumber = trainNumber,
                    CurrentZone = zone,
                    IsBougoActive = isBougoActive,
                    LastUpdated = DateTime.Now,
                    ClientId = _connection.ConnectionId
                };

                // 状態を保存
                _currentTrainNumber = trainNumber;
                _currentZone = zone;
                _isBougoActive = isBougoActive;

                await _connection.InvokeAsync("UpdateBougoStatus", status);
                
                System.Diagnostics.Debug.WriteLine($"📡 防護状態送信: 列車={trainNumber}, ゾーン={zone}, 防護={isBougoActive}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 防護状態更新エラー: {ex.Message}");
                OnError?.Invoke($"状態更新エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ゾーン情報を要求
        /// </summary>
        public async Task RequestZoneInfoAsync(string zoneName)
        {
            if (!_isConnected || _connection == null) return;

            try
            {
                await _connection.InvokeAsync("RequestZoneInfo", zoneName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ゾーン情報要求エラー: {ex.Message}");
                OnError?.Invoke($"ゾーン情報要求エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// システム状態を要求
        /// </summary>
        public async Task RequestSystemStatusAsync()
        {
            if (!_isConnected || _connection == null) return;

            try
            {
                await _connection.InvokeAsync("RequestSystemStatus");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ システム状態要求エラー: {ex.Message}");
                OnError?.Invoke($"システム状態要求エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// イベントハンドラーを設定
        /// </summary>
        private void SetupEventHandlers()
        {
            if (_connection == null) return;

            // 防護無線発砲通知
            _connection.On<object>("BougoFired", (fireInfo) =>
            {
                var json = JsonConvert.SerializeObject(fireInfo, Formatting.Indented);
                System.Diagnostics.Debug.WriteLine($"🚨 防護無線発砲通知受信: {json}");
                OnBougoFired?.Invoke(json);
            });

            // ゾーン内防護無線警告
            _connection.On<object>("ZoneBougoWarning", (warning) =>
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ ゾーン内防護無線警告: {JsonConvert.SerializeObject(warning)}");
                OnZoneBougoWarning?.Invoke(warning);
            });

            // 他クライアントの状態更新通知
            _connection.On<object>("BougoStatusUpdated", (status) =>
            {
                System.Diagnostics.Debug.WriteLine($"📊 他クライアント状態更新: {JsonConvert.SerializeObject(status)}");
            });

            // システム状態更新
            _connection.On<object>("SystemStatusUpdated", (systemStatus) =>
            {
                System.Diagnostics.Debug.WriteLine($"🖥️ システム状態更新: {JsonConvert.SerializeObject(systemStatus)}");
            });

            // ゾーン情報応答
            _connection.On<object>("ZoneInfoResponse", (zoneInfo) =>
            {
                System.Diagnostics.Debug.WriteLine($"📍 ゾーン情報応答: {JsonConvert.SerializeObject(zoneInfo)}");
            });

            // システム状態応答
            _connection.On<object>("SystemStatusResponse", (systemStatus) =>
            {
                System.Diagnostics.Debug.WriteLine($"📊 システム状態応答: {JsonConvert.SerializeObject(systemStatus)}");
            });

            // エラー通知
            _connection.On<string>("Error", (error) =>
            {
                System.Diagnostics.Debug.WriteLine($"❌ サーバーエラー: {error}");
                OnError?.Invoke(error);
            });

            // ハートビート応答
            _connection.On<DateTime>("HeartbeatResponse", (timestamp) =>
            {
                System.Diagnostics.Debug.WriteLine($"💓 ハートビート応答: {timestamp:HH:mm:ss.fff}");
            });

            // 接続状態変更
            _connection.Closed += (error) =>
            {
                lock (_lock)
                {
                    _isConnected = false;
                }
                OnConnectionChanged?.Invoke(false);
                System.Diagnostics.Debug.WriteLine($"🔌 SignalR接続切断: {error?.Message ?? "正常切断"}");
                return Task.CompletedTask;
            };

            _connection.Reconnecting += (error) =>
            {
                System.Diagnostics.Debug.WriteLine($"🔄 SignalR再接続中: {error?.Message}");
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                lock (_lock)
                {
                    _isConnected = true;
                }
                OnConnectionChanged?.Invoke(true);
                System.Diagnostics.Debug.WriteLine($"✅ SignalR再接続完了: {connectionId}");
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// ハートビートタイマーを開始
        /// </summary>
        private void StartHeartbeat()
        {
            _heartbeatTimer = new System.Threading.Timer(async _ =>
            {
                if (_isConnected && _connection != null)
                {
                    try
                    {
                        await _connection.InvokeAsync("Heartbeat");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ ハートビートエラー: {ex.Message}");
                    }
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
    }
}
