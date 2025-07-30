using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using TrainCrewAPI;

namespace tatehama_bougo_client
{
    public partial class DebugWindowDLL : Form
    {
        private System.Windows.Forms.Timer updateTimer;
        private RichTextBox debugTextBox;
        private Button refreshButton;
        private Button emergencyBrakeButton;
        private Button resetEBButton;
        private NumericUpDown notchUpDown;
        private Button setNotchButton;
        private Label statusLabel;

        // マネージドアセンブリ用フィールド
        private Assembly trainCrewAssembly;
        private Type trainCrewType;
        private bool dllInitialized = false;

        // WebSocket用フィールド
        private TrainCrewWebSocketClient webSocketClient;
        private bool webSocketConnected = false;
        private TrainCrewStateData lastWebSocketData;

        public DebugWindowDLL()
        {
            // コンポーネントの初期化をコードで行う
            InitializeComponents();
            InitializeDLL();
            // InitializeWebSocket(); // 一時的に無効化
            SetupUpdateTimer();
        }

        private void InitializeComponents()
        {
            // フォームの基本設定
            this.Text = "館浜防護システム デバッグウィンドウ";
            this.Size = new Size(1000, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            // デバッグテキストボックス
            debugTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.Black,
                ForeColor = Color.Lime
            };

            // ステータスラベル
            statusLabel = new Label
            {
                Text = "初期化中...",
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.LightGray
            };

            // コントロールをフォームに追加
            this.Controls.Add(debugTextBox);
            this.Controls.Add(statusLabel);
        }

        private void InitializeDLL()
        {
            try
            {
                // DLLファイルの存在確認
                var possiblePaths = new[]
                {
                    Path.Combine(Application.StartupPath, "DLL", "TrainCrewInput.dll"),
                    Path.Combine(Application.StartupPath, "TrainCrewInput.dll"),
                    "DLL/TrainCrewInput.dll",
                    "TrainCrewInput.dll"
                };

                string foundPath = null;
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        foundPath = path;
                        break;
                    }
                }

                if (foundPath == null)
                {
                    statusLabel.Text = "DLLファイルが見つかりません";
                    statusLabel.ForeColor = Color.Red;
                    dllInitialized = false;
                    return;
                }

                statusLabel.Text = $"DLL発見: {foundPath}";
                statusLabel.ForeColor = Color.Blue;

                // マネージドアセンブリとして読み込み
                var assembly = Assembly.LoadFrom(foundPath);
                
                // 利用可能なタイプを全て列挙
                var types = assembly.GetTypes();
                System.Diagnostics.Debug.WriteLine($"DLL内のタイプ数: {types.Length}");
                foreach (var type in types.Take(10))
                {
                    System.Diagnostics.Debug.WriteLine($"タイプ: {type.FullName}");
                }
                
                var trainCrewType = assembly.GetType("TrainCrew.TrainCrewInput");
                
                if (trainCrewType == null)
                {
                    // より柔軟な検索
                    foreach (var type in types)
                    {
                        System.Diagnostics.Debug.WriteLine($"検査中タイプ: {type.Name}");
                        if (type.Name.Contains("TrainCrewInput") || type.Name.Contains("TrainCrew"))
                        {
                            trainCrewType = type;
                            System.Diagnostics.Debug.WriteLine($"見つかったタイプ: {type.FullName}");
                            break;
                        }
                    }
                }

