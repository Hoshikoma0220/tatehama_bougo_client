using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;
using TakumiteAudioWrapper;
using TatehamaATS_v1.RetsubanWindow;
using TrainCrewAPI;

namespace tatehama_bougo_client
{
    public partial class Form1 : Form
    {
        private PictureBox retsubanButton;
        private AudioManager audioManager;
        private AudioWrapper bougomusenno;
        private AudioWrapper set_trainnum;
        private AudioWrapper set_complete;
        private static bool shouldPlayLoop = true;
        private bool loopStarted = false;
        private static readonly object audioLock = new object();

        // TrainCrew連携関連
        private TrainCrewWebSocketClient trainCrewClient;
        private Label statusLabel;
        private Label trainInfoLabel;
        private ListBox trackCircuitListBox;
        private Label zoneInfoLabel;
        private Dictionary<string, string> zoneMappings;

        public Form1()
        {
            InitializeComponent();
            Load += Form1_Load;
            FormClosing += Form1_FormClosing;
            LoadZoneMappings();
            
            // TrainCrew接続はLoad時に行う（フォーム表示を優先）
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // アプリケーション終了時に非常ブレーキを確実に解除
            EmergencyBrakeController.OnApplicationExit();
            
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
            this.Text = "メインウィンドウ";
            this.Width = 800;
            this.Height = 600;

            // TrainCrew情報表示エリアの初期化
            InitializeTrainCrewDisplay();

            // 音声管理初期化
            audioManager = new AudioManager();
            bougomusenno = audioManager.AddAudio("Sound/bougomusenno.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
            set_trainnum = audioManager.AddAudio("Sound/set_trainnum.wav", 1.0f, TakumiteAudioWrapper.AudioType.MainLoop);
            set_complete = audioManager.AddAudio("Sound/set_complete.wav", 1.0f, TakumiteAudioWrapper.AudioType.System);
            
            // 音声ファイルの存在確認
            System.Diagnostics.Debug.WriteLine("=== 音声ファイル確認 ===");
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var bougoPath = System.IO.Path.Combine(exeDir, "Sound/bougomusenno.wav");
            var trainnumPath = System.IO.Path.Combine(exeDir, "Sound/set_trainnum.wav");
            var completePath = System.IO.Path.Combine(exeDir, "Sound/set_complete.wav");
            
            System.Diagnostics.Debug.WriteLine($"防護無線: {bougoPath} - {System.IO.File.Exists(bougoPath)}");
            System.Diagnostics.Debug.WriteLine($"列車番号: {trainnumPath} - {System.IO.File.Exists(trainnumPath)}");
            System.Diagnostics.Debug.WriteLine($"完了音: {completePath} - {System.IO.File.Exists(completePath)}");
            System.Diagnostics.Debug.WriteLine("==================");
            
            // 音声ループ開始（一度だけ）
            if (!loopStarted)
            {
                StartSoundLoop();
                loopStarted = true;
            }

            retsubanButton = new PictureBox
            {
                Image = Image.FromFile("Images/Button_Retsuban.png"),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Cursor = Cursors.Hand
            };

            // ウィンドウ右下に配置
            retsubanButton.Left = this.ClientSize.Width - retsubanButton.Width - 20;
            retsubanButton.Top = this.ClientSize.Height - retsubanButton.Height - 40;
            retsubanButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            retsubanButton.Click += (s, ev) =>
            {
                var subWindow = new RetsubanWindow();
                subWindow.ShowDialog();
            };

            this.Controls.Add(retsubanButton);

            // TrainCrewクライアントを安全に初期化（エラーが発生してもフォーム表示を妨げない）
            try
            {
                InitializeTrainCrewClient();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TrainCrewクライアント初期化エラー: {ex.Message}");
                statusLabel.Text = "TrainCrew: 初期化エラー";
                statusLabel.ForeColor = Color.Red;
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
                    if (!shouldPlayLoop) break;
                    
                    // bougomusenno.wavを再生
                    System.Diagnostics.Debug.WriteLine($"防護無線音声開始: {DateTime.Now:HH:mm:ss.fff}");
                    bougomusenno?.PlayOnce(1.0f);
                    
                    // 実際の音声長分待機
                    await Task.Delay(bougoDurationMs);
                    System.Diagnostics.Debug.WriteLine($"防護無線音声終了: {DateTime.Now:HH:mm:ss.fff}");
                    
                    if (!shouldPlayLoop) break;
                    
                    // set_trainnum.wavを再生
                    System.Diagnostics.Debug.WriteLine($"列車番号設定音声開始: {DateTime.Now:HH:mm:ss.fff}");
                    set_trainnum?.PlayOnce(1.0f);
                    
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
            }
        }

        // インスタンスメソッドで完了音再生
        public void PlaySetComplete()
        {
            // まず既存の音声を停止
            shouldPlayLoop = false;
            
            // 少し待ってから完了音を再生
            Task.Run(async () =>
            {
                await Task.Delay(500); // 既存音声の停止を待つ
                set_complete?.PlayOnce(1.0f);
                System.Diagnostics.Debug.WriteLine("完了音を再生しました");
            });
        }

        // 静的インスタンス参照
        private static Form1 instance;
        
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

        private void InitializeTrainCrewDisplay()
        {
            // 接続状態ラベル
            statusLabel = new Label
            {
                Text = "TrainCrew: 未接続",
                Location = new Point(20, 20),
                Size = new Size(300, 30),
                Font = new Font("Arial", 10, FontStyle.Bold),
                ForeColor = Color.Red
            };
            this.Controls.Add(statusLabel);

            // 列車情報ラベル
            trainInfoLabel = new Label
            {
                Text = "列車番号: --",
                Location = new Point(20, 60),
                Size = new Size(400, 30),
                Font = new Font("Arial", 12, FontStyle.Bold),
                ForeColor = Color.Blue
            };
            this.Controls.Add(trainInfoLabel);

            // 軌道回路リストボックス
            var trackCircuitTitleLabel = new Label
            {
                Text = "現在在線している軌道回路:",
                Location = new Point(20, 100),
                Size = new Size(200, 20),
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            this.Controls.Add(trackCircuitTitleLabel);

            trackCircuitListBox = new ListBox
            {
                Location = new Point(20, 125),
                Size = new Size(350, 120),
                Font = new Font("ＭＳ ゴシック", 9)
            };
            this.Controls.Add(trackCircuitListBox);

            // ゾーン情報ラベル
            zoneInfoLabel = new Label
            {
                Text = "現在のゾーン: --",
                Location = new Point(20, 260),
                Size = new Size(400, 60),
                Font = new Font("Arial", 12, FontStyle.Bold),
                ForeColor = Color.Green
            };
            this.Controls.Add(zoneInfoLabel);
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
                        if (statusLabel.InvokeRequired)
                        {
                            statusLabel.Invoke(new Action(() => {
                                statusLabel.Text = $"TrainCrew: {status}";
                                statusLabel.ForeColor = status.Contains("接続中") ? Color.Green : Color.Red;
                            }));
                        }
                        else
                        {
                            statusLabel.Text = $"TrainCrew: {status}";
                            statusLabel.ForeColor = status.Contains("接続中") ? Color.Green : Color.Red;
                        }
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
                        if (this.InvokeRequired)
                        {
                            this.Invoke(new Action(() => UpdateTrainCrewDisplay(data)));
                        }
                        else
                        {
                            UpdateTrainCrewDisplay(data);
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

        private void UpdateTrainCrewDisplay(TrainCrewAPI.TrainCrewStateData data)
        {
            try
            {
                // 列車情報の更新
                if (data.myTrainData != null)
                {
                    string trainName = data.myTrainData.diaName ?? "N/A";
                    trainInfoLabel.Text = $"列車番号: {trainName}";
                }

                // 軌道回路リストの更新
                trackCircuitListBox.Items.Clear();
                
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
                        trackCircuitListBox.Items.Add(circuit.Name);

                        // ゾーンマッピングから対応するゾーンを取得
                        if (zoneMappings.ContainsKey(circuit.Name))
                        {
                            currentZones.Add(zoneMappings[circuit.Name]);
                        }
                    }
                }

                // 軌道回路が見つからない場合の表示
                if (currentTrackCircuits.Count == 0)
                {
                    trackCircuitListBox.Items.Add("(在線軌道回路なし)");
                }

                // ゾーン情報の更新
                if (currentZones.Count > 0)
                {
                    var zoneList = string.Join(", ", currentZones.OrderBy(z => z));
                    zoneInfoLabel.Text = $"現在のゾーン: {zoneList}";
                }
                else
                {
                    zoneInfoLabel.Text = "現在のゾーン: (不明)";
                }

                // デバッグ情報
                System.Diagnostics.Debug.WriteLine($"=== TrainCrew表示更新 ===");
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
                System.Diagnostics.Debug.WriteLine($"表示更新エラー: {ex.Message}");
            }
        }
    }
}
