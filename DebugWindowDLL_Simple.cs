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
    public partial class DebugWindowDLL_Simple : Form
    {
        private System.Windows.Forms.Timer updateTimer;
        private RichTextBox debugTextBox;
        private Label statusLabel;

        // マネージドアセンブリ用フィールド
        private Assembly trainCrewAssembly;
        private Type trainCrewType;
        private bool dllInitialized = false;

        public DebugWindowDLL_Simple()
        {
            InitializeComponents();
            InitializeDLL();
            SetupUpdateTimer();
        }

        private void InitializeComponents()
        {
            // フォームの基本設定
            this.Text = "館浜防護システム DLLデバッグウィンドウ";
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

        private void SetupUpdateTimer()
        {
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 1000; // 1秒間隔
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
                if (trainCrewType == null)
                {
                    sb.AppendLine("❌ trainCrewType が null です");
                    return;
                }

                sb.AppendLine($"🔧 DLLタイプ: {trainCrewType.FullName}");
                sb.AppendLine();

                // GetTrainStateメソッドを呼び出してTrainStateオブジェクトを取得
                var trainState = GetDLLValue("GetTrainState");
                
                if (trainState != null)
                {
                    sb.AppendLine("🚄 === TrainState データ ===");
                    sb.AppendLine($"TrainState型: {trainState.GetType().FullName}");
                    sb.AppendLine();
                    
                    // TrainStateオブジェクトの全プロパティを表示
                    var trainStateType = trainState.GetType();
                    var properties = trainStateType.GetProperties();
                    
                    sb.AppendLine($"📋 利用可能プロパティ数: {properties.Length}");
                    sb.AppendLine("📋 プロパティ一覧:");
                    
                    foreach (var prop in properties.Take(30))
                    {
                        try
                        {
                            var value = prop.GetValue(trainState);
                            sb.AppendLine($"   • {prop.Name} ({prop.PropertyType.Name}): {FormatValue(value)}");
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"   • {prop.Name} ({prop.PropertyType.Name}): エラー - {ex.Message}");
                        }
                    }
                    
                    // よく使われそうなプロパティの値を明示的に表示
                    sb.AppendLine();
                    sb.AppendLine("� === 主要データ ===");
                    
                    var importantProps = new[] { "Speed", "Pnotch", "Bnotch", "Reverser", "MR_Press", "AllClose", 
                                               "diaName", "nextStaName", "nextStaDistance", "KilometerPost", "speedLimit" };
                    foreach (var propName in importantProps)
                    {
                        var prop = properties.FirstOrDefault(p => p.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));
                        if (prop != null)
                        {
                            try
                            {
                                var value = prop.GetValue(trainState);
                                sb.AppendLine($"   🎯 {propName}: {FormatValue(value)}");
                            }
                            catch (Exception ex)
                            {
                                sb.AppendLine($"   🎯 {propName}: エラー - {ex.Message}");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"   ❌ {propName}: プロパティが見つかりません");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("❌ GetTrainState() が null を返しました");
                    
                    // 利用可能な全メソッドを表示
                    var allMethods = trainCrewType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    sb.AppendLine($"📋 利用可能メソッド総数: {allMethods.Length}");
                    
                    foreach (var method in allMethods.Take(20))
                    {
                        sb.AppendLine($"   - {method.Name} ({method.ReturnType.Name})");
                    }
                }
                
            }
            catch (Exception ex)
            {
                sb.AppendLine($"❌ DLLデータ取得エラー: {ex.Message}");
                sb.AppendLine($"スタックトレース: {ex.StackTrace}");
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

        private string FormatValue(object value)
        {
            if (value == null) return "null";
            
            if (value is float f) return f.ToString("F1");
            if (value is double d) return d.ToString("F1");
            if (value is string s) return $"\"{s}\"";
            
            return value.ToString();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                updateTimer?.Stop();
                updateTimer?.Dispose();
                base.OnFormClosing(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォーム終了エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
