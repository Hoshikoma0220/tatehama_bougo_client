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
        power.BackColor = SystemColors.ControlDarkDark;
        power.Image = tatehama_bougo_client.RetsubanWindow.RetsubanResource.power_on;
        power.Location = new Point(37, 51);
        power.Name = "power";
        power.Size = new Size(62, 55);
        power.SizeMode = PictureBoxSizeMode.Zoom;
        power.TabIndex = 0;
        power.TabStop = false;
        // 
        // fail
        // 
        fail.BackColor = SystemColors.ControlDarkDark;
        fail.Image = tatehama_bougo_client.RetsubanWindow.RetsubanResource.kosyou;
        fail.Location = new Point(98, 51);
        fail.Name = "fail";
        fail.Size = new Size(64, 56);
        fail.SizeMode = PictureBoxSizeMode.Zoom;
        fail.TabIndex = 1;
        fail.TabStop = false;
        // 
        // retuban
        // 
        retuban.BackColor = SystemColors.ControlDarkDark;
        retuban.Image = tatehama_bougo_client.RetsubanWindow.RetsubanResource.Button_Retsuban;
        retuban.Location = new Point(520, 350);
        retuban.Name = "retuban";
        retuban.Size = new Size(96, 57);
        retuban.SizeMode = PictureBoxSizeMode.Zoom;
        retuban.TabIndex = 2;
        retuban.TabStop = false;
        // 
        // EBkaihou
        // 
        EBkaihou.BackColor = SystemColors.Control;
        EBkaihou.Image = tatehama_bougo_client.RetsubanWindow.RetsubanResource.EBkaihou_off1;
        EBkaihou.Location = new Point(434, 344);
        EBkaihou.Name = "EBkaihou";
        EBkaihou.Size = new Size(70, 63);
        EBkaihou.SizeMode = PictureBoxSizeMode.Zoom;
        EBkaihou.TabIndex = 3;
        EBkaihou.TabStop = false;
        // 
        // bougo
        // 
        bougo.BackColor = SystemColors.ControlDarkDark;
        bougo.Image = (Image)resources.GetObject("bougo.Image");
        bougo.Location = new Point(98, 220);
        bougo.Name = "bougo";
        bougo.Size = new Size(126, 128);
        bougo.SizeMode = PictureBoxSizeMode.Zoom;
        bougo.TabIndex = 4;
        bougo.TabStop = false;
        // 
        // shiken
        // 
        shiken.BackColor = SystemColors.ControlDarkDark;
        shiken.Image = tatehama_bougo_client.RetsubanWindow.RetsubanResource.botan;
        shiken.Location = new Point(306, 353);
        shiken.Name = "shiken";
        shiken.Size = new Size(51, 56);
        shiken.SizeMode = PictureBoxSizeMode.Zoom;
        shiken.TabIndex = 5;
        shiken.TabStop = false;
        // 
        // onryou
        // 
        onryou.BackColor = SystemColors.ControlDarkDark;
        onryou.Image = tatehama_bougo_client.RetsubanWindow.RetsubanResource.botan;
        onryou.Location = new Point(369, 353);
        onryou.Name = "onryou";
        onryou.Size = new Size(51, 56);
        onryou.SizeMode = PictureBoxSizeMode.Zoom;
        onryou.TabIndex = 6;
        onryou.TabStop = false;
        // 
        // kosyouLCD
        // 
        kosyouLCD.AutoSize = true;
        kosyouLCD.Location = new Point(434, 51);
        kosyouLCD.Name = "kosyouLCD";
        kosyouLCD.Size = new Size(38, 15);
        kosyouLCD.TabIndex = 7;
        kosyouLCD.Text = "label1";
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
        Text = "Form1";
        ((System.ComponentModel.ISupportInitialize)power).EndInit();
        ((System.ComponentModel.ISupportInitialize)fail).EndInit();
        ((System.ComponentModel.ISupportInitialize)retuban).EndInit();
        ((System.ComponentModel.ISupportInitialize)EBkaihou).EndInit();
        ((System.ComponentModel.ISupportInitialize)bougo).EndInit();
        ((System.ComponentModel.ISupportInitialize)shiken).EndInit();
        ((System.ComponentModel.ISupportInitialize)onryou).EndInit();
        ResumeLayout(false);
        PerformLayout();
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
