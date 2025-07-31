using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace tatehama_bougo_client
{
    /// <summary>
    /// 現実的な列車フロー非常ブレーキ制御クラス
    /// 列番設定・WebSocket接続状態・EB開放オーバーライドを考慮した制御
    /// </summary>
    public static class EmergencyBrakeController
    {
        private static Type? trainInputType;
        private static MethodInfo? setAtoNotchMethod;
        private static MethodInfo? initMethod;
        private static bool isDllLoaded = false;
        private static CancellationTokenSource? controlLoopCancellationToken;
        
        // 現実的な列車フロー状態管理
        private static bool isTrainNumberSet = false;      // 列番設定済みフラグ
        private static bool isWebSocketConnected = false;  // WebSocket接続状態
        private static bool isEbReleaseOverride = false;   // EB開放オーバーライド
        private static bool isCurrentlyBraking = false;    // 現在非常ブレーキ中フラグ
        
        // 制御ループ用
        private static bool isControlLoopActive = false;

        /// <summary>
        /// 非常ブレーキ制御を初期化
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // DLL 読み込み
                var asm = Assembly.LoadFrom(@"DLL\TrainCrewInput.dll");
                trainInputType = asm.GetType("TrainCrew.TrainCrewInput");

                if (trainInputType == null)
                {
                    Debug.WriteLine("🚨 EmergencyBrakeController: TrainCrewInput クラスが見つかりませんでした");
                    return;
                }

                // Init メソッド実行（必要な初期化処理）
                initMethod = trainInputType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
                initMethod?.Invoke(null, null);
                Debug.WriteLine("✅ EmergencyBrakeController: TrainCrewInput.Init() 実行完了");

                // SetATO_Notch メソッド取得
                setAtoNotchMethod = trainInputType.GetMethod("SetATO_Notch", new[] { typeof(int) });

                if (setAtoNotchMethod == null)
                {
                    Debug.WriteLine("🚨 EmergencyBrakeController: SetATO_Notch メソッドが見つかりませんでした");
                    return;
                }

                isDllLoaded = true;
                
                // 制御ループを開始
                StartControlLoop();
                
                Debug.WriteLine("✅ EmergencyBrakeController: 初期化完了 - 現実的な列車フロー制御開始");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ EmergencyBrakeController: 初期化エラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 現実的な列車フローに基づく非常ブレーキ状態判定
        /// </summary>
        private static bool DetermineEmergencyBrakeState()
        {
            // EB開放オーバーライドが有効な場合は無条件でブレーキ解除
            if (isEbReleaseOverride)
            {
                return false;
            }
            
            // 列番未設定の場合は非常ブレーキ
            if (!isTrainNumberSet)
            {
                return true;
            }
            
            // WebSocket未接続の場合も非常ブレーキ
            if (!isWebSocketConnected)
            {
                return true;
            }
            
            // 両方の条件が満たされている場合はブレーキ解除
            return false;
        }

        /// <summary>
        /// 制御ループを開始（現実的な列車フローを監視）
        /// </summary>
        private static async void StartControlLoop()
        {
            if (isControlLoopActive) return;
            
            controlLoopCancellationToken = new CancellationTokenSource();
            isControlLoopActive = true;

            try
            {
                while (!controlLoopCancellationToken.Token.IsCancellationRequested && isControlLoopActive)
                {
                    // 現在の状態に基づいて非常ブレーキ状態を判定
                    bool shouldBrake = DetermineEmergencyBrakeState();
                    
                    // 状態が変化した場合のみ処理
                    if (shouldBrake != isCurrentlyBraking)
                    {
                        if (shouldBrake)
                        {
                            ApplyEmergencyBrakeSignal();
                            isCurrentlyBraking = true;
                            Debug.WriteLine("🚨 EmergencyBrakeController: 非常ブレーキ作動（現実的フロー判定）");
                        }
                        else
                        {
                            ReleaseEmergencyBrakeSignal();
                            isCurrentlyBraking = false;
                            Debug.WriteLine("✅ EmergencyBrakeController: 非常ブレーキ解除（現実的フロー判定）");
                        }
                    }
                    
                    // 非常ブレーキ中は連続送信、そうでなければ監視のみ
                    int delayMs = isCurrentlyBraking ? 100 : 500;
                    await Task.Delay(delayMs, controlLoopCancellationToken.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("🔴 EmergencyBrakeController: 制御ループが停止されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ EmergencyBrakeController: 制御ループエラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 列番設定状態を更新
        /// </summary>
        public static void SetTrainNumberStatus(bool isSet)
        {
            isTrainNumberSet = isSet;
            Debug.WriteLine($"🚆 EmergencyBrakeController: 列番設定状態変更 - {(isSet ? "設定済み" : "未設定")}");
        }

        /// <summary>
        /// WebSocket接続状態を更新
        /// </summary>
        public static void SetWebSocketStatus(bool isConnected)
        {
            isWebSocketConnected = isConnected;
            Debug.WriteLine($"🌐 EmergencyBrakeController: WebSocket状態変更 - {(isConnected ? "接続中" : "切断中")}");
        }

        /// <summary>
        /// EB開放オーバーライド状態を設定
        /// </summary>
        public static void SetEbReleaseOverride(bool isOverride)
        {
            isEbReleaseOverride = isOverride;
            Debug.WriteLine($"🔓 EmergencyBrakeController: EB開放オーバーライド - {(isOverride ? "有効" : "無効")}");
        }

        /// <summary>
        /// 非常ブレーキ信号を送信（ノッチ値 -8）
        /// </summary>
        private static void ApplyEmergencyBrakeSignal()
        {
            try
            {
                if (isDllLoaded && setAtoNotchMethod != null)
                {
                    setAtoNotchMethod.Invoke(null, new object[] { -8 });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ EmergencyBrakeController: 非常ブレーキ信号送信エラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 非常ブレーキ解除信号を送信（ノッチ値 0）
        /// </summary>
        private static void ReleaseEmergencyBrakeSignal()
        {
            try
            {
                if (isDllLoaded && setAtoNotchMethod != null)
                {
                    setAtoNotchMethod.Invoke(null, new object[] { 0 });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ EmergencyBrakeController: 非常ブレーキ解除信号送信エラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 列番設定完了時の処理（レガシー互換性）
        /// </summary>
        public static void OnTrainNumberSet(string trainNumber)
        {
            // 9999も有効な列車番号として扱い、0000のみ除外
            bool isValid = !string.IsNullOrEmpty(trainNumber) && trainNumber != "0000";
            SetTrainNumberStatus(isValid);
            
            if (isValid)
            {
                Debug.WriteLine($"🚆 EmergencyBrakeController: 列番設定完了 - {trainNumber}");
            }
        }

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        public static void OnApplicationExit()
        {
            try
            {
                // 制御ループを停止
                isControlLoopActive = false;
                controlLoopCancellationToken?.Cancel();
                
                // 最終的に非常ブレーキを解除
                if (isDllLoaded && setAtoNotchMethod != null)
                {
                    setAtoNotchMethod.Invoke(null, new object[] { 0 });
                    Debug.WriteLine("✅ EmergencyBrakeController: アプリケーション終了により非常ブレーキ解除");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ EmergencyBrakeController: アプリケーション終了処理エラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 現在の非常ブレーキ状態を取得
        /// </summary>
        public static bool IsEmergencyBrakeActive => isCurrentlyBraking;

        /// <summary>
        /// DLLが正常にロードされているかを確認
        /// </summary>
        public static bool IsDllLoaded => isDllLoaded;
    }
}
