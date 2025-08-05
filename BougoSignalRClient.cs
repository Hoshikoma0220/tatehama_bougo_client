using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;

namespace tatehama_bougo_client
{
    /// <summary>
    /// 防護無線SignalRクライアント（シンプル版）
    /// </summary>
    public class BougoSignalRClient
    {
        private HubConnection _connection;
        private bool _isConnected = false;
        private string _serverUrl = "http://tatehama.turara.me:5233/bougohub"; // プライマリサーバー
        private readonly string _fallbackServerUrl = "http://192.168.10.101:5233/bougohub"; // フォールバックサーバー
        private bool _autoReconnectEnabled = false; // 自動再接続フラグ
        private readonly int _reconnectDelayMs = 5000; // 再接続間隔（5秒）
        private bool _useFallbackServer = false; // フォールバックサーバーを使用中かどうか

        // イベント
        public event Action<string, string> OnBougoFired;  // 他列車の発報通知
        public event Action<string, string> OnBougoStopped; // 他列車の停止通知
        public event Action<bool> OnConnectionChanged; // 接続状態変更
        public event Action<string> OnError; // エラー通知

        /// <summary>
        /// サーバーに接続（自動再接続オプション付き）
        /// </summary>
        /// <param name="enableAutoReconnect">自動再接続を有効にするか</param>
        public async Task ConnectAsync(bool enableAutoReconnect = false)
        {
            _autoReconnectEnabled = enableAutoReconnect;
            
            // まずプライマリサーバーを試行
            string currentServerUrl = _useFallbackServer ? _fallbackServerUrl : _serverUrl;
            
            try
            {
                await TryConnectToServer(currentServerUrl);
                System.Diagnostics.Debug.WriteLine($"🔗 防護無線SignalR接続成功: {currentServerUrl} (自動再接続: {enableAutoReconnect})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR接続エラー ({currentServerUrl}): {ex.Message}");
                
                // プライマリサーバーが失敗した場合、フォールバックサーバーを試行
                if (!_useFallbackServer)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 フォールバックサーバーに切り替え: {_fallbackServerUrl}");
                    _useFallbackServer = true;
                    
                    try
                    {
                        await TryConnectToServer(_fallbackServerUrl);
                        System.Diagnostics.Debug.WriteLine($"🔗 フォールバックサーバー接続成功: {_fallbackServerUrl}");
                    }
                    catch (Exception fallbackEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ フォールバックサーバー接続エラー: {fallbackEx.Message}");
                        await HandleConnectionFailure(enableAutoReconnect, fallbackEx);
                    }
                }
                else
                {
                    // フォールバックサーバーも失敗した場合、プライマリサーバーに戻してリトライ
                    System.Diagnostics.Debug.WriteLine($"🔄 プライマリサーバーに戻してリトライ: {_serverUrl}");
                    _useFallbackServer = false;
                    await HandleConnectionFailure(enableAutoReconnect, ex);
                }
            }
        }

        /// <summary>
        /// 指定されたサーバーURLに接続を試行
        /// </summary>
        private async Task TryConnectToServer(string serverUrl)
        {
            _connection = new HubConnectionBuilder()
                .WithUrl(serverUrl, options =>
                {
                    // HTTPSでない場合のセキュリティ設定を緩和
                    options.HttpMessageHandlerFactory = handler =>
                    {
                        if (handler is HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
                        }
                        return handler;
                    };
                })
                .WithAutomaticReconnect() // 自動再接続を追加
                .Build();

            // イベントハンドラー設定
            SetupEventHandlers();

            // 接続開始
            await _connection.StartAsync();
            _isConnected = true;
            OnConnectionChanged?.Invoke(true);
        }

