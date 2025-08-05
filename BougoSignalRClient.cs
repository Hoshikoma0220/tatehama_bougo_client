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
        private readonly string _serverUrl = "http://localhost:5233/bougohub"; // ポート番号を5233に修正

        // イベント
        public event Action<string, string> OnBougoFired;  // 他列車の発報通知
        public event Action<string, string> OnBougoStopped; // 他列車の停止通知
        public event Action<bool> OnConnectionChanged; // 接続状態変更
        public event Action<string> OnError; // エラー通知

        /// <summary>
        /// サーバーに接続
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(_serverUrl, options =>
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
                
                System.Diagnostics.Debug.WriteLine($"🔗 防護無線SignalR接続成功: {_serverUrl}");
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnConnectionChanged?.Invoke(false);
                OnError?.Invoke($"接続エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ SignalR接続エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   サーバーURL: {_serverUrl}");
                System.Diagnostics.Debug.WriteLine($"   詳細: {ex.InnerException?.Message}");
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
                
                // 5秒後に再接続を試行
                await Task.Delay(5000);
                try 
                {
                    if (_connection.State == HubConnectionState.Disconnected)
                    {
                        System.Diagnostics.Debug.WriteLine("🔄 SignalR再接続を試行中...");
                        await _connection.StartAsync();
                        _isConnected = true;
                        OnConnectionChanged?.Invoke(true);
                        System.Diagnostics.Debug.WriteLine("🔗 SignalR再接続成功");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ SignalR再接続失敗: {ex.Message}");
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
