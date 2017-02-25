namespace Certify.Forms.Controls
{
    partial class CertRequestSettingsIIS
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lstSites = new System.Windows.Forms.ComboBox();
            this.btnRequestCertificate = new System.Windows.Forms.Button();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.dataGridViewDomains = new System.Windows.Forms.DataGridView();
            this.chkSkipConfigCheck = new System.Windows.Forms.CheckBox();
            this.chkAutoBindings = new System.Windows.Forms.CheckBox();
            this.lblWebsiteRoot = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.chkIncludeInAutoRenew = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.tabPage2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewDomains)).BeginInit();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.Controls.Add(this.tabControl1);
            this.groupBox1.Controls.Add(this.progressBar1);
            this.groupBox1.Controls.Add(this.btnCancel);
            this.groupBox1.Controls.Add(this.lstSites);
            this.groupBox1.Controls.Add(this.btnRequestCertificate);
            this.groupBox1.Location = new System.Drawing.Point(14, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(584, 369);
            this.groupBox1.TabIndex = 11;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "IIS Certificate Request";
            this.groupBox1.Enter += new System.EventHandler(this.groupBox1_Enter);
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(15, 328);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(152, 23);
            this.progressBar1.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar1.TabIndex = 22;
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(352, 328);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(82, 23);
            this.btnCancel.TabIndex = 18;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // lstSites
            // 
            this.lstSites.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.lstSites.FormattingEnabled = true;
            this.lstSites.Location = new System.Drawing.Point(15, 19);
            this.lstSites.Name = "lstSites";
            this.lstSites.Size = new System.Drawing.Size(389, 21);
            this.lstSites.TabIndex = 15;
            this.lstSites.SelectedIndexChanged += new System.EventHandler(this.lstSites_SelectedIndexChanged);
            // 
            // btnRequestCertificate
            // 
            this.btnRequestCertificate.Location = new System.Drawing.Point(440, 328);
            this.btnRequestCertificate.Name = "btnRequestCertificate";
            this.btnRequestCertificate.Size = new System.Drawing.Size(128, 23);
            this.btnRequestCertificate.TabIndex = 13;
            this.btnRequestCertificate.Text = "Request Certificate";
            this.btnRequestCertificate.UseVisualStyleBackColor = true;
            this.btnRequestCertificate.Click += new System.EventHandler(this.btnRequestCertificate_Click);
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Location = new System.Drawing.Point(15, 46);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(557, 263);
            this.tabControl1.TabIndex = 26;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Controls.Add(this.chkIncludeInAutoRenew);
            this.tabPage1.Controls.Add(this.label4);
            this.tabPage1.Controls.Add(this.dataGridViewDomains);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(549, 237);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Domains";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // tabPage2
            // 
            this.tabPage2.Controls.Add(this.chkSkipConfigCheck);
            this.tabPage2.Controls.Add(this.chkAutoBindings);
            this.tabPage2.Controls.Add(this.lblWebsiteRoot);
            this.tabPage2.Controls.Add(this.label2);
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(549, 237);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Advanced";
            this.tabPage2.UseVisualStyleBackColor = true;
            // 
            // dataGridViewDomains
            // 
            this.dataGridViewDomains.AllowUserToAddRows = false;
            this.dataGridViewDomains.AllowUserToDeleteRows = false;
            this.dataGridViewDomains.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewDomains.Location = new System.Drawing.Point(10, 45);
            this.dataGridViewDomains.Name = "dataGridViewDomains";
            this.dataGridViewDomains.Size = new System.Drawing.Size(533, 163);
            this.dataGridViewDomains.TabIndex = 26;
            // 
            // chkSkipConfigCheck
            // 
            this.chkSkipConfigCheck.AutoSize = true;
            this.chkSkipConfigCheck.Location = new System.Drawing.Point(16, 38);
            this.chkSkipConfigCheck.Name = "chkSkipConfigCheck";
            this.chkSkipConfigCheck.Size = new System.Drawing.Size(223, 17);
            this.chkSkipConfigCheck.TabIndex = 30;
            this.chkSkipConfigCheck.Text = "Skip challenge response file config check";
            this.chkSkipConfigCheck.UseVisualStyleBackColor = true;
            // 
            // chkAutoBindings
            // 
            this.chkAutoBindings.AutoSize = true;
            this.chkAutoBindings.Checked = true;
            this.chkAutoBindings.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoBindings.Location = new System.Drawing.Point(16, 15);
            this.chkAutoBindings.Name = "chkAutoBindings";
            this.chkAutoBindings.Size = new System.Drawing.Size(229, 17);
            this.chkAutoBindings.TabIndex = 29;
            this.chkAutoBindings.Text = "Auto create/update IIS bindings (uses SNI)";
            this.chkAutoBindings.UseVisualStyleBackColor = true;
            // 
            // lblWebsiteRoot
            // 
            this.lblWebsiteRoot.AutoSize = true;
            this.lblWebsiteRoot.Location = new System.Drawing.Point(91, 71);
            this.lblWebsiteRoot.Name = "lblWebsiteRoot";
            this.lblWebsiteRoot.Size = new System.Drawing.Size(77, 13);
            this.lblWebsiteRoot.TabIndex = 26;
            this.lblWebsiteRoot.Text = "(Not Specified)";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(13, 71);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(75, 13);
            this.label2.TabIndex = 25;
            this.label2.Text = "Website Root:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(7, 7);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(333, 13);
            this.label4.TabIndex = 27;
            this.label4.Text = "The following domains will be included as a single certificate request. ";
            // 
            // chkIncludeInAutoRenew
            // 
            this.chkIncludeInAutoRenew.AutoSize = true;
            this.chkIncludeInAutoRenew.Checked = true;
            this.chkIncludeInAutoRenew.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkIncludeInAutoRenew.Location = new System.Drawing.Point(10, 214);
            this.chkIncludeInAutoRenew.Name = "chkIncludeInAutoRenew";
            this.chkIncludeInAutoRenew.Size = new System.Drawing.Size(129, 17);
            this.chkIncludeInAutoRenew.TabIndex = 32;
            this.chkIncludeInAutoRenew.Text = "Enable Auto Renewal";
            this.chkIncludeInAutoRenew.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(7, 29);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(520, 13);
            this.label1.TabIndex = 33;
            this.label1.Text = "LetsEncrypt must be able to access all of theses sites via HTTP (port 80) for the" +
    " certification process to work.";
            // 
            // CertRequestSettingsIIS
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox1);
            this.Name = "CertRequestSettingsIIS";
            this.Size = new System.Drawing.Size(616, 389);
            this.Load += new System.EventHandler(this.CertRequestSettingsIIS_Load);
            this.groupBox1.ResumeLayout(false);
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            this.tabPage2.ResumeLayout(false);
            this.tabPage2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewDomains)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.ComboBox lstSites;
        private System.Windows.Forms.Button btnRequestCertificate;
        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.DataGridView dataGridViewDomains;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.CheckBox chkIncludeInAutoRenew;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.CheckBox chkSkipConfigCheck;
        private System.Windows.Forms.CheckBox chkAutoBindings;
        private System.Windows.Forms.Label lblWebsiteRoot;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
    }
}