        /// <summary>
        /// 接続失敗時の処理
        /// </summary>
        private async Task HandleConnectionFailure(bool enableAutoReconnect, Exception ex)
        {
            _isConnected = false;
            OnConnectionChanged?.Invoke(false);
            OnError?.Invoke($"接続エラー: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"   詳細: {ex.InnerException?.Message}");
            
            // 自動再接続が有効で初回接続に失敗した場合、再接続を試行
            if (enableAutoReconnect)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 {_reconnectDelayMs}ms後に再接続を試行します...");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(_reconnectDelayMs);
                    await TryReconnectAsync();
                });
            }
        }

        /// <summary>
        /// 防護無線発砲をサーバーに通知
        /// </summary>
        public async Task FireBougoAsync(string trainNumber, string zone)
        {
            if (!_isConnected || _connection == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ SignalR未接続 - 発報通知スキップ");
                return;
            }

            try
            {
                await _connection.InvokeAsync("FireBougo", new { 
                    TrainNumber = trainNumber, 
                    Zone = zone 
                });
                System.Diagnostics.Debug.WriteLine($"🚨 発報通知送信: {trainNumber} @ {zone}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"発報通知エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 発報通知エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 防護無線停止をサーバーに通知
        /// </summary>
        public async Task StopBougoAsync(string trainNumber, string zone)
        {
            if (!_isConnected || _connection == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ SignalR未接続 - 停止通知スキップ");
                return;
            }

            try
            {
                await _connection.InvokeAsync("StopBougo", new { 
                    TrainNumber = trainNumber, 
                    Zone = zone 
                });
                System.Diagnostics.Debug.WriteLine($"🔴 停止通知送信: {trainNumber} @ {zone}");
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"停止通知エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 停止通知エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// サーバーから切断
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (_connection != null)
                {
                    await _connection.DisposeAsync();
                    _connection = null;
                }
                _isConnected = false;
                OnConnectionChanged?.Invoke(false);
                System.Diagnostics.Debug.WriteLine("🔌 SignalR切断完了");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR切断エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 接続状態を取得
        /// </summary>
        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        /// <summary>
        /// サーバーURLを設定
        /// </summary>
        /// <param name="serverAddress">サーバーアドレス（例: "192.168.1.100:5233" または "localhost:5233"）</param>
        public void SetServerUrl(string serverAddress)
        {
            if (string.IsNullOrWhiteSpace(serverAddress))
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 空のサーバーアドレスが指定されました");
                return;
            }

            // プロトコルが指定されていない場合はhttpを追加
            if (!serverAddress.StartsWith("http://") && !serverAddress.StartsWith("https://"))
            {
                serverAddress = "http://" + serverAddress;
            }

            // /bougohubが付いていない場合は追加
            if (!serverAddress.EndsWith("/bougohub"))
            {
                if (!serverAddress.EndsWith("/"))
                {
                    serverAddress += "/bougohub";
                }
                else
                {
                    serverAddress += "bougohub";
                }
            }

            _serverUrl = serverAddress;
            System.Diagnostics.Debug.WriteLine($"🔧 サーバーURL設定: {_serverUrl}");
        }

        /// <summary>
        /// 現在のサーバーURLを取得
        /// </summary>
        public string GetServerUrl()
        {
            return _useFallbackServer ? _fallbackServerUrl : _serverUrl;
        }

        /// <summary>
        /// 現在使用中のサーバー情報を取得
        /// </summary>
        public string GetCurrentServerInfo()
        {
            string currentUrl = _useFallbackServer ? _fallbackServerUrl : _serverUrl;
            string serverType = _useFallbackServer ? "フォールバック" : "プライマリ";
            return $"{serverType}サーバー: {currentUrl}";
        }

        /// <summary>
        /// 自動再接続を有効/無効にする
        /// </summary>
        /// <param name="enabled">有効にするか</param>
        public void SetAutoReconnect(bool enabled)
        {
            _autoReconnectEnabled = enabled;
            System.Diagnostics.Debug.WriteLine($"🔧 自動再接続設定: {enabled}");
        }

        /// <summary>
        /// 再接続を試行する（内部メソッド）
        /// </summary>
        private async Task TryReconnectAsync()
        {
            if (!_autoReconnectEnabled)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 自動再接続が無効のため、再接続をスキップ");
                return;
            }

            try
            {
                string currentUrl = _useFallbackServer ? _fallbackServerUrl : _serverUrl;
                System.Diagnostics.Debug.WriteLine($"🔄 SignalR再接続を試行中... ({currentUrl})");
                
                // 既存の接続があれば破棄
                if (_connection != null)
                {
                    try
                    {
                        await _connection.DisposeAsync();
                    }
                    catch
                    {
                        // 既に破棄されている場合は無視
                    }
                    _connection = null;
                }

                // 新しい接続を作成して接続試行
                await ConnectAsync(_autoReconnectEnabled);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR再接続失敗: {ex.Message}");
                
                // 再接続に失敗した場合、さらに再試行
                if (_autoReconnectEnabled)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 {_reconnectDelayMs}ms後に再度再接続を試行します...");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(_reconnectDelayMs);
                        await TryReconnectAsync();
                    });
                }
            }
        }

        /// <summary>
        /// イベントハンドラーを設定
        /// </summary>
        private void SetupEventHandlers()
        {
            if (_connection == null) return;

            System.Diagnostics.Debug.WriteLine("🔧 SignalRイベントハンドラー設定開始");

            // 防護無線発砲通知
            _connection.On<object>("BougoFired", (fireInfo) =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🚨 SignalR受信: BougoFired イベント");
                    System.Diagnostics.Debug.WriteLine($"   受信データタイプ: {fireInfo?.GetType()?.Name ?? "null"}");
                    System.Diagnostics.Debug.WriteLine($"   受信データ: {System.Text.Json.JsonSerializer.Serialize(fireInfo)}");
                    
                    if (fireInfo is System.Text.Json.JsonElement jsonElement)
                    {
                        // 小文字のプロパティ名でアクセス（サーバーから送信される実際の形式）
                        string trainNumber = jsonElement.TryGetProperty("trainNumber", out var tnProp) ? tnProp.GetString() ?? "" : "";
                        string zone = jsonElement.TryGetProperty("zone", out var zoneProp) ? zoneProp.GetString() ?? "" : "";
                        
                        System.Diagnostics.Debug.WriteLine($"🚨 他列車発報受信: {trainNumber} @ {zone}");
                        System.Diagnostics.Debug.WriteLine($"   OnBougoFiredイベント実行中...");
                        
                        OnBougoFired?.Invoke(trainNumber, zone);
                        
                        System.Diagnostics.Debug.WriteLine($"   OnBougoFiredイベント完了");
                    }
                    else
                    {
                        // dynamic fallback（小文字プロパティ名も試行）
                        dynamic info = fireInfo;
                        string trainNumber = info.trainNumber?.ToString() ?? info.TrainNumber?.ToString() ?? "";
                        string zone = info.zone?.ToString() ?? info.Zone?.ToString() ?? "";
                        
                        System.Diagnostics.Debug.WriteLine($"🚨 他列車発報受信（dynamic）: {trainNumber} @ {zone}");
                        System.Diagnostics.Debug.WriteLine($"   OnBougoFiredイベント実行中...");
                        
                        OnBougoFired?.Invoke(trainNumber, zone);
                        
                        System.Diagnostics.Debug.WriteLine($"   OnBougoFiredイベント完了");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 発報通知処理エラー: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"   スタックトレース: {ex.StackTrace}");
                }
            });

            // 防護無線停止通知
            _connection.On<object>("BougoStopped", (stopInfo) =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔴 SignalR受信: BougoStopped イベント");
                    System.Diagnostics.Debug.WriteLine($"   受信データタイプ: {stopInfo?.GetType()?.Name ?? "null"}");
                    System.Diagnostics.Debug.WriteLine($"   受信データ: {System.Text.Json.JsonSerializer.Serialize(stopInfo)}");
                    
                    if (stopInfo is System.Text.Json.JsonElement jsonElement)
                    {
                        // 小文字のプロパティ名でアクセス（サーバーから送信される実際の形式）
                        string trainNumber = jsonElement.TryGetProperty("trainNumber", out var tnProp) ? tnProp.GetString() ?? "" : "";
                        string zone = jsonElement.TryGetProperty("zone", out var zoneProp) ? zoneProp.GetString() ?? "" : "";
                        
                        System.Diagnostics.Debug.WriteLine($"🔴 他列車停止受信: {trainNumber} @ {zone}");
                        System.Diagnostics.Debug.WriteLine($"   OnBougoStoppedイベント実行中...");
                        
                        OnBougoStopped?.Invoke(trainNumber, zone);
                        
                        System.Diagnostics.Debug.WriteLine($"   OnBougoStoppedイベント完了");
                    }
                    else
                    {
                        // dynamic fallback（小文字プロパティ名も試行）
                        dynamic info = stopInfo;
                        string trainNumber = info.trainNumber?.ToString() ?? info.TrainNumber?.ToString() ?? "";
                        string zone = info.zone?.ToString() ?? info.Zone?.ToString() ?? "";
                        
                        System.Diagnostics.Debug.WriteLine($"🔴 他列車停止受信（dynamic）: {trainNumber} @ {zone}");
                        System.Diagnostics.Debug.WriteLine($"   OnBougoStoppedイベント実行中...");
                        
                        OnBougoStopped?.Invoke(trainNumber, zone);
                        
                        System.Diagnostics.Debug.WriteLine($"   OnBougoStoppedイベント完了");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 停止通知処理エラー: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"   スタックトレース: {ex.StackTrace}");
                }
            });

            // 接続切断
            _connection.Closed += async (error) =>
            {
                _isConnected = false;
                OnConnectionChanged?.Invoke(false);
                System.Diagnostics.Debug.WriteLine($"🔌 SignalR接続切断: {error?.Message ?? "正常切断"}");
                
                // 自動再接続が有効な場合のみ再接続を試行
                if (_autoReconnectEnabled)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 {_reconnectDelayMs}ms後に再接続を試行します...");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(_reconnectDelayMs);
                        await TryReconnectAsync();
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 自動再接続が無効のため、再接続をスキップ");
                }
            };
            
            // 再接続中
            _connection.Reconnecting += (error) =>
            {
                System.Diagnostics.Debug.WriteLine($"🔄 SignalR再接続中: {error?.Message ?? "再接続試行"}");
                return Task.CompletedTask;
            };
            
            // 再接続成功
            _connection.Reconnected += (connectionId) =>
            {
                _isConnected = true;
                OnConnectionChanged?.Invoke(true);
                System.Diagnostics.Debug.WriteLine($"🔗 SignalR再接続完了: {connectionId}");  
                return Task.CompletedTask;
            };
            
            System.Diagnostics.Debug.WriteLine("✅ SignalRイベントハンドラー設定完了");
        }
    }
}
