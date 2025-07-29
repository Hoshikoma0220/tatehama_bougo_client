using System;
using System.Reflection;
using System.Windows.Forms;

namespace tatehama_bougo_client
{
    public partial class ATOTestForm : Form
    {
        private Type? trainInputType;
        private MethodInfo? setAtoNotchMethod;
        private MethodInfo? initMethod;

        public ATOTestForm()
        {
            InitializeComponent();
            Load += ATOTestForm_Load;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(600, 300);
            this.Name = "ATOTestForm";
            this.Text = "ATO ノッチ制御テスト";
            this.ResumeLayout(false);
        }

        private void ATOTestForm_Load(object? sender, EventArgs e)
        {
            try
            {
                // DLL 読み込み
                var asm = Assembly.LoadFrom(@"DLL\TrainCrewInput.dll");
                trainInputType = asm.GetType("TrainCrew.TrainCrewInput");

                if (trainInputType == null)
                {
                    MessageBox.Show("TrainCrewInput クラスが見つかりませんでした。");
                    return;
                }

                // Init メソッド実行（必要な初期化処理）
                initMethod = trainInputType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
                initMethod?.Invoke(null, null);
                System.Diagnostics.Debug.WriteLine("TrainCrewInput.Init() 実行完了");

                // SetATO_Notch メソッド取得
                setAtoNotchMethod = trainInputType.GetMethod("SetATO_Notch", new[] { typeof(int) });

                if (setAtoNotchMethod == null)
                {
                    MessageBox.Show("SetATO_Notch メソッドが見つかりませんでした。");
                    return;
                }

                // ノッチ制御ボタン作成
                CreateButton("停止 (0)", 0, 30, 20);
                CreateButton("力行 1", 1, 150, 20);
                CreateButton("力行 3", 3, 270, 20);
                CreateButton("ブレーキ 1", -1, 390, 20);
                CreateButton("ブレーキ 3", -3, 510, 20);
                
                CreateButton("ブレーキ 5", -5, 30, 80);
                CreateButton("非常ブレーキ", -8, 150, 80);
                CreateButton("状態確認", 999, 270, 80); // 特殊値で状態確認

                // 状態表示用ラベル
                var statusLabel = new Label
                {
                    Text = "DLL初期化完了。ノッチ制御テスト可能です。",
                    Width = 500,
                    Height = 40,
                    Left = 30,
                    Top = 150,
                    BackColor = System.Drawing.Color.LightGreen
                };
                Controls.Add(statusLabel);

                MessageBox.Show("ATO制御初期化完了！各ボタンでノッチ制御をテストできます。");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初期化失敗: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"ATO初期化エラー: {ex}");
            }
        }

        private void CreateButton(string text, int notchValue, int x, int y)
        {
            var btn = new Button
            {
                Text = text,
                Width = 100,
                Height = 40,
                Left = x,
                Top = y
            };
            btn.Click += (s, e) => SendAtoNotch(notchValue);
            Controls.Add(btn);
        }

        private void SendAtoNotch(int notch)
        {
            try
            {
                if (notch == 999) // 状態確認用
                {
                    MessageBox.Show($"DLL状態: {(trainInputType != null ? "OK" : "NG")}\n" +
                                  $"SetATO_Notchメソッド: {(setAtoNotchMethod != null ? "OK" : "NG")}");
                    return;
                }

                if (setAtoNotchMethod != null)
                {
                    setAtoNotchMethod.Invoke(null, new object[] { notch });
                    System.Diagnostics.Debug.WriteLine($"ノッチ {notch} を送信しました");
                    
                    // 非常ブレーキの場合は特別な表示
                    if (notch == -8)
                    {
                        MessageBox.Show($"非常ブレーキ (ノッチ {notch}) を送信しました！", "非常ブレーキ", 
                                      MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        this.Text = $"ATO ノッチ制御テスト - 最後の送信: {notch}";
                    }
                }
                else
                {
                    MessageBox.Show("SetATO_Notch メソッドが初期化されていません");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"送信エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"ノッチ送信エラー: {ex}");
            }
        }
    }
}
