using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using TakumiteAudioWrapper;
using TatehamaATS_v1.RetsubanWindow;
using TrainCrewAPI;

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

        // 非常ブレーキ関連
        private bool emergencyBrakeButtonState = false; // false: 作動状態(非常ブレーキ有効), true: 開放状態(非常ブレーキ無効)
        private System.Windows.Forms.Timer ebBlinkTimer; // EB開放中の故障ランプ点滅タイマー
        private bool ebBlinkState = false; // EB開放中の故障ランプ点滅状態
        private string currentTrainNumber = "--"; // 列番入力画面で設定された列車番号
        private bool isTrainMoving = false; // 列車走行状態

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
            
            // TrainCrew接続はLoad時に行う（フォーム表示を優先）
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // グローバルホットキーを解除
            UnregisterHotKey(this.Handle, HOTKEY_ID_F4);
            
            // EB開放点滅タイマーを停止
            ebBlinkTimer?.Stop();
            ebBlinkTimer?.Dispose();
            
            // アプリケーション終了時に非常ブレーキを確実に解除
            EmergencyBrakeController.OnApplicationExit();
            
            // 全ての音声ループを停止
            shouldPlayLoop = false;
            shouldPlayKosyouLoop = false; 
            shouldPlayEBKaihouLoop = false;
            isBougoActive = false;
            isKosyouActive = false;
            isEBKaihouActive = false;
            
            // 故障検出時刻をリセット
            failureDetectedTime = null;
            webSocketTimeoutDetectedTime = null;
            
            // アプリケーション終了時に音量を100%に戻す
            try
            {
                TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                                    System.Diagnostics.Debug.WriteLine("� 防護無線フォールバック音量変更を実行");
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
                    
                    // 防護無線開始時にWindows Audio APIで音量を100%に設定
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 防護無線開始時：Windows Audio APIで100%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 防護無線開始時音量設定エラー: {ex.Message}");
                    }
                    
                    // 防護無線中の音量を100%に設定
                    currentVolume = 1.0f;
                    System.Diagnostics.Debug.WriteLine("🔊 防護無線音量を100%に設定");
                    
                    // 防護無線中: 通常音声ループのみ停止（他の音声は継続）
                    shouldPlayLoop = false;
                    // shouldPlayKosyouLoop = false; // 故障音は継続
                    // shouldPlayEBKaihouLoop = false; // EB開放音は継続
                    
                    // 故障音・EB開放音は停止せず継続再生
                    System.Diagnostics.Debug.WriteLine("� 防護無線発砲中 - 故障音・EB開放音は継続再生");
                    
                    // PlayLoopで継続再生（100%音量）
                    bougoF4Audio?.PlayLoop(currentVolume);
                    System.Diagnostics.Debug.WriteLine($"🔊 防護無線開始: 音量{(int)(currentVolume * 100)}%");
                    
                    // 防護無線ボタンの表示を更新
                    UpdateBougoDisplay();
                }
                else
                {
                    // 防護無線停止
                    System.Diagnostics.Debug.WriteLine("🔴 防護無線停止");
                    isBougoActive = false;
                    
                    // 防護無線を停止
                    bougoF4Audio?.Stop();
                    
                    // 防護無線停止時に音量を100%に戻す
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 防護無線停止時：アプリケーション音量を100%に復旧");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 防護無線停止時音量復旧エラー: {ex.Message}");
                    }
                    
                    // 音量はユーザー設定を維持（リセットしない）
                    System.Diagnostics.Debug.WriteLine($"🔊 防護無線停止 - 音量{(int)(currentVolume * 100)}%を維持");
                    
                    // 通常音声ループを再開
                    shouldPlayLoop = true;
                    if (!loopStarted)
                    {
                        StartSoundLoop();
                        loopStarted = true;
                    }
                    
                    // 防護無線ボタンの表示を更新
                    UpdateBougoDisplay();
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

        private void Form1_Load(object sender, EventArgs e)
        {
            // 音声管理初期化
            audioManager = new AudioManager();
            bougomusenno = audioManager.AddAudio("Sound/bougomusenno.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
            bougoF4Audio = audioManager.AddAudio("Sound/bougo.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
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
            var trainnumPath = System.IO.Path.Combine(exeDir, "Sound/set_trainnum.wav");
            var completePath = System.IO.Path.Combine(exeDir, "Sound/set_complete.wav");
            var kosyouPath = System.IO.Path.Combine(exeDir, "Sound/kosyou.wav");
            var kosyouKoePath = System.IO.Path.Combine(exeDir, "Sound/kosyou_koe.wav");
            var ebkaihouPath = System.IO.Path.Combine(exeDir, "Sound/EBkaihou.wav");
            var ebkaihouKoePath = System.IO.Path.Combine(exeDir, "Sound/EBkaihou_koe.wav");
            
            System.Diagnostics.Debug.WriteLine($"防護無線: {bougoPath} - {System.IO.File.Exists(bougoPath)}");
            System.Diagnostics.Debug.WriteLine($"防護音F4: {bougoF4Path} - {System.IO.File.Exists(bougoF4Path)}");
            System.Diagnostics.Debug.WriteLine($"列車番号: {trainnumPath} - {System.IO.File.Exists(trainnumPath)}");
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

            // TrainCrewクライアントを安全に初期化（エラーが発生してもフォーム表示を妨げない）
            try
            {
                InitializeTrainCrewClient();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrainCrewクライアント初期化エラー: {ex.Message}");
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
                
                // 通常音声ループ停止時に音量を100%に戻す
                try
                {
                    TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                    System.Diagnostics.Debug.WriteLine("🔊 通常音声ループ停止時：アプリケーション音量を100%に復旧");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 通常音声ループ停止時音量復旧エラー: {ex.Message}");
                }
            }
        }

        // インスタンスメソッドで完了音再生
        public void PlaySetComplete()
        {
            // まず既存の音声を停止
            shouldPlayLoop = false;
            
            // 完了音再生時にWindows Audio APIで音量を100%に設定
            try
            {
                TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                System.Diagnostics.Debug.WriteLine("🔊 完了音再生時：Windows Audio APIで100%に設定");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 完了音再生時音量設定エラー: {ex.Message}");
            }
            
            // 少し待ってから完了音を再生
            Task.Run(async () =>
            {
                await Task.Delay(500); // 既存音声の停止を待つ
                set_complete?.PlayOnce(systemVolume);
                System.Diagnostics.Debug.WriteLine("完了音を再生しました");
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
                    instance.StartKosyouLoop();
                    instance.kosyouLoopStarted = true;
                    
                    // 故障音開始時にWindows Audio APIで音量を100%に設定
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 故障音開始時：Windows Audio APIで100%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 故障音開始時音量設定エラー: {ex.Message}");
                    }
                    
                    // 故障音中の音量を100%に設定
                    instance.currentVolume = 1.0f;
                    System.Diagnostics.Debug.WriteLine("🔊 故障音音量を100%に設定");
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
                    
                    // 故障音停止時にアプリケーション音量を100%に戻す
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 故障音停止時：アプリケーション音量を100%に復旧");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 故障音停止時音量復旧エラー: {ex.Message}");
                    }
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
                    instance.StartEBKaihouLoop();
                    instance.ebKaihouLoopStarted = true;
                    
                    // EB開放音開始時にWindows Audio APIで音量を100%に設定
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 EB開放音開始時：Windows Audio APIで100%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ EB開放音開始時音量設定エラー: {ex.Message}");
                    }
                    
                    // EB開放音中の音量を100%に設定
                    instance.currentVolume = 1.0f;
                    System.Diagnostics.Debug.WriteLine("🔊 EB開放音音量を100%に設定");
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
                    
                    // EB開放音停止時にアプリケーション音量を100%に戻す
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 EB開放音停止時：アプリケーション音量を100%に復旧");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ EB開放音停止時音量復旧エラー: {ex.Message}");
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
                    
                    // 外部防護無線開始時にWindows Audio APIで音量を100%に設定
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 外部防護無線開始時：Windows Audio APIで100%に設定");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 外部防護無線開始時音量設定エラー: {ex.Message}");
                    }
                    
                    // 防護無線中の音量を100%に設定
                    instance.currentVolume = 1.0f;
                    System.Diagnostics.Debug.WriteLine("🔊 外部防護無線音量を100%に設定");
                    
                    // 外部防護無線中: 通常音声ループのみ停止（他の音声は継続）
                    shouldPlayLoop = false;
                    // shouldPlayKosyouLoop = false; // 故障音は継続
                    // shouldPlayEBKaihouLoop = false; // EB開放音は継続
                    
                    // 故障音・EB開放音は停止せず継続再生
                    System.Diagnostics.Debug.WriteLine("� 外部防護無線発砲中 - 故障音・EB開放音は継続再生");
                    
                    // PlayLoopで継続再生（100%音量）
                    instance.bougoF4Audio?.PlayLoop(instance.currentVolume);
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
                    
                    // 外部防護無線停止時に音量を100%に戻す
                    try
                    {
                        TakumiteAudioWrapper.WindowsAudioManager.SetApplicationVolume(1.0f);
                        System.Diagnostics.Debug.WriteLine("🔊 外部防護無線停止時：アプリケーション音量を100%に復旧");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 外部防護無線停止時音量復旧エラー: {ex.Message}");
                    }
                    
                    // 音量はユーザー設定を維持（リセットしない）
                    System.Diagnostics.Debug.WriteLine($"🔊 防護無線停止 - 音量{(int)(instance.currentVolume * 100)}%を維持");
                    
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

                if (data.trackCircuitList != null && data.myTrainData?.diaName != null)
                {
                    // 自列車が在線している軌道回路を抽出
                    var myTrainCircuits = data.trackCircuitList
                        .Where(tc => tc.On && tc.Last == data.myTrainData.diaName)
                        .ToList();

                    foreach (var circuit in myTrainCircuits)
                    {
                        currentTrackCircuits.Add(circuit.Name);

                        // ゾーンマッピングから対応するゾーンを取得
                        if (zoneMappings.ContainsKey(circuit.Name))
                        {
                            currentZones.Add(zoneMappings[circuit.Name]);
                        }
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
                power.Image = Image.FromFile(PowerOnImagePath);
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
                power.Image = Image.FromFile(PowerOffImagePath);
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
                fail.Image = Image.FromFile(KosyouErrorImagePath);
                kosyouLCD.Text = "故障発生";
                kosyouLCD.ForeColor = Color.Red;
                System.Diagnostics.Debug.WriteLine("⚠️ 故障音開始");
            }
            else
            {
                // 故障音停止
                StopKosyouSound();
                fail.Image = Image.FromFile(KosyouNormalImagePath);
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
                foreach (Form openForm in Application.OpenForms)
                {
                    if (openForm is RetsubanWindow)
                    {
                        existingWindow = openForm as RetsubanWindow;
                        break;
                    }
                }

                if (existingWindow != null)
                {
                    // 既存ウィンドウがある場合は閉じる
                    existingWindow.Close();
                    System.Diagnostics.Debug.WriteLine("🚊 列番入力画面を閉じました");
                }
                else
                {
                    // 既存ウィンドウがない場合は新しく開く
                    var subWindow = new RetsubanWindow();
                    subWindow.Show();
                    System.Diagnostics.Debug.WriteLine("🚊 列番入力画面を開きました");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 列番画面エラー: {ex.Message}");
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
            
            if (!powerOn) 
            {
                System.Diagnostics.Debug.WriteLine("🔊 音量調整無効 - 電源オフ");
                return; // 電源OFFの場合は動作しない
            }
            
            // 防護無線発砲中または故障音発生中またはEB開放音発生中のみ音量調整可能
            if (!isBougoActive && !isKosyouActive && !isEBKaihouActive) 
            {
                System.Diagnostics.Debug.WriteLine("🔊 音量調整無効 - 防護無線・故障音・EB開放音すべて停止中");
                return;
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
                
                // フォールバック：従来の停止→再開方式
                if (isBougoActive && bougoF4Audio != null)
                {
                    System.Diagnostics.Debug.WriteLine("� フォールバック音量変更を実行");
                    
                    try
                    {
                        // 停止して即座に新しい音量で再開
                        bougoF4Audio.Stop();
                        bougoF4Audio.PlayLoop(currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 フォールバック音量変更完了: {(int)(currentVolume * 100)}%");
                    }
                    catch (Exception fallbackEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ フォールバック音量変更エラー: {fallbackEx.Message}");
                        
                        // エラー時は再度試行
                        await Task.Delay(50);
                        bougoF4Audio?.PlayLoop(currentVolume);
                        System.Diagnostics.Debug.WriteLine($"🔊 リトライ音量変更: {(int)(currentVolume * 100)}%");
                    }
                }
                
                // 故障音の場合のフォールバック（故障音は再開ではなく音量変更のみ）
                if (isKosyouActive)
                {
                    System.Diagnostics.Debug.WriteLine("🔄 故障音フォールバック音量変更を実行");
                    // 故障音は連続ループなので、Windows Audio APIの変更のみで対応
                    // 特別な処理は不要（既にtry節で実行済み）
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
                EBkaihou.Image = Image.FromFile(EBKaihouOnImagePath);
            }
            else
            {
                EBkaihou.Image = Image.FromFile(EBKaihouOffImagePath);
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
            if (isBougoActive)
            {
                bougo.Image = Image.FromFile(BougoOnImagePath);
            }
            else
            {
                bougo.Image = Image.FromFile(BougoOffImagePath);
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

        // 故障ランプの表示を更新
        private void UpdateFailureLamp()
        {
            // EB開放中は点滅制御
            if (emergencyBrakeButtonState)
            {
                if (ebBlinkState)
                {
                    fail.Image = Image.FromFile(KosyouNormalImagePath); // 点灯
                    kosyouLCD.Text = "EB開放";
                    kosyouLCD.ForeColor = Color.Orange;
                    kosyouLCD.BackColor = Color.Black;
                }
                else
                {
                    fail.Image = null; // 消灯
                    kosyouLCD.Text = "EB開放";
                    kosyouLCD.ForeColor = Color.Orange;
                    kosyouLCD.BackColor = Color.Black;
                }
            }
            else if (failureLampOn)
            {
                fail.Image = Image.FromFile(KosyouNormalImagePath); // kosyou.pngを使用
                kosyouLCD.Text = "故障発生";
                kosyouLCD.ForeColor = Color.Red;
                kosyouLCD.BackColor = Color.Black; // 黒背景
            }
            else
            {
                fail.Image = null; // 消灯
                kosyouLCD.Text = "正常";
                kosyouLCD.ForeColor = Color.Lime; // 緑色（LED風）
                kosyouLCD.BackColor = Color.Black; // 黒背景
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
                            if (hasEverMetReleaseConditions)
                            {
                                // 5秒経過：故障ランプ点灯とEB作動、故障音開始
                                failureLampOn = true;
                                UpdateFailureLamp();
                                PlayKosyouSound();
                                
                                System.Diagnostics.Debug.WriteLine("🚨 5秒経過 - 故障ランプ点灯・EB作動・故障音開始");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("ℹ️ 条件不満足による故障検出 - EB解除条件未達成のため故障ランプ・音声をスキップ");
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
                        if (hasEverMetReleaseConditions || emergencyBrakeButtonState)
                        {
                            if (!failureLampOn)
                            {
                                failureLampOn = true;
                                UpdateFailureLamp();
                                System.Diagnostics.Debug.WriteLine("⚠️ 故障ランプ点灯 - 起動時EB作動");
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("ℹ️ 起動時EB作動 - EB解除条件未達成のため故障ランプ点灯をスキップ");
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
    }
}
