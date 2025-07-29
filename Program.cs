using System;
using System.Windows.Forms;

namespace tatehama_bougo_client
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize(); // Windows Forms 用の推奨設定
            
            // テスト用にATOTestFormを起動（通常時はForm1を使用）
            // Application.Run(new ATOTestForm()); // テスト用
            
            // 非常ブレーキ制御を初期化（列番設定まで非常ブレーキ作動）
            EmergencyBrakeController.Initialize();
            
            // アプリケーション終了時に非常ブレーキを解除
            Application.ApplicationExit += (sender, e) => EmergencyBrakeController.OnApplicationExit();
            
            Application.Run(new Form1());
        }
    }
}
