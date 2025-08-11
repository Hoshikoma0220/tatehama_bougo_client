using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TakumiteAudioWrapper;
using TatehamaATS_v1.RetsubanWindow;
using TrainCrewAPI;
using tatehama_bougo_client.API;

namespace tatehama_bougo_client
{
    public partial class Form1 : Form
    {
        // グローバルホットキー用のWinAPI宣言
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID_F4 = 2; // F4キー用ホットキーID
        private const uint VK_F4 = 0x73; // F4キーのVirtual Key Code
        
        private AudioManager audioManager;
        private AudioWrapper bougomusenno;
        private AudioWrapper bougoF4Audio; // F4キー用の防護音声
        private AudioWrapper bougoOtherAudio; // 他列車受報用の防護音声
        private AudioWrapper set_trainnum;
        private AudioWrapper set_complete;
        private AudioWrapper kosyou; // 故障音声
        private AudioWrapper kosyou_koe; // 故障音声（音声）
        private AudioWrapper ebkaihou; // EB開放音声
        private AudioWrapper ebkaihou_koe; // EB開放音声（音声）
        
        // UI状態管理
        private float currentVolume = 1.0f; // 現在の音量（0.0～1.0）
        private float systemVolume = 1.0f; // システム全体音量（防護無線中は50%）
        private bool powerOn = true; // 電源状態
        private bool powerLampOn = false; // 電源ランプ表示状態
        private bool failureLampOn = false; // 故障ランプ表示状態
        private bool initialSetupComplete = false; // 初期設定完了フラグ
        private bool isWebSocketConnected = false; // WebSocket接続状態
        private DateTime lastWebSocketActivity = DateTime.Now; // 最後のWebSocket通信時刻
        private DateTime? failureDetectedTime = null; // 故障検出開始時刻
        private DateTime? webSocketTimeoutDetectedTime = null; // WebSocket接続タイムアウト検出開始時刻
        private DateTime? ebActivationTime = null; // EB作動開始時刻（5秒遅延用）
        private bool isStartupEBActivated = false; // 起動時EB作動済みフラグ
        private bool hasEverMetReleaseConditions = false; // EB開放条件を1回でも満たしたことがあるかのフラグ
        private static bool shouldPlayLoop = true;
        private bool loopStarted = false;
        private static bool shouldPlayKosyouLoop = false; // 故障音ループ制御
        private bool kosyouLoopStarted = false; // 故障音ループ開始状態
        private static bool shouldPlayEBKaihouLoop = false; // EB開放音ループ制御
        private bool ebKaihouLoopStarted = false; // EB開放音ループ開始状態
        private static bool isBougoActive = false; // 防護無線発砲状態
        private static bool isBougoOtherActive = false; // 他列車受報状態
        private static bool isKosyouActive = false; // 故障音発生状態
        private static bool isEBKaihouActive = false; // EB開放音発生状態
        private static readonly object audioLock = new object();

        // 画像パス定数
        private const string PowerOnImagePath = "Images/power_on.png";
        private const string PowerOffImagePath = "Images/power_on.png"; // 同じ画像を使用（とりあえず）
        private const string KosyouNormalImagePath = "Images/kosyou.png";
        private const string KosyouErrorImagePath = "Images/inop.png";
        private const string EBKaihouOffImagePath = "Images/EBkaihou_off.png";
        private const string EBKaihouOnImagePath = "Images/EBkaihou_on.png";
        private const string BougoOffImagePath = "Images/bougo_off.png";
        private const string BougoOnImagePath = "Images/bougo_on.png";

        // TrainCrew連携関連
        private TrainCrewWebSocketClient trainCrewClient;
        private Dictionary<string, string> zoneMappings;
        private TrainCrewAPI.TrainCrewStateData currentTrainCrewData; // 最新のTrainCrewデータ
        
        // SignalR防護無線通信関連
        private BougoSignalRClient bougoSignalRClient;
        private string lastSentZone = ""; // 最後に送信したゾーン情報
        
        // ゾーン移動検知・自動再発報関連
        private string previousZone = ""; // 前回のゾーン（移動検知用）
        private string lastValidZone = ""; // 最後に有効だったゾーン（軌道回路なしの場合の保持用）
        private DateTime lastZoneCheckTime = DateTime.Now; // 最後のゾーンチェック時刻
        private System.Windows.Forms.Timer zoneCheckTimer; // ゾーン変化監視タイマー
        private readonly object zoneMovementLock = new object(); // ゾーン移動処理の排他制御

        // 非常ブレーキ関連
        private bool emergencyBrakeButtonState = false; // false: 作動状態(非常ブレーキ有効), true: 開放状態(非常ブレーキ無効)
        private bool isEBTemporarilyDisabled = true; // EB一時無効化フラグ（初期状態は無効）
        
        /// <summary>
        /// EB一時無効化フラグへのパブリックアクセス
        /// </summary>
        public static bool IsEBTemporarilyDisabled { get; set; } = true;
        
        private System.Windows.Forms.Timer ebBlinkTimer; // EB開放中の故障ランプ点滅タイマー
        private bool ebBlinkState = false; // EB開放中の故障ランプ点滅状態
        private string currentTrainNumber = "--"; // 列番入力画面で設定された列車番号
        private bool isTrainMoving = false; // 列車走行状態
        private bool wasManuallyReleased = false; // 手動でEB開放したかどうかのフラグ

        // 受報時点滅関連
        private System.Windows.Forms.Timer bougoBlinkTimer; // 受報時の防護無線ボタン点滅タイマー
        private bool bougoBlinkState = false; // 受報時の防護無線ボタン点滅状態

        // 故障コード表示関連
        private List<string> failureCodes = new List<string>(); // 故障コード一覧
        private int currentFailureCodeIndex = 0; // 現在表示中の故障コードインデックス
        private System.Windows.Forms.Timer failureCodeTimer; // 故障コード切り替えタイマー
        private System.Windows.Forms.Timer dotAnimationTimer; // ドット表示アニメーションタイマー
        private int dotCount = 0; // ドット表示カウント
        private bool isDotAnimationActive = false; // ドットアニメーション状態

        // デバッグ表示用
        private Label debugTrackCircuitLabel; // 現在の軌道回路表示用ラベル
        private Label debugZoneLabel; // 現在のゾーン表示用ラベル

        public Form1()
        {
            InitializeComponent();
            Load += Form1_Load;
            FormClosing += Form1_FormClosing;
            KeyPreview = true; // キーイベントを受け取るために必要
            KeyDown += Form1_KeyDown; // F4キー処理用
            LoadZoneMappings();
            
            // グローバルホットキーを登録
            RegisterHotKey(this.Handle, HOTKEY_ID_F4, 0, (int)VK_F4);
            
            // EB開放オーバーライドを確実に無効化（初期状態）
            EmergencyBrakeController.SetEbReleaseOverride(false);
            
            // ゾーン移動監視タイマーを初期化（2秒間隔でチェック）
            zoneCheckTimer = new System.Windows.Forms.Timer();
            zoneCheckTimer.Interval = 2000; // 2秒間隔
            zoneCheckTimer.Tick += ZoneCheckTimer_Tick;
            
            // SignalRクライアント初期化
            InitializeSignalRClient();
            
            // TrainCrew接続はLoad時に行う（フォーム表示を優先）
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // グローバルホットキーを解除
            UnregisterHotKey(this.Handle, HOTKEY_ID_F4);
            
            // EB開放点滅タイマーを停止
            ebBlinkTimer?.Stop();
            ebBlinkTimer?.Dispose();
            
            // 受報点滅タイマーを停止
            bougoBlinkTimer?.Stop();
            bougoBlinkTimer?.Dispose();
            
            // 故障コード表示タイマーを停止
            failureCodeTimer?.Stop();
            failureCodeTimer?.Dispose();
            
            // ドットアニメーションタイマーを停止
            dotAnimationTimer?.Stop();
            dotAnimationTimer?.Dispose();
            
            // ゾーン移動監視タイマーを停止
            zoneCheckTimer?.Stop();
            zoneCheckTimer?.Dispose();
            
            // アプリケーション終了時に非常ブレーキを確実に解除
            EmergencyBrakeController.OnApplicationExit();
            
            // 全ての音声ループを停止
            shouldPlayLoop = false;
            shouldPlayKosyouLoop = false; 
            shouldPlayEBKaihouLoop = false;
            isBougoActive = false;
            isBougoOtherActive = false;
            isKosyouActive = false;
            isEBKaihouActive = false;
            
            // 故障検出時刻をリセット
            failureDetectedTime = null;
            webSocketTimeoutDetectedTime = null;
            
            // アプリケーション終了時でもユーザー設定音量を維持
            try
            {
                TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(currentVolume);
                System.Diagnostics.Debug.WriteLine($"🔊 終了時音量{(int)(currentVolume * 100)}%を維持");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 終了時音量復旧エラー: {ex.Message}");
            }
            
            // TrainCrewクライアントを安全に切断
            try
            {
                trainCrewClient?.Disconnect();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrainCrewクライアント切断エラー: {ex.Message}");
            }
            
            // SignalRクライアントを安全に切断
            try
            {
                bougoSignalRClient?.DisconnectAsync().Wait();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalRクライアント切断エラー: {ex.Message}");
            }
        }
        
        /// <summary>
        /// SignalRクライアント初期化
        /// </summary>
        private void InitializeSignalRClient()
        {
            try
            {
                bougoSignalRClient = new BougoSignalRClient();
                
                // イベント登録
                bougoSignalRClient.OnBougoFired += OnBougoFiredReceived;
                bougoSignalRClient.OnBougoStopped += OnBougoStoppedReceived;
                bougoSignalRClient.OnConnectionChanged += OnSignalRConnectionChanged;
                bougoSignalRClient.OnError += OnSignalRError;
                
                System.Diagnostics.Debug.WriteLine("🔗 SignalRクライアント初期化完了");
                
                // 初回自動接続を開始（非同期）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("🔄 初回SignalR接続を開始します...");
                        await bougoSignalRClient.ConnectAsync(enableAutoReconnect: true); // 自動再接続を有効にして接続
                        System.Diagnostics.Debug.WriteLine("✅ 初回SignalR接続完了");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 初回SignalR接続エラー: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalRクライアント初期化エラー: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 他列車の防護無線発砲を受信
        /// </summary>
        private void OnBougoFiredReceived(string trainNumber, string zone)
        {
            System.Diagnostics.Debug.WriteLine($"🚨 Form1: OnBougoFiredReceived 開始");
            System.Diagnostics.Debug.WriteLine($"   InvokeRequired: {InvokeRequired}");
            System.Diagnostics.Debug.WriteLine($"   パラメータ: trainNumber={trainNumber}, zone={zone}");
            
            if (InvokeRequired)
            {
                System.Diagnostics.Debug.WriteLine($"   UIスレッドへInvoke実行中...");
                Invoke(new Action<string, string>(OnBougoFiredReceived), trainNumber, zone);
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"🚨 他列車発報受信: {trainNumber} @ {zone}");
            System.Diagnostics.Debug.WriteLine($"   PlayOtherTrainBougoSound() 実行中...");
            
            // 他列車の発砲時は音声を再生（bougoOther.wav があると仮定）
            PlayOtherTrainBougoSound();
            
            System.Diagnostics.Debug.WriteLine($"🚨 Form1: OnBougoFiredReceived 完了");
        }
        
        /// <summary>
        /// 他列車の防護無線停止を受信
        /// </summary>
        private void OnBougoStoppedReceived(string trainNumber, string zone)
        {
            System.Diagnostics.Debug.WriteLine($"🔴 Form1: OnBougoStoppedReceived 開始");
            System.Diagnostics.Debug.WriteLine($"   InvokeRequired: {InvokeRequired}");
            System.Diagnostics.Debug.WriteLine($"   パラメータ: trainNumber={trainNumber}, zone={zone}");
            
            if (InvokeRequired)
            {
                System.Diagnostics.Debug.WriteLine($"   UIスレッドへInvoke実行中...");
                Invoke(new Action<string, string>(OnBougoStoppedReceived), trainNumber, zone);
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"🔴 他列車停止受信: {trainNumber} @ {zone}");
            System.Diagnostics.Debug.WriteLine($"   StopOtherTrainBougoSound() 実行中...");
            
            // 他列車の停止通知処理（必要に応じて音声停止など）
            StopOtherTrainBougoSound();
            
            System.Diagnostics.Debug.WriteLine($"🔴 Form1: OnBougoStoppedReceived 完了");
        }
        
        /// <summary>
        /// SignalR接続状態変更
        /// </summary>
        private void OnSignalRConnectionChanged(bool isConnected)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(OnSignalRConnectionChanged), isConnected);
                return;
            }
            
            string status = isConnected ? "✅ 接続中" : "❌ 切断";
            string serverInfo = bougoSignalRClient?.GetCurrentServerInfo() ?? "サーバー情報不明";
            System.Diagnostics.Debug.WriteLine($"🔗 SignalR接続状態: {status} ({serverInfo})");
            
            // タイトルバーに接続状態とサーバー情報を表示
            this.Text = $"立濱防護無線クライアント - SignalR: {status} ({serverInfo})";
        }
        