                if (trainCrewType != null)
                {
                    trainCrewAssembly = assembly;
                    this.trainCrewType = trainCrewType;
                    dllInitialized = true;
                    statusLabel.Text = $"DLL初期化成功: {trainCrewType.FullName}";
                    statusLabel.ForeColor = Color.Green;
                    
                    // 利用可能なメソッドを全て出力
                    var methods = trainCrewType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    System.Diagnostics.Debug.WriteLine($"利用可能メソッド数: {methods.Length}");
                    foreach (var method in methods.Take(20))
                    {
                        System.Diagnostics.Debug.WriteLine($"メソッド: {method.Name} ({method.ReturnType.Name})");
                    }
                }
                else
                {
                    statusLabel.Text = "TrainCrewInputクラスが見つかりません";
                    statusLabel.ForeColor = Color.Orange;
                    dllInitialized = false;
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"DLL初期化エラー: {ex.Message}";
                statusLabel.ForeColor = Color.Red;
                dllInitialized = false;
                System.Diagnostics.Debug.WriteLine($"DLL初期化例外: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        private void InitializeWebSocket()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("WebSocket初期化開始");
                webSocketClient = new TrainCrewWebSocketClient();
                
                // データ受信イベントを設定
                webSocketClient.OnDataReceived += (data) =>
                {
                    System.Diagnostics.Debug.WriteLine($"WebSocketデータ受信: {data?.myTrainData?.diaName}");
                    lastWebSocketData = data;
                };
                
                // 接続状態変更イベントを設定
                webSocketClient.OnConnectionStatusChanged += (status) =>
                {
                    System.Diagnostics.Debug.WriteLine($"WebSocket接続状態: {status}");
                    if (statusLabel.InvokeRequired)
                    {
                        statusLabel.Invoke(new Action(() => {
                            statusLabel.Text = $"WebSocket: {status}";
                        }));
                    }
                    else
                    {
                        statusLabel.Text = $"WebSocket: {status}";
                    }
                };
                
                webSocketClient.Connect();
                webSocketConnected = true;
                System.Diagnostics.Debug.WriteLine("WebSocket接続成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebSocket接続エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                webSocketConnected = false;
            }
        }

        private void SetupUpdateTimer()
        {
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 500; // 0.5秒間隔
            updateTimer.Tick += (sender, e) => UpdateDebugInfo();
            updateTimer.Start();
        }

        private void UpdateDebugInfo()
        {
            try
            {
                var sb = new StringBuilder();
                
                sb.AppendLine("=== 館浜防護システム DLLデバッグ情報 ===");
                sb.AppendLine($"更新時刻: {DateTime.Now:HH:mm:ss.fff}");
                sb.AppendLine($"DLL状態: {(dllInitialized ? "✅ 初期化済み" : "❌ 未初期化")}");
                sb.AppendLine();

                // DLL側のデータを表示
                if (dllInitialized)
                {
                    sb.AppendLine("🔧 === DLLデータ ===");
                    DisplayDLLData(sb);
                }
                else
                {
                    sb.AppendLine("❌ DLLが初期化されていません");
                }

                debugTextBox.Text = sb.ToString();
                
                // 自動スクロールを最下部に設定
                debugTextBox.SelectionStart = debugTextBox.Text.Length;
                debugTextBox.ScrollToCaret();
            }
            catch (Exception ex)
            {
                debugTextBox.Text = $"エラー: {ex.Message}\n\nスタックトレース:\n{ex.StackTrace}";
            }
        }

        private void DisplayDLLData(StringBuilder sb)
        {
            try
            {
                // 基本情報
                sb.AppendLine("🚄 === DLL基本情報 ===");
                sb.AppendLine($"🏃 速度: {FormatValue(GetDLLValue("GetSpeed"))} | ⚡ 力行ノッチ: {FormatValue(GetDLLValue("GetNotch"))} | 🛑 ブレーキノッチ: {FormatValue(GetDLLValue("GetBrakeNotch"))}");
                sb.AppendLine($"� レバーサ: {FormatValue(GetDLLValue("GetReverser"))} | 🔧 MR圧: {FormatValue(GetDLLValue("GetMRPress"))} | 🚪 戸閉: {FormatValue(GetDLLValue("GetAllClose"))}");
                
                // 駅情報
                sb.AppendLine();
                sb.AppendLine("🚉 === DLL駅情報 ===");
                sb.AppendLine($"🚉 次駅: {FormatValue(GetDLLValue("GetNextStationName"))} | 📏 距離: {FormatValue(GetDLLValue("GetNextStationDistance"))}m");
                sb.AppendLine($"📍 キロポスト: {FormatValue(GetDLLValue("GetKilometerPost"))}km | ⚠️ 制限速度: {FormatValue(GetDLLValue("GetSpeedLimit"))}km/h");
                
                // システム状態
                sb.AppendLine();
                sb.AppendLine("🎮 === DLLシステム状態 ===");
                sb.AppendLine($"🎮 ゲーム画面: {FormatValue(GetDLLValue("GetGameScreen"))} | 👤 乗務員: {FormatValue(GetDLLValue("GetCrewType"))}");
                sb.AppendLine($"🕐 時刻: {FormatValue(GetDLLValue("GetNowTime"))} | 📊 状態: {FormatValue(GetDLLValue("GetTrainState"))}");
                
                // 利用可能なメソッド一覧（デバッグ用）
                if (trainCrewType != null)
                {
                    var allMethods = trainCrewType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    var getMethods = allMethods.Where(m => m.Name.StartsWith("Get")).Take(5).Select(m => m.Name);
                    var allMethodNames = allMethods.Take(10).Select(m => m.Name);
                    
                    sb.AppendLine();
                    sb.AppendLine($"📋 利用可能Getメソッド(最初の5個): {string.Join(", ", getMethods)}");
                    sb.AppendLine($"📋 全メソッド(最初の10個): {string.Join(", ", allMethodNames)}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"❌ DLLデータ取得エラー: {ex.Message}");
            }
        }

        private void DisplayCompleteWebSocketData(StringBuilder sb, TrainCrewStateData data)
        {
            if (data?.myTrainData != null)
            {
                var train = data.myTrainData;
                
                // 基本列車情報を横並びで表示
                sb.AppendLine("🚄 === 基本列車情報 ===");
                sb.AppendLine($"🚄 列車番号: {train.diaName ?? "未設定"} | 📋 種別: {train.Class ?? "未設定"} | 🎯 行先: {train.For ?? train.BoundFor ?? "未設定"}");
                sb.AppendLine($"🏃 速度: {train.Speed:F1} km/h | ⚠️ 制限速度: {train.speedLimit:F0} km/h | 📏 編成長: {train.TotalLength:F1}m");
                sb.AppendLine();
                
                // 運転台操作情報を横並びで表示
                sb.AppendLine("🎮 === 運転操作情報 ===");
                sb.AppendLine($"⚡ 力行ノッチ: {train.Pnotch} | 🛑 ブレーキノッチ: {train.Bnotch} | 🔄 レバーサ: {train.Reverser} ({(train.Reverser == 1 ? "前進" : train.Reverser == -1 ? "後退" : "中立")})");
                sb.AppendLine($"🔧 MR圧: {train.MR_Press:F1} kPa | 🚪 戸閉: {(train.AllClose ? "締切" : "開放")} | ⛰️ 勾配: {train.gradient:F1}‰");
                sb.AppendLine();
                
                // 駅・距離情報を横並びで表示
                sb.AppendLine("🚉 === 駅・距離情報 ===");
                sb.AppendLine($"🚉 次駅: {train.nextStaName ?? "未設定"} | 📏 次駅距離: {train.nextStaDistance:F0}m | 🎯 停車種別: {train.nextStopType ?? "未設定"}");
                sb.AppendLine($"📍 次UI距離: {train.nextUIDistance:F0}m | 📍 キロポスト: {train.KilometerPost:F1}km | 📍 現在駅番号: {train.nowStaIndex}");
                
                if (train.nextSpeedLimit > 0)
                {
                    sb.AppendLine($"⚠️ 次制限速度: {train.nextSpeedLimit:F0} km/h | 📏 次制限距離: {train.nextSpeedLimitDistance:F0}m");
                }
                sb.AppendLine();
            }

            // 軌道回路情報
            if (data?.trackCircuitList != null && data.trackCircuitList.Any())
            {
                sb.AppendLine("🛤️ === 軌道回路情報 ===");
                var trackCircuits = data.trackCircuitList.Where(tc => tc != null).ToList();
                var occupiedCircuits = trackCircuits.Where(tc => tc.On.HasValue && tc.On.Value).ToList();
                
                sb.AppendLine($"📊 軌道回路総数: {trackCircuits.Count} | 占有中: {occupiedCircuits.Count}");
                
                if (occupiedCircuits.Any())
                {
                    sb.AppendLine("🔴 占有中の軌道回路:");
                    for (int i = 0; i < occupiedCircuits.Count && i < 20; i += 4) // 最大20個、4個ずつ表示
                    {
                        var batch = occupiedCircuits.Skip(i).Take(4).Select(tc => tc.Name ?? "不明");
                        sb.AppendLine($"  🔴 {string.Join(" | ", batch)}");
                    }
                }
                sb.AppendLine();
            }

            // 信号情報
            if (data?.signalDataList != null && data.signalDataList.Any())
            {
                sb.AppendLine("🚦 === 信号情報 ===");
                var signals = data.signalDataList.Where(s => s != null).ToList();
                var phaseGroups = signals.GroupBy(s => s.phase).OrderBy(g => g.Key).ToList();
                
                sb.AppendLine($"📊 信号機総数: {signals.Count}");
                var phaseSummary = phaseGroups.Select(g => $"{GetSignalPhaseName(g.Key)}:{g.Count()}機").ToList();
                
                // 3個ずつ横並びで表示
                for (int i = 0; i < phaseSummary.Count; i += 3)
                {
                    sb.AppendLine($"🚦 {string.Join(" | ", phaseSummary.Skip(i).Take(3))}");
                }
                sb.AppendLine();
            }

            // 他列車情報
            if (data?.otherTrainDataList != null && data.otherTrainDataList.Any())
            {
                sb.AppendLine("🚈 === 他列車情報 ===");
                var otherTrains = data.otherTrainDataList.Where(t => t != null).Take(5).ToList(); // 最大5列車
                
                sb.AppendLine($"📊 他列車総数: {data.otherTrainDataList.Count}");
                foreach (var train in otherTrains)
                {
                    sb.AppendLine($"🚈 {train.Name ?? "不明"}: 速度{train.Speed:F1}km/h | 位置{train.Location ?? "不明"} | 種別{train.Class ?? "不明"}");
                }
                sb.AppendLine();
            }

            // ゲーム状態情報
            sb.AppendLine("🎮 === ゲーム状態情報 ===");
            sb.AppendLine($"🎮 ゲーム画面: {GetGameScreenName(data.gameScreen)} | 👤 乗務員: {GetCrewTypeName(data.crewType)} | 🚗 運転モード: {GetDriveModeName(data.driveMode)}");
            
            if (data.nowTime != null)
            {
                sb.AppendLine($"🕐 現在時刻: {data.nowTime.hour:D2}:{data.nowTime.minute:D2}:{data.nowTime.second:F1}");
            }
            sb.AppendLine();
        }

        private void DisplayDataComparison(StringBuilder sb)
        {
            if (lastWebSocketData?.myTrainData == null)
            {
                sb.AppendLine("❌ WebSocketデータが無効です");
                return;
            }

            try
            {
                var wsData = lastWebSocketData.myTrainData;
                
                // DLLから主要データを取得（リフレクションを使用）
                var dllSpeed = GetDLLValue("GetSpeed");
                var dllNotch = GetDLLValue("GetNotch");
                var dllBrakeNotch = GetDLLValue("GetBrakeNotch");
                
                sb.AppendLine("📊 主要データ比較:");
                sb.AppendLine($"🏃 速度: DLL={FormatValue(dllSpeed)} | WS={wsData.Speed:F1} | 一致={CompareValues(dllSpeed, wsData.Speed)}");
                sb.AppendLine($"⚡ 力行ノッチ: DLL={FormatValue(dllNotch)} | WS={wsData.Pnotch} | 一致={CompareValues(dllNotch, wsData.Pnotch)}");
                sb.AppendLine($"🛑 ブレーキノッチ: DLL={FormatValue(dllBrakeNotch)} | WS={wsData.Bnotch} | 一致={CompareValues(dllBrakeNotch, wsData.Bnotch)}");
                
                // 比較統計
                var comparisons = new[]
                {
                    CompareValues(dllSpeed, wsData.Speed),
                    CompareValues(dllNotch, wsData.Pnotch),
                    CompareValues(dllBrakeNotch, wsData.Bnotch)
                };
                
                var matchCount = comparisons.Count(c => c);
                var matchRate = (matchCount * 100.0) / comparisons.Length;
                
                sb.AppendLine($"📈 一致率: {matchCount}/{comparisons.Length} ({matchRate:F1}%)");
                sb.AppendLine();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"❌ 比較エラー: {ex.Message}");
            }
        }

        private object GetDLLValue(string methodName)
        {
            try
            {
                if (trainCrewType == null) return null;
                
                var method = trainCrewType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
                if (method == null) return null;
                
                return method.Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }

        private bool CompareValues(object dllValue, object wsValue)
        {
            if (dllValue == null || wsValue == null) return false;
            
            // 数値の場合は誤差を考慮
            if (dllValue is float df && wsValue is float wf)
                return Math.Abs(df - wf) < 0.1f;
            
            if (dllValue is double dd && wsValue is double wd)
                return Math.Abs(dd - wd) < 0.1;
                
            if (dllValue is int di && wsValue is int wi)
                return di == wi;
            
            // 文字列の場合
            if (dllValue is string ds && wsValue is string ws)
                return string.Equals(ds, ws, StringComparison.OrdinalIgnoreCase);
            
            return dllValue.Equals(wsValue);
        }

        private string FormatValue(object value)
        {
            if (value == null) return "null";
            
            if (value is float f) return f.ToString("F1");
            if (value is double d) return d.ToString("F1");
            if (value is string s) return $"\"{s}\"";
            
            return value.ToString();
        }

        private string GetSignalPhaseName(int phase)
        {
            return phase switch
            {
                0 => "停止(R)",
                1 => "注意(Y)", 
                2 => "進行(G)",
                3 => "減速(YY)",
                4 => "抑速(YG)",
                _ => $"未知({phase})"
            };
        }

        private string GetGameScreenName(int gameScreen)
        {
            return gameScreen switch
            {
                0 => "タイトル画面",
                1 => "メインメニュー", 
                2 => "運転シミュレータ",
                3 => "設定画面",
                4 => "リプレイ画面",
                _ => $"未知の画面({gameScreen})"
            };
        }

        private string GetCrewTypeName(int crewType)
        {
            return crewType switch
            {
                0 => "運転士",
                1 => "車掌",
                2 => "指導員",
                _ => $"未知の職種({crewType})"
            };
        }

        private string GetDriveModeName(int driveMode)
        {
            return driveMode switch
            {
                0 => "手動運転",
                1 => "ATO運転",
                2 => "タイマー運転",
                _ => $"未知のモード({driveMode})"
            };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                updateTimer?.Stop();
                updateTimer?.Dispose();
                
                webSocketClient?.Disconnect();

                base.OnFormClosing(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォーム終了エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
