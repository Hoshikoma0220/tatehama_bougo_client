using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TakumiteAudioWrapper;
using TatehamaATS_v1.Exceptions;
using TrainCrewAPI;

namespace TatehamaATS_v1.RetsubanWindow
{
    internal class TimeLogic
    {
        private TimeData BeforeTimeData { get; set; }
        private TimeSpan ShiftTime { get; set; } = TimeSpan.FromHours(-10);
        private Dictionary<string, Image> Images_7seg { get; set; }
        private string NewHour { get; set; }

        public bool nowSetting;

        private AudioManager AudioManager;
        private AudioWrapper beep1;
        private AudioWrapper beep2;

        private PictureBox Time_h2 { get; set; }
        private PictureBox Time_h1 { get; set; }
        private PictureBox Time_m2 { get; set; }
        private PictureBox Time_m1 { get; set; }
        private PictureBox Time_s2 { get; set; }
        private PictureBox Time_s1 { get; set; }

        internal TimeLogic(PictureBox time_h2, PictureBox time_h1, PictureBox time_m2, PictureBox time_m1, PictureBox time_s2, PictureBox time_s1)
        {
            // 表示領域渡し
            Time_h2 = time_h2;
            Time_h1 = time_h1;
            Time_m2 = time_m2;
            Time_m1 = time_m1;
            Time_s2 = time_s2;
            Time_s1 = time_s1;

            // 初期化
            var tst_time = DateTime.Now + ShiftTime;
            BeforeTimeData = new TimeData()
            {
                hour = tst_time.Hour,
                minute = tst_time.Minute,
                second = tst_time.Second
            };
            Images_7seg = new Dictionary<string, Image> {
                {"0",  RetsubanResource._7seg_0} ,
                {"1",  RetsubanResource._7seg_1} ,
                {"2",  RetsubanResource._7seg_2} ,
                {"3",  RetsubanResource._7seg_3} ,
                {"4",  RetsubanResource._7seg_4} ,
                {"5",  RetsubanResource._7seg_5} ,
                {"6",  RetsubanResource._7seg_6} ,
                {"7",  RetsubanResource._7seg_7} ,
                {"8",  RetsubanResource._7seg_8} ,
                {"9",  RetsubanResource._7seg_9} ,
                {" ",  RetsubanResource._7seg_N} ,
                {"",  RetsubanResource._7seg_N}
            };
            NewHour = BeforeTimeData.hour.ToString();
            Time_h2 = time_h2;

            AudioManager = new AudioManager();
            beep1 = AudioManager.AddAudio("Sound/beep1.wav", 1.0f, TakumiteAudioWrapper.AudioType.Effect);
            beep2 = AudioManager.AddAudio("Sound/beep2.wav", 1.0f, TakumiteAudioWrapper.AudioType.Effect);
        }

        public void ClockTimer_Tick()
        {
            var tst_time = DateTime.Now + ShiftTime;
            TimeData timeData = new TimeData()
            {
                hour = tst_time.Hour < 4 ? tst_time.Hour + 24 : tst_time.Hour,
                minute = tst_time.Minute,
                second = tst_time.Second
            };
            if (nowSetting)
            {
                TimeDrawing(timeData, DateTime.Now.Millisecond < 500);
                BeforeTimeData = timeData;
            }
            else
            {
                if (BeforeTimeData.second != timeData.second)
                {
                    NewHour = timeData.hour.ToString();
                    TimeDrawing(timeData);
                    BeforeTimeData = timeData;
                }
            }
        }
             
        /// <summary>
        /// 時間部描画
        /// </summary>
        /// <param name="timeData"></param>
        private void TimeDrawing(TimeData timeData, bool hourDot = false)
        {
            // 時刻表示は背景画像に組み込まれているため処理をスキップ
            Debug.WriteLine($"時刻表示 - 無効化済み（背景画像に組み込み）: {timeData.hour:00}:{timeData.minute:00}:{timeData.second:00}");
            return;
        }

        internal void Buttons_Digit(string Digit)
        {
            // 時計機能を無効化 - 何もしない
            return;
        }

        internal void Buttons_Func(string Name)
        {
            // 時計機能を無効化 - 何もしない
            return;
        }
    }
}
