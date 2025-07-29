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
            Application.Run(new Form1());
        }
    }
}
