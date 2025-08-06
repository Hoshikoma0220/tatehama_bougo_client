using System.Drawing;

namespace tatehama_bougo_client;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
        power = new PictureBox();
        fail = new PictureBox();
        retuban = new PictureBox();
        EBkaihou = new PictureBox();
        bougo = new PictureBox();
        shiken = new PictureBox();
        onryou = new PictureBox();
        kosyouLCD = new Label();
        ((System.ComponentModel.ISupportInitialize)power).BeginInit();
        ((System.ComponentModel.ISupportInitialize)fail).BeginInit();
        ((System.ComponentModel.ISupportInitialize)retuban).BeginInit();
        ((System.ComponentModel.ISupportInitialize)EBkaihou).BeginInit();
        ((System.ComponentModel.ISupportInitialize)bougo).BeginInit();
        ((System.ComponentModel.ISupportInitialize)shiken).BeginInit();
        ((System.ComponentModel.ISupportInitialize)onryou).BeginInit();
        SuspendLayout();
        // 
        // power
        // 
        power.BackColor = Color.Transparent;
        power.Location = new Point(37, 47);
        power.Name = "power";
        power.Size = new Size(62, 55);
        power.SizeMode = PictureBoxSizeMode.Zoom;
        power.TabIndex = 0;
        power.TabStop = false;
        // 
        // fail
        // 
        fail.BackColor = Color.Transparent;
        fail.Location = new Point(98, 47);
        fail.Name = "fail";
        fail.Size = new Size(64, 56);
        fail.SizeMode = PictureBoxSizeMode.Zoom;
        fail.TabIndex = 1;
        fail.TabStop = false;
        // 
        // retuban
        // 
        retuban.BackColor = Color.Transparent;
        retuban.Cursor = Cursors.Hand;
        retuban.Location = new Point(520, 350);
        retuban.Name = "retuban";
        retuban.Size = new Size(96, 57);
        retuban.SizeMode = PictureBoxSizeMode.Zoom;
        retuban.TabIndex = 2;
        retuban.TabStop = false;
        // 
        // EBkaihou
        // 
        EBkaihou.BackColor = Color.Transparent;
        EBkaihou.Cursor = Cursors.Hand;
        EBkaihou.Location = new Point(434, 344);
        EBkaihou.Name = "EBkaihou";
        EBkaihou.Size = new Size(70, 63);
        EBkaihou.SizeMode = PictureBoxSizeMode.Zoom;
        EBkaihou.TabIndex = 3;
        EBkaihou.TabStop = false;
        // 
        // bougo
        // 
        bougo.BackColor = Color.Transparent;
        bougo.Cursor = Cursors.Hand;
        bougo.Location = new Point(98, 220);
        bougo.Name = "bougo";
        bougo.Size = new Size(126, 128);
        bougo.SizeMode = PictureBoxSizeMode.Zoom;
        bougo.TabIndex = 4;
        bougo.TabStop = false;
        // 
        // shiken
        // 
        shiken.BackColor = Color.Transparent;
        shiken.Location = new Point(306, 353);
        shiken.Name = "shiken";
        shiken.Size = new Size(51, 56);
        shiken.SizeMode = PictureBoxSizeMode.Zoom;
        shiken.TabIndex = 5;
        shiken.TabStop = false;
        // 
        // onryou
        // 
        onryou.BackColor = Color.Transparent;
        onryou.Cursor = Cursors.Hand;
        onryou.Location = new Point(369, 353);
        onryou.Name = "onryou";
        onryou.Size = new Size(51, 56);
        onryou.SizeMode = PictureBoxSizeMode.Zoom;
        onryou.TabIndex = 6;
        onryou.TabStop = false;
        // 
        // kosyouLCD
        // 
        kosyouLCD.BackColor = Color.FromArgb(40, 60, 40); // LCD風の暗緑色背景
        kosyouLCD.BorderStyle = BorderStyle.Fixed3D;
        kosyouLCD.Font = new Font("ＭＳ ゴシック", 28F, FontStyle.Bold); // フォントサイズを大幅に増加
        kosyouLCD.ForeColor = Color.Lime;
        kosyouLCD.Location = new Point(306, 19);
        kosyouLCD.Name = "kosyouLCD";
        kosyouLCD.Size = new Size(310, 96);
        kosyouLCD.TabIndex = 7;
        kosyouLCD.Text = ""; // 初期表示は空
        kosyouLCD.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackgroundImage = (Image)resources.GetObject("$this.BackgroundImage");
        BackgroundImageLayout = ImageLayout.None;
        ClientSize = new Size(640, 424);
        Controls.Add(kosyouLCD);
        Controls.Add(onryou);
        Controls.Add(shiken);
        Controls.Add(bougo);
        Controls.Add(EBkaihou);
        Controls.Add(retuban);
        Controls.Add(fail);
        Controls.Add(power);
        ForeColor = Color.Transparent;
        Name = "Form1";
        Text = "立浜防護無線システム";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ((System.ComponentModel.ISupportInitialize)power).EndInit();
        ((System.ComponentModel.ISupportInitialize)fail).EndInit();
        ((System.ComponentModel.ISupportInitialize)retuban).EndInit();
        ((System.ComponentModel.ISupportInitialize)EBkaihou).EndInit();
        ((System.ComponentModel.ISupportInitialize)bougo).EndInit();
        ((System.ComponentModel.ISupportInitialize)shiken).EndInit();
        ((System.ComponentModel.ISupportInitialize)onryou).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private PictureBox power;
    private PictureBox fail;
    private PictureBox retuban;
    private PictureBox EBkaihou;
    private PictureBox bougo;
    private PictureBox shiken;
    private PictureBox onryou;
    private Label kosyouLCD;
}
