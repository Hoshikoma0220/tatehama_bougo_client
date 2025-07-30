using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace tatehama_bougo_client
{
    /// <summary>
    /// 非常ブレーキ制御クラス
    /// 列番設定まで非常ブレーキを維持し、設定完了時またはアプリ終了時に解除
    /// </summary>
    public static class EmergencyBrakeController
    {
        private static Type? trainInputType;
        private static MethodInfo? setAtoNotchMethod;
        private static MethodInfo? initMethod;
        private static bool isEmergencyBrakeActive = false;
        private static bool isDllLoaded = false;
        private static CancellationTokenSource? emergencyBrakeCancellationToken;

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
                    Debug.WriteLine("非常ブレーキ制御: TrainCrewInput クラスが見つかりませんでした");
                    return;
                }

                // Init メソッド実行（必要な初期化処理）
                initMethod = trainInputType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
                initMethod?.Invoke(null, null);
                Debug.WriteLine("非常ブレーキ制御: TrainCrewInput.Init() 実行完了");

                // SetATO_Notch メソッド取得
                setAtoNotchMethod = trainInputType.GetMethod("SetATO_Notch", new[] { typeof(int) });

                if (setAtoNotchMethod == null)
                {
                    Debug.WriteLine("非常ブレーキ制御: SetATO_Notch メソッドが見つかりませんでした");
                    return;
                }

                isDllLoaded = true;
                StartEmergencyBrakeLoop();
                Debug.WriteLine("非常ブレーキ制御: 初期化完了 - 非常ブレーキループ開始");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"非常ブレーキ制御: 初期化エラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 非常ブレーキループを開始（定期的に-8を送信し続ける）
        /// </summary>
        private static async void StartEmergencyBrakeLoop()
        {
            emergencyBrakeCancellationToken = new CancellationTokenSource();
            isEmergencyBrakeActive = true;

            try
            {
                while (!emergencyBrakeCancellationToken.Token.IsCancellationRequested && isEmergencyBrakeActive)
                {
                    SendEmergencyBrakeSignal();
                    await Task.Delay(100, emergencyBrakeCancellationToken.Token); // 100msごとに送信
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("非常ブレーキ制御: 非常ブレーキループが停止されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"非常ブレーキ制御: 非常ブレーキループエラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 非常ブレーキ信号を送信（ノッチ値 -8）
        /// </summary>
        private static void SendEmergencyBrakeSignal()
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
                Debug.WriteLine($"非常ブレーキ制御: 非常ブレーキ信号送信エラー - {ex.Message}");
            }
        }

        /// <summary>
        /// 非常ブレーキを作動（ループ開始）
        /// </summary>
        public static void ApplyEmergencyBrake()
        {
            if (!isEmergencyBrakeActive)
            {
                StartEmergencyBrakeLoop();
                Debug.WriteLine("非常ブレーキ制御: 非常ブレーキ作動（ループ開始）");
            }
        }

        /// <summary>
        /// 非常ブレーキを解除（ループ停止、ノッチ値 0 を送信）
        /// </summary>
        public static void ReleaseEmergencyBrake()
        {
            if (!isEmergencyBrakeActive) return;

            try
            {
                // 非常ブレーキループを停止
                emergencyBrakeCancellationToken?.Cancel();
                isEmergencyBrakeActive = false;

                // ノッチを0に戻す
                if (isDllLoaded && setAtoNotchMethod != null)
                {
                    setAtoNotchMethod.Invoke(null, new object[] { 0 });
                    Debug.WriteLine("非常ブレーキ制御: 非常ブレーキ解除（ノッチ 0 送信）");
                }
                else
                {
                    Debug.WriteLine("非常ブレーキ制御: DLL未初期化のため非常ブレーキ解除送信をスキップ");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"非常ブレーキ制御: 非常ブレーキ解除エラー - {ex.Message}");
                isEmergencyBrakeActive = false; // エラー時もフラグはクリア
            }
        }

        /// <summary>
        /// 列番設定完了時の処理
        /// </summary>
        public static void OnTrainNumberSet(string trainNumber)
        {
            // 9999も有効な列車番号として扱い、0000のみ除外
            if (!string.IsNullOrEmpty(trainNumber) && trainNumber != "0000")
            {
                ReleaseEmergencyBrake();
                Debug.WriteLine($"非常ブレーキ制御: 列番設定完了により非常ブレーキ解除 - {trainNumber}");
            }
        }

        /// <summary>
        /// アプリケーション終了時の処理
        /// </summary>
        public static void OnApplicationExit()
        {
            ReleaseEmergencyBrake();
            Debug.WriteLine("非常ブレーキ制御: アプリケーション終了により非常ブレーキ解除");
        }

        /// <summary>
        /// 現在の非常ブレーキ状態を取得
        /// </summary>
        public static bool IsEmergencyBrakeActive => isEmergencyBrakeActive;

        /// <summary>
        /// DLLが正常にロードされているかを確認
        /// </summary>
        public static bool IsDllLoaded => isDllLoaded;
    }
}
