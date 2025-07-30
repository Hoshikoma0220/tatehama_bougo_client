using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WebSocketSharp;
using Newtonsoft.Json;
using tatehama_bougo_client;

namespace tatehama_bougo_client
{
    public class TrainCrewWebSocketClient
    {
        private WebSocket webSocket;
        private Dictionary<string, string> zoneMappings;
        private bool isConnected = false;
        private bool shouldReconnect = true;
        private System.Threading.Timer reconnectTimer;
        private System.Threading.Timer dataRequestTimer; // 定期データ要求用
        private System.Threading.Timer connectionCheckTimer; // 接続状態チェック用
        private DateTime lastDataReceived = DateTime.MinValue;
        
        public event Action<TrainCrewAPI.TrainCrewStateData> OnDataReceived;
        public event Action<string> OnConnectionStatusChanged;

        public TrainCrewWebSocketClient()
        {
            LoadZoneMappings();
        }

        private void LoadZoneMappings()
        {
            zoneMappings = new Dictionary<string, string>();
            try
            {
                string csvPath = "ZoneMapping.csv";
                if (File.Exists(csvPath))
                {
                    var lines = File.ReadAllLines(csvPath);
                    foreach (var line in lines.Skip(1)) // ヘッダーをスキップ
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            zoneMappings[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                    System.Diagnostics.Debug.WriteLine($"ゾーンマッピング読み込み完了: {zoneMappings.Count}件");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ゾーンマッピング読み込みエラー: {ex.Message}");
            }
        }

        public void Connect()
        {
            try
            {
                // 既存の接続をクリーンアップ
                if (webSocket != null)
                {
                    try
                    {
                        webSocket.Close();
                    }
                    catch { }
                    webSocket = null;
                }

                string serverAddress = "ws://127.0.0.1:50300/";
                System.Diagnostics.Debug.WriteLine($"WebSocket接続を開始します: {serverAddress}");
                OnConnectionStatusChanged?.Invoke("接続試行中...");
                
                webSocket = new WebSocket(serverAddress);

                webSocket.OnOpen += (sender, e) =>
                {
                    isConnected = true;
                    lastDataReceived = DateTime.Now;
                    System.Diagnostics.Debug.WriteLine("TrainCrewに接続しました");
                    OnConnectionStatusChanged?.Invoke("接続完了 - データ要求送信中");
                    
                    // 接続後すぐにデータ要求を送信
                    RequestAllData();
                    
                    // 定期的なデータ要求タイマーを開始 (3秒間隔に短縮)
                    dataRequestTimer?.Dispose();
                    dataRequestTimer = new System.Threading.Timer(
                        _ => RequestAllData(), 
                        null, 
                        TimeSpan.FromSeconds(3), 
                        TimeSpan.FromSeconds(3)
                    );

                    // 接続状態チェックタイマーを開始 (10秒間隔)
                    connectionCheckTimer?.Dispose();
                    connectionCheckTimer = new System.Threading.Timer(
                        _ => CheckConnectionHealth(),
                        null,
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromSeconds(10)
                    );
                };

                webSocket.OnMessage += (sender, e) =>
                {
                    try
                    {
                        lastDataReceived = DateTime.Now; // データ受信時刻を更新
                        
                        System.Diagnostics.Debug.WriteLine($"=== 生データ受信 ===");
                        System.Diagnostics.Debug.WriteLine($"データサイズ: {e.Data.Length}文字");
                        System.Diagnostics.Debug.WriteLine($"データ形式: {(e.Data.TrimStart().StartsWith("{") ? "JSON" : "TEXT")}");
                        
                        // まず生データをそのまま表示用に保存
                        string rawData = e.Data;
                        
                        // 接続状態を更新
                        OnConnectionStatusChanged?.Invoke($"接続中 - 最終受信: {DateTime.Now:HH:mm:ss}");
                        
                        // JSONかどうかチェック
                        if (rawData.TrimStart().StartsWith("{"))
                        {
                            try
                            {
                                // まずTrainCrewState（wrapper）として解析を試行
                                var wrapperState = JsonConvert.DeserializeObject<TrainCrewAPI.TrainCrewState>(rawData);
                                if (wrapperState?.data != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"✅ Wrapper JSON解析成功 - 列車: {wrapperState.data.myTrainData?.diaName ?? "N/A"}");
                                    OnDataReceived?.Invoke(wrapperState.data);
                                    return;
                                }

                                // 次にTrainCrewStateData（直接）として解析を試行
                                var stateData = JsonConvert.DeserializeObject<TrainCrewAPI.TrainCrewStateData>(rawData);
                                if (stateData != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"✅ Direct JSON解析成功 - 列車: {stateData.myTrainData?.diaName ?? "N/A"}");
                                    OnDataReceived?.Invoke(stateData);
                                    return;
                                }
                            }
                            catch (JsonException jsonEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ JSON解析エラー: {jsonEx.Message}");
                                
                                // 解析エラーでも、生データとして処理を試行
                                var fallbackData = new TrainCrewAPI.TrainCrewStateData
                                {
                                    myTrainData = new TrainCrewAPI.TrainState { diaName = "JSON解析エラー" }
                                };
                                OnDataReceived?.Invoke(fallbackData);
                                
                                // エラー詳細をデバッグ出力
                                System.Diagnostics.Debug.WriteLine($"問題のあるJSON: {rawData.Substring(0, Math.Min(rawData.Length, 500))}...");
                                return;
                            }
                        }
                        
                        // JSONでない場合もテキストとして処理
                        System.Diagnostics.Debug.WriteLine($"📝 テキストデータ受信: {rawData}");
                        var textData = new TrainCrewAPI.TrainCrewStateData
                        {
                            myTrainData = new TrainCrewAPI.TrainState { diaName = $"テキスト受信: {rawData.Substring(0, Math.Min(rawData.Length, 50))}" }
                        };
                        OnDataReceived?.Invoke(textData);
                        
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ メッセージ処理エラー: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                        
                        // エラーでも何らかのデータを返す
                        var errorData = new TrainCrewAPI.TrainCrewStateData
                        {
                            myTrainData = new TrainCrewAPI.TrainState { diaName = $"処理エラー: {ex.Message}" }
                        };
                        OnDataReceived?.Invoke(errorData);
                        OnConnectionStatusChanged?.Invoke($"データ処理エラー: {ex.Message}");
                    }
                };

                webSocket.OnError += (sender, e) =>
                {
                    isConnected = false;
                    dataRequestTimer?.Dispose();
                    connectionCheckTimer?.Dispose();
                    System.Diagnostics.Debug.WriteLine($"WebSocketエラー: {e.Message}");
                    OnConnectionStatusChanged?.Invoke($"エラー: {e.Message} - 3秒後に再接続");
                    
                    // エラー発生時は短い間隔で再接続を試行
                    if (shouldReconnect)
                    {
                        reconnectTimer?.Dispose();
                        reconnectTimer = new System.Threading.Timer(
                            _ => 
                            {
                                System.Diagnostics.Debug.WriteLine("エラー後の自動再接続を実行中...");
                                Connect();
                            }, 
                            null, 3000, System.Threading.Timeout.Infinite);
                    }
                };

                webSocket.OnClose += (sender, e) =>
                {
                    isConnected = false;
                    dataRequestTimer?.Dispose();
                    connectionCheckTimer?.Dispose();
                    System.Diagnostics.Debug.WriteLine($"TrainCrewとの接続が切れました。Code: {e.Code}, Reason: {e.Reason}");
                    OnConnectionStatusChanged?.Invoke($"切断 (Code: {e.Code}) - 3秒後に再接続");
                    
                    // 自動再接続を開始（短い間隔で）
                    if (shouldReconnect)
                    {
                        System.Diagnostics.Debug.WriteLine("3秒後に自動再接続を開始します");
                        reconnectTimer?.Dispose();
                        reconnectTimer = new System.Threading.Timer(
                            _ => 
                            {
                                System.Diagnostics.Debug.WriteLine("自動再接続を実行中...");
                                Connect();
                            }, 
                            null, 3000, System.Threading.Timeout.Infinite);
                    }
                };

                System.Diagnostics.Debug.WriteLine("WebSocket接続を開始...");
                webSocket.Connect();
                
                // 接続タイムアウトチェック (短縮: 3秒)
                System.Threading.Tasks.Task.Delay(3000).ContinueWith(t =>
                {
                    if (!isConnected && shouldReconnect)
                    {
                        System.Diagnostics.Debug.WriteLine("接続タイムアウト - 自動再接続を開始");
                        OnConnectionStatusChanged?.Invoke("接続タイムアウト - 3秒後に再試行");
                        
                        // タイムアウト後も短い間隔で再接続
                        reconnectTimer?.Dispose();
                        reconnectTimer = new System.Threading.Timer(
                            _ => 
                            {
                                System.Diagnostics.Debug.WriteLine("タイムアウト後の自動再接続を実行中...");
                                Connect();
                            }, 
                            null, 3000, System.Threading.Timeout.Infinite);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"接続エラー: {ex.Message}");
                OnConnectionStatusChanged?.Invoke($"接続失敗: {ex.Message} - 3秒後に再試行");
                
                // 例外発生時も短い間隔で再接続
                if (shouldReconnect)
                {
                    reconnectTimer?.Dispose();
                    reconnectTimer = new System.Threading.Timer(
                        _ => 
                        {
                            System.Diagnostics.Debug.WriteLine("例外後の自動再接続を実行中...");
                            Connect();
                        }, 
                        null, 3000, System.Threading.Timeout.Infinite);
                }
            }
        }

        private void CheckConnectionHealth()
        {
            try
            {
                // データ受信から30秒以上経過している場合は接続に問題があると判断
                if (isConnected && (DateTime.Now - lastDataReceived).TotalSeconds > 30)
                {
                    System.Diagnostics.Debug.WriteLine("接続ヘルスチェック失敗 - データ受信が途絶えています");
                    OnConnectionStatusChanged?.Invoke("接続異常検出 - 再接続中");
                    
                    // 強制的に再接続
                    isConnected = false;
                    Connect();
                }
                else if (isConnected)
                {
                    // 正常な場合は追加のデータ要求を送信
                    RequestAllData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"接続ヘルスチェックエラー: {ex.Message}");
            }
        }

        private void RequestAllData()
        {
            if (!isConnected)
            {
                System.Diagnostics.Debug.WriteLine("データ要求失敗: 未接続");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("=== データ要求プロセス開始 ===");
                
                // 自列車の軌道回路のみを効率的に取得
                var command = new TrainCrewAPI.CommandToTrainCrew
                {
                    command = "DataRequest",
                    args = new string[] { "tconlyontrain" }
                };

                string jsonRequest = JsonConvert.SerializeObject(command);
                System.Diagnostics.Debug.WriteLine($"データ要求送信: {jsonRequest}");
                webSocket.Send(jsonRequest);
                
                OnConnectionStatusChanged?.Invoke("接続中 - データ要求送信済み");
                System.Diagnostics.Debug.WriteLine("自列車軌道回路データの要求を送信しました");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"データ要求送信エラー: {ex.Message}");
                OnConnectionStatusChanged?.Invoke($"データ要求エラー: {ex.Message}");
            }
        }

        public string GetZoneFromTrackCircuit(string trackCircuitName)
        {
            if (zoneMappings.TryGetValue(trackCircuitName, out string zone))
            {
                return zone;
            }
            return "不明";
        }

        public List<string> GetMyTrainTrackCircuits(TrainCrewAPI.TrainCrewStateData data)
        {
            if (data?.myTrainData?.diaName == null || data.trackCircuitList == null)
                return new List<string>();

            string myTrainName = data.myTrainData.diaName;
            return data.trackCircuitList
                .Where(tc => tc.On && tc.Last == myTrainName)
                .Select(tc => tc.Name)
                .ToList();
        }

        public void Disconnect()
        {
            shouldReconnect = false;
            dataRequestTimer?.Dispose();
            connectionCheckTimer?.Dispose();
            reconnectTimer?.Dispose();
            
            if (webSocket != null)
            {
                try
                {
                    webSocket.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WebSocket切断エラー: {ex.Message}");
                }
                webSocket = null;
            }
            
            isConnected = false;
            OnConnectionStatusChanged?.Invoke("切断済み");
            System.Diagnostics.Debug.WriteLine("TrainCrewWebSocketClientが正常に切断されました");
        }
    }
}