        /// <summary>
        /// SignalRエラー通知
        /// </summary>
        private void OnSignalRError(string errorMessage)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnSignalRError), errorMessage);
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"❌ SignalRエラー: {errorMessage}");
        }
        
        /// <summary>
        /// 他列車の防護無線音声を再生
        /// </summary>
        private void PlayOtherTrainBougoSound()
        {
            System.Diagnostics.Debug.WriteLine($"🔊 PlayOtherTrainBougoSound 開始");
            System.Diagnostics.Debug.WriteLine($"   isBougoOtherActive: {isBougoOtherActive}");
            System.Diagnostics.Debug.WriteLine($"   isBougoActive: {isBougoActive}");
            System.Diagnostics.Debug.WriteLine($"   bougoOtherAudio != null: {bougoOtherAudio != null}");
            
            lock (audioLock)
            {
                if (!isBougoOtherActive)
                {
                    System.Diagnostics.Debug.WriteLine("🚨 他列車防護無線受報開始");
                    isBougoOtherActive = true;
                    
                    // 受報開始時に即座にボタンを点灯（0.5秒待たずに即座に光らせる）
                    bougoBlinkState = true; // 最初は点灯状態
                    UpdateBougoDisplayWithBlink(); // 即座に表示更新
                    System.Diagnostics.Debug.WriteLine("💡 受報開始時即座点灯");
                    
                    // 受報時の点滅タイマーを開始
                    bougoBlinkTimer?.Start();
                    System.Diagnostics.Debug.WriteLine("💡 受報点滅タイマー開始");
                    
                    // 発砲中でない場合のみ受報音を再生（発砲優先）
                    if (!isBougoActive)
                    {
                        // 音声再生前に音量を必ず100%に設定
                        try
                        {
                            TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                            currentVolume = 1.0f; // currentVolumeも100%に更新
                            System.Diagnostics.Debug.WriteLine("🔊 受報音再生前：音量を100%に設定");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ 受報音再生前音量設定エラー: {ex.Message}");
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"   bougoOtherAudio.PlayLoop 実行中... (音量: 100%)");
                        bougoOtherAudio?.PlayLoop(1.0f);
                        System.Diagnostics.Debug.WriteLine($"🔊 他列車防護無線音声開始: 音量100%");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ 発砲中のため受報音は待機中");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 既に受報状態です");
                }
            }
            System.Diagnostics.Debug.WriteLine($"🔊 PlayOtherTrainBougoSound 完了");
        }
        
        /// <summary>
        /// 他列車の防護無線音声を停止
        /// </summary>
        private void StopOtherTrainBougoSound()
        {
            lock (audioLock)
            {
                if (isBougoOtherActive)
                {
                    System.Diagnostics.Debug.WriteLine("🔴 他列車防護無線受報停止");
                    isBougoOtherActive = false;
                    
                    // 受報時の点滅タイマーを停止
                    bougoBlinkTimer?.Stop();
                    System.Diagnostics.Debug.WriteLine("💡 受報点滅タイマー停止");
                    
                    // 他列車受報音を停止
                    bougoOtherAudio?.Stop();
                    System.Diagnostics.Debug.WriteLine("🔇 他列車防護無線音声停止");
                    
                    // ボタン表示を通常状態に戻す
                    UpdateBougoDisplay();
                }
            }
        }
        
        /// <summary>
        /// 安全に画像ファイルを読み込む
        /// </summary>
        /// <param name="imagePath">画像ファイルパス</param>
        /// <returns>読み込まれた画像（失敗時はデフォルト画像）</returns>
        private Image LoadImageSafely(string imagePath)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    return Image.FromFile(imagePath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 画像ファイルが見つかりません: {imagePath}");
                    // デフォルト画像を作成
                    var bitmap = new Bitmap(64, 64);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.FillRectangle(Brushes.Gray, 0, 0, 64, 64);
                        g.DrawString("NO IMG", SystemFonts.DefaultFont, Brushes.White, 5, 25);
                    }
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 画像読み込みエラー ({imagePath}): {ex.Message}");
                // エラー時のデフォルト画像を作成
                var bitmap = new Bitmap(64, 64);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.FillRectangle(Brushes.Red, 0, 0, 64, 64);
                    g.DrawString("ERROR", SystemFonts.DefaultFont, Brushes.White, 5, 25);
                }
                return bitmap;
            }
        }
        
        /// <summary>
        /// 現在のゾーン情報を取得
        /// </summary>
        private string GetCurrentZone()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🗺️ GetCurrentZone開始");
                
                // 基本チェック
                if (currentTrainCrewData == null)
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ currentTrainCrewData is null");
                    UpdateDebugDisplay(new List<string>(), "データなし");
                    return "データなし";
                }
                
                if (currentTrainCrewData.myTrainData == null)
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ currentTrainCrewData.myTrainData is null");
                    UpdateDebugDisplay(new List<string>(), "列車データなし");
                    return "列車データなし";
                }
                
                if (currentTrainCrewData.trackCircuitList == null)
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ currentTrainCrewData.trackCircuitList is null");
                    UpdateDebugDisplay(new List<string>(), "軌道回路データなし");
                    return "軌道回路データなし";
                }
                
                if (zoneMappings == null || zoneMappings.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ zoneMappings is null or empty (Count: {zoneMappings?.Count ?? 0})");
                    UpdateDebugDisplay(new List<string>(), "マッピングなし");
                    return "マッピングなし";
                }
                
                string trainName = currentTrainCrewData.myTrainData.diaName;
                if (string.IsNullOrEmpty(trainName))
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ 列車名が空またはnull");
                    UpdateDebugDisplay(new List<string>(), "列車名なし");
                    return "列車名なし";
                }
                
                System.Diagnostics.Debug.WriteLine($"🗺️ 列車名: '{trainName}'");
                System.Diagnostics.Debug.WriteLine($"🗺️ 軌道回路総数: {currentTrainCrewData.trackCircuitList.Count}");
                System.Diagnostics.Debug.WriteLine($"🗺️ ゾーンマッピング総数: {zoneMappings.Count}");
                
                // 現在の自列車在線軌道回路を取得
                var currentTrackCircuits = new List<string>();
                var onTrackCircuits = currentTrainCrewData.trackCircuitList.Where(tc => tc.On).ToList();
                
                System.Diagnostics.Debug.WriteLine($"🗺️ 在線中軌道回路総数: {onTrackCircuits.Count}");
                
                foreach (var tc in onTrackCircuits)
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ 在線軌道回路: '{tc.Name}' - 最終列車: '{tc.Last}'");
                    
                    if (tc.Last == trainName)
                    {
                        currentTrackCircuits.Add(tc.Name);
                        System.Diagnostics.Debug.WriteLine($"🗺️ ✅ 自列車在線: '{tc.Name}'");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"🗺️ 自列車在線軌道回路数: {currentTrackCircuits.Count}");
                
                // 対応するゾーンを収集
                var currentZones = new HashSet<string>();
                foreach (string circuitName in currentTrackCircuits)
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ 軌道回路ゾーンチェック: '{circuitName}'");
                    
                    if (zoneMappings.ContainsKey(circuitName))
                    {
                        string zone = zoneMappings[circuitName];
                        currentZones.Add(zone);
                        System.Diagnostics.Debug.WriteLine($"🗺️ ✅ {circuitName} -> '{zone}'");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"🗺️ ❌ {circuitName} -> マッピング未定義");
                        
                        // 似た名前の軌道回路を探してヒントを提供
                        var similarKeys = zoneMappings.Keys.Where(k => k.Contains(circuitName.Substring(0, Math.Min(4, circuitName.Length)))).Take(3).ToList();
                        if (similarKeys.Any())
                        {
                            System.Diagnostics.Debug.WriteLine($"� 類似軌道回路: {string.Join(", ", similarKeys)}");
                        }
                    }
                }
                
                if (currentZones.Count > 0)
                {
                    string result = string.Join(",", currentZones.OrderBy(z => z));
                    System.Diagnostics.Debug.WriteLine($"🗺️ GetCurrentZone結果: '{result}'");
                    
                    // 有効なゾーンが見つかった場合は最後の有効ゾーンとして保存
                    lastValidZone = result;
                    
                    // デバッグ表示を更新
                    UpdateDebugDisplay(currentTrackCircuits, result);
                    
                    return result;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"🗺️ GetCurrentZone結果: ゾーン未検出");
                    System.Diagnostics.Debug.WriteLine($"🗺️ 詳細: 在線軌道回路{currentTrackCircuits.Count}件、マッピング{zoneMappings.Count}件");
                    
                    // 軌道回路なしの場合、直前の有効ゾーンを返す
                    if (!string.IsNullOrEmpty(lastValidZone))
                    {
                        System.Diagnostics.Debug.WriteLine($"🗺️ 軌道回路なし - 直前の有効ゾーンを使用: '{lastValidZone}'");
                        
                        // デバッグ表示を更新
                        UpdateDebugDisplay(currentTrackCircuits, $"未検出(直前: {lastValidZone})");
                        
                        return lastValidZone;
                    }
                    
                    // デバッグ表示を更新
                    UpdateDebugDisplay(currentTrackCircuits, "未検出");
                    
                    return "未検出";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetCurrentZone例外: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                
                // エラー時のデバッグ表示
                UpdateDebugDisplay(new List<string>(), $"エラー: {ex.Message}");
                
                return "エラー";
            }
        }

        // WndProcをオーバーライドしてホットキーメッセージを処理
        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID_F4)
                {
                    HandleF4KeyPress();
                }
            }
            base.WndProc(ref m);
        }

        private void HandleF4KeyPress()
        {
            System.Diagnostics.Debug.WriteLine("🔥 グローバルF4キーが押されました - 防護無線制御");
            
            lock (audioLock)
            {
                if (!isBougoActive)
                {
                    // 防護無線発砲開始
                    System.Diagnostics.Debug.WriteLine("🚨 防護無線発砲開始");
                    isBougoActive = true;
                    
                    // 受報中の場合は受報音を一時停止（発砲優先）
                    if (isBougoOtherActive)
                    {
                        bougoOtherAudio?.Stop();
                        System.Diagnostics.Debug.WriteLine("⚠️ 発砲優先：受報音を一時停止");
                    }
                    
                    // 防護無線開始時にWindows Audio APIで現在の音量を維持
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 防護無線開始時：Windows Audio APIで{(int)(currentVolume * 100)}%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 防護無線開始時音量設定エラー: {ex.Message}");
                    }
                    
                    // 防護無線中も現在の音量を維持
                    System.Diagnostics.Debug.WriteLine($"🔊 防護無線音量を{(int)(currentVolume * 100)}%で開始");
                    
                    // 防護無線中: 通常音声ループのみ停止（他の音声は継続）
                    shouldPlayLoop = false;
                    // shouldPlayKosyouLoop = false; // 故障音は継続
                    // shouldPlayEBKaihouLoop = false; // EB開放音は継続
                    
                    // 故障音・EB開放音は停止せず継続再生
                    System.Diagnostics.Debug.WriteLine("� 防護無線発砲中 - 故障音・EB開放音は継続再生");
                    
                    // PlayLoopで継続再生（100%音量）
                    // 発砲音再生前に音量を100%に設定
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        currentVolume = 1.0f; // currentVolumeも100%に更新
                        System.Diagnostics.Debug.WriteLine("🔊 発砲音再生前：音量を100%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 発砲音再生前音量設定エラー: {ex.Message}");
                    }
                    
                    bougoF4Audio?.PlayLoop(1.0f);
                    System.Diagnostics.Debug.WriteLine("🔊 防護無線開始: 音量100%");
                    
                    // 防護無線ボタンの表示を更新
                    UpdateBougoDisplay();
                    
                    // SignalRサーバーに発砲通知を送信
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"📡 SignalR発砲通知準備開始");
                            System.Diagnostics.Debug.WriteLine($"   現在時刻: {DateTime.Now:HH:mm:ss.fff}");
                            System.Diagnostics.Debug.WriteLine($"   列車番号: '{currentTrainNumber}'");
                            System.Diagnostics.Debug.WriteLine($"   SignalRクライアント状態: {bougoSignalRClient?.IsConnected}");
                            
                            // デバッグ用: 列車番号がデフォルト値の場合はテスト値を使用
                            string testTrainNumber = currentTrainNumber;
                            if (string.IsNullOrEmpty(currentTrainNumber) || currentTrainNumber == "--" || currentTrainNumber == "0000")
                            {
                                testTrainNumber = "TEST001";
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ デバッグ用テスト列車番号使用: '{testTrainNumber}'");
                            }
                            
                            string currentZone = GetCurrentZone();
                            System.Diagnostics.Debug.WriteLine($"   取得ゾーン: '{currentZone}'");
                            System.Diagnostics.Debug.WriteLine($"   ゾーン取得完了時刻: {DateTime.Now:HH:mm:ss.fff}");
                            
                            // 有効なゾーンが取得できない場合はデフォルトゾーンを使用
                            if (string.IsNullOrEmpty(currentZone) || 
                                currentZone == "未検出" || 
                                currentZone == "不明" || 
                                currentZone == "データなし" || 
                                currentZone == "列車データなし" || 
                                currentZone == "軌道回路データなし" || 
                                currentZone == "マッピングなし" || 
                                currentZone == "列車名なし")
                            {
                                currentZone = "ゾーン1"; // デフォルトゾーン（安全のため）
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ 無効ゾーンのためデフォルト使用: '{currentZone}'");
                            }
                            
                            if (bougoSignalRClient?.IsConnected == true)
                            {
                                // 複数ゾーンの場合は各ゾーンで発報
                                var currentZones = currentZone.Split(',').Select(z => z.Trim()).ToList();
                                foreach (var zone in currentZones)
                                {
                                    await bougoSignalRClient.FireBougoAsync(testTrainNumber, zone);
                                    System.Diagnostics.Debug.WriteLine($"📡 ✅ SignalR発砲通知送信完了: '{testTrainNumber}' @ '{zone}'");
                                    
                                    // 複数ゾーンの場合は少し間隔を空ける
                                    if (currentZones.Count > 1)
                                    {
                                        await Task.Delay(200);
                                    }
                                }
                                
                                // 初回発報時にゾーン監視を開始
                                previousZone = currentZone;
                                lastZoneCheckTime = DateTime.Now;
                                System.Diagnostics.Debug.WriteLine($"🔍 ゾーン監視開始: {currentZone}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"📡 ❌ SignalR接続なし - 発砲通知送信スキップ");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ SignalR発砲通知エラー: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                        }
                    });
                }
                else
                {
                    // 防護無線停止
                    System.Diagnostics.Debug.WriteLine("🔴 防護無線停止");
                    isBougoActive = false;
                    
                    // 防護無線を停止
                    bougoF4Audio?.Stop();
                    
                    // 音量はユーザー設定を維持（リセットしない）
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 防護無線停止時：音量{(int)(currentVolume * 100)}%を維持");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 防護無線停止時音量設定エラー: {ex.Message}");
                    }
                    
                    // 通常音声ループを再開
                    shouldPlayLoop = true;
                    if (!loopStarted)
                    {
                        StartSoundLoop();
                        loopStarted = true;
                    }
                    
                    // 防護無線ボタンの表示を更新
                    UpdateBougoDisplay();
                    
                    // 受報状態が継続中の場合は受報音を再開
                    if (isBougoOtherActive)
                    {
                        bougoOtherAudio?.PlayLoop(currentVolume);
                        System.Diagnostics.Debug.WriteLine("🔄 発砲停止：受報音を再開");
                    }
                    
                    // SignalRサーバーに停止通知を送信
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"📡 SignalR停止通知準備開始");
                            System.Diagnostics.Debug.WriteLine($"   現在時刻: {DateTime.Now:HH:mm:ss.fff}");
                            System.Diagnostics.Debug.WriteLine($"   列車番号: '{currentTrainNumber}'");
                            System.Diagnostics.Debug.WriteLine($"   SignalRクライアント状態: {bougoSignalRClient?.IsConnected}");
                            
                            // デバッグ用: 列車番号がデフォルト値の場合はテスト値を使用
                            string testTrainNumber = currentTrainNumber;
                            if (string.IsNullOrEmpty(currentTrainNumber) || currentTrainNumber == "--" || currentTrainNumber == "0000")
                            {
                                testTrainNumber = "TEST001";
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ デバッグ用テスト列車番号使用: '{testTrainNumber}'");
                            }
                            
                            string currentZone = GetCurrentZone();
                            System.Diagnostics.Debug.WriteLine($"   取得ゾーン: '{currentZone}'");
                            System.Diagnostics.Debug.WriteLine($"   ゾーン取得完了時刻: {DateTime.Now:HH:mm:ss.fff}");
                            
                            // 有効なゾーンが取得できない場合はデフォルトゾーンを使用
                            if (string.IsNullOrEmpty(currentZone) || 
                                currentZone == "未検出" || 
                                currentZone == "不明" || 
                                currentZone == "データなし" || 
                                currentZone == "列車データなし" || 
                                currentZone == "軌道回路データなし" || 
                                currentZone == "マッピングなし" || 
                                currentZone == "列車名なし")
                            {
                                currentZone = "ゾーン1"; // デフォルトゾーン（安全のため）
                                System.Diagnostics.Debug.WriteLine($"   ⚠️ 無効ゾーンのためデフォルト使用: '{currentZone}'");
                            }
                            
                            if (bougoSignalRClient?.IsConnected == true)
                            {
                                // 停止時は直前のゾーン（previousZone）を使用して各ゾーンで停止
                                string zoneToStop = !string.IsNullOrEmpty(previousZone) ? previousZone : currentZone;
                                var zonesToStop = zoneToStop.Split(',').Select(z => z.Trim()).ToList();
                                
                                foreach (var zone in zonesToStop)
                                {
                                    await bougoSignalRClient.StopBougoAsync(testTrainNumber, zone);
                                    System.Diagnostics.Debug.WriteLine($"📡 ✅ SignalR停止通知送信完了: '{testTrainNumber}' @ '{zone}'");
                                    
                                    // 複数ゾーンの場合は少し間隔を空ける
                                    if (zonesToStop.Count > 1)
                                    {
                                        await Task.Delay(200);
                                    }
                                }
                                
                                // 防護無線停止時にゾーン監視をリセット
                                previousZone = null;
                                lastValidZone = ""; // 最後の有効ゾーンもリセット
                                lastZoneCheckTime = DateTime.MinValue;
                                System.Diagnostics.Debug.WriteLine($"🔍 ゾーン監視停止");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"📡 ❌ SignalR接続なし - 停止通知送信スキップ");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ SignalR停止通知エラー: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                        }
                    });
                }
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F4)
            {
                HandleF4KeyPress();
                e.Handled = true;
            }
        }

        private void LoadZoneMappings()
        {
            System.Diagnostics.Debug.WriteLine($"🗺️ ハードコードゾーンマッピング初期化開始: {DateTime.Now:HH:mm:ss.fff}");
            
            // JSONファイルの内容を直接ハードコーディング（214件の完全なマッピング）
            zoneMappings = new Dictionary<string, string>
            {
                // ゾーン1（47件）
                {"TH75_1RET", "ゾーン1"},
                {"TH75_1RT", "ゾーン1"},
                {"TH75_34LT", "ゾーン1"},
                {"TH75_41イT", "ゾーン1"},
                {"TH75_41ロT", "ゾーン1"},
                {"TH75_42イT", "ゾーン1"},
                {"TH75_42ロT", "ゾーン1"},
                {"TH75_43イT", "ゾーン1"},
                {"TH75_43ロT", "ゾーン1"},
                {"TH75_44T", "ゾーン1"},
                {"TH75_45T", "ゾーン1"},
                {"TH75_46イT", "ゾーン1"},
                {"TH75_46ロT", "ゾーン1"},
                {"TH75_48T", "ゾーン1"},
                {"TH75_49イT", "ゾーン1"},
                {"TH75_49ロT", "ゾーン1"},
                {"TH75_4T", "ゾーン1"},
                {"TH75_50イT", "ゾーン1"},
                {"TH75_50ロT", "ゾーン1"},
                {"TH75_5RAT", "ゾーン1"},
                {"TH75_5RBT", "ゾーン1"},
                {"TH75_5T", "ゾーン1"},
                {"TH75_6LT", "ゾーン1"},
                {"TH75_6T", "ゾーン1"},
                {"TH75_7T", "ゾーン1"},
                {"TH75_8T", "ゾーン1"},
                {"TH75_9LCT", "ゾーン1"},
                {"TH75_9LT", "ゾーン1"},
                {"TH75_9T", "ゾーン1"},
                {"TH75_SST", "ゾーン1"},
                {"TH75_TST", "ゾーン1"},
                {"TH76_21イT", "ゾーン1"},
                {"TH76_21ロT", "ゾーン1"},
                {"TH76_22T", "ゾーン1"},
                {"TH76_23T", "ゾーン1"},
                {"TH76_24T", "ゾーン1"},
                {"TH76_25T", "ゾーン1"},
                {"TH76_26イT", "ゾーン1"},
                {"TH76_26ロT", "ゾーン1"},
                {"TH76_27T", "ゾーン1"},
                {"TH76_5LAT", "ゾーン1"},
                {"TH76_5LBT", "ゾーン1"},
                {"TH76_5LCT", "ゾーン1"},
                {"TH76_5LDT", "ゾーン1"},
                {"上り6T", "ゾーン1"},
                {"上り8T", "ゾーン1"},
                {"下り7T", "ゾーン1"},
                {"下り9T", "ゾーン1"},
                
                // ゾーン2（16件）
                {"下り27T", "ゾーン2"},
                {"下り35T", "ゾーン2"},
                {"下り41T", "ゾーン2"},
                {"下り45T", "ゾーン2"},
                {"下り49T", "ゾーン2"},
                {"下り55T", "ゾーン2"},
                {"下り59T", "ゾーン2"},
                {"下り67T", "ゾーン2"},
                {"下り71T", "ゾーン2"},
                {"上り26T", "ゾーン2"},
                {"上り30T", "ゾーン2"},
                {"上り36T", "ゾーン2"},
                {"上り42T", "ゾーン2"},
                {"上り48T", "ゾーン2"},
                {"上り56T", "ゾーン2"},
                {"上り62T", "ゾーン2"},
                
                // ゾーン3（20件）
                {"下り75T", "ゾーン3"},
                {"下り89T", "ゾーン3"},
                {"TH70_1RAT", "ゾーン3"},
                {"TH70_21イT", "ゾーン3"},
                {"TH70_21ロT", "ゾーン3"},
                {"TH70_2LT", "ゾーン3"},
                {"TH70_5LBT", "ゾーン3"},
                {"TH70_5LT", "ゾーン3"},
                {"TH70_SST", "ゾーン3"},
                {"TH71_1RAT", "ゾーン3"},
                {"TH71_1RBT", "ゾーン3"},
                {"TH71_1RT", "ゾーン3"},
                {"TH71_21T", "ゾーン3"},
                {"TH71_22T", "ゾーン3"},
                {"TH71_23T", "ゾーン3"},
                {"TH71_24T", "ゾーン3"},
                {"TH71_6LCT", "ゾーン3"},
                {"TH71_6LDT", "ゾーン3"},
                {"TH71_6LT", "ゾーン3"},
                {"TH71_SST", "ゾーン3"},
                {"TH71_TST", "ゾーン3"},
                {"上り68T", "ゾーン3"},
                {"上り74T", "ゾーン3"},
                {"上り86T", "ゾーン3"},
                {"上り92T", "ゾーン3"},
                
                // ゾーン4（9件）
                {"下り103T", "ゾーン4"},
                {"下り105T", "ゾーン4"},
                {"下り111T", "ゾーン4"},
                {"下り117T", "ゾーン4"},
                {"下り123T", "ゾーン4"},
                {"上り102T", "ゾーン4"},
                {"上り108T", "ゾーン4"},
                {"上り114T", "ゾーン4"},
                {"上り120T", "ゾーン4"},
                
                // ゾーン5（19件）
                {"下り137T", "ゾーン5"},
                {"下り143T", "ゾーン5"},
                {"下り145T", "ゾーン5"},
                {"TH67_10LT", "ゾーン5"},
                {"TH67_1RAT", "ゾーン5"},
                {"TH67_1RBT", "ゾーン5"},
                {"TH67_1RT", "ゾーン5"},
                {"TH67_23RT", "ゾーン5"},
                {"TH67_31T", "ゾーン5"},
                {"TH67_32T", "ゾーン5"},
                {"TH67_33イT", "ゾーン5"},
                {"TH67_33ロT", "ゾーン5"},
                {"TH67_34T", "ゾーン5"},
                {"TH67_35イT", "ゾーン5"},
                {"TH67_35ロT", "ゾーン5"},
                {"TH67_36イT", "ゾーン5"},
                {"TH67_36ロT", "ゾーン5"},
                {"TH67_4LT", "ゾーン5"},
                {"TH67_5LT", "ゾーン5"},
                {"TH67_SST", "ゾーン5"},
                {"TH67_TST", "ゾーン5"},
                {"上り124T", "ゾーン5"},
                {"上り136T", "ゾーン5"},
                {"上り142T", "ゾーン5"},
                
                // ゾーン6（34件）
                {"下り151T", "ゾーン6"},
                {"TH65_11LT", "ゾーン6"},
                {"TH65_12LT", "ゾーン6"},
                {"TH65_1RT", "ゾーン6"},
                {"TH65_2RT", "ゾーン6"},
                {"TH65_3RT", "ゾーン6"},
                {"TH65_41T", "ゾーン6"},
                {"TH65_42イT", "ゾーン6"},
                {"TH65_42ロT", "ゾーン6"},
                {"TH65_44T", "ゾーン6"},
                {"TH65_45T", "ゾーン6"},
                {"TH65_47T", "ゾーン6"},
                {"TH65_48T", "ゾーン6"},
                {"TH65_49T", "ゾーン6"},
                {"TH65_50イT", "ゾーン6"},
                {"TH65_50ロT", "ゾーン6"},
                {"TH65_5T", "ゾーン6"},
                {"TH65_6T", "ゾーン6"},
                {"TH65_ET", "ゾーン6"},
                {"TH65_TST", "ゾーン6"},
                {"TH65_XT", "ゾーン6"},
                {"TH65_YT", "ゾーン6"},
                {"TH66S_13T", "ゾーン6"},
                {"TH66S_1RAT", "ゾーン6"},
                {"TH66S_1RBT", "ゾーン6"},
                {"TH66S_1RCT", "ゾーン6"},
                {"TH66S_1RT", "ゾーン6"},
                {"TH66S_51イT", "ゾーン6"},
                {"TH66S_51ロT", "ゾーン6"},
                {"TH66S_52T", "ゾーン6"},
                {"TH66S_53T", "ゾーン6"},
                {"TH66S_54T", "ゾーン6"},
                {"TH66S_55T", "ゾーン6"},
                {"TH66S_56T", "ゾーン6"},
                {"TH66S_57T", "ゾーン6"},
                {"TH66S_5LDT", "ゾーン6"},
                {"TH66S_5LET", "ゾーン6"},
                {"TH66S_5LT", "ゾーン6"},
                {"上り146T", "ゾーン6"},
                {"上り156T", "ゾーン6"},
                
                // ゾーン7（10件）
                {"DF1T", "ゾーン7"},
                {"DF2T", "ゾーン7"},
                {"TH64_12LT", "ゾーン7"},
                {"TH64_12RT", "ゾーン7"},
                {"TH64_13LT", "ゾーン7"},
                {"TH64_14RT", "ゾーン7"},
                {"TH64_15LT", "ゾーン7"},
                {"TH64_15RT", "ゾーン7"},
                {"TH64_21T", "ゾーン7"},
                {"TH64_22T", "ゾーン7"},
                
                // ゾーン8（9件）
                {"FMT", "ゾーン8"},
                {"MT1T", "ゾーン8"},
                {"MT2T", "ゾーン8"},
                {"TH63_12RT", "ゾーン8"},
                {"TH63_15LT", "ゾーン8"},
                {"TH63_21イT", "ゾーン8"},
                {"TH63_21ロT", "ゾーン8"},
                {"TH63_22イT", "ゾーン8"},
                {"TH63_22ロT", "ゾーン8"},
                
                // ゾーン9（18件）
                {"TH1T", "ゾーン9"},
                {"TH2T", "ゾーン9"},
                {"TH61_21イT", "ゾーン9"},
                {"TH61_21ロT", "ゾーン9"},
                {"TH61_22T", "ゾーン9"},
                {"TH61_2RAT", "ゾーン9"},
                {"TH61_2RBT", "ゾーン9"},
                {"TH61_2RT", "ゾーン9"},
                {"TH61_5RT", "ゾーン9"},
                {"TH61_6LT", "ゾーン9"},
                {"TH62_12LT", "ゾーン9"},
                {"TH62_12RT", "ゾーン9"},
                {"TH62_13LT", "ゾーン9"},
                {"TH62_14RT", "ゾーン9"},
                {"TH62_15LT", "ゾーン9"},
                {"TH62_15RT", "ゾーン9"},
                {"TH62_21T", "ゾーン9"},
                {"TH62_22T", "ゾーン9"},
                
                // ゾーン10（14件）
                {"下り237T", "ゾーン10"},
                {"下り241T", "ゾーン10"},
                {"下り245T", "ゾーン10"},
                {"下り249T", "ゾーン10"},
                {"TH59_11RT", "ゾーン10"},
                {"TH59_12LT", "ゾーン10"},
                {"TH59_13LT", "ゾーン10"},
                {"TH59_21イT", "ゾーン10"},
                {"TH59_21ロT", "ゾーン10"},
                {"上り238T", "ゾーン10"},
                {"上り242T", "ゾーン10"},
                {"上り246T", "ゾーン10"},
                {"上り250T", "ゾーン10"},
                
                // ゾーン11（16件）
                {"NA1T", "ゾーン11"},
                {"NA2T", "ゾーン11"},
                {"TH58_21T", "ゾーン11"},
                {"TH58_22イT", "ゾーン11"},
                {"TH58_22ロT", "ゾーン11"},
                {"TH58_23イT", "ゾーン11"},
                {"TH58_23ロT", "ゾーン11"},
                {"TH58_24T", "ゾーン11"},
                {"TH58_25T", "ゾーン11"},
                {"TH58_2RAT", "ゾーン11"},
                {"TH58_2RBT", "ゾーン11"},
                {"TH58_2RT", "ゾーン11"},
                {"TH58_9LCT", "ゾーン11"},
                {"TH58_9LT", "ゾーン11"},
                {"TH58_DT", "ゾーン11"},
                {"TH58_ET", "ゾーン11"}
            };
            
            System.Diagnostics.Debug.WriteLine($"✅ ハードコードゾーンマッピング初期化完了: {zoneMappings.Count}件");
            System.Diagnostics.Debug.WriteLine($"🗺️ 各ゾーンの軌道回路数:");
            
            // ゾーン別の統計を表示
            for (int zone = 1; zone <= 11; zone++)
            {
                var zoneKey = $"ゾーン{zone}";
                var count = zoneMappings.Values.Count(v => v == zoneKey);
                System.Diagnostics.Debug.WriteLine($"   {zoneKey}: {count}件");
            }
            
            // 重要な軌道回路の確認
            var importantCircuits = new[] { "TH58_9LCT", "TH76_5LDT", "下り27T", "上り26T", "上り108T" };
            foreach (var circuit in importantCircuits)
            {
                if (zoneMappings.ContainsKey(circuit))
                {
                    System.Diagnostics.Debug.WriteLine($"✅ {circuit} → {zoneMappings[circuit]}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ {circuit}が見つかりません");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"�️ ハードコードマッピング完了: {DateTime.Now:HH:mm:ss.fff}");
        }

        /// <summary>
        /// 最小限のハードコードマッピング（JSONファイルが使用できない場合の緊急フォールバック）
        /// </summary>
        private void LoadMinimalHardcodedZoneMappings()
        {
            System.Diagnostics.Debug.WriteLine($"📋 最小限マッピング読み込み開始");
            
            // 緊急時のみの最小限マッピング（JSONファイルを使用することが前提）
            var emergencyMappingData = new Dictionary<string, string>
            {
                // 実際に確認された軌道回路のみ
                {"上り108T", "ゾーン4"}, // 実際のデータで確認済み
                {"TH76_5LDT", "ゾーン1"}, // 実際のデータで確認済み
                {"下り27T", "ゾーン2"}, 
                {"上り26T", "ゾーン2"}
            };
            
            int addedCount = 0;
            foreach (var mapping in emergencyMappingData)
            {
                if (!zoneMappings.ContainsKey(mapping.Key))
                {
                    zoneMappings[mapping.Key] = mapping.Value;
                    addedCount++;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"⚠️ 緊急時最小限マッピング追加: {addedCount}件 (総計: {zoneMappings.Count}件)");
            System.Diagnostics.Debug.WriteLine($"� 通常運用ではZoneMapping.jsonファイルを使用してください");
        }

        /// <summary>
        /// パターンマッチングによるゾーン検索（柔軟なマッピング）
        /// </summary>
        /// <param name="circuitName">軌道回路名</param>
        /// <returns>見つかったゾーン名、見つからない場合は空文字</returns>
        private string TryFindZoneByPattern(string circuitName)
        {
            if (string.IsNullOrEmpty(circuitName)) return "";

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 パターンマッチング開始: '{circuitName}'");

                // パターン1: 駅番号ベース (TH75, TH76 = ゾーン1, TH77, TH78 = ゾーン2, etc.)
                if (circuitName.StartsWith("TH"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(circuitName, @"TH(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int stationNum))
                    {
                        // 駅番号からゾーンを推定
                        if (stationNum >= 75 && stationNum <= 76) return "ゾーン1";
                        if (stationNum >= 77 && stationNum <= 78) return "ゾーン2";
                        if (stationNum >= 70 && stationNum <= 74) return "ゾーン3";
                        if (stationNum >= 79 && stationNum <= 80) return "ゾーン4";
                        if (stationNum >= 81 && stationNum <= 82) return "ゾーン5";
                        if (stationNum >= 83 && stationNum <= 84) return "ゾーン6";
                        if (stationNum >= 85 && stationNum <= 86) return "ゾーン7";
                        
                        System.Diagnostics.Debug.WriteLine($"   駅番号パターン: {stationNum} → 推定ゾーン特定済み");
                    }
                }

                // パターン2: 上り下りベース
                if (circuitName.Contains("下り"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(circuitName, @"下り(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int trackNum))
                    {
                        // 100番台の軌道回路パターン
                        if (trackNum >= 100 && trackNum <= 109) return "ゾーン2";
                        if (trackNum >= 110 && trackNum <= 119) return "ゾーン3";
                        if (trackNum >= 120 && trackNum <= 129) return "ゾーン4";
                        if (trackNum >= 130 && trackNum <= 139) return "ゾーン5";
                        if (trackNum >= 140 && trackNum <= 149) return "ゾーン6";
                        if (trackNum >= 150 && trackNum <= 159) return "ゾーン7";
                        
                        // 既存パターン
                        if (trackNum >= 27 && trackNum <= 30) return "ゾーン2";
                        if (trackNum >= 31 && trackNum <= 32) return "ゾーン4";
                        if (trackNum >= 33 && trackNum <= 34) return "ゾーン5";
                        if (trackNum >= 35 && trackNum <= 36) return "ゾーン6";
                        if (trackNum >= 37 && trackNum <= 38) return "ゾーン7";
                    }
                }

                if (circuitName.Contains("上り"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(circuitName, @"上り(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int trackNum))
                    {
                        // 100番台の軌道回路パターン
                        if (trackNum >= 100 && trackNum <= 109) return "ゾーン2";
                        if (trackNum >= 110 && trackNum <= 119) return "ゾーン3";
                        if (trackNum >= 120 && trackNum <= 129) return "ゾーン4";
                        if (trackNum >= 130 && trackNum <= 139) return "ゾーン5";
                        if (trackNum >= 140 && trackNum <= 149) return "ゾーン6";
                        if (trackNum >= 150 && trackNum <= 159) return "ゾーン7";
                        
                        // 既存パターン
                        if (trackNum >= 23 && trackNum <= 26) return "ゾーン2";
                        if (trackNum >= 21 && trackNum <= 22) return "ゾーン4";
                        if (trackNum >= 19 && trackNum <= 20) return "ゾーン5";
                        if (trackNum >= 17 && trackNum <= 18) return "ゾーン6";
                        if (trackNum >= 15 && trackNum <= 16) return "ゾーン7";
                    }
                }

                // パターン3: 既存のマッピングとの部分マッチ
                var partialMatches = zoneMappings.Keys.Where(key => 
                    key.Contains(circuitName) || circuitName.Contains(key) ||
                    (circuitName.Length >= 4 && key.Contains(circuitName.Substring(0, 4)))
                ).ToList();
                
                if (partialMatches.Any())
                {
                    var bestMatch = partialMatches.OrderByDescending(m => m.Length).First();
                    System.Diagnostics.Debug.WriteLine($"   部分マッチ: '{circuitName}' ≈ '{bestMatch}'");
                    return zoneMappings[bestMatch];
                }

                // パターン4: 単純なパターンマッチ（数字ベース）
                var numberMatch = System.Text.RegularExpressions.Regex.Match(circuitName, @"(\d+)");
                if (numberMatch.Success && int.TryParse(numberMatch.Groups[1].Value, out int num))
                {
                    // 数字に基づく簡易ゾーン割り当て
                    int zone = ((num - 1) % 7) + 1; // 1-7の循環
                    System.Diagnostics.Debug.WriteLine($"   数字パターン: {num} → ゾーン{zone}");
                    return $"ゾーン{zone}";
                }

                System.Diagnostics.Debug.WriteLine($"   パターンマッチング失敗");
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ パターンマッチングエラー: {ex.Message}");
                return "";
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // 音声管理初期化
            audioManager = new AudioManager();
            bougomusenno = audioManager.AddAudio("Sound/bougomusenno.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
            bougoF4Audio = audioManager.AddAudio("Sound/bougo.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
            bougoOtherAudio = audioManager.AddAudio("Sound/bougoOther.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
            set_trainnum = audioManager.AddAudio("Sound/set_trainnum.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
            set_complete = audioManager.AddAudio("Sound/set_complete.wav", 1.0f, TakumiteAudioWrapper.AudioType.System);
            kosyou = audioManager.AddAudio("Sound/kosyou.wav", 1.0f, TakumiteAudioWrapper.AudioType.System);
            kosyou_koe = audioManager.AddAudio("Sound/kosyou_koe.wav", 1.0f, TakumiteAudioWrapper.AudioType.System);
            ebkaihou = audioManager.AddAudio("Sound/EBkaihou.wav", 1.0f, TakumiteAudioWrapper.AudioType.System);
            ebkaihou_koe = audioManager.AddAudio("Sound/EBkaihou_koe.wav", 1.0f, TakumiteAudioWrapper.AudioType.System);
            
            // 音声ファイルの存在確認
            System.Diagnostics.Debug.WriteLine("=== 音声ファイル確認 ===");
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var bougoPath = System.IO.Path.Combine(exeDir, "Sound/bougomusenno.wav");
            var bougoF4Path = System.IO.Path.Combine(exeDir, "Sound/bougo.wav");
            var bougoOtherPath = System.IO.Path.Combine(exeDir, "Sound/bougoOther.wav");
            var trainnumPath = System.IO.Path.Combine(exeDir, "Sound/set_trainnum.wav");
            var completePath = System.IO.Path.Combine(exeDir, "Sound/set_complete.wav");
            var kosyouPath = System.IO.Path.Combine(exeDir, "Sound/kosyou.wav");
            var kosyouKoePath = System.IO.Path.Combine(exeDir, "Sound/kosyou_koe.wav");
            var ebkaihouPath = System.IO.Path.Combine(exeDir, "Sound/EBkaihou.wav");
            var ebkaihouKoePath = System.IO.Path.Combine(exeDir, "Sound/EBkaihou_koe.wav");
            
            System.Diagnostics.Debug.WriteLine($"防護無線: {bougoPath} - {System.IO.File.Exists(bougoPath)}");
            System.Diagnostics.Debug.WriteLine($"防護音F4: {bougoF4Path} - {System.IO.File.Exists(bougoF4Path)}");
            System.Diagnostics.Debug.WriteLine($"防護受報音: {bougoOtherPath} - {System.IO.File.Exists(bougoOtherPath)}");
            System.Diagnostics.Debug.WriteLine($"列車番号: {trainnumPath} - {System.IO.File.Exists(trainnumPath)}");
            
            // 音声オブジェクトの初期化状況を確認
            System.Diagnostics.Debug.WriteLine("=== 音声オブジェクト初期化確認 ===");
            System.Diagnostics.Debug.WriteLine($"bougomusenno != null: {bougomusenno != null}");
            System.Diagnostics.Debug.WriteLine($"bougoF4Audio != null: {bougoF4Audio != null}");
            System.Diagnostics.Debug.WriteLine($"bougoOtherAudio != null: {bougoOtherAudio != null}");
            System.Diagnostics.Debug.WriteLine($"set_trainnum != null: {set_trainnum != null}");
            System.Diagnostics.Debug.WriteLine($"完了音: {completePath} - {System.IO.File.Exists(completePath)}");
            System.Diagnostics.Debug.WriteLine($"故障音: {kosyouPath} - {System.IO.File.Exists(kosyouPath)}");
            System.Diagnostics.Debug.WriteLine($"故障音声: {kosyouKoePath} - {System.IO.File.Exists(kosyouKoePath)}");
            System.Diagnostics.Debug.WriteLine($"EB開放音: {ebkaihouPath} - {System.IO.File.Exists(ebkaihouPath)}");
            System.Diagnostics.Debug.WriteLine($"EB開放音声: {ebkaihouKoePath} - {System.IO.File.Exists(ebkaihouKoePath)}");
            System.Diagnostics.Debug.WriteLine("==================");
            
            // 音声ループ開始（一度だけ）
            if (!loopStarted)
            {
                StartSoundLoop();
                loopStarted = true;
            }

            // 非常ブレーキロジックを初期化
            InitializeEmergencyBrakeLogic();

            // UI イベントハンドラーを接続
            ConnectUIEventHandlers();

            // EB開放中の故障ランプ点滅タイマーを初期化
            ebBlinkTimer = new System.Windows.Forms.Timer();
            ebBlinkTimer.Interval = 500; // 500ms間隔で点滅
            ebBlinkTimer.Tick += EBBlinkTimer_Tick;

            // 受報時の防護無線ボタン点滅タイマーを初期化
            bougoBlinkTimer = new System.Windows.Forms.Timer();
            bougoBlinkTimer.Interval = 500; // 0.5秒間隔で点滅
            bougoBlinkTimer.Tick += BougoBlinkTimer_Tick;

            // 故障コード表示タイマーを初期化
            failureCodeTimer = new System.Windows.Forms.Timer();
            failureCodeTimer.Interval = 2000; // 2秒間隔で切り替え
            failureCodeTimer.Tick += FailureCodeTimer_Tick;

            // ドットアニメーションタイマーを初期化
            dotAnimationTimer = new System.Windows.Forms.Timer();
            dotAnimationTimer.Interval = 300; // 300ms間隔でドット表示
            dotAnimationTimer.Tick += DotAnimationTimer_Tick;

            // LCD表示を初期化（故障コードなしの状態）
            UpdateFailureCodeDisplay();

            // デバッグ表示ラベルを初期化
            InitializeDebugLabels();

            // TrainCrewクライアントを安全に初期化（エラーが発生してもフォーム表示を妨げない）
            try
            {
                InitializeTrainCrewClient();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrainCrewクライアント初期化エラー: {ex.Message}");
            }
            
            // ゾーン移動監視タイマーを開始
            zoneCheckTimer.Start();
            System.Diagnostics.Debug.WriteLine("🗺️ ゾーン移動監視タイマー開始");
        }

        /// <summary>
        /// デバッグ表示ラベルを初期化
        /// </summary>
        private void InitializeDebugLabels()
        {
            // 軌道回路表示ラベル
            debugTrackCircuitLabel = new Label();
            debugTrackCircuitLabel.Name = "debugTrackCircuitLabel";
            debugTrackCircuitLabel.Text = "軌道回路: 取得中...";
            debugTrackCircuitLabel.Location = new Point(10, this.Height - 80);
            debugTrackCircuitLabel.Size = new Size(this.Width - 20, 20);
            debugTrackCircuitLabel.Font = new Font("MS Gothic", 9);
            debugTrackCircuitLabel.ForeColor = Color.Yellow;
            debugTrackCircuitLabel.BackColor = Color.Black;
            debugTrackCircuitLabel.BorderStyle = BorderStyle.FixedSingle;
            debugTrackCircuitLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.Controls.Add(debugTrackCircuitLabel);

            // ゾーン判定表示ラベル
            debugZoneLabel = new Label();
            debugZoneLabel.Name = "debugZoneLabel";
            debugZoneLabel.Text = "判定ゾーン: 取得中...";
            debugZoneLabel.Location = new Point(10, this.Height - 55);
            debugZoneLabel.Size = new Size(this.Width - 20, 20);
            debugZoneLabel.Font = new Font("MS Gothic", 9);
            debugZoneLabel.ForeColor = Color.Lime;
            debugZoneLabel.BackColor = Color.Black;
            debugZoneLabel.BorderStyle = BorderStyle.FixedSingle;
            debugZoneLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            this.Controls.Add(debugZoneLabel);

            System.Diagnostics.Debug.WriteLine("✅ デバッグラベル初期化完了");
        }

        /// <summary>
        /// デバッグ情報を画面に更新表示
        /// </summary>
        /// <param name="trackCircuits">現在の軌道回路リスト</param>
        /// <param name="detectedZone">検出されたゾーン</param>
        private void UpdateDebugDisplay(List<string> trackCircuits, string detectedZone)
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new Action<List<string>, string>(UpdateDebugDisplay), trackCircuits, detectedZone);
                    return;
                }

                if (debugTrackCircuitLabel != null)
                {
                    string trackCircuitText = trackCircuits.Count > 0 
                        ? $"軌道回路({trackCircuits.Count}件): {string.Join(", ", trackCircuits.Take(5))}{(trackCircuits.Count > 5 ? "..." : "")}"
                        : "軌道回路: 在線なし";
                    
                    debugTrackCircuitLabel.Text = trackCircuitText;
                }

                if (debugZoneLabel != null)
                {
                    string statusInfo = "";
                    if (currentTrainCrewData == null) statusInfo = " [TrainCrewデータなし]";
                    else if (currentTrainCrewData.trackCircuitList == null) statusInfo = " [軌道回路リストなし]";
                    else if (currentTrainCrewData.trackCircuitList.Count == 0) statusInfo = " [軌道回路リスト空]";
                    else if (!currentTrainCrewData.trackCircuitList.Any(tc => tc.On)) statusInfo = " [在線軌道回路なし]";
                    else statusInfo = $" [全軌道回路{currentTrainCrewData.trackCircuitList.Count}件]";
                    
                    debugZoneLabel.Text = $"判定ゾーン: {detectedZone} ({DateTime.Now:HH:mm:ss}){statusInfo}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ デバッグ表示更新エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ゾーン変更チェックタイマーのイベントハンドラー
        /// ゾーン移動を検知し、自動で再発報を行う
        /// </summary>
        private async void ZoneCheckTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 防護無線発報中のみチェック
                if (!isBougoActive)
                    return;

                // 同時実行を防ぐためのロック
                if (!Monitor.TryEnter(zoneMovementLock))
                    return;

                try
                {
                    // 現在のゾーンを取得
                    string currentZone = GetCurrentZone();
                    
                    // ゾーンが変更されたかチェック
                    if (!string.IsNullOrEmpty(previousZone) && 
                        !string.IsNullOrEmpty(currentZone) && 
                        previousZone != currentZone)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔄 ゾーン移動検知: {previousZone} → {currentZone}");
                        
                        // サーバーから現在の受報ゾーンの発報状況をチェック
                        await CheckActiveFiresOnZoneChange(currentZone);
                        
                        // UIに移動を表示
                        if (InvokeRequired)
                        {
                            Invoke(new Action(() => {
                                if (debugZoneLabel != null)
                                {
                                    debugZoneLabel.Text = $"ゾーン移動検知: {previousZone} → {currentZone} 再発報中...";
                                }
                            }));
                        }
                        else
                        {
                            if (debugZoneLabel != null)
                            {
                                debugZoneLabel.Text = $"ゾーン移動検知: {previousZone} → {currentZone} 再発報中...";
                            }
                        }

                        // 現在の列車番号を取得
                        string testTrainNumber = currentTrainNumber;
                        if (string.IsNullOrEmpty(currentTrainNumber) || currentTrainNumber == "--" || currentTrainNumber == "0000")
                        {
                            testTrainNumber = "TEST001"; // デバッグ用
                        }

                        // 1. まず現在の発報を停止
                        System.Diagnostics.Debug.WriteLine("📢 再発報のため一時停止中...");
                        
                        // 前回が複数ゾーンの場合は各ゾーンで停止
                        var previousZones = previousZone.Split(',').Select(z => z.Trim()).ToList();
                        foreach (var zone in previousZones)
                        {
                            await bougoSignalRClient.StopBougoAsync(testTrainNumber, zone);
                            System.Diagnostics.Debug.WriteLine($"📢 {zone}で停止通知送信");
                        }

                        // 少し待機（サーバー処理のため）
                        await Task.Delay(500);

                        // 2. 新しいゾーンで再発報
                        System.Diagnostics.Debug.WriteLine($"📢 新ゾーン {currentZone} で再発報開始");
                        
                        // 現在が複数ゾーンの場合は各ゾーンで発報
                        var currentZones = currentZone.Split(',').Select(z => z.Trim()).ToList();
                        foreach (var zone in currentZones)
                        {
                            await bougoSignalRClient.FireBougoAsync(testTrainNumber, zone);
                            System.Diagnostics.Debug.WriteLine($"📢 {zone}で発報通知送信");
                            
                            // 複数ゾーンの場合は少し間隔を空ける
                            if (currentZones.Count > 1)
                            {
                                await Task.Delay(200);
                            }
                        }

                        System.Diagnostics.Debug.WriteLine($"✅ ゾーン移動対応完了: {previousZone} → {currentZone}");
                    }

                    // 前回のゾーンを更新
                    previousZone = currentZone;
                    lastZoneCheckTime = DateTime.Now;
                }
                finally
                {
                    Monitor.Exit(zoneMovementLock);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ゾーンチェックエラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// ゾーン変更時にサーバーからアクティブな発報をチェックし、必要に応じて受報状態を更新
        /// </summary>
        /// <param name="newZone">新しいゾーン</param>
        private async Task CheckActiveFiresOnZoneChange(string newZone)
        {
            try
            {
                // 新しいゾーンが複数ゾーンの場合は各ゾーンをチェック
                var newZones = newZone.Split(',').Select(z => z.Trim()).ToList();
                
                foreach (var zone in newZones)
                {
                    // サーバーから現在のゾーンに影響するアクティブな発報を取得
                    var affectingFires = await BougoApiClient.GetAffectingFiresAsync(zone);
                    
                    if (affectingFires.Any())
                    {
                        System.Diagnostics.Debug.WriteLine($"🚨 ゾーン{zone}に影響するアクティブ発報を検出: {affectingFires.Count}件");
                        
                        foreach (var fire in affectingFires)
                        {
                            System.Diagnostics.Debug.WriteLine($"   - 列車番号: {fire.TrainNumber}, 発報ゾーン: {fire.Zone}, 発報時刻: {fire.FireTime:HH:mm:ss}");
                            
                            // UI更新（受報表示を更新）
                            if (InvokeRequired)
                            {
                                Invoke(new Action(() => {
                                    // 他列車からの発報による受報状態を反映
                                    OnBougoFiredReceived(fire.TrainNumber, fire.Zone);
                                }));
                            }
                            else
                            {
                                OnBougoFiredReceived(fire.TrainNumber, fire.Zone);
                            }
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ ゾーン{zone}にアクティブな発報なし");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ アクティブ発報チェックエラー: {ex.Message}");
            }
        }

        private async void StartSoundLoop()
        {
            // 音声の長さを事前に取得してログ出力
            int bougoDurationMs = bougomusenno?.GetDurationMs() ?? 8000;
            int trainnumDurationMs = set_trainnum?.GetDurationMs() ?? 4000;
            
            System.Diagnostics.Debug.WriteLine($"=== 音声長情報 ===");
            System.Diagnostics.Debug.WriteLine($"防護無線音声長: {bougoDurationMs}ms");
            System.Diagnostics.Debug.WriteLine($"列車番号設定音声長: {trainnumDurationMs}ms");
            System.Diagnostics.Debug.WriteLine($"===============");

            await Task.Run(async () =>
            {
                while (shouldPlayLoop)
                {
                    // 防護無線発砲中またはEB開放中は通常ループを停止
                    if (!shouldPlayLoop || isBougoActive || emergencyBrakeButtonState) break;
                    
                    // bougomusenno.wavを再生（通常時の防護無線アナウンス）
                    System.Diagnostics.Debug.WriteLine($"防護無線音声開始: {DateTime.Now:HH:mm:ss.fff}");
                    bougomusenno?.PlayOnce(systemVolume);
                    
                    // 実際の音声長分待機
                    await Task.Delay(bougoDurationMs);
                    System.Diagnostics.Debug.WriteLine($"防護無線音声終了: {DateTime.Now:HH:mm:ss.fff}");
                    
                    if (!shouldPlayLoop || isBougoActive || emergencyBrakeButtonState) break;
                    
                    // set_trainnum.wavを再生
                    System.Diagnostics.Debug.WriteLine($"列車番号設定音声開始: {DateTime.Now:HH:mm:ss.fff}");
                    set_trainnum?.PlayOnce(systemVolume);
                    
                    // 実際の音声長分待機
                    await Task.Delay(trainnumDurationMs);
                    System.Diagnostics.Debug.WriteLine($"列車番号設定音声終了: {DateTime.Now:HH:mm:ss.fff}");
                }
            });
        }

        // 静的メソッドで音声完了処理
        public static void StopSoundLoop()
        {
            lock (audioLock)
            {
                shouldPlayLoop = false;
                System.Diagnostics.Debug.WriteLine("音声ループを停止しました");
                
                // 通常音声ループ停止時も現在の音量を維持
                try
                {
                    TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(instance.currentVolume);
                    System.Diagnostics.Debug.WriteLine($"🔊 通常音声ループ停止時：アプリケーション音量を{(int)(instance.currentVolume * 100)}%で維持");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 通常音声ループ停止時音量維持エラー: {ex.Message}");
                }
            }
        }

        // インスタンスメソッドで完了音再生
        public void PlaySetComplete()
        {
            // まず既存の音声を停止
            shouldPlayLoop = false;
            
            // 完了音再生前にWindows Audio APIで100%に設定
            try
            {
                TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                currentVolume = 1.0f; // currentVolumeも100%に更新
                System.Diagnostics.Debug.WriteLine("🔊 完了音再生前：Windows Audio APIで100%に設定");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 完了音再生前音量設定エラー: {ex.Message}");
            }
            
            // 少し待ってから完了音を再生
            Task.Run(async () =>
            {
                await Task.Delay(500); // 既存音声の停止を待つ
                set_complete?.PlayOnce(1.0f); // 100%で再生
                System.Diagnostics.Debug.WriteLine("完了音を100%で再生しました");
            });
        }

        // 静的インスタンス参照
        private static Form1 instance;

        public static void UpdateTrainNumber(string trainNumber)
        {
            if (instance != null)
            {
                instance.currentTrainNumber = trainNumber;
                
                // EmergencyBrakeControllerに列番設定状態を通知
                bool isValidTrainNumber = !string.IsNullOrEmpty(trainNumber) && trainNumber != "--" && trainNumber != "0000";
                EmergencyBrakeController.SetTrainNumberStatus(isValidTrainNumber);
                
                // 条件チェックを実行
                instance.CheckEBReleaseConditions();
                
                System.Diagnostics.Debug.WriteLine($"Form1: 列車番号更新 - {trainNumber}");
            }
        }
        
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
            if (value) instance = this;
        }

        public static void PlayCompletionSound()
        {
            lock (audioLock)
            {
                instance?.PlaySetComplete();
            }
        }

        public static void PlayKosyouSound()
        {
            lock (audioLock)
            {
                // 既に再生中の場合は重複を防ぐ
                if (shouldPlayKosyouLoop && isKosyouActive)
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ 故障音は既に再生中 - 重複開始をスキップ");
                    return;
                }
                
                shouldPlayKosyouLoop = true;
                isKosyouActive = true; // 故障音発生状態に設定
                if (instance != null && !instance.kosyouLoopStarted)
                {
                    // 故障音開始時にWindows Audio APIで100%に設定
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        instance.currentVolume = 1.0f; // currentVolumeも100%に更新
                        System.Diagnostics.Debug.WriteLine("🔊 故障音開始前：Windows Audio APIで100%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 故障音開始前音量設定エラー: {ex.Message}");
                    }
                    
                    instance.StartKosyouLoop();
                    instance.kosyouLoopStarted = true;
                    
                    System.Diagnostics.Debug.WriteLine("🔊 故障音音量を100%で開始");
                    
                    // 故障コードを追加
                    instance.AddFailureCode("ERR-404");
                }
                System.Diagnostics.Debug.WriteLine("故障音ループを開始しました");
            }
        }

        public static void StopKosyouSound()
        {
            lock (audioLock)
            {
                shouldPlayKosyouLoop = false;
                isKosyouActive = false; // 故障音発生状態を解除
                if (instance != null)
                {
                    instance.kosyouLoopStarted = false;
                    
                    // 故障音停止時も現在の音量を維持
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(instance.currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 故障音停止時：アプリケーション音量を{(int)(instance.currentVolume * 100)}%で維持");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 故障音停止時音量維持エラー: {ex.Message}");
                    }
                    
                    // 故障コードをクリア
                    instance.ClearFailureCodes();
                }
                System.Diagnostics.Debug.WriteLine("故障音ループを停止しました");
            }
        }

        public static void PlayEBKaihouSound()
        {
            lock (audioLock)
            {
                // 既に再生中の場合は重複を防ぐ
                if (shouldPlayEBKaihouLoop && isEBKaihouActive)
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ EB開放音は既に再生中 - 重複開始をスキップ");
                    return;
                }
                
                shouldPlayEBKaihouLoop = true;
                isEBKaihouActive = true; // EB開放音発生状態に設定
                if (instance != null && !instance.ebKaihouLoopStarted)
                {
                    // EB開放音開始時にWindows Audio APIで100%に設定
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        instance.currentVolume = 1.0f; // currentVolumeも100%に更新
                        System.Diagnostics.Debug.WriteLine("🔊 EB開放音開始前：Windows Audio APIで100%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ EB開放音開始前音量設定エラー: {ex.Message}");
                    }
                    
                    instance.StartEBKaihouLoop();
                    instance.ebKaihouLoopStarted = true;
                    
                    System.Diagnostics.Debug.WriteLine("🔊 EB開放音音量を100%で開始");
                }
                System.Diagnostics.Debug.WriteLine("EB開放音ループを開始しました");
            }
        }

        public static void StopEBKaihouSound()
        {
            lock (audioLock)
            {
                shouldPlayEBKaihouLoop = false;
                isEBKaihouActive = false; // EB開放音発生状態を解除
                if (instance != null)
                {
                    instance.ebKaihouLoopStarted = false;
                    
                    // EB開放音停止時も現在の音量を維持
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(instance.currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 EB開放音停止時：アプリケーション音量を{(int)(instance.currentVolume * 100)}%で維持");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ EB開放音停止時音量維持エラー: {ex.Message}");
                    }
                }
                System.Diagnostics.Debug.WriteLine("EB開放音ループを停止しました");
            }
        }

        // 防護無線の状態管理メソッド
        public static void StartBougoMuenno()
        {
            lock (audioLock)
            {
                if (instance != null && !isBougoActive)
                {
                    System.Diagnostics.Debug.WriteLine("🚨 外部から防護無線発砲要求");
                    isBougoActive = true;
                    
                    // 外部防護無線開始時にWindows Audio APIで現在の音量を維持
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(instance.currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 外部防護無線開始時：Windows Audio APIで{(int)(instance.currentVolume * 100)}%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 外部防護無線開始時音量設定エラー: {ex.Message}");
                    }
                    
                    // 外部防護無線中も現在の音量を維持
                    System.Diagnostics.Debug.WriteLine($"🔊 外部防護無線音量を{(int)(instance.currentVolume * 100)}%で開始");
                    
                    // 外部防護無線中: 通常音声ループのみ停止（他の音声は継続）
                    shouldPlayLoop = false;
                    // shouldPlayKosyouLoop = false; // 故障音は継続
                    // shouldPlayEBKaihouLoop = false; // EB開放音は継続
                    
                    // 故障音・EB開放音は停止せず継続再生
                    System.Diagnostics.Debug.WriteLine("� 外部防護無線発砲中 - 故障音・EB開放音は継続再生");
                    
                    // PlayLoopで継続再生（100%音量）
                    instance.bougoF4Audio?.PlayLoop(1.0f);
                    System.Diagnostics.Debug.WriteLine($"🔊 外部防護無線開始: 音量{(int)(instance.currentVolume * 100)}%");
                    
                    // UI更新
                    instance.UpdateBougoDisplay();
                }
            }
        }

        public static void StopBougoMuenno()
        {
            lock (audioLock)
            {
                if (instance != null && isBougoActive)
                {
                    System.Diagnostics.Debug.WriteLine("🔴 外部から防護無線停止要求");
                    isBougoActive = false;
                    
                    // 防護無線を停止
                    instance.bougoF4Audio?.Stop();
                    
                    // 音量はユーザー設定を維持（リセットしない）
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(instance.currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 外部防護無線停止時：音量{(int)(instance.currentVolume * 100)}%を維持");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 外部防護無線停止時音量設定エラー: {ex.Message}");
                    }
                    
                    // 通常音声ループを再開
                    shouldPlayLoop = true;
                    if (!instance.loopStarted)
                    {
                        instance.StartSoundLoop();
                        instance.loopStarted = true;
                    }
                    
                    // UI更新
                    instance.UpdateBougoDisplay();
                }
            }
        }

        public static bool IsBougoActive()
        {
            lock (audioLock)
            {
                return isBougoActive;
            }
        }

        private void StartKosyouLoop()
        {
            // 故障音の長さを事前に取得
            int kosyouDurationMs = kosyou?.GetDurationMs() ?? 3000;
            int kosyouKoeDurationMs = kosyou_koe?.GetDurationMs() ?? 5000;
            
            System.Diagnostics.Debug.WriteLine($"=== 故障音ループ情報 ===");
            System.Diagnostics.Debug.WriteLine($"kosyou音声長: {kosyouDurationMs}ms");
            System.Diagnostics.Debug.WriteLine($"kosyou_koe音声長: {kosyouKoeDurationMs}ms");
            System.Diagnostics.Debug.WriteLine($"====================");

            // 順番に再生するループ（kosyou -> kosyou_koe -> 繰り返し）
            _ = Task.Run(async () =>
            {
                while (shouldPlayKosyouLoop)
                {
                    // EB開放中は故障音を停止
                    if (!shouldPlayKosyouLoop || emergencyBrakeButtonState) break;
                    
                    // kosyou.wavを再生
                    System.Diagnostics.Debug.WriteLine($"故障音開始: {DateTime.Now:HH:mm:ss.fff}");
                    kosyou?.PlayOnce(systemVolume);
                    
                    await Task.Delay(kosyouDurationMs);
                    System.Diagnostics.Debug.WriteLine($"故障音終了: {DateTime.Now:HH:mm:ss.fff}");
                    
                    if (!shouldPlayKosyouLoop || emergencyBrakeButtonState) break;
                    
                    // kosyou_koe.wavを再生
                    System.Diagnostics.Debug.WriteLine($"故障音声開始: {DateTime.Now:HH:mm:ss.fff}");
                    kosyou_koe?.PlayOnce(systemVolume);
                    
                    await Task.Delay(kosyouKoeDurationMs);
                    System.Diagnostics.Debug.WriteLine($"故障音声終了: {DateTime.Now:HH:mm:ss.fff}");
                }
            });
        }

        private void StartEBKaihouLoop()
        {
            // EB開放音の長さを事前に取得
            int ebkaihouDurationMs = ebkaihou?.GetDurationMs() ?? 3000;
            int ebkaihouKoeDurationMs = ebkaihou_koe?.GetDurationMs() ?? 5000;
            
            System.Diagnostics.Debug.WriteLine($"=== EB開放音ループ情報 ===");
            System.Diagnostics.Debug.WriteLine($"ebkaihou音声長: {ebkaihouDurationMs}ms");
            System.Diagnostics.Debug.WriteLine($"ebkaihou_koe音声長: {ebkaihouKoeDurationMs}ms");
            System.Diagnostics.Debug.WriteLine($"=======================");

            // 順番に再生するループ（ebkaihou -> ebkaihou_koe -> 繰り返し）
            _ = Task.Run(async () =>
            {
                while (shouldPlayEBKaihouLoop)
                {
                    if (!shouldPlayEBKaihouLoop) break;
                    
                    // EBkaihou.wavを再生
                    System.Diagnostics.Debug.WriteLine($"EB開放音開始: {DateTime.Now:HH:mm:ss.fff}");
                    ebkaihou?.PlayOnce(currentVolume);
                    
                    await Task.Delay(ebkaihouDurationMs);
                    System.Diagnostics.Debug.WriteLine($"EB開放音終了: {DateTime.Now:HH:mm:ss.fff}");
                    
                    if (!shouldPlayEBKaihouLoop) break;
                    
                    // EBkaihou_koe.wavを再生
                    System.Diagnostics.Debug.WriteLine($"EB開放音声開始: {DateTime.Now:HH:mm:ss.fff}");
                    ebkaihou_koe?.PlayOnce(currentVolume);
                    
                    await Task.Delay(ebkaihouKoeDurationMs);
                    System.Diagnostics.Debug.WriteLine($"EB開放音声終了: {DateTime.Now:HH:mm:ss.fff}");
                }
            });
        }

        private void InitializeEmergencyBrakeLogic()
        {
            System.Diagnostics.Debug.WriteLine("🚀 EBロジック初期化開始");
            
            // 初期状態を明示的に設定（作動状態 = false）
            emergencyBrakeButtonState = false;
            
            // EmergencyBrakeControllerに初期状態を通知（作動状態 = オーバーライド無効）
            EmergencyBrakeController.SetEbReleaseOverride(false);
            
            System.Diagnostics.Debug.WriteLine("✅ EBロジック初期化完了");
        }

        // UI イベントハンドラーを接続
        private void ConnectUIEventHandlers()
        {
            // power.Click += power_Click; // 電源ランプはクリック不可
            // fail.Click += fail_Click; // 故障ランプはクリック不可
            retuban.Click += retuban_Click;
            EBkaihou.Click += EBkaihou_Click;
            bougo.Click += bougo_Click;
            onryou.Click += onryou_Click;
            // shiken.Click += shiken_Click; // 試験モードは後で実装

            // 初期UI状態を設定
            UpdateEBDisplay();
            UpdateVolumeDisplay();
            UpdateBougoDisplay(); // 防護無線表示も初期化
            UpdatePowerLamp(); // 電源ランプ初期化（消灯）
            UpdateFailureLamp(); // 故障ランプ初期化（消灯）
            
            // 初期設定完了後に条件チェックを開始
            Task.Delay(3000).ContinueWith(_ => {
                initialSetupComplete = true;
                CheckEBReleaseConditions();
                System.Diagnostics.Debug.WriteLine("✅ 初期設定完了 - 条件チェック開始");
            });
            
            // 定期的な接続チェックタイマー（1秒間隔）
            var connectionCheckTimer = new System.Windows.Forms.Timer();
            connectionCheckTimer.Interval = 1000; // 1秒
            connectionCheckTimer.Tick += (sender, e) => {
                if (initialSetupComplete)
                {
                    CheckEBReleaseConditions();
                }
            };
            connectionCheckTimer.Start();
            
            System.Diagnostics.Debug.WriteLine("✅ UIイベントハンドラー接続完了");
        }

        // EmergencyBrake状態を更新するメソッド（UI要素なし）
        public void ToggleEmergencyBrakeState()
        {
            try
            {
                // 状態を反転
                emergencyBrakeButtonState = !emergencyBrakeButtonState;
                
                // EmergencyBrakeControllerに状態を通知
                EmergencyBrakeController.SetEbReleaseOverride(emergencyBrakeButtonState);

                // EB開放時の音声再生
                if (emergencyBrakeButtonState)
                {
                    // 手動でEB開放したフラグを設定
                    wasManuallyReleased = true;
                    
                    // EB開放時の故障コードを追加
                    AddFailureCode("ERR-403"); // EB開放コード
                    
                    // EB開放時にLCDを確実に設定（専用メソッドで制御）
                    UpdateLCDDisplay();
                    
                    // EB開放音声をループ再生
                    System.Diagnostics.Debug.WriteLine("🔊 EB開放音声ループ開始");
                    PlayEBKaihouSound();
                    
                    // EB開放中: 他の音声を停止（防護無線は除く）
                    shouldPlayLoop = false; // 通常音声ループ停止
                    if (isKosyouActive)
                    {
                        StopKosyouSound(); // 故障音停止
                        System.Diagnostics.Debug.WriteLine("🔊 EB開放中 - 故障音を停止");
                    }
                    
                    // EB開放中: 電源ランプ点灯、故障ランプ点滅開始
                    powerLampOn = true;
                    UpdatePowerLamp();
                    ebBlinkTimer.Start(); // 点滅開始
                    System.Diagnostics.Debug.WriteLine("💡 EB開放 - 電源ランプ点灯、故障ランプ点滅開始");
                }
                else
                {
                    // EB作動時: EB開放故障コードを削除
                    RemoveFailureCode("ERR-403");
                    
                    // EB作動時: 音声停止、点滅停止
                    System.Diagnostics.Debug.WriteLine("🔊 EB開放音声ループ停止");
                    StopEBKaihouSound();
                    
                    // EB開放終了後: 他の音声を再開
                    if (!isBougoActive) // 防護無線中でなければ通常音声を再開
                    {
                        shouldPlayLoop = true;
                        if (!loopStarted)
                        {
                            StartSoundLoop();
                            loopStarted = true;
                        }
                        System.Diagnostics.Debug.WriteLine("🔊 EB開放終了 - 通常音声ループを再開");
                    }
                    
                    // 故障状態だった場合は故障音も再開
                    if (failureLampOn && !isBougoActive)
                    {
                        PlayKosyouSound();
                        System.Diagnostics.Debug.WriteLine("🔊 EB開放終了 - 故障音を再開");
                    }
                    
                    ebBlinkTimer.Stop();
                    ebBlinkState = false;
                    UpdateFailureLamp(); // 故障ランプを通常状態に戻す
                    UpdateLCDDisplay(); // LCD表示も更新
                    System.Diagnostics.Debug.WriteLine("💡 EB作動 - 故障ランプ点滅停止");
                }

                string stateText = emergencyBrakeButtonState ? "オン" : "オフ";
                System.Diagnostics.Debug.WriteLine($"🔘 EB開放スイッチ: {stateText}に変更");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EB開放スイッチエラー: {ex.Message}");
            }
        }

        private void InitializeTrainCrewClient()
        {
            try
            {
                trainCrewClient = new TrainCrewWebSocketClient();
                
                // イベントハンドラの設定
                trainCrewClient.OnConnectionStatusChanged += (status) =>
                {
                    try
                    {
                        bool wasConnected = isWebSocketConnected;
                        isWebSocketConnected = status.Contains("接続中");
                        lastWebSocketActivity = DateTime.Now; // 接続状態変更時に更新
                        
                        // 接続状態が変化した場合
                        if (wasConnected != isWebSocketConnected)
                        {
                            CheckEBReleaseConditions();
                        }
                        
                        // EmergencyBrakeControllerに接続状態を通知
                        EmergencyBrakeController.SetWebSocketStatus(isWebSocketConnected);
                        
                        System.Diagnostics.Debug.WriteLine($"TrainCrew: {status}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"接続状態更新エラー: {ex.Message}");
                    }
                };

                trainCrewClient.OnDataReceived += (data) =>
                {
                    try
                    {
                        lastWebSocketActivity = DateTime.Now; // データ受信時に更新
                        
                        if (this.InvokeRequired)
                        {
                            this.Invoke(new Action(() => ProcessTrainCrewData(data)));
                        }
                        else
                        {
                            ProcessTrainCrewData(data);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"データ受信処理エラー: {ex.Message}");
                    }
                };

                // 接続開始
                trainCrewClient.Connect();
                System.Diagnostics.Debug.WriteLine("TrainCrewクライアントが正常に初期化されました");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrainCrewクライアント初期化で例外が発生: {ex.Message}");
                throw; // 上位で適切にハンドリング
            }
        }

        private void ProcessTrainCrewData(TrainCrewAPI.TrainCrewStateData data)
        {
            try
            {
                // データの基本情報をログ出力
                System.Diagnostics.Debug.WriteLine($"=== ProcessTrainCrewData 開始 ===");
                System.Diagnostics.Debug.WriteLine($"Data null check: {data == null}");
                if (data != null)
                {
                    System.Diagnostics.Debug.WriteLine($"myTrainData null check: {data.myTrainData == null}");
                    System.Diagnostics.Debug.WriteLine($"trackCircuitList null check: {data.trackCircuitList == null}");
                    if (data.trackCircuitList != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"trackCircuitList count: {data.trackCircuitList.Count}");
                    }
                }

                // 列車の走行状態を検知
                bool wasMoving = isTrainMoving;
                if (data.myTrainData != null)
                {
                    string trainName = data.myTrainData.diaName ?? "N/A";
                    System.Diagnostics.Debug.WriteLine($"TrainCrew列車番号: {trainName}");
                    
                    // 速度 > 0 または力行・ブレーキノッチが入っている場合は走行中と判定
                    isTrainMoving = data.myTrainData.Speed > 0.1f || 
                                   data.myTrainData.Pnotch > 0 || 
                                   data.myTrainData.Bnotch > 0;
                    
                    // 走行状態が変化した場合はログ出力
                    if (wasMoving != isTrainMoving)
                    {
                        System.Diagnostics.Debug.WriteLine($"🚂 列車走行状態変更: {(isTrainMoving ? "走行中" : "停止中")}");
                        
                        // 条件チェックを実行
                        CheckEBReleaseConditions();
                    }
                }

                // 軌道回路情報の処理
                var currentTrackCircuits = new List<string>();
                var currentZones = new HashSet<string>();

                System.Diagnostics.Debug.WriteLine($"🔍 軌道回路処理開始");
                System.Diagnostics.Debug.WriteLine($"   trackCircuitList: {(data.trackCircuitList != null ? "存在" : "null")}");
                System.Diagnostics.Debug.WriteLine($"   myTrainData: {(data.myTrainData != null ? "存在" : "null")}");
                System.Diagnostics.Debug.WriteLine($"   myTrainData.diaName: {data.myTrainData?.diaName ?? "null"}");

                if (data.trackCircuitList != null && data.myTrainData?.diaName != null)
                {
                    System.Diagnostics.Debug.WriteLine($"   全軌道回路数: {data.trackCircuitList.Count}");
                    
                    // 自列車が在線している軌道回路を抽出
                    var myTrainCircuits = data.trackCircuitList
                        .Where(tc => tc.On && tc.Last == data.myTrainData.diaName)
                        .ToList();

                    System.Diagnostics.Debug.WriteLine($"   自列車在線軌道回路数: {myTrainCircuits.Count}");
                    
                    if (myTrainCircuits.Count == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ❌ 自列車が在線している軌道回路が見つかりません");
                        System.Diagnostics.Debug.WriteLine($"   列車名でマッチング中: '{data.myTrainData.diaName}'");
                        
                        // デバッグ：すべての軌道回路を確認
                        var allOnCircuits = data.trackCircuitList.Where(tc => tc.On).ToList();
                        System.Diagnostics.Debug.WriteLine($"   在線中の軌道回路総数: {allOnCircuits.Count}");
                        
                        if (allOnCircuits.Count > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"   在線中軌道回路（最初の5件）:");
                            foreach (var tc in allOnCircuits.Take(5))
                            {
                                System.Diagnostics.Debug.WriteLine($"     - {tc.Name} (Last: '{tc.Last}')");
                            }
                            
                            // 全ての在線軌道回路名を出力（マッピング作成用）
                            System.Diagnostics.Debug.WriteLine($"🔍 全在線軌道回路名リスト:");
                            var allNames = allOnCircuits.Select(tc => tc.Name).Distinct().OrderBy(n => n).ToList();
                            foreach (var name in allNames.Take(20)) // 最初の20件
                            {
                                System.Diagnostics.Debug.WriteLine($"     '{name}'");
                            }
                            if (allNames.Count > 20)
                            {
                                System.Diagnostics.Debug.WriteLine($"     ... 他 {allNames.Count - 20}件");
                            }
                            
                            // 列車名が「--」の場合、在線中の軌道回路を使用してみる
                            if (data.myTrainData.diaName == "--" && allOnCircuits.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"   🔄 列車名が「--」のため、在線軌道回路を試用");
                                myTrainCircuits = allOnCircuits;
                            }
                        }
                    }

                    foreach (var circuit in myTrainCircuits)
                    {
                        currentTrackCircuits.Add(circuit.Name);
                        System.Diagnostics.Debug.WriteLine($"   自列車軌道回路: {circuit.Name}");

                        // ゾーンマッピングから対応するゾーンを取得
                        if (zoneMappings.ContainsKey(circuit.Name))
                        {
                            currentZones.Add(zoneMappings[circuit.Name]);
                            System.Diagnostics.Debug.WriteLine($"     → ゾーン: {zoneMappings[circuit.Name]}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"     → ❌ ゾーンマッピングなし");
                            
                            // 部分マッチを試行（柔軟なマッピング）
                            string foundZone = TryFindZoneByPattern(circuit.Name);
                            if (!string.IsNullOrEmpty(foundZone))
                            {
                                currentZones.Add(foundZone);
                                System.Diagnostics.Debug.WriteLine($"     → パターンマッチ: {foundZone}");
                            }
                        }
                    }

                    // 現在のゾーン情報をサーバーに送信（有効なゾーンのみ、かつ位置が変化した場合のみ）
                    string currentZone = "ゾーン1"; // デフォルト値
                    
                    // 🔍 ゾーン判定デバッグ開始
                    System.Diagnostics.Debug.WriteLine($"🔍 ゾーン判定デバッグ開始");
                    System.Diagnostics.Debug.WriteLine($"   軌道回路数: {currentTrackCircuits.Count}");
                    System.Diagnostics.Debug.WriteLine($"   検出されたゾーン数: {currentZones.Count}");
                    
                    if (currentTrackCircuits.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"   軌道回路リスト: [{string.Join(", ", currentTrackCircuits)}]");
                    }
                    
                    if (currentZones.Count > 0)
                    {
                        // 最初に見つかったゾーンを使用
                        currentZone = currentZones.OrderBy(z => z).First();
                        System.Diagnostics.Debug.WriteLine($"   検出されたゾーンリスト: [{string.Join(", ", currentZones.OrderBy(z => z))}]");
                        System.Diagnostics.Debug.WriteLine($"   採用されたゾーン: {currentZone}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"   ❌ ゾーンが検出されませんでした");
                        currentZone = "ゾーン1"; // デフォルト
                    }
                    
                    // CSVマッピング確認
                    System.Diagnostics.Debug.WriteLine($"   CSVマッピング数: {zoneMappings.Count}");
                    if (currentTrackCircuits.Count > 0)
                    {
                        foreach (var tc in currentTrackCircuits)
                        {
                            if (zoneMappings.ContainsKey(tc))
                            {
                                System.Diagnostics.Debug.WriteLine($"   軌道回路 {tc} → {zoneMappings[tc]}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"   ❌ 軌道回路 {tc} はマッピングされていません");
                            }
                        }
                    }
                    
                    // デバッグ表示ラベル更新
                    if (debugTrackCircuitLabel != null)
                    {
                        debugTrackCircuitLabel.Text = $"軌道回路: {string.Join(", ", currentTrackCircuits)}";
                    }
                    if (debugZoneLabel != null)
                    {
                        debugZoneLabel.Text = $"ゾーン: {currentZone}";
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"🔍 ゾーン判定結果: {currentZone}");
                    
                    if (!string.IsNullOrEmpty(currentZone) && 
                        currentZone != "不明" && 
                        currentZone != "データなし" && 
                        currentZone != "列車データなし" && 
                        currentZone != "軌道回路データなし" && 
                        currentZone != "マッピングなし" && 
                        currentZone != "列車名なし" && 
                        currentZone != "未検出")
                    {
                        // 前回送信したゾーンと同じ場合は送信しない
                        if (currentZone != lastSentZone)
                        {
                            lastSentZone = currentZone;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    if (bougoSignalRClient != null && bougoSignalRClient.IsConnected)
                                    {
                                        await bougoSignalRClient.UpdateLocationAsync(currentZone);
                                        System.Diagnostics.Debug.WriteLine($"📍 位置情報送信: {currentZone}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"❌ 位置情報送信エラー: {ex.Message}");
                                }
                            });
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"🚫 位置情報送信スキップ: {currentZone}");
                    }
                    
                    // 軌道回路データをサーバーに送信（2重化のため）
                    if (currentTrackCircuits.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                if (bougoSignalRClient != null && bougoSignalRClient.IsConnected)
                                {
                                    await bougoSignalRClient.SendTrackCircuitDataAsync(
                                        data.myTrainData.diaName, 
                                        currentTrackCircuits.ToArray()
                                    );
                                    System.Diagnostics.Debug.WriteLine($"🛤️ 軌道回路データ送信: {currentTrackCircuits.Count}件");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ 軌道回路データ送信エラー: {ex.Message}");
                            }
                        });
                    }
                }

                // ゾーン情報のログ出力
                if (currentZones.Count > 0)
                {
                    var zoneList = string.Join(", ", currentZones.OrderBy(z => z));
                    System.Diagnostics.Debug.WriteLine($"現在のゾーン: {zoneList}");
                }

                // デバッグ情報
                System.Diagnostics.Debug.WriteLine($"=== TrainCrewデータ処理 ===");
                System.Diagnostics.Debug.WriteLine($"列車名: {data.myTrainData?.diaName ?? "N/A"}");
                System.Diagnostics.Debug.WriteLine($"軌道回路数: {currentTrackCircuits.Count}");
                System.Diagnostics.Debug.WriteLine($"ゾーン数: {currentZones.Count}");
                foreach (var circuit in currentTrackCircuits)
                {
                    var zone = zoneMappings.ContainsKey(circuit) ? zoneMappings[circuit] : "未定義";
                    System.Diagnostics.Debug.WriteLine($"  {circuit} -> {zone}");
                }
                System.Diagnostics.Debug.WriteLine("========================");

                // 最新のTrainCrewデータを保存
                currentTrainCrewData = data;
                System.Diagnostics.Debug.WriteLine($"✅ currentTrainCrewDataを更新");

                // デバッグ表示を更新（現在の状況を即座に反映）
                _ = Task.Run(() =>
                {
                    try
                    {
                        string currentZone = GetCurrentZone();
                        System.Diagnostics.Debug.WriteLine($"🔄 ProcessTrainCrewData後のゾーン取得: {currentZone}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ ProcessTrainCrewData後のゾーン取得エラー: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"データ処理エラー: {ex.Message}");
            }
        }

        // ======== UI イベントハンドラー ========

        // 電源ボタンクリック（現在は無効化）
        /*
        private void power_Click(object sender, EventArgs e)
        {
            powerOn = !powerOn;
            
            if (powerOn)
            {
                // 電源ON - 通常の動作を開始
                power.Image = LoadImageSafely(PowerOnImagePath);
                power.BackColor = Color.Green; // 一時的に色で表現
                shouldPlayLoop = true;
                if (!loopStarted)
                {
                    StartSoundLoop();
                    loopStarted = true;
                }
                System.Diagnostics.Debug.WriteLine("🔌 電源: ON");
            }
            else
            {
                // 電源OFF - 全ての音声を停止
                power.Image = LoadImageSafely(PowerOffImagePath);
                power.BackColor = Color.Red; // 一時的に色で表現
                shouldPlayLoop = false;
                shouldPlayKosyouLoop = false;
                isBougoActive = false;
                System.Diagnostics.Debug.WriteLine("🔌 電源: OFF");
            }
        }
        */

        // 故障表示クリック（現在は無効化）
        private void fail_Click(object sender, EventArgs e)
        {
            if (!powerOn) return; // 電源OFFの場合は動作しない
            
            if (!shouldPlayKosyouLoop)
            {
                // 故障音開始
                PlayKosyouSound();
                fail.Image = LoadImageSafely(KosyouErrorImagePath);
                kosyouLCD.Text = "故障発生";
                kosyouLCD.ForeColor = Color.Red;
                System.Diagnostics.Debug.WriteLine("⚠️ 故障音開始");
            }
            else
            {
                // 故障音停止
                StopKosyouSound();
                fail.Image = LoadImageSafely(KosyouNormalImagePath);
                kosyouLCD.Text = "正常";
                kosyouLCD.ForeColor = Color.Green;
                System.Diagnostics.Debug.WriteLine("✅ 故障音停止");
            }
        }

        // 列番ボタンクリック
        private void retuban_Click(object sender, EventArgs e)
        {
            if (!powerOn) return; // 電源OFFの場合は動作しない
            
            try
            {
                // 既存のRetsubanWindowが開いているかチェック
                RetsubanWindow existingWindow = null;
                
                // Application.OpenFormsを使って既存の画面を検索
                var openForms = Application.OpenForms.Cast<Form>().ToList();
                foreach (Form form in openForms)
                {
                    if (form is RetsubanWindow retsubanForm && !form.IsDisposed && form.Visible)
                    {
                        existingWindow = retsubanForm;
                        break;
                    }
                }

                if (existingWindow != null)
                {
                    // 既存の表示中ウィンドウがある場合は閉じる
                    try
                    {
                        existingWindow.Close();
                        System.Diagnostics.Debug.WriteLine("🚊 既存の列番入力画面を閉じました");
                    }
                    catch (Exception closeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 既存画面のクローズ失敗: {closeEx.Message}");
                    }
                }
                else
                {
                    // 既存の表示中ウィンドウがない場合は新しく開く
                    var subWindow = new RetsubanWindow();
                    subWindow.Show();
                    subWindow.BringToFront();
                    System.Diagnostics.Debug.WriteLine("🚊 新しい列番入力画面を開きました");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 列番画面エラー: {ex.Message}");
                
                // エラーが発生した場合は新しい画面を開く
                try
                {
                    var subWindow = new RetsubanWindow();
                    subWindow.Show();
                    System.Diagnostics.Debug.WriteLine("🚊 エラー回復：列番入力画面を新規作成しました");
                }
                catch (Exception innerEx)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 列番画面強制作成エラー: {innerEx.Message}");
                }
            }
        }

        // EB開放ボタンクリック
        private void EBkaihou_Click(object sender, EventArgs e)
        {
            if (!powerOn) return; // 電源OFFの場合は動作しない
            
            try
            {
                ToggleEmergencyBrakeState();
                UpdateEBDisplay();
                System.Diagnostics.Debug.WriteLine($"🚨 EB開放ボタンクリック: {(emergencyBrakeButtonState ? "開放" : "作動")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EB開放エラー: {ex.Message}");
            }
        }

        // 防護無線ボタンクリック
        private void bougo_Click(object sender, EventArgs e)
        {
            if (!powerOn) return; // 電源OFFの場合は動作しない
            
            HandleF4KeyPress(); // F4キーと同じ処理
            System.Diagnostics.Debug.WriteLine("📡 防護無線ボタンクリック");
        }

        // 音量ボタンクリック
        private async void onryou_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("🔊 音量ボタンがクリックされました");
            
            // 電源ONの場合のみ音量調整可能（音声の種類に関係なく）
            if (!powerOn) 
            {
                System.Diagnostics.Debug.WriteLine("🔊 音量調整無効 - 電源オフ");
                return; // 電源OFFの場合は動作しない
            }
            
            // 現在の音量をログ出力
            System.Diagnostics.Debug.WriteLine($"🔊 音量調整前: currentVolume={currentVolume}");
            
            // 30% ↔ 100% をトグル
            if (currentVolume >= 0.6f)
            {
                currentVolume = 0.3f; // 30%に設定
                System.Diagnostics.Debug.WriteLine("🔊 音量変更: 30%に設定");
            }
            else
            {
                currentVolume = 1.0f; // 100%に戻す
                System.Diagnostics.Debug.WriteLine("🔊 音量変更: 100%に戻す");
            }
            
            // Windows Audio Session APIを使用してアプリケーション音量を制御
            try
            {
                TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(currentVolume);
                System.Diagnostics.Debug.WriteLine($"🔊 Windows Audio API音量変更完了: {(int)(currentVolume * 100)}%");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Windows Audio API音量変更エラー: {ex.Message}");
                
                // フォールバック：各種音声の停止→再開
                System.Diagnostics.Debug.WriteLine("🔊 フォールバック音量変更を実行");
                
                try
                {
                    // 防護無線発砲音の音量変更
                    if (isBougoActive && bougoF4Audio != null)
                    {
                        bougoF4Audio.Stop();
                        bougoF4Audio.PlayLoop(currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 防護無線音量変更: {(int)(currentVolume * 100)}%");
                    }
                    
                    // 他列車受報音の音量変更
                    if (isBougoOtherActive && bougoOtherAudio != null)
                    {
                        bougoOtherAudio.Stop();
                        bougoOtherAudio.PlayLoop(currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 受報音量変更: {(int)(currentVolume * 100)}%");
                    }
                    
                    // 故障音の音量変更（Windows Audio APIで制御）
                    if (isKosyouActive)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔊 故障音量変更: {(int)(currentVolume * 100)}%");
                    }
                    
                    // EB開放音の音量変更（Windows Audio APIで制御）
                    if (isEBKaihouActive)
                    {
                        System.Diagnostics.Debug.WriteLine($"🔊 EB開放音量変更: {(int)(currentVolume * 100)}%");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"🔊 フォールバック音量変更完了: {(int)(currentVolume * 100)}%");
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ フォールバック音量変更エラー: {fallbackEx.Message}");
                    
                    // エラー時は再度試行
                    await Task.Delay(50);
                    
                    // 再試行
                    if (isBougoActive) bougoF4Audio?.PlayLoop(currentVolume);
                    if (isBougoOtherActive) bougoOtherAudio?.PlayLoop(currentVolume);
                    
                    System.Diagnostics.Debug.WriteLine($"🔊 リトライ音量変更: {(int)(currentVolume * 100)}%");
                }
            }
            
            // 音量表示を更新
            UpdateVolumeDisplay();
        }

        // ======== UI 更新メソッド ========

        // EB開放ボタンの表示を更新
        private void UpdateEBDisplay()
        {
            if (emergencyBrakeButtonState)
            {
                EBkaihou.Image = LoadImageSafely(EBKaihouOnImagePath);
            }
            else
            {
                EBkaihou.Image = LoadImageSafely(EBKaihouOffImagePath);
            }
        }

        // 音量表示を更新
        private void UpdateVolumeDisplay()
        {
            // 防護無線発砲中でも故障音発生中でもEB開放音発生中でもない場合は通常状態
            if (!isBougoActive && !isKosyouActive && !isEBKaihouActive)
            {
                // 通常時は音量ボタンは単純に表示
                System.Diagnostics.Debug.WriteLine("🔊 音量表示: 通常状態（防護無線・故障音・EB開放音すべて停止中）");
                return;
            }
            
            // 防護無線発砲中または故障音発生中またはEB開放音発生中の音量状態をログ出力（30%↔100%）
            int volumePercent = (int)(currentVolume * 100);
            string activeMode = isBougoActive ? "防護無線中" : isKosyouActive ? "故障音中" : "EB開放音中";
            System.Diagnostics.Debug.WriteLine($"🔊 音量表示: {volumePercent}%（{activeMode}）");
        }

        // 防護無線ボタンの表示を更新
        private void UpdateBougoDisplay()
        {
            // 受報中の場合は点滅対応の更新を使用
            if (isBougoOtherActive)
            {
                UpdateBougoDisplayWithBlink();
            }
            else
            {
                // 通常の表示更新
                if (isBougoActive)
                {
                    bougo.Image = LoadImageSafely(BougoOnImagePath);
                }
                else
                {
                    bougo.Image = LoadImageSafely(BougoOffImagePath);
                }
            }
        }

        // 防護無線ボタンの表示を更新（点滅対応）
        private void UpdateBougoDisplayWithBlink()
        {
            if (isBougoActive)
            {
                // 発報中は常にON表示
                bougo.Image = LoadImageSafely(BougoOnImagePath);
            }
            else if (isBougoOtherActive)
            {
                // 受報中は点滅
                if (bougoBlinkState)
                {
                    bougo.Image = LoadImageSafely(BougoOnImagePath);
                }
                else
                {
                    bougo.Image = LoadImageSafely(BougoOffImagePath);
                }
            }
            else
            {
                // 通常状態はOFF表示
                bougo.Image = LoadImageSafely(BougoOffImagePath);
            }
        }

        // EB開放中の故障ランプ点滅タイマーイベント
        private void EBBlinkTimer_Tick(object sender, EventArgs e)
        {
            if (emergencyBrakeButtonState) // EB開放中のみ動作
            {
                ebBlinkState = !ebBlinkState; // 点滅状態を反転
                UpdateFailureLamp(); // 故障ランプ更新
            }
        }

        // 受報時の防護無線ボタン点滅タイマーイベント
        private void BougoBlinkTimer_Tick(object sender, EventArgs e)
        {
            if (isBougoOtherActive) // 受報中のみ動作
            {
                bougoBlinkState = !bougoBlinkState; // 点滅状態を反転
                UpdateBougoDisplayWithBlink(); // 防護無線ボタン更新（点滅対応）
            }
        }

        // 電源ランプの表示を更新
        private void UpdatePowerLamp()
        {
            if (powerLampOn)
            {
                power.Image = Image.FromFile(PowerOnImagePath);
            }
            else
            {
                power.Image = null; // 消灯
            }
        }

        // 故障ランプの表示を更新（LCDは一切操作しない）
        private void UpdateFailureLamp()
        {
            // EB開放中は点滅制御（故障ランプのみ）
            if (emergencyBrakeButtonState)
            {
                if (ebBlinkState)
                {
                    fail.Image = LoadImageSafely(KosyouNormalImagePath); // 点灯
                }
                else
                {
                    fail.Image = null; // 消灯
                }
            }
            else if (failureLampOn)
            {
                fail.Image = LoadImageSafely(KosyouNormalImagePath); // kosyou.pngを使用
            }
            else
            {
                fail.Image = null; // 消灯
            }
        }

        // LCDディスプレイ専用の更新メソッド（故障ランプとは完全に独立）
        private void UpdateLCDDisplay()
        {
            // EB開放中はERR-403を固定表示（点滅しない）
            if (emergencyBrakeButtonState)
            {
                kosyouLCD.Text = "ERR-403";
                kosyouLCD.ForeColor = Color.Red;
                kosyouLCD.BackColor = Color.FromArgb(40, 60, 40);
                return; // EB開放中は他の処理をスキップ
            }

            // 故障コードがある場合は故障表示
            if (failureCodes.Count > 0)
            {
                string currentCode = failureCodes[currentFailureCodeIndex];
                kosyouLCD.Text = currentCode;
                kosyouLCD.ForeColor = Color.Red; // 故障時は赤色
                kosyouLCD.BackColor = Color.FromArgb(40, 60, 40); // LCD風背景
            }
            else
            {
                // 正常時は何も表示しない
                kosyouLCD.Text = "";
                kosyouLCD.ForeColor = Color.Lime; // 緑色（LED風）
                kosyouLCD.BackColor = Color.FromArgb(40, 60, 40); // LCD風背景
            }
        }

        // EBを開放できる条件をチェックして電源ランプを制御
        private void CheckEBReleaseConditions()
        {
            // WebSocket接続タイムアウトチェック（2秒に短縮、ただし初期化完了後のみ）
            bool isWebSocketTimedOut = initialSetupComplete && (DateTime.Now - lastWebSocketActivity).TotalSeconds > 2;
            if (isWebSocketTimedOut && isWebSocketConnected)
            {
                isWebSocketConnected = false;
                System.Diagnostics.Debug.WriteLine("⚠️ WebSocket接続タイムアウト - 2秒間応答なし");
                
                // WebSocket接続タイムアウト検出開始時刻を記録（初期化完了後のみ、EB開放中は除く）
                if (webSocketTimeoutDetectedTime == null && initialSetupComplete && !emergencyBrakeButtonState)
                {
                    webSocketTimeoutDetectedTime = DateTime.Now;
                    System.Diagnostics.Debug.WriteLine("⚠️ WebSocket接続タイムアウト検出開始 - 5秒後に故障判定予定");
                }
                else if (emergencyBrakeButtonState)
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ WebSocket接続タイムアウト - EB開放中のため検出をスキップ");
                }
                
                // 5秒経過チェック（WebSocket接続タイムアウト）
                if (initialSetupComplete && isStartupEBActivated && (DateTime.Now - webSocketTimeoutDetectedTime.Value).TotalSeconds >= 5.0 && !failureLampOn && !emergencyBrakeButtonState)
                {
                    // EB解除条件を一度でも満たしている場合のみ故障判定実行
                    if (hasEverMetReleaseConditions)
                    {
                        failureLampOn = true;
                        UpdateFailureLamp();
                        PlayKosyouSound();
                        AddFailureCode("ERR-503"); // WebSocket接続タイムアウト
                        System.Diagnostics.Debug.WriteLine("⚠️ 故障ランプ点灯・故障音開始 - WebSocket接続タイムアウト（5秒経過）");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ℹ️ WebSocket接続タイムアウト - EB解除条件未達成のため故障ランプ・音声をスキップ");
                    }
                }
                else if (emergencyBrakeButtonState && webSocketTimeoutDetectedTime != null)
                {
                    System.Diagnostics.Debug.WriteLine("ℹ️ WebSocket接続タイムアウト - EB開放中のため故障判定をスキップ");
                }
            }
            else if (!isWebSocketTimedOut && isWebSocketConnected && initialSetupComplete)
            {
                // WebSocket接続が復旧した場合、タイムアウト検出時刻をリセット
                if (webSocketTimeoutDetectedTime != null)
                {
                    webSocketTimeoutDetectedTime = null;
                    System.Diagnostics.Debug.WriteLine("✅ WebSocket接続復旧 - タイムアウト検出時刻リセット");
                }
            }
            else if (emergencyBrakeButtonState && webSocketTimeoutDetectedTime != null)
            {
                // EB開放中はWebSocket接続タイムアウト検出をリセット
                webSocketTimeoutDetectedTime = null;
                System.Diagnostics.Debug.WriteLine("ℹ️ EB開放中 - WebSocket接続タイムアウト検出をリセット");
            }

            // EBを開放できる条件：列車番号設定済み かつ TrainCrew接続済み
            bool canReleaseEB = !string.IsNullOrEmpty(currentTrainNumber) && 
                               currentTrainNumber != "--" && 
                               currentTrainNumber != "0000" &&
                               isWebSocketConnected; // 自分で管理する接続状態

            if (canReleaseEB)
            {
                // EB開放条件を1回でも満たしたことを記録
                if (!hasEverMetReleaseConditions)
                {
                    hasEverMetReleaseConditions = true;
                    System.Diagnostics.Debug.WriteLine("✅ EB開放条件を初回満足 - 以降起動時故障ランプ点灯を許可");
                }
                
                // 条件が満たされた場合は手動開放フラグをリセット
                if (wasManuallyReleased)
                {
                    wasManuallyReleased = false;
                    System.Diagnostics.Debug.WriteLine("✅ 手動EB開放フラグをリセット - 条件満足");
                }
                
                // 条件が満たされた場合：電源ランプ点灯、故障状態解除
                if (!powerLampOn)
                {
                    powerLampOn = true;
                    UpdatePowerLamp();
                    System.Diagnostics.Debug.WriteLine("💡 電源ランプ点灯 - EB開放条件満足");
                }
                
                // 故障状態解除
                if (failureLampOn)
                {
                    failureLampOn = false;
                    UpdateFailureLamp();
                    StopKosyouSound();
                    System.Diagnostics.Debug.WriteLine("✅ 故障ランプ消灯・故障音停止 - 条件回復");
                }
                
                // 故障検出時間をリセット
                failureDetectedTime = null;
                webSocketTimeoutDetectedTime = null;
            }
            else
            {
                // 条件が満たされていない場合：故障検出開始（ただしEB開放中は除く）
                if (initialSetupComplete && isStartupEBActivated && !emergencyBrakeButtonState)
                {
                    if (failureDetectedTime == null)
                    {
                        failureDetectedTime = DateTime.Now;
                        System.Diagnostics.Debug.WriteLine("⚠️ 故障検出開始 - EB開放条件不満足");
                    }
                    else
                    {
                        // 5秒経過チェック
                        var elapsedSeconds = (DateTime.Now - failureDetectedTime.Value).TotalSeconds;
                        
                        if (elapsedSeconds >= 5.0 && !failureLampOn)
                        {
                            // EB解除条件を一度でも満たしている場合のみ故障判定実行
                            // ただし、手動でEB開放していた場合は故障処理をスキップ
                            if (hasEverMetReleaseConditions && !wasManuallyReleased)
                            {
                                // 5秒経過：故障ランプ点灯とEB作動、故障音開始
                                failureLampOn = true;
                                UpdateFailureLamp();
                                PlayKosyouSound();
                                AddFailureCode("ERR-400"); // 条件不満足
                                
                                System.Diagnostics.Debug.WriteLine("🚨 5秒経過 - 故障ランプ点灯・EB作動・故障音開始");
                            }
                            else
                            {
                                if (wasManuallyReleased)
                                {
                                    System.Diagnostics.Debug.WriteLine("ℹ️ 条件不満足による故障検出 - 手動EB開放のため故障ランプ・音声をスキップ");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("ℹ️ 条件不満足による故障検出 - EB解除条件未達成のため故障ランプ・音声をスキップ");
                                }
                            }
                            
                            // 電源ランプを消灯
                            powerLampOn = false;
                            UpdatePowerLamp();
                        }
                    }
                }
                else if (emergencyBrakeButtonState && failureDetectedTime != null)
                {
                    // EB開放中は故障検出をリセット
                    failureDetectedTime = null;
                    System.Diagnostics.Debug.WriteLine("ℹ️ EB開放中 - 故障検出タイマーをリセット");
                }
                
                // 電源ランプを消灯（条件不満足時、ただしEB開放中は除く）
                if (powerLampOn && !emergencyBrakeButtonState)
                {
                    powerLampOn = false;
                    UpdatePowerLamp();
                    System.Diagnostics.Debug.WriteLine("💡 電源ランプ消灯 - EB開放条件不満足");
                }
            }

            // 初期設定完了後、起動時のみEBを作動
            if (initialSetupComplete && !isStartupEBActivated)
            {
                // 起動時条件：列車番号未設定 または WebSocket未接続
                bool shouldActivateStartupEB = string.IsNullOrEmpty(currentTrainNumber) || 
                                              currentTrainNumber == "--" || 
                                              currentTrainNumber == "0000" ||
                                              !isWebSocketConnected;
                
                if (shouldActivateStartupEB)
                {
                    // 起動時EB作動条件が始まった時刻を記録
                    if (ebActivationTime == null)
                    {
                        ebActivationTime = DateTime.Now;
                        System.Diagnostics.Debug.WriteLine("⚠️ 起動時EB作動条件検出開始 - 5秒後にEB作動予定");
                    }
                    
                    // 5秒経過したら起動時EBを作動（ただし、EB解除条件を一度も満たしていない場合は故障ランプ・音声はスキップ）
                    if ((DateTime.Now - ebActivationTime.Value).TotalSeconds >= 5)
                    {
                        // 起動時EB作動処理
                        System.Diagnostics.Debug.WriteLine("🚨 起動時EB作動実行 - 5秒経過後");
                        isStartupEBActivated = true;
                        
                        // 故障ランプ点灯処理（EB解除条件を一度でも満たしている場合のみ、またはEB開放スイッチでのオーバーライドは例外）
                        // ただし、手動でEB開放していた場合は故障処理をスキップ
                        if ((hasEverMetReleaseConditions || emergencyBrakeButtonState) && !wasManuallyReleased)
                        {
                            if (!failureLampOn)
                            {
                                failureLampOn = true;
                                UpdateFailureLamp();
                                AddFailureCode("ERR-500"); // 起動時EB作動
                                System.Diagnostics.Debug.WriteLine("⚠️ 故障ランプ点灯 - 起動時EB作動");
                            }
                        }
                        else
                        {
                            if (wasManuallyReleased)
                            {
                                System.Diagnostics.Debug.WriteLine("ℹ️ 起動時EB作動 - 手動EB開放のため故障ランプ点灯をスキップ");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("ℹ️ 起動時EB作動 - EB解除条件未達成のため故障ランプ点灯をスキップ");
                            }
                        }
                    }
                }
                else
                {
                    // 起動時EB作動条件が解除された場合（正常な起動完了）
                    if (ebActivationTime != null)
                    {
                        ebActivationTime = null;
                        isStartupEBActivated = true; // 正常起動完了としてマーク
                        System.Diagnostics.Debug.WriteLine("✅ 起動時EB作動条件解除 - 正常起動");
                    }
                    
                    // 故障状態が解除された場合
                    if (failureDetectedTime != null || failureLampOn)
                    {
                        failureDetectedTime = null;
                        failureLampOn = false;
                        UpdateFailureLamp();
                        System.Diagnostics.Debug.WriteLine("✅ 故障ランプ消灯 - 起動時EB作動条件解除");
                    }
                }
            }
        }
        
        // ======== 故障コード表示関連メソッド ========
        
        // 故障コードを追加
        private void AddFailureCode(string code)
        {
            if (!failureCodes.Contains(code))
            {
                failureCodes.Add(code);
                System.Diagnostics.Debug.WriteLine($"📟 故障コード追加: {code}");
                
                // 最初の故障コードの場合は表示開始
                if (failureCodes.Count == 1)
                {
                    currentFailureCodeIndex = 0;
                    StartFailureCodeDisplay();
                }
            }
        }
        
        // 故障コードをクリア
        private void ClearFailureCodes()
        {
            failureCodes.Clear();
            currentFailureCodeIndex = 0;
            failureCodeTimer.Stop();
            isDotAnimationActive = false;
            UpdateFailureCodeDisplay(); // 空表示に戻す
            System.Diagnostics.Debug.WriteLine("📟 故障コード表示クリア");
        }
        
        // 特定の故障コードを削除
        private void RemoveFailureCode(string code)
        {
            if (failureCodes.Contains(code))
            {
                failureCodes.Remove(code);
                System.Diagnostics.Debug.WriteLine($"📟 故障コード削除: {code}");
                
                // 現在のインデックスを調整
                if (currentFailureCodeIndex >= failureCodes.Count && failureCodes.Count > 0)
                {
                    currentFailureCodeIndex = 0;
                }
                
                // 故障コードがなくなった場合は表示停止
                if (failureCodes.Count == 0)
                {
                    failureCodeTimer.Stop();
                    isDotAnimationActive = false;
                    UpdateFailureCodeDisplay(); // 表示をクリア
                }
                else if (failureCodes.Count == 1)
                {
                    // 1つだけになった場合は切り替えタイマーを停止
                    failureCodeTimer.Stop();
                    isDotAnimationActive = false;
                    UpdateFailureCodeDisplay();
                }
            }
        }
        
        // 故障コード表示開始
        private void StartFailureCodeDisplay()
        {
            if (failureCodes.Count > 0)
            {
                // ドットアニメーションは使用せず、直接表示
                isDotAnimationActive = false;
                UpdateFailureCodeDisplay();
                
                if (failureCodes.Count > 1)
                {
                    failureCodeTimer.Start();
                }
            }
        }
        
        // 故障コード表示更新（新しいLCD専用メソッドを呼び出し）
        private void UpdateFailureCodeDisplay()
        {
            UpdateLCDDisplay(); // LCD専用メソッドを呼び出し
        }
        
        // 故障コード切り替えタイマーイベント
        private void FailureCodeTimer_Tick(object sender, EventArgs e)
        {
            if (failureCodes.Count > 1)
            {
                currentFailureCodeIndex = (currentFailureCodeIndex + 1) % failureCodes.Count;
                
                // ドットアニメーションなしで直接切り替え
                isDotAnimationActive = false;
                UpdateFailureCodeDisplay();
                
                System.Diagnostics.Debug.WriteLine($"📟 故障コード切り替え: {failureCodes[currentFailureCodeIndex]}");
            }
        }
        
        // ドットアニメーションタイマーイベント
        private void DotAnimationTimer_Tick(object sender, EventArgs e)
        {
            dotCount++;
            
            if (dotCount > 3)
            {
                // ドットアニメーション終了、故障コード表示
                isDotAnimationActive = false;
                dotAnimationTimer.Stop();
            }
            
            UpdateFailureCodeDisplay();
        }
    }
}
