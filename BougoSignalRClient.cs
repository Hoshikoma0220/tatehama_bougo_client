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
        private readonly string _serverUrl = "http://localhost:5000/bougohub";

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
                    .WithUrl(_serverUrl)
                    .Build();

                // イベントハンドラー設定
                SetupEventHandlers();

                // 接続開始
                await _connection.StartAsync();
                _isConnected = true;
                OnConnectionChanged?.Invoke(true);
                
                System.Diagnostics.Debug.WriteLine("🔗 防護無線SignalR接続成功");
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnConnectionChanged?.Invoke(false);
                OnError?.Invoke($"接続エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ SignalR接続エラー: {ex.Message}");
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
        public bool IsConnected => _isConnected;

        /// <summary>
        /// イベントハンドラーを設定
        /// </summary>
        private void SetupEventHandlers()
        {
            if (_connection == null) return;

            // 防護無線発砲通知
            _connection.On<object>("BougoFired", (fireInfo) =>
            {
                try
                {
                    dynamic info = fireInfo;
                    string trainNumber = info.TrainNumber?.ToString() ?? "";
                    string zone = info.Zone?.ToString() ?? "";
                    
                    System.Diagnostics.Debug.WriteLine($"🚨 他列車発報受信: {trainNumber} @ {zone}");
                    OnBougoFired?.Invoke(trainNumber, zone);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 発報通知処理エラー: {ex.Message}");
                }
            });

            // 防護無線停止通知
            _connection.On<object>("BougoStopped", (stopInfo) =>
            {
                try
                {
                    dynamic info = stopInfo;
                    string trainNumber = info.TrainNumber?.ToString() ?? "";
                    string zone = info.Zone?.ToString() ?? "";
                    
                    System.Diagnostics.Debug.WriteLine($"🔴 他列車停止受信: {trainNumber} @ {zone}");
                    OnBougoStopped?.Invoke(trainNumber, zone);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 停止通知処理エラー: {ex.Message}");
                }
            });

            // 接続切断
            _connection.Closed += (error) =>
            {
                _isConnected = false;
                OnConnectionChanged?.Invoke(false);
                System.Diagnostics.Debug.WriteLine($"🔌 SignalR接続切断: {error?.Message ?? "正常切断"}");
                return Task.CompletedTask;
            };
        }
    }
}
